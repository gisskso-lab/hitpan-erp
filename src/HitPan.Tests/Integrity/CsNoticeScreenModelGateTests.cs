using System.Security.Claims;
using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using HitPan.Backoffice.Components.Pages.Platform.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261013작1 갈래③ — <b>G-W7-6 : 「히트판공지 전송」 목록을 화면 모델로 읽을 수 있나.</b>
///
/// <para><b>왜 이 게이트가 있나</b> — 2026-10-12 사고의 모양 그대로다. 선발신 쪽지 한 건이
/// <c>attempt_count</c> NULL 을 들고 왔고, 화면 모델의 <b>비널러블 int</b> 가 그 NULL 을 못 받아
/// 역직렬화가 터졌다. <b>쪽지 한 건이 목록 전부를 가렸다.</b> 그때 통과하고 있던 게이트는
/// 컨트롤러를 바로 불러 결과를 <b>사전(dictionary)</b>으로 읽었다 — 화면이 실제로 지나는
/// 「JSON → 모델」 경로를 아무도 지나가지 않았다.</para>
///
/// <para>⇒ 그래서 이 게이트는 실물 <see cref="CsAdminController.ListOutbound"/> 를 부른 뒤
/// 응답을 <b>직렬화하고, 화면이 쓰는 그 형</b>(<see cref="CsOutboundRow"/>)으로 되돌려 읽는다.
/// 그 형은 베낀 모양이 아니다 — 화면 파일을 시험 프로젝트가 <b>소스로 링크</b>해 같은 형을 본다
/// (<c>HitPan.Tests.csproj</c>). 베낀 모양으로 재면 화면이 바뀌어도 게이트는 초록이다.</para>
///
/// <para>🔴 <b>무는 약속</b>:
/// <list type="number">
/// <item>폐기된 줄(<c>body</c> NULL) · 고아 줄(<c>companyName</c> NULL · LEFT JOIN) · 제목 없는 줄을
///       <b>섞어 놓아도</b> 목록 전체가 읽힌다 — 한 건이 전부를 가리지 않는다.</item>
/// <item>상태 낱말 <b>4종 판정</b>이 맞다 — 전달 대기 · 전달됨 · 취소됨 · <b>폐기됨</b>.
///       「폐기됨」은 서버가 이미 돌려주는데 화면 모델에 칸이 없어 <b>한 번도 안 보였다</b>(PM 실측 2026-10-13).</item>
/// </list></para>
///
/// <para>🔴 <b>낱말을 세지 않는다</b>(간판·설명글 검사 금지 — 글자는 사장님이 바꿀 수 있다).
/// 여기서 비교하는 넷은 <b>판정 결과</b>이고, 그 판정은 <see cref="CsOutboundRow.StatusLabel"/>
/// 한 곳에만 있다. 그 자리가 틀리면 화면·상세·게이트가 함께 틀린다 — 그게 과녁이다.</para>
///
/// <para>DB 자리: CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 · 로컬은
/// <c>HITPAN_BO_GATE_DB</c>(이름에 <c>test</c>·<c>gate</c> 필요 · 운영 축 오폭 차단 #39) ·
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFailStrict"/> — <c>[SKIP]</c> 을 <b>찍고</b> 넘긴다(조용한 초록 금지).</para>
///
/// <para>🔴 로컬에서는 이 클래스를 <b>하나씩</b> 돌려라 — <c>HITPAN_BO_GATE_DB</c> 는 DB 가 하나인데
/// xUnit 은 클래스를 병렬로 돌린다(2026-10-11 실측).</para>
/// </summary>
public sealed class CsNoticeScreenModelGateTests
{
    private const string AdminId = "w7-6-gate-admin";

