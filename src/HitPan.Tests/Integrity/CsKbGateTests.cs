using System.Security.Claims;
using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 작14 묶음 C 게이트 — CS 책상 + 누리집의 **안전핀을 동작으로** 잰다
/// (작업지시서 §4 C-게이트 · 설계서 §2-ⓓ).
///
/// <list type="bullet">
/// <item>G-C-1 승격 초안 — 티켓엔 회사명·테넌트 식별자가 **실제로 있는데도** 초안엔 0건(화이트리스트 SELECT)</item>
/// <item>G-C-2 승인 없이는 승인 판 **행 자체가 안 생긴다** — DB 가 거절(CHECK)</item>
/// <item>G-C-3 식별정보 품은 본문 승인 시도 = 거부 + 사유코드 로그 · 양성 대조군 = 깨끗한 본문은 통과</item>
/// <item>G-C-4 고침은 version+1 — 옛 판이 남는다(UPDATE 0건)</item>
/// <item>G-C-5 재사용 재료는 **승인 판만** — 초안·폐기는 WHERE 절에서 빠진다</item>
/// <item>G-C-6 외부 AI 잠금장치 fail-closed — 동의 0건·표 부재 = 닫힘(음성) · 1행 = 열림(양성)</item>
/// <item>G-C-7 8폴더·이슈코드·PRD 틀(레포 실물 · DB 없이도 돈다)</item>
/// <item>G-C-8 분류 다시 고르기 — 8종 밖 거부 + 기록 행 실존</item>
/// <item>G-C-9 전화 접수 — 식별정보 모양 거부(음성) · 깨끗한 통화는 1행(양성)</item>
/// </list>
/// </summary>
public sealed class CsKbGateTests : IDisposable
{
    private readonly BackofficeRoleGateHarness _h =
        new($"hitpan_gate_cskb_{Guid.NewGuid():N}");

    public void Dispose() => _h.Dispose();

