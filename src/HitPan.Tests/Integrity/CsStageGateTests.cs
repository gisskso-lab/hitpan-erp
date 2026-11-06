using System.Security.Claims;
using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 CS 진행 4단계 · 응대 유형 3종 · 만족도 게이트 (사장님 지시 2026-10-08).
///
/// <list type="bullet">
/// <item>G-C-10 4단계가 **서로 겹치지 않고 합이 전체** — 화면 아래 띠 숫자가 서로 안 맞는 사고 방지</item>
/// <item>G-C-11 읽음은 **최초 1회만** 박힌다(두 번 열어도 첫 시각 유지 — 「며칠 방치」 사실 보존)</item>
/// <item>G-C-12 완료 전이는 **응대 유형 없으면 거부**(음성) · 고르면 통과(양성) · 소급 불가 자료를 지금 받는다</item>
/// <item>G-C-13 만족도 수신 — 1~3 밖 거부 · 두 번 보내도 첫 평가 유지(멱등) · ④단계로 옮겨간다</item>
/// <item>G-C-14 한 줄 평에 식별정보 모양이면 **점수는 살리고 글만 버린다**(평가 자체가 사라지면 안 된다)</item>
/// </list>
/// </summary>
// 🔴 20261019작B2 절 A (G-P0-A-1) — 하네스가 HITPAN_BO_JWT_SECRET 전역을 쓴다. Collection 직렬화. 삭제 0(#1).
[Collection(BoSignupTenantKeyGateCollection.Name)]
public sealed class CsStageGateTests : IDisposable
{
    private readonly BackofficeRoleGateHarness _h =
        new($"hitpan_gate_csstage_{Guid.NewGuid():N}");

    public void Dispose() => _h.Dispose();