    [Fact(DisplayName = "G-W7-6 폐기·고아·제목없음 줄을 섞어도 발신 목록을 화면 모델로 읽는다 · 상태 낱말 4종 판정")]
    public async Task G_W7_6_발신목록을_화면모델로_읽는다()
    {
        const string gate = "G-W7-6 발신 목록 화면 모델";
        await using var env = await NoticeGateDb.TryCreateAsync(gate, "hitpan_bo_w7_gate_6");
        if (env is null) return;

        await env.SeedTenantAsync("W7G6", "공지 화면 상사");
        var cs = NewController(env);
        await using var db = await env.OpenAsync();

        // ══════════════════════════════════════════════════════════
        // 섞어 넣는다 — 다섯 모양. 전부 실물 컨트롤러·실물 SQL 을 지난다.
        // ══════════════════════════════════════════════════════════

        // ① 평범한 줄 — 전달 대기
        var waiting = await SendAsync(cs, gate, "W7G6", "update", "1.3.54 업데이트 안내", "오늘 밤 업데이트가 있습니다.");

        // ② 제목 없는 줄 — `title` 은 처음부터 선택 칸이다(NULL 이 들어온다)
        var noTitle = await SendAsync(cs, gate, "W7G6", "notice", null, "제목 없이 보낸 공지입니다.");
        Assert.True(await db.ExecuteScalarAsync<string?>(
            "SELECT title FROM bo_cs_outbound WHERE outbound_id = @Id", new { Id = noTitle }) is null,
            gate + " 제목 없는 줄의 title 이 NULL 이 아니다 — 전제가 성립하지 않는다.");

        // ③ 전달된 줄 — 전달됨. (전달은 ERP Pull 이 하는 일이라 여기서는 시각만 앉힌다)
        var delivered = await SendAsync(cs, gate, "W7G6", "promo", "가을 프로모션", "이번 달 할인 안내입니다.");
        await db.ExecuteAsync(
            "UPDATE bo_cs_outbound SET delivered_at = UTC_TIMESTAMP(6) WHERE outbound_id = @Id",
            new { Id = delivered });

        // ④ 취소된 줄 — 취소됨. 실물 입구를 부른다.
        var canceled = await SendAsync(cs, gate, "W7G6", "etc", "점검 안내", "점검 일정이 바뀌어 취소합니다.");
        Assert.True(StatusOf(await cs.CancelOutbound(canceled, default)) == 200,
            gate + " 취소 입구가 200 이 아니다 — 전제가 성립하지 않는다.");

        // ⑤ 🔴 폐기된 줄 — 실물 폐기 입구가 **본문을 NULL 로 비운다**(사실은 남긴다).
        var purged = await SendAsync(cs, gate, "W7G6", "event", "옛 값 이벤트", "폐기될 공지입니다.");
        Assert.True(StatusOf(await cs.PurgeOutbound(purged,
            new CsAdminController.PurgeRequest { Reason = "고객 요청으로 내용 삭제" }, default)) == 200,
            gate + " 폐기 입구가 200 이 아니다 — 전제가 성립하지 않는다.");
        Assert.True(await db.ExecuteScalarAsync<string?>(
            "SELECT body FROM bo_cs_outbound WHERE outbound_id = @Id", new { Id = purged }) is null,
            gate + " 폐기한 줄의 body 가 NULL 이 아니다 — 폐기의 뜻이 바뀌었다.");

        // ⑥ 🔴 고아 줄 — `tenants` 에 없는 고객사. LEFT JOIN 이 companyName 에 **NULL** 을 준다.
        //    실물 입구는 없는 고객사를 거부하므로(그게 맞다) 이 모양은 표에 직접 앉힌다.
        //    고객사 줄이 지워진 옛 발신이 운영에 실제로 생길 수 있는 모양이다.
        var orphan = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO bo_cs_outbound (outbound_id, tenant_id, purpose, title, body, created_by, created_at)
            VALUES (@Id, @Tid, 'billing', '지워진 고객사에게 보낸 안내', '본문', @By, UTC_TIMESTAMP(6))",
            new { Id = orphan, Tid = Guid.NewGuid().ToString(), By = AdminId });

        // ══════════════════════════════════════════════════════════
        // 🔴 과녁 — 응답을 **직렬화 → 화면 모델로 역직렬화**한다(화면이 지나는 그 경로)
        // ══════════════════════════════════════════════════════════

        var res = await cs.ListOutbound(null, default);
        Assert.True(StatusOf(res) == 200, gate + " 목록이 200 이 아니다. " + Describe(res));

        var json = JsonSerializer.Serialize(ValueOf(res));

        // 🔴 전제 확인 — NULL 이 **정말 JSON 을 타고 왔나.** 안 왔으면 아래 단언은 「없으니 통과」가 된다.
        Assert.Contains("\"companyName\":null", json);
        Assert.Contains("\"body\":null", json);
        Assert.Contains("\"title\":null", json);
        Assert.Contains("\"purgedAt\":null", json);