    private const string CompanyName = "게이트상사";   // 🔴 초안에 나오면 안 되는 값
    private const string TicketBody = "수주 등록에서 저장을 누르면 아무 반응이 없습니다";

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = _h.DbConnString(),
        }).Build();

    private KbController Kb(string actor) => new(Config(), NullLogger<KbController>.Instance)
    {
        ControllerContext = Ctx(actor),
    };

    private CsAdminController Cs(string actor) => new(Config(), NullLogger<CsAdminController>.Instance)
    {
        ControllerContext = Ctx(actor),
    };

    private static ControllerContext Ctx(string actor) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", actor) }, "gate")),
        },
    };

    /// <summary>회사명·테넌트 식별자가 **실재하는** 티켓 1장. 초안이 그것을 안 가져오는지가 G-C-1 이다.</summary>
    private async Task<(long TicketId, string TenantId, string TenantCode)> SeedTicketAsync(string? body = null)
    {
        var tenantId = Guid.NewGuid().ToString();
        var code = $"G{Guid.NewGuid():N}"[..12];
        await using var db = await _h.OpenAsync();
        await db.ExecuteAsync(@"
            INSERT INTO tenants (tenant_id, tenant_code, company_name, license_key_hash)
            VALUES (@Id, @Code, @Company, 'x');
            INSERT INTO bo_cs_tickets
                (tenant_id, client_ticket_uid, received_channel, category, sub_tag, shape_tag,
                 body, screen_code, erp_version, status)
            VALUES (@Id, @Uid, 'erp_message', 'use', 'estimate_sales', 'open_fail',
                    @Body, 'sales_order', '1.3.53', '완료');",
            new
            {
                Id = tenantId, Code = code, Company = CompanyName,
                Uid = Guid.NewGuid().ToString(), Body = body ?? TicketBody,
            });
        var id = await db.ExecuteScalarAsync<long>(
            "SELECT id FROM bo_cs_tickets WHERE tenant_id = @Id", new { Id = tenantId });
        return (id, tenantId, code);
    }

    // 🔴 한글을 \uXXXX 로 바꾸지 않는 직렬화 — 이 한 줄이 게이트의 생명이다.
    //    기본 설정은 비 ASCII 를 escape 한다. 그러면 「회사명이 응답에 있나」를 한글로 묻는
    //    음성 단언이 **늘 통과해 버린다**(글자가 게… 로 바뀌어 있으니 못 찾는다) —
    //    새는 걸 못 잡는 게이트가 된다. CI 가 양성 축(「문제점」이 있나)으로 이걸 잡았다(2026-10-08).
    private static readonly JsonSerializerOptions RawKorean = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Json(IActionResult r)
    {
        // 자기 검사(미끼 글자) — 한글이 escape 되면 아래 음성 단언들이 전부 거짓 통과한다.
        //   🔴 「\u 가 하나도 없어야 한다」로 적으면 안 된다: 이모지는 서러게이트 쌍이라
        //   UnsafeRelaxed 에서도 \uD83D… 로 남는 것이 정상이다(CI 실측 2026-10-08).
        //   재야 할 것은 **한글이 비교 가능한 상태인가** 하나뿐이다.
        Assert.Equal("\"문제점\"", JsonSerializer.Serialize("문제점", RawKorean));
        return JsonSerializer.Serialize(((ObjectResult)r).Value, RawKorean);
    }

    // ── G-C-1 ────────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-1 🔴 승격 초안 — 티켓엔 회사명·식별자가 있는데도 초안엔 0건(가져오지 않은 값은 잊을 수도 없다)")]
    public async Task G_C_1_초안_식별정보_화이트리스트()
    {
        if (!await _h.TrySetUpAsync("G-C-1")) return;
        var (ticketId, tenantId, tenantCode) = await SeedTicketAsync();

        var res = await Kb("cs-1").PromoteDraft(ticketId, default);
        var json = Json(res);

        // 양성 — 초안은 만들어진다(그래야 「못 만들어서 깨끗한 것」과 구별된다)
        Assert.Contains("문제점", json);
        Assert.Contains("해결점", json);
        Assert.Contains("sales_order", json);   // 화면 코드는 가져온다(식별정보가 아니다)
        // 🔴 음성 단언의 효력 증명 — 티켓 본문(한글)은 그대로 실려 온다.
        //    즉 한글이 비교 가능한 상태인데도 회사명·식별자만 없는 것이다(거짓 통과 아님).
        Assert.Contains(TicketBody, json);

        // 🔴 음성 — 식별정보는 한 글자도 없다
        Assert.DoesNotContain(CompanyName, json);
        Assert.DoesNotContain(tenantId, json);
        Assert.DoesNotContain(tenantCode, json);

        // 초안 생성 자체도 기록된다(누가 창고로 옮기려 했나)
        await using var db = await _h.OpenAsync();
        Assert.Equal(1, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bo_kb_doc_logs WHERE action = 'promote_draft' AND actor_id = 'cs-1'"));
    }

    // ── G-C-2 ────────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-2 🔴 승인 없이는 승인 판 행이 안 생긴다 — DB 가 거절(CHECK) · API 우회로도 못 넣는다")]
    public async Task G_C_2_승인없으면_행_불생성()
    {
        if (!await _h.TrySetUpAsync("G-C-2")) return;
        await using var db = await _h.OpenAsync();

        // 음성 — 승인 판인데 승인자가 없다 ⇒ DB 거절
        var ex = await Assert.ThrowsAsync<MySqlException>(() => db.ExecuteAsync(@"
            INSERT INTO bo_kb_docs (issue_code, category, title, body_md, status, version, created_by)
            VALUES ('USE-999', 'use', '우회 시도', '본문', '승인', 1, 'attacker')"));
        Assert.True(ex.Number is 4025 or 3819 or 1105,
            $"CHECK 위반이 아닌 다른 이유로 실패했다 — number={ex.Number} / {ex.Message}");
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bo_kb_docs WHERE issue_code = 'USE-999'"));

        // 양성 대조군 — 승인자가 있으면 같은 INSERT 가 통과한다(제약이 늘 막는 게 아니라는 증거)
        await db.ExecuteAsync(@"
            INSERT INTO bo_kb_docs (issue_code, category, title, body_md, status, version,
                                    approved_by, approved_at, created_by)
            VALUES ('USE-998', 'use', '정상', '본문', '승인', 1, 'cs-1', UTC_TIMESTAMP(6), 'cs-1')");
        Assert.Equal(1, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bo_kb_docs WHERE issue_code = 'USE-998'"));
    }

    // ── G-C-3 ────────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-3 🔴 식별정보 품은 본문 등재 = 거부 + 사유코드 로그 · 양성 대조군 = 깨끗한 본문은 통과")]
    public async Task G_C_3_승인전_재스캔()
    {
        if (!await _h.TrySetUpAsync("G-C-3")) return;
        var kb = Kb("cs-1");

        // 음성 — 사업자번호 모양(값은 전부 0 — 모양만 맞춘 더미)
        var bad = await kb.Create(new KbController.DocRequest
        {
            Category = "dat", Title = "숫자 안 맞음", BodyMd = "고객 사업자번호 000-00-00000 으로 조회했습니다",
            Status = "승인",
        }, default);
        Assert.IsType<BadRequestObjectResult>(bad);

        await using var db = await _h.OpenAsync();
        Assert.Equal("biz_no", await db.ExecuteScalarAsync<string>(
            "SELECT rule_code FROM bo_kb_doc_logs WHERE action = 'reject' ORDER BY id DESC LIMIT 1"));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_kb_docs"));

        // 양성 대조군 — 그 줄을 지우면 같은 요청이 통과한다(재스캔을 빼면 위가 통과해 버린다)
        var ok = await kb.Create(new KbController.DocRequest
        {
            Category = "dat", Title = "숫자 안 맞음", BodyMd = "재고 현황과 장부 숫자가 다릅니다",
            Status = "승인",
        }, default);
        Assert.IsType<OkObjectResult>(ok);
        Assert.Contains("DAT-1", Json(ok));      // 이슈코드 발급 규칙(분류 머리글 + 번호)
        var row = await db.QueryFirstAsync("SELECT status AS S, approved_by AS A FROM bo_kb_docs LIMIT 1");
        Assert.Equal("승인", (string)row.S);
        Assert.Equal("cs-1", (string)row.A);     // 쓴 사람이 승인자
    }

    // ── G-C-4 ────────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-4 🔴 고침은 version+1 새 행 — 옛 판이 그대로 남는다(UPDATE 0건)")]
    public async Task G_C_4_이력_보존()
    {
        if (!await _h.TrySetUpAsync("G-C-4")) return;
        var kb = Kb("cs-1");
        Assert.IsType<OkObjectResult>(await kb.Create(new KbController.DocRequest
        {
            Category = "net", Title = "접속이 끊깁니다", BodyMd = "첫 판 본문", Status = "승인",
        }, default));

        Assert.IsType<OkObjectResult>(await kb.Revise("NET-1", new KbController.DocRequest
        {
            Title = "접속이 끊깁니다(보강)", BodyMd = "둘째 판 본문", Status = "승인",
        }, default));

        await using var db = await _h.OpenAsync();
        var vers = (await db.QueryAsync<int>(
            "SELECT version FROM bo_kb_docs WHERE issue_code = 'NET-1' ORDER BY version")).ToList();
        Assert.Equal(new[] { 1, 2 }, vers);
        Assert.Equal("첫 판 본문", await db.ExecuteScalarAsync<string>(
            "SELECT body_md FROM bo_kb_docs WHERE issue_code = 'NET-1' AND version = 1"));
    }

    // ── G-C-5 ────────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-5 🔴 재사용 재료는 승인 판만 — 초안·폐기는 WHERE 절에서 빠진다(if 분기 아님)")]
    public async Task G_C_5_승인판만_재료()
    {
        if (!await _h.TrySetUpAsync("G-C-5")) return;
        var kb = Kb("cs-1");
        await kb.Create(new KbController.DocRequest
        { Category = "use", Title = "승인된 것", BodyMd = "승인 본문", Status = "승인" }, default);
        await kb.Create(new KbController.DocRequest
        { Category = "use", Title = "초안인 것", BodyMd = "초안 본문", Status = "초안" }, default);

        var json = Json(await kb.Reference(null, default));
        Assert.Contains("승인된 것", json);
        Assert.DoesNotContain("초안인 것", json);

        // 폐기 판으로 내리면 재료에서 빠진다
        await kb.Revise("USE-1", new KbController.DocRequest
        { Title = "승인된 것", BodyMd = "더 이상 맞지 않습니다", Status = "폐기" }, default);
        var after = Json(await kb.Reference(null, default));
        Assert.DoesNotContain("승인된 것", after);

        // 내보내기도 같은 규칙을 쓴다(미러에 초안이 섞이면 안 된다)
        Assert.DoesNotContain("초안인 것", Json(await kb.Export(default)));
    }

    // ── G-C-6 ────────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-6 🔴 외부 AI 잠금장치 fail-closed — 동의 0건·표 부재 = 닫힘(음성) · 1행 = 열림(양성)")]
    public async Task G_C_6_AI잠금_failclosed()
    {
        if (!await _h.TrySetUpAsync("G-C-6")) return;
        var gate = new BoExternalAiGate(Config(), NullLogger<BoExternalAiGate>.Instance);
        var tenantId = Guid.NewGuid().ToString();

        // 음성 ① — 동의 기록 0건
        Assert.False(await gate.IsOpenAsync(tenantId));
        // 음성 ② — 테넌트 식별이 비면
        Assert.False(await gate.IsOpenAsync(""));

        // 양성 — 동의 행이 생기면 열린다(늘 닫혀 있는 장치가 아니라는 증거)
        await using (var db = await _h.OpenAsync())
        {
            await db.ExecuteAsync(
                "INSERT INTO bo_ai_export_consents (tenant_id, granted_by) VALUES (@T, 'hq-1')",
                new { T = tenantId });
        }
        Assert.True(await gate.IsOpenAsync(tenantId));

        // 음성 ③ — 다른 테넌트의 동의는 내 문을 열지 않는다
        Assert.False(await gate.IsOpenAsync(Guid.NewGuid().ToString()));

        // 음성 ④ — 표 자체가 없으면(조회 예외) 닫힘. 조용히 여는 쪽이 아니다.
        await using (var db = await _h.OpenAsync())
        {
            await db.ExecuteAsync("DROP TABLE bo_ai_export_consents");
        }
        Assert.False(await gate.IsOpenAsync(tenantId));
    }

    // ── G-C-7 (DB 없이도 돈다 — 레포 실물 검사) ──────────────────────
    [Fact(DisplayName = "G-C-7 🔴 8폴더·이슈코드 머리글·문제점/해결점 PRD 틀이 레포에 실재한다")]
    public void G_C_7_8폴더_이슈코드_PRD틀()
    {
        var root = RepoRoot();
        var baseDir = Path.Combine(root, "docs", "CS", "누리집");
        Assert.True(Directory.Exists(baseDir), $"누리집 폴더가 없다: {baseDir}");

        // 8폴더 — 이름·개수 그대로(백오피스 CategoryFolder 와 글자까지 같아야 한다)
        var folders = new[] { "1.사용", "2.설정", "3.통신", "4.데이터", "5.업데이트", "6.기능오류", "7.설치", "8.기타" };
        foreach (var f in folders)
            Assert.True(Directory.Exists(Path.Combine(baseDir, f)), $"8폴더 중 하나가 없다: {f}");
        Assert.Equal(8, folders.Length);

        // 이슈코드 머리글 8개가 폴더 README 에 적혀 있다
        var codes = new[] { "USE", "SET", "NET", "DAT", "UPD", "BUG", "INS", "ETC" };
        for (int i = 0; i < folders.Length; i++)
        {
            var readme = Path.Combine(baseDir, folders[i], "README.md");
            Assert.True(File.Exists(readme), $"폴더 안내가 없다: {readme}");
            Assert.Contains(codes[i], File.ReadAllText(readme));
        }

        // PRD 틀 — 문제점/해결점 두 꼭지가 반드시 있다(틀이 틀이 아니면 문서가 제각각이 된다)
        var tpl = Path.Combine(baseDir, "_틀_문제점해결점_PRD.md");
        Assert.True(File.Exists(tpl), "문제점/해결점 PRD 틀이 없다");
        var text = File.ReadAllText(tpl);
        Assert.Contains("## 문제점", text);
        Assert.Contains("## 해결점", text);
    }

    // ── G-C-8 ────────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-8 🔴 분류 다시 고르기 — 8종 밖 거부(음성) · 안쪽 전이는 기록 행 실존 · 배정·약속도 기록")]
    public async Task G_C_8_분류_배정_약속()
    {
        if (!await _h.TrySetUpAsync("G-C-8")) return;
        var (ticketId, _, _) = await SeedTicketAsync();
        var cs = Cs("cs-1");

        // 음성 — 8종 밖
        Assert.IsType<BadRequestObjectResult>(
            await cs.Recategorize(ticketId, new CsAdminController.CategoryRequest { To = "돈" }, default));

        // 양성 — 안쪽 값은 바뀌고 기록이 남는다
        Assert.IsType<OkObjectResult>(
            await cs.Recategorize(ticketId, new CsAdminController.CategoryRequest { To = "bug" }, default));

        await using var db = await _h.OpenAsync();
        Assert.Equal("bug", await db.ExecuteScalarAsync<string>(
            "SELECT category FROM bo_cs_tickets WHERE id = @Id", new { Id = ticketId }));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bo_cs_ticket_logs WHERE ticket_id = @Id AND to_status = '분류:bug'",
            new { Id = ticketId }));

        // 배정 — 내 응대함(누가 잡았나)
        Assert.IsType<OkObjectResult>(
            await cs.Assign(ticketId, new CsAdminController.AssignRequest { HandlerType = "본사2차" }, default));
        Assert.Equal("cs-1", await db.ExecuteScalarAsync<string>(
            "SELECT handler_id FROM bo_cs_tickets WHERE id = @Id", new { Id = ticketId }));
        Assert.IsType<BadRequestObjectResult>(
            await cs.Assign(ticketId, new CsAdminController.AssignRequest { HandlerType = "아무개" }, default));

        // 약속 시계 — 서버 제안값이 실제로 칸에 들어간다
        Assert.IsType<OkObjectResult>(
            await cs.SetPromise(ticketId, new CsAdminController.PromiseRequest(), default));
        Assert.NotNull(await db.ExecuteScalarAsync<DateTime?>(
            "SELECT promised_at FROM bo_cs_tickets WHERE id = @Id", new { Id = ticketId }));
    }

    // ── G-C-9 ────────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-9 🔴 전화 접수 — 식별정보 모양 거부(음성) · 깨끗한 통화는 1행 + 담당·약속이 함께 박힌다")]
    public async Task G_C_9_전화접수()
    {
        if (!await _h.TrySetUpAsync("G-C-9")) return;
        var (_, _, tenantCode) = await SeedTicketAsync();
        var cs = Cs("cs-1");

        // 음성 — 받아 적다가 주민번호 모양이 들어간 경우(값은 전부 0 — 모양만)
        Assert.IsType<BadRequestObjectResult>(await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = tenantCode, Category = "use", SubTag = "etc",
            Body = "확인차 000000-0000000 불러 주셨습니다",
        }, default));

        // 음성 — 없는 고객사
        Assert.IsType<NotFoundObjectResult>(await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = "NOPE000", Category = "use", SubTag = "etc", Body = "전화 왔습니다",
        }, default));

        // 양성 — 깨끗한 통화는 접수된다
        Assert.IsType<OkObjectResult>(await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = tenantCode, Category = "use", SubTag = "estimate_sales",
            Body = "수주 화면에서 거래처가 안 보인다고 하십니다",
        }, default));

        await using var db = await _h.OpenAsync();
        var row = await db.QueryFirstAsync(@"
            SELECT received_channel AS C, handler_id AS H, promised_at AS P
              FROM bo_cs_tickets WHERE received_channel = 'phone'");
        Assert.Equal("phone", (string)row.C);
        Assert.Equal("cs-1", (string)row.H);
        Assert.NotNull(row.P);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 누리집 폴더를 읽을 수 없다.");
    }
}