    private const string Pepper = "gate-pepper-cs-stage";
    private const string GoodLicense = "GATE-CS-STAGE-0001";
    private const string TenantCode = "GATESTG";

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = _h.DbConnString(),
            ["License:Pepper"] = Pepper,
        }).Build();

    private CsAdminController Cs(string actor) => new(Config(), NullLogger<CsAdminController>.Instance, NullLoggerFactory.Instance)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", actor) }, "gate")),
            },
        },
    };

    private CsInboundController Inbound() => new(Config(), NullLogger<CsInboundController>.Instance)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private static string Hmac(string license)
    {
        using var h = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(Pepper));
        var normalized = license.Trim().ToUpperInvariant().Replace(" ", "");   // 컨트롤러와 같은 정규화
        return Convert.ToHexString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    /// <summary>테넌트 1개 + 티켓 n개. 티켓은 전부 「안 읽음 · 접수」 상태로 시작한다.</summary>
    private async Task<(string TenantId, List<(long Id, string Uid)> Tickets)> SeedAsync(int count)
    {
        var tenantId = Guid.NewGuid().ToString();
        await using var db = await _h.OpenAsync();
        await db.ExecuteAsync(@"
            INSERT INTO tenants (tenant_id, tenant_code, company_name, license_key_hash, owner_account_id)
            VALUES (@Id, @Code, '게이트상사', @Hash, 'owner@gate.test')",
            new { Id = tenantId, Code = TenantCode, Hash = Hmac(GoodLicense) });

        var list = new List<(long, string)>();
        for (int i = 0; i < count; i++)
        {
            var uid = Guid.NewGuid().ToString();
            var id = await db.ExecuteScalarAsync<long>(@"
                INSERT INTO bo_cs_tickets (tenant_id, client_ticket_uid, received_channel, category, sub_tag, body)
                VALUES (@T, @Uid, 'erp_message', 'use', 'etc', '저장이 안 됩니다');
                SELECT LAST_INSERT_ID();",
                new { T = tenantId, Uid = uid });
            list.Add((id, uid));
        }
        return (tenantId, list);
    }

    private static JsonElement Envelope(string uid, int rating, string? comment, string license = GoodLicense)
    {
        var data = comment is null
            ? $"{{\"csRequestId\":\"{uid}\",\"rating\":{rating}}}"
            : $"{{\"csRequestId\":\"{uid}\",\"rating\":{rating},\"comment\":\"{comment}\"}}";
        var json = $"{{\"tenantCode\":\"{TenantCode}\",\"licenseKey\":\"{license}\"," +
                   $"\"ownerAccountId\":\"owner@gate.test\",\"data\":{data}}}";
        return JsonDocument.Parse(json).RootElement;
    }

    private static long Num(object v) => Convert.ToInt64(v);

    // ── G-C-10 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-10 🔴 4단계가 서로 겹치지 않고 합이 전체 — 아래 띠 숫자가 어긋나지 않는다")]
    public async Task G_C_10_4단계_상호배타()
    {
        if (!await _h.TrySetUpAsync("G-C-10")) return;
        var (_, t) = await SeedAsync(4);
        var cs = Cs("cs-1");

        // 시작: 4건 전부 ① 안 읽음
        var c0 = (OkObjectResult)await cs.Counts(default);
        var j0 = JsonSerializer.Serialize(c0.Value);
        Assert.Contains("\"unread\":4", j0);
        Assert.Contains("\"total\":4", j0);

        // ②로 하나 — 상세를 열면 읽음이 박힌다
        await cs.Detail(t[0].Id, default);

        // ③으로 하나 — 완료
        // 🔴 20261013작2 — 여기 있던 `SetHandledVia` 한 줄을 **뺐다.** 이 건들은 쪽지 건(`erp_message`)이고
        //   응대유형이 고정으로 바뀌어 그 호출은 이제 **400 으로 거부된다.** 반환값을 안 보던 줄이라
        //   남겨 두면 「거부된 호출에 기대어 도는 시험」이 된다(조용한 초록의 씨앗).
        //   완료 전이는 쪽지 건이라 **묻지 않고 통과**하므로 단계 셈(이 게이트의 과녁)은 그대로다.
        await cs.ChangeStatus(t[1].Id, new CsAdminController.StatusRequest { To = "완료" }, default);

        // ④로 하나 — 완료 + 평가
        await cs.ChangeStatus(t[2].Id, new CsAdminController.StatusRequest { To = "완료" }, default);
        await using (var db = await _h.OpenAsync())
        {
            await db.ExecuteAsync(
                "UPDATE bo_cs_tickets SET rating = 3, rated_at = UTC_TIMESTAMP(6) WHERE id = @Id",
                new { Id = t[2].Id });
        }

        var c1 = (OkObjectResult)await cs.Counts(default);
        var d = JsonSerializer.SerializeToElement(c1.Value);
        long unread = Num(d.GetProperty("unread").GetInt64());
        long open = Num(d.GetProperty("open").GetInt64());
        long done = Num(d.GetProperty("done").GetInt64());
        long rated = Num(d.GetProperty("rated").GetInt64());
        long total = Num(d.GetProperty("total").GetInt64());

        // 🔴 핵심: 네 숫자의 합이 전체와 같다(겹치면 합이 커지고, 빠뜨리면 작아진다)
        Assert.Equal(total, unread + open + done + rated);
        Assert.Equal(4, total);
        Assert.Equal(1, unread);   // 손대지 않은 1건
        Assert.Equal(1, open);     // 읽기만 한 1건
        Assert.Equal(1, done);     // 완료·평가 전
        Assert.Equal(1, rated);    // 평가까지 끝난 1건
    }

    // ── G-C-11 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-11 🔴 읽음은 최초 1회만 — 두 번 열어도 첫 시각·첫 사람이 유지된다")]
    public async Task G_C_11_읽음_덮어쓰기_금지()
    {
        if (!await _h.TrySetUpAsync("G-C-11")) return;
        var (_, t) = await SeedAsync(1);

        await Cs("cs-first").Detail(t[0].Id, default);
        await using var db = await _h.OpenAsync();
        var first = await db.QueryFirstAsync(
            "SELECT read_at AS A, read_by AS B FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id });
        Assert.NotNull(first.A);
        Assert.Equal("cs-first", (string)first.B);

        // 다른 사람이 다시 열어도 처음 본 사람·시각은 바뀌지 않는다
        await Cs("cs-second").Detail(t[0].Id, default);
        var again = await db.QueryFirstAsync(
            "SELECT read_at AS A, read_by AS B FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id });
        Assert.Equal((DateTime)first.A, (DateTime)again.A);
        Assert.Equal("cs-first", (string)again.B);
    }

    // ── G-C-12 ───────────────────────────────────────────────────────
    //
    // 🔴 **2026-10-13 작2 — 이 게이트가 무는 규칙이 사장님 결재로 갈렸다.**
    //   종전 규칙(10/08): 완료로 넘기려면 **모든 건이** 응대 유형을 먼저 가져야 한다.
    //   새 규칙(10/13 실측 반려 2)·3)): 「응대유형은 **쪽지 건은 「히트판a/s센터」로 고정**,
    //   손으로 적은 건만 받는다」 ⇒ 쪽지 건에 사람 손을 요구한 것이 반려 4)「상태변경이 잘 안 됨」의 범인이었다.
    //
    //   🔴 **음성 대조군을 버리지 않았다 — 수기 건으로 옮겼다.** 「규칙이 풀렸나」를 재는 축은 그대로 살아 있고,
    //   재는 대상이 「모든 건」에서 「손으로 적은 건」으로 좁혀졌을 뿐이다.
    //   양성도 둘로 늘었다: ⓐ 쪽지 건은 **묻지 않고 통과**(새 규칙) ⓑ 수기 건은 **고르면 통과**(옛 규칙 보존).
    //   ⇒ 게이트를 코드에 맞춰 느슨하게 만든 것이 아니라, **단언이 늘었다**(2 → 5).
    //
    [Fact(DisplayName = "G-C-12 🔴 완료 전이 — 손으로 적은 건은 응대 유형 필수(음성) · 쪽지 건은 묻지 않는다(양성)")]
    public async Task G_C_12_완료전이_응대유형_필수()
    {
        if (!await _h.TrySetUpAsync("G-C-12")) return;
        var (_, t) = await SeedAsync(1);
        var cs = Cs("cs-1");
        await using var db = await _h.OpenAsync();

        // ── ⓐ 🟢 양성(새 규칙) — **쪽지 건**은 응대 유형 없이도 완료가 된다
        Assert.IsType<OkObjectResult>(
            await cs.ChangeStatus(t[0].Id, new CsAdminController.StatusRequest { To = "완료" }, default));

        var erpRow = await db.QueryFirstAsync(@"
            SELECT status AS S, handled_via AS V, completed_at AS C, read_at AS R
              FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id });
        Assert.Equal("완료", (string)erpRow.S);
        // 🔴 서버가 그 자리에서 고정값을 앉힌다 — 화면이 「히트판 A/S센터」라 적는 것과 자료가 맞아야 한다
        Assert.Equal("as_center", (string)erpRow.V);
        Assert.NotNull(erpRow.C);
        Assert.NotNull(erpRow.R);   // 완료된 건이 「안 읽음」으로 남아 단계가 꼬이지 않는다

        // ── ⓑ 🔴 음성(옛 규칙 보존) — **손으로 적은 건**은 응대 유형이 없으면 완료 거부
        var manualId = await SeedManualTicketAsync();

        Assert.IsType<BadRequestObjectResult>(
            await cs.ChangeStatus(manualId, new CsAdminController.StatusRequest { To = "완료" }, default));
        Assert.Equal("접수", await db.ExecuteScalarAsync<string>(
            "SELECT status FROM bo_cs_tickets WHERE id = @Id", new { Id = manualId }));
        // 🔴 거부한 건에 칸을 몰래 채우지도 않았나(쪽지 가지가 수기 건으로 새면 이 줄이 잡는다)
        Assert.Null(await db.ExecuteScalarAsync<string>(
            "SELECT handled_via FROM bo_cs_tickets WHERE id = @Id", new { Id = manualId }));

        // ── ⓒ 🔴 음성 — 목록 밖 응대 유형은 수기 건에서도 거부
        Assert.IsType<BadRequestObjectResult>(
            await cs.SetHandledVia(manualId, new CsAdminController.HandledViaRequest { Via = "카톡" }, default));

        // ── ⓓ 🔴 음성 — 쪽지 건에는 응대 유형을 **꽂을 수 없다**(고정이다)
        Assert.IsType<BadRequestObjectResult>(
            await cs.SetHandledVia(t[0].Id, new CsAdminController.HandledViaRequest { Via = "phone" }, default));

        // ── ⓔ 🟢 양성(옛 규칙 보존) — 수기 건은 고르면 완료가 된다
        Assert.IsType<OkObjectResult>(
            await cs.SetHandledVia(manualId, new CsAdminController.HandledViaRequest { Via = "remote" }, default));
        Assert.IsType<OkObjectResult>(
            await cs.ChangeStatus(manualId, new CsAdminController.StatusRequest { To = "완료" }, default));

        var manualRow = await db.QueryFirstAsync(@"
            SELECT status AS S, handled_via AS V, completed_at AS C
              FROM bo_cs_tickets WHERE id = @Id", new { Id = manualId });
        Assert.Equal("완료", (string)manualRow.S);
        Assert.Equal("remote", (string)manualRow.V);
        Assert.NotNull(manualRow.C);
    }

    /// <summary>
    /// 🔴 20261013작2 — **손으로 적은 A/S** 한 건(경로가 `erp_message` 가 아니다).
    /// 이 파일의 <c>SeedAsync</c> 는 쪽지 건만 만들어서, 갈라진 규칙의 음성 대조군을 세울 수 없었다.
    /// </summary>
    private async Task<long> SeedManualTicketAsync()
    {
        await using var db = await _h.OpenAsync();
        return await db.ExecuteScalarAsync<long>(@"
            INSERT INTO bo_cs_tickets (tenant_id, client_ticket_uid, received_channel, category, sub_tag, body)
            SELECT tenant_id, @Uid, 'phone', 'use', 'etc', '전화로 받은 A/S 입니다'
              FROM bo_cs_tickets WHERE received_channel = 'erp_message' ORDER BY id LIMIT 1;
            SELECT LAST_INSERT_ID();",
            // 🔴 `client_ticket_uid` 는 varchar(36) — 접두어를 붙이면 39자가 되어 STRICT 에서 잘린다(실측).
            new { Uid = Guid.NewGuid().ToString() });
    }

    // ── G-C-13 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-13 🔴 만족도 수신 — 1~3 밖 거부 · 두 번 보내도 첫 평가 유지 · ④단계로 옮겨간다")]
    public async Task G_C_13_만족도_수신()
    {
        if (!await _h.TrySetUpAsync("G-C-13")) return;
        var (_, t) = await SeedAsync(1);
        var inbound = Inbound();

        // 음성 — 범위 밖 점수
        Assert.IsType<BadRequestObjectResult>(
            await inbound.ReceiveRating(Envelope(t[0].Uid, 7, null), default));
        // 음성 — 라이선스 한 축이 틀리면 인증 거부(쪽지와 같은 문)
        Assert.IsType<UnauthorizedObjectResult>(
            await inbound.ReceiveRating(Envelope(t[0].Uid, 3, null, "WRONG-LICENSE"), default));

        // 양성 — 1~3 이면 들어온다
        Assert.IsType<OkObjectResult>(
            await inbound.ReceiveRating(Envelope(t[0].Uid, 3, "빠르게 해결해 주셨어요"), default));

        await using var db = await _h.OpenAsync();
        var row = await db.QueryFirstAsync(@"
            SELECT rating AS R, rated_at AS A, rating_comment AS C
              FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id });
        Assert.Equal(3, (int)(sbyte)row.R);
        Assert.NotNull(row.A);
        Assert.Equal("빠르게 해결해 주셨어요", (string)row.C);

        // 멱등 — 고객이 두 번 눌러도 첫 평가가 정본(점수가 2로 바뀌지 않는다)
        Assert.IsType<OkObjectResult>(
            await inbound.ReceiveRating(Envelope(t[0].Uid, 1, "다시 생각해보니 아쉬워요"), default));
        Assert.Equal(3, (int)(sbyte)await db.ExecuteScalarAsync<sbyte>(
            "SELECT rating FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id }));

        // ④ 단계로 세어진다
        var c = (OkObjectResult)await Cs("cs-1").Counts(default);
        Assert.Contains("\"rated\":1", JsonSerializer.Serialize(c.Value));
    }

    // ── G-C-14 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-14 🔴 한 줄 평에 식별정보 모양 — 점수는 살리고 글만 버린다(평가가 사라지면 안 된다)")]
    public async Task G_C_14_평가_한줄평_금지필드()
    {
        if (!await _h.TrySetUpAsync("G-C-14")) return;
        var (_, t) = await SeedAsync(1);

        // 모양만 맞춘 더미(값은 전부 0)
        Assert.IsType<OkObjectResult>(
            await Inbound().ReceiveRating(Envelope(t[0].Uid, 2, "제 번호 000000-0000000 로 확인했습니다"), default));

        await using var db = await _h.OpenAsync();
        var row = await db.QueryFirstAsync(@"
            SELECT rating AS R, rated_at AS A, rating_comment AS C
              FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id });
        Assert.Equal(2, (int)(sbyte)row.R);     // 점수는 살았다
        Assert.NotNull(row.A);
        Assert.Null(row.C);                     // 글은 버렸다(값이 저장되지 않는다)

        // 거부 기록은 남는다(사유코드만)
        Assert.True(await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bo_cs_reject_logs WHERE rule_code = 'forbidden_field'") >= 1);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 🔴 20261107작 갈래 B §7-1 — 아래 표시줄 고객사 거름 (G-C-15 ~ G-C-20)
    //   새 입구 `CountsForTenant` · 목록 `List(..., tenant)` 를 **제품 메서드 그대로** 불러서 잰다.
    //   대조군은 **시드 상수(기대 숫자)** 에 묶는다(3V-B4) — 두 입구를 서로만 견주면 둘 다 거름을 빼도 같다.
    //   시드 이름·코드·값은 전부 가짜(C4) · 고객사마다 다른 가짜 코드(3V-B5).
    // ══════════════════════════════════════════════════════════════════════

    private async Task<string> SeedBarTenantAsync(string code, string name)
    {
        var tenantId = Guid.NewGuid().ToString();
        await using var db = await _h.OpenAsync();
        await db.ExecuteAsync(@"
            INSERT INTO tenants (tenant_id, tenant_code, company_name, license_key_hash, owner_account_id)
            VALUES (@Id, @Code, @Name, @Hash, @Owner)",
            new { Id = tenantId, Code = code, Name = name, Hash = Hmac("GATE-CS-BAR-" + code), Owner = $"owner-{code.ToLowerInvariant()}@gate.test" });
        return tenantId;
    }

    /// <summary>한 고객사에 단계별 쪽지를 정해진 수만큼 넣는다(단계 식과 같은 칸만 쓴다).</summary>
    private async Task SeedBarTicketsAsync(string tenantId, int unread, int open, int done, int rated)
    {
        await using var db = await _h.OpenAsync();
        async Task Put(string status, bool read, bool isRated)
        {
            await db.ExecuteAsync(@"
                INSERT INTO bo_cs_tickets (tenant_id, client_ticket_uid, received_channel, category, sub_tag, body,
                                           status, read_at, rating, rated_at)
                VALUES (@T, @Uid, 'erp_message', 'use', 'etc', '가짜 본문',
                        @Status, @ReadAt, @Rating, @RatedAt)",
                new
                {
                    T = tenantId, Uid = Guid.NewGuid().ToString(), Status = status,
                    ReadAt = read ? DateTime.UtcNow : (DateTime?)null,
                    Rating = isRated ? 3 : (int?)null,
                    RatedAt = isRated ? DateTime.UtcNow : (DateTime?)null,
                });
        }
        for (int i = 0; i < unread; i++) await Put("접수", false, false);
        for (int i = 0; i < open; i++) await Put("접수", true, false);
        for (int i = 0; i < done; i++) await Put("완료", true, false);
        for (int i = 0; i < rated; i++) await Put("완료", true, true);
    }

    private static JsonElement BarJson(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    private static int BarCode(IActionResult r) => r switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => -1,
    };

    private static async Task<int> BarListCount(CsAdminController cs, string? stage, string? tenant, string? q = null)
    {
        var res = await cs.List(null, q, false, stage, default, tenant);
        Assert.Equal(200, BarCode(res));
        return BarJson(res).GetProperty("data").GetArrayLength();
    }

    private static long BarNum(JsonElement j, string name) => j.GetProperty(name).GetInt64();

    // 시드 상수 — G-C-15 · G-C-16 (고객사 X 와 Y 가 단계마다 **갈리는 값**을 갖도록 골랐다)
    private const int XUnread = 2, XOpen = 1, XDone = 1, XRated = 1, XTotal = 5;
    private const int YUnread = 3, YTotal = 3;

    // ── G-C-15 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-15 🔴 고객사 숫자 = 그 고객사 목록 줄 수 = 시드 상수 (+대조: 전체 숫자는 다르다)")]
    public async Task G_C_15_고객사숫자_목록_시드상수()
    {
        if (!await _h.TrySetUpAsync("G-C-15")) return;
        var x = await SeedBarTenantAsync("GBARX15", "게이트가짜엑스");
        var y = await SeedBarTenantAsync("GBARY15", "게이트가짜와이");
        await SeedBarTicketsAsync(x, XUnread, XOpen, XDone, XRated);
        await SeedBarTicketsAsync(y, YUnread, 0, 0, 0);
        var cs = Cs("cs-bar");

        // 🔴 대조군 먼저 — 시드가 「갈리는 값」을 가졌나(전체 ≠ X). 거름 없는 구현이면 아래 X 단언이 빨강.
        var all = BarJson(await cs.Counts(default));
        Assert.Equal(XTotal + YTotal, BarNum(all, "total"));
        Assert.Equal(XUnread + YUnread, BarNum(all, "unread"));
        Assert.NotEqual(XTotal, BarNum(all, "total"));
        Assert.NotEqual(XUnread, BarNum(all, "unread"));

        var res = await cs.CountsForTenant(x, default);
        Assert.Equal(200, BarCode(res));
        var j = BarJson(res);
        Assert.True(j.GetProperty("found").GetBoolean());
        Assert.Equal("게이트가짜엑스", j.GetProperty("companyName").GetString());
        // 입구가 돌려준 값을 **시드 상수**에 묶는다
        Assert.Equal(XUnread, BarNum(j, "unread"));
        Assert.Equal(XOpen, BarNum(j, "open"));
        Assert.Equal(XDone, BarNum(j, "done"));
        Assert.Equal(XRated, BarNum(j, "rated"));
        Assert.Equal(XTotal, BarNum(j, "total"));

        // 목록도 같은 시드 상수 — 단계마다
        Assert.Equal(XUnread, await BarListCount(cs, "unread", x));
        Assert.Equal(XOpen, await BarListCount(cs, "open", x));
        Assert.Equal(XDone, await BarListCount(cs, "done", x));
        Assert.Equal(XRated, await BarListCount(cs, "rated", x));
        Assert.Equal(XTotal, await BarListCount(cs, "all", x));
        Assert.Equal(XTotal, await BarListCount(cs, null, x));
    }

    // ── G-C-16 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-16 🔴 다른 고객사 쪽지가 늘어도 X 숫자·X 목록 불변 (+대조: 전체는 늘었다)")]
    public async Task G_C_16_서로안섞임()
    {
        if (!await _h.TrySetUpAsync("G-C-16")) return;
        var x = await SeedBarTenantAsync("GBARX16", "게이트가짜엑스");
        var y = await SeedBarTenantAsync("GBARY16", "게이트가짜와이");
        await SeedBarTicketsAsync(x, XUnread, XOpen, XDone, XRated);
        await SeedBarTicketsAsync(y, YUnread, 0, 0, 0);
        var cs = Cs("cs-bar");

        var before = BarNum(BarJson(await cs.Counts(default)), "total");
        await SeedBarTicketsAsync(y, 2, 0, 0, 0);
        var after = BarNum(BarJson(await cs.Counts(default)), "total");

        // 대조: 같은 시점 전체 숫자는 정말 늘었다(시드가 먹었다)
        Assert.Equal(before + 2, after);
        Assert.Equal(XTotal + YTotal + 2, after);

        var j = BarJson(await cs.CountsForTenant(x, default));
        Assert.Equal(XUnread, BarNum(j, "unread"));
        Assert.Equal(XTotal, BarNum(j, "total"));
        Assert.Equal(XTotal, await BarListCount(cs, null, x));
        Assert.Equal(XUnread, await BarListCount(cs, "unread", x));
    }

    // ── G-C-17 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-17 🔴 이름이 겹치는 고객사 — tenant 고유값 일치는 안 섞인다 (+대조: q 부분 일치는 섞인다 · % _ 무력)")]
    public async Task G_C_17_고유값일치()
    {
        if (!await _h.TrySetUpAsync("G-C-17")) return;
        const string nameA = "게이트겹침가짜";
        const string nameB = "게이트겹침가짜둘";
        var a = await SeedBarTenantAsync("GBARA17", nameA);
        var b = await SeedBarTenantAsync("GBARB17", nameB);
        await SeedBarTicketsAsync(a, 2, 0, 0, 0);
        await SeedBarTicketsAsync(b, 3, 0, 0, 0);
        var cs = Cs("cs-bar");

        // 🔴 대조군 — 같은 시드에서 부분 일치(q)는 두 고객사를 다 잡는다(부분 일치로 베낀 구현 = 아래가 빨강)
        var byQ = await BarListCount(cs, null, null, q: nameA);
        Assert.Equal(5, byQ);

        var byTenant = await BarListCount(cs, null, a);
        Assert.Equal(2, byTenant);
        Assert.True(byQ > byTenant);

        var rows = BarJson(await cs.List(null, null, false, null, default, a)).GetProperty("data");
        foreach (var r in rows.EnumerateArray())
            Assert.Equal(nameA, r.GetProperty("companyName").GetString());

        Assert.Equal(2, BarNum(BarJson(await cs.CountsForTenant(a, default)), "total"));

        // 🔴 3V-B4 — LIKE 로 베낀 구현이면 아래 칸들이 전 줄을 연다(빨강). 고유값 일치는 0줄 · found=false.
        //   「_」 한 글자는 LIKE 여도 36자 값과 안 맞으므로, 고객사값 꼴(36자)로 만든 「_」 무늬도 함께 잰다.
        var underscoreUuid = "________-____-____-____-____________";
        foreach (var pattern in new[] { "%", "_", underscoreUuid })
        {
            Assert.Equal(0, await BarListCount(cs, null, pattern));
            var p = BarJson(await cs.CountsForTenant(pattern, default));
            Assert.False(p.GetProperty("found").GetBoolean(), $"「{pattern}」 가 고객사로 잡혔다 — 부분 일치 구현");
        }
    }

    // ── G-C-18 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-18 인자 없음 = 종전 — 전체 숫자 합 = 전체 · 고객사 인자 없는/빈 목록 = 전 줄 (회귀)")]
    public async Task G_C_18_인자없음_종전()
    {
        if (!await _h.TrySetUpAsync("G-C-18")) return;
        var x = await SeedBarTenantAsync("GBARX18", "게이트가짜엑스");
        var y = await SeedBarTenantAsync("GBARY18", "게이트가짜와이");
        await SeedBarTicketsAsync(x, 1, 1, 0, 0);
        await SeedBarTicketsAsync(y, 0, 0, 1, 1);
        var cs = Cs("cs-bar");

        var all = BarJson(await cs.Counts(default));
        Assert.Equal(4, BarNum(all, "total"));
        Assert.Equal(BarNum(all, "total"),
            BarNum(all, "unread") + BarNum(all, "open") + BarNum(all, "done") + BarNum(all, "rated"));

        Assert.Equal(4, await BarListCount(cs, null, null));
        Assert.Equal(4, await BarListCount(cs, null, ""));
        Assert.Equal(4, await BarListCount(cs, null, "   "));

        // 기존 직접 호출 꼴(인자 5개)도 그대로 전 줄
        var legacy = await cs.List(null, null, false, null, default);
        Assert.Equal(4, BarJson(legacy).GetProperty("data").GetArrayLength());
    }

    // ── G-C-19 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-19 🔴 없는 고객사 = found=false · 숫자 칸 없음 · 빈/공백/36자 초과 = 400 (+대조: 있는 고객사 0건 = 숫자 0 있음)")]
    public async Task G_C_19_없는고객사_값검사()
    {
        if (!await _h.TrySetUpAsync("G-C-19")) return;
        var empty = await SeedBarTenantAsync("GBARE19", "게이트가짜빈곳");
        var cs = Cs("cs-bar");
        var numberFields = new[] { "unread", "open", "done", "rated", "total" };

        // 없는 고객사 — 숫자 칸 자체가 없어야 한다(0 으로 채우면 「0건」 으로 읽힌다)
        var none = await cs.CountsForTenant(Guid.NewGuid().ToString(), default);
        Assert.Equal(200, BarCode(none));
        var jn = BarJson(none);
        Assert.False(jn.GetProperty("found").GetBoolean());
        foreach (var f in numberFields)
            Assert.False(jn.TryGetProperty(f, out _), $"없는 고객사 응답에 숫자 칸 「{f}」 가 있다 — 0 채우기");

        // 🔴 대조군(3V-B5) — 있는 고객사 · 쪽지 0건 = found=true · 숫자 칸 **있음** · 0
        var je = BarJson(await cs.CountsForTenant(empty, default));
        Assert.True(je.GetProperty("found").GetBoolean());
        foreach (var f in numberFields)
            Assert.Equal(0, BarNum(je, f));

        // 값 검사
        Assert.Equal(400, BarCode(await cs.CountsForTenant(null, default)));
        Assert.Equal(400, BarCode(await cs.CountsForTenant("", default)));
        Assert.Equal(400, BarCode(await cs.CountsForTenant("   ", default)));
        Assert.Equal(400, BarCode(await cs.CountsForTenant(new string('a', 37), default)));
        Assert.Equal(200, BarCode(await cs.CountsForTenant(new string('a', 36), default)));   // 경계 — 36자는 받는다
    }

    // ── G-C-20 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-20 🔴 새 입구 CountsForTenant 는 PlatformAdmin 정책 아래 · AllowAnonymous 0 (+음성 대조)")]
    public void G_C_20_새입구_인가()
    {
        var t = typeof(CsAdminController);
        var policy = t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
                      .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                      .Select(a => a.Policy).ToList();
        Assert.Contains("PlatformAdmin", policy);

        var m = t.GetMethod(nameof(CsAdminController.CountsForTenant));
        Assert.NotNull(m);
        Assert.Empty(m!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false));
        var route = m.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>().Select(a => a.Template).ToList();
        Assert.Contains("counts/tenant", route);

        // 🔴 음성 대조군 — 익명 허용 타입을 같은 식으로 재면 정책이 **없음**으로 나와야 한다(늘 통과하는 검사가 아니다)
        var anonPolicies = typeof(CsInboundController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Select(a => a.Policy).ToList();
        Assert.DoesNotContain("PlatformAdmin", anonPolicies);
    }
}