        var rows = ReadAsScreen(json, gate);
        Assert.True(rows.Count == 6, gate + " 목록이 " + rows.Count + "건이다(기대 6건).");

        // ── 상태 낱말 4종 판정 ──
        Assert.Equal("전달 대기", Row(rows, waiting, gate).StatusLabel);
        Assert.Equal("전달 대기", Row(rows, noTitle, gate).StatusLabel);
        Assert.Equal("전달됨", Row(rows, delivered, gate).StatusLabel);
        Assert.Equal("취소됨", Row(rows, canceled, gate).StatusLabel);
        Assert.Equal("폐기됨", Row(rows, purged, gate).StatusLabel);

        // 🔴 넷이 **모두** 나왔나 — 하나라도 안 나오면 그 낱말은 화면에 영원히 안 보이는 낱말이다.
        var words = rows.Select(r => r.StatusLabel).Distinct().OrderBy(w => w, StringComparer.Ordinal).ToList();
        Assert.True(words.Count == 4,
            gate + " 상태 낱말이 " + words.Count + "종이다(기대 4종: " + string.Join(", ", words) + ").");
        foreach (var expected in new[] { "전달 대기", "전달됨", "취소됨", "폐기됨" })
        {
            Assert.True(words.Contains(expected),
                gate + " 상태 낱말 「" + expected + "」가 한 줄도 없다 — 화면 모델이 그 축을 못 받는다. "
              + "(나온 낱말: " + string.Join(", ", words) + ")");
        }

        // ── 폐기된 줄: 본문은 비었고 사유는 남았다 ──
        var purgedRow = Row(rows, purged, gate);
        Assert.True(purgedRow.Body is null, gate + " 폐기한 줄의 본문이 화면 모델에 남아 있다.");
        Assert.True(purgedRow.PurgedAt is not null, gate + " 폐기한 줄의 PurgedAt 이 비었다 — 「폐기됨」을 못 가른다.");
        Assert.Equal("고객 요청으로 내용 삭제", purgedRow.PurgeReason);
        Assert.False(purgedRow.IsWaiting, gate + " 폐기한 줄에 「보내기 취소」가 열려 있다.");

        // ── 고아 줄: 고객사 이름이 NULL 인데도 **목록 전체가 읽혔다** ──
        var orphanRow = Row(rows, orphan, gate);
        Assert.True(orphanRow.CompanyName is null,
            gate + " 고아 줄의 CompanyName 이 NULL 이 아니다 — 전제가 성립하지 않는다.");
        Assert.True(orphanRow.OneLine.Length > 0, gate + " 고아 줄의 목록 한 줄이 비었다.");

        // ── 제목 없는 줄: 한 줄 요약이 본문으로 채워진다(빈 칸이 아니다) ──
        Assert.Contains("제목 없이 보낸 공지", Row(rows, noTitle, gate).OneLine);

        // ══════════════════════════════════════════════════════════
        // 🔴 음성 대조 — 「JSON 을 타고 왔다」가 **실제로 무는** 자리를 증명한다.
        //   날짜 칸을 비널러블로 되돌린 모양으로 같은 JSON 을 읽으면 터진다 ⇒
        //   PurgedAt·DeliveredAt·CanceledAt 의 널 허용은 **장식이 아니다.**
        // ══════════════════════════════════════════════════════════
        var boom = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<StrictDateRow[]>(ItemsOf(json),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
        Assert.True(boom.Message.Length > 0,
            gate + " 음성 대조가 비어 있다 — 이 게이트는 아무 모양이나 통과시킨다.");

        // 🔴 측정해 둔 사실(설계 §9 의 FAIL 대조 ⓐ 교정) — 참고 문자 칸을 비널러블 `string` 으로 적어도
        //   .NET 8 의 System.Text.Json 은 **터지지 않고 null 을 그대로 앉힌다**(참조형 널 주석을 안 본다).
        //   ⇒ 「비널러블 string 으로 되돌리면 역직렬화가 터진다」는 **사실이 아니다.**
        //      터지는 것은 값 형(DateTime·int)이다. 그 측정을 여기 고정해 둔다 — 런타임이 바뀌면 이 줄이 알려 준다.
        var lenient = JsonSerializer.Deserialize<StrictStringRow[]>(ItemsOf(json),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(lenient);
        Assert.Contains(lenient!, r => r.CompanyName is null);
    }

    // ══════════════════════════════════════════════════════════════
    // 읽는 자리 — 🔴 사전으로 읽지 않는다. 화면이 지나는 경로만 지난다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 응답 JSON 을 <b>화면이 쓰는 형</b>으로 되돌려 읽는다. 사전으로 읽으면 비널러블 칸의 NULL 을
    /// 영원히 못 잡는다(2026-10-12 사고의 자리). 터지면 화면에는 「공지 목록을 불러오지 못했습니다」로 보인다.
    /// </summary>
    private static List<CsOutboundRow> ReadAsScreen(string json, string gate)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<ScreenListResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(doc?.Items);
            return doc!.Items!;
        }
        catch (JsonException ex)
        {
            throw new Xunit.Sdk.XunitException(
                gate + " 🔴 화면 모델로 발신 목록을 읽지 못했다 — 화면에는 「공지 목록을 불러오지 못했습니다」로 보인다. "
              + "한 건이 목록 전부를 가린 것이다. 사유: " + ex.Message);
        }
    }

    /// <summary>음성 대조용 — 응답에서 <c>items</c> 배열만 떼어낸다.</summary>
    private static string ItemsOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("items").GetRawText();
    }

    private static CsOutboundRow Row(List<CsOutboundRow> rows, string id, string gate)
    {
        var hit = rows.FirstOrDefault(r => string.Equals(r.OutboundId, id, StringComparison.Ordinal));
        Assert.True(hit is not null, gate + " 목록에 " + id + " 줄이 없다 — 어떤 줄이 목록에서 사라졌다.");
        return hit!;
    }

    /// <summary>화면 목록 응답 — 칸 이름은 화면 파일과 같다(<c>items</c>·<c>schemaPending</c>).</summary>
    private sealed class ScreenListResponse
    {
        public List<CsOutboundRow>? Items { get; set; }
        public bool SchemaPending { get; set; }
    }

    /// <summary>🔴 음성 대조 — 날짜 세 칸을 <b>비널러블</b>로 적은 모양. 같은 JSON 에서 터져야 한다.</summary>
    private sealed class StrictDateRow
    {
        public string OutboundId { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime PurgedAt { get; set; }
    }

    /// <summary>🔴 측정 고정 — 문자 칸을 비널러블로 적은 모양. .NET 8 STJ 는 <b>안 터지고</b> null 을 앉힌다.</summary>
    private sealed class StrictStringRow
    {
        public string OutboundId { get; set; } = "";
        public string CompanyName { get; set; } = "";
    }

    // ══════════════════════════════════════════════════════════════
    // 시험 장치 — 갈래① 토대 게이트(CsSupportCenterFoundationGateTests)와 같은 모양
    // ══════════════════════════════════════════════════════════════

    private static async Task<string> SendAsync(
        CsAdminController cs, string gate, string code, string purpose, string? title, string body)
    {
        var sent = await cs.CreateOutbound(new CsAdminController.OutboundRequest
        {
            TenantCode = code, Purpose = purpose, Title = title, Body = body,
        }, default);
        Assert.True(StatusOf(sent) == 200,
            gate + " 공지 넣기(" + purpose + ")가 200 이 아니다 — 전제가 성립하지 않는다. " + Describe(sent));
        return Field<string>(ValueOf(sent), "outboundId");
    }

    private static CsAdminController NewController(NoticeGateDb env) =>
        new(env.Config(), NullLogger<CsAdminController>.Instance, NullLoggerFactory.Instance) { ControllerContext = AdminContext() };

    private static ControllerContext AdminContext()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", AdminId),
            new Claim(ClaimTypes.Email, "w76gate@hitpan.test"),
            new Claim(ClaimTypes.Role, "platform_admin"),
        }, "gate");
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        return new ControllerContext { HttpContext = ctx };
    }

    private static int StatusOf(IActionResult res) => res switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        ForbidResult => 403,
        _ => 200,
    };

    private static object? ValueOf(IActionResult res) => (res as ObjectResult)?.Value;

    private static T Field<T>(object? value, string name)
    {
        Assert.NotNull(value);
        var prop = value!.GetType().GetProperty(name);
        Assert.NotNull(prop);
        return (T)prop!.GetValue(value)!;
    }

    private static string Describe(IActionResult res) =>
        "status=" + StatusOf(res) + " value=" + JsonSerializer.Serialize(ValueOf(res));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 레포 뿌리를 모른다.");
    }

    /// <summary>격리 DB 한 벌 — 실물 적용기로 <c>installer/backoffice/*.sql</c> 전부 적재(45번 포함).</summary>
    private sealed class NoticeGateDb : IAsyncDisposable
    {
        private readonly string _name;
        private readonly bool _created;

        private NoticeGateDb(string name, bool created) { _name = name; _created = created; }

        private static string BoSqlDir() => Path.Combine(RepoRoot(), "installer", "backoffice");

        private static string ServerConnString()
        {
            var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
            var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
            var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
            var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
            return $"Server={host};Port={port};User={user};Password={pass};"
                 + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
        }

        public string ConnString => ServerConnString().Replace("User=", $"Database={_name};User=");

        public IConfiguration Config() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:BackofficeDb"] = ConnString })
            .Build();

        public static async Task<NoticeGateDb?> TryCreateAsync(string gateName, string defaultName)
        {
            var overrideName = Environment.GetEnvironmentVariable("HITPAN_BO_GATE_DB");
            var useOverride = !string.IsNullOrWhiteSpace(overrideName);
            var name = useOverride ? overrideName!.Trim() : defaultName;

            if (useOverride)
            {
                // 지정 DB 는 표를 비우므로 시험 성격의 이름만 허용 — 운영·실데이터 축 오폭 차단(#39).
                var lower = name.ToLowerInvariant();
                if (lower is "hitpan_erp" or "hitpan_erp_t004" or "hitpan_backoffice" or "hitpan_e2e"
                    || (!lower.Contains("test") && !lower.Contains("gate")))
                {
                    throw new Xunit.Sdk.XunitException(
                        $"HITPAN_BO_GATE_DB={name} — 시험 DB 가 아니다(이름에 test/gate 필요 · 운영 축 금지).");
                }
            }

            var created = false;
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();
                if (useOverride)
                {
                    var tables = (await admin.QueryAsync<string>(
                        "SELECT table_name FROM information_schema.tables WHERE table_schema = @Db AND table_type = 'BASE TABLE'",
                        new { Db = name })).ToList();
                    if (tables.Count > 0)
                    {
                        await admin.ExecuteAsync($"USE `{name}`; SET FOREIGN_KEY_CHECKS = 0; "
                            + string.Join(" ", tables.Select(t => $"DROP TABLE IF EXISTS `{t}`;"))
                            + " SET FOREIGN_KEY_CHECKS = 1;");
                    }
                }
                else
                {
                    await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{name}`; "
                        + $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
                    created = true;
                }
            }
            catch (MySqlException ex)
            {
                Console.Error.WriteLine($"[{gateName}] DB 준비 실패({ex.Number}): {ex.Message}");
                DbGateEnvironment.SkipOrFailStrict(gateName);
                return null;
            }

            var env = new NoticeGateDb(name, created);
            await new SchemaMigrator(env.ConnString, BoSqlDir(), NullLogger<SchemaMigrator>.Instance).ApplyAsync();
            return env;
        }

        /// <summary>고객사 1곳. 반환값은 <c>tenant_id</c>(문자열).</summary>
        public async Task<string> SeedTenantAsync(string code, string companyName)
        {
            var tenantId = Guid.NewGuid().ToString();
            await using var db = await OpenAsync();
            await db.ExecuteAsync(@"
                INSERT INTO tenants (tenant_id, tenant_code, company_name, status)
                VALUES (@Tid, @Code, @Name, 'active')",
                new { Tid = tenantId, Code = code, Name = companyName });
            return tenantId;
        }

        public async Task<MySqlConnection> OpenAsync()
        {
            var c = new MySqlConnection(ConnString);
            await c.OpenAsync();
            return c;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_created) return;
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();
                await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_name}`;");
            }
            catch (MySqlException ex)
            {
                // #15 — 뒷정리 실패를 삼키지 않는다(다음 실행의 DROP IF EXISTS 가 지운다).
                Console.Error.WriteLine($"[작1갈래3게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }
}
