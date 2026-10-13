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
/// 🔴 20261013작1 갈래② — <b>G-W7-2 화면 낱말이 접수 경로를 덮지 않는다.</b>
///
/// <para><b>봉합한 사고</b>: 오늘까지 목록 셀과 상세 캡션이 두 곳에서
/// <c>Channel == "erp_message" ? "쪽지" : "전화"</c> 로 적고 있었다 ⇒
/// <c>email</c>·<c>sns</c>·<c>etc</c> 로 들어온 A/S 가 화면에서 <b>전부 「전화」</b>로 보였다.
/// 서버가 경로를 그대로 앉혀 줘도(갈래① G-W7-1) 화면이 그 값을 덮어쓰면 경로 낱말 전체가 뜻을 잃는다.</para>
///
/// <para>🔴 <b>왜 컨트롤러를 바로 불러 사전으로 읽지 않나</b> — 그러면 화면이 지나는 경로를 안 지나간다.
/// 2026-10-12 사고가 그 모양이었다: G-W3-2 는 컨트롤러 결과를 dictionary 로 읽어 통과했는데,
/// 실제 화면은 <c>JSON null → 비널러블 int</c> 에서 터져 쪽지 한 건이 목록 전부를 가렸다.
/// ⇒ 이 게이트는 목록 응답을 <b>직렬화한 뒤 화면 모델 모양으로 역직렬화</b>하고,
/// 그 모델의 값을 <see cref="CsVocab.ChannelLabel"/> 에 <b>실제로 넣어</b> 낱말을 받는다
/// (본보기 <c>CsPipelineRoundTripGateTests.cs:981-1004</c> <c>WebCsItem</c>).</para>
///
/// <para><b>글자를 세지 않는다</b> — 화면 파일에 「이메일」이라는 글자가 있나를 보지 않는다.
/// 함수를 불러 <b>받은 낱말</b>을 본다. 글자검사는 가짜 초록의 본산이다(누적 30번).</para>
///
/// <para>DB 자리: CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 ·
/// 로컬은 <c>HITPAN_BO_GATE_DB</c> 로 지정해 실측한다(이름에 <c>test</c>·<c>gate</c> 가 없으면 거부 · #39).
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFailStrict"/> — <c>[SKIP]</c> 를 찍고 넘긴다(조용한 초록 금지).</para>
///
/// <para>🔴 <b>로컬에서는 이 클래스를 하나씩</b> 돌려라 — <c>HITPAN_BO_GATE_DB</c> 는 DB 가 하나인데
/// xUnit 은 클래스를 병렬로 돌린다(2026-10-11 실측).</para>
/// </summary>
public sealed class CsSupportCenterScreenModelGateTests
{
    private const string AdminId = "w7-screen-gate-admin";

    /// <summary>
    /// 🔴 <c>AdminCsListPage.razor</c> 의 <c>CsTicketRow</c> 와 <b>같은 널 허용성</b>.
    /// 목록이 화면에서 지나는 자리가 여기다 — <c>Channel</c> 은 <c>string</c>(비널러블),
    /// <c>ReplyCount</c> 는 비널러블 <c>int</c>, <c>Rating</c>·<c>ReadAt</c> 은 널 허용이다.
    /// 화면 모델을 복사해 두는 이유: 화면 쪽 모양이 바뀌면 이 게이트가 같이 깨져야 한다.
    /// </summary>
    private sealed class WebCsTicketRow
    {
        public long Id { get; set; }
        public string CompanyName { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string Category { get; set; } = "";
        public string? ShapeTag { get; set; }
        public string? SubTag { get; set; }
        public string Channel { get; set; } = "";
        public string Status { get; set; } = "";
        public string? HandlerType { get; set; }
        public string? HandlerId { get; set; }
        public DateTime? PromisedAt { get; set; }
        public string? HandledVia { get; set; }
        public DateTime? ReadAt { get; set; }
        public int? Rating { get; set; }
        public DateTime? RatedAt { get; set; }
        public DateTime ReceivedAt { get; set; }
        public int ReplyCount { get; set; }
    }

    private sealed class WebListResponse
    {
        public bool Success { get; set; }
        public List<WebCsTicketRow>? Data { get; set; }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W7-2 — 접수 경로가 **화면 낱말에서도** 살아 있다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W7-2 목록을 화면 모델로 읽어 접수경로 4종+고객사쪽지가 제 낱말로 나온다 · 모르는 값은 아는 척 안 한다")]
    public async Task G_W7_2_화면_낱말이_경로를_덮지_않는다()
    {
        const string gate = "G-W7-2 화면 낱말이 경로를 덮나";
        await using var env = await ScreenGateDb.TryCreateAsync(gate, "hitpan_bo_w7_gate_2");
        if (env is null) return;

        await env.SeedTenantAsync("W7G2", "낱말 게이트 상사");
        var cs = NewController(env);

        // ── 네 경로로 실제 접수한다(화면이 보내는 그 길 · 엔드포인트 그대로)
        foreach (var channel in new[] { "phone", "email", "sns", "etc" })
        {
            var made = await cs.PhoneIntake(new CsAdminController.PhoneRequest
            {
                TenantCode = "W7G2", Category = "use", SubTag = "etc", Channel = channel,
                Body = $"{channel} 로 들어온 A/S 입니다",
            }, default);
            Assert.True(StatusOf(made) == 200, $"{gate} 접수경로 {channel} 접수가 200 이 아니다. {Describe(made)}");
        }

        // ── 고객사에서 올라온 쪽지 1건 — 사람이 고를 수 없는 값이라 표에 직접 앉힌다
        //    (수동 접수로는 400 이다 — 갈래① G-W7-1 이 그것을 문다)
        await using (var seed = await env.OpenAsync())
        {
            await seed.ExecuteAsync(@"
                INSERT INTO bo_cs_tickets
                  (tenant_id, client_ticket_uid, received_channel, category, sub_tag, body, status, received_at)
                SELECT t.tenant_id, @Uid, 'erp_message', 'bug', 'stock', '고객사 ERP 에서 올라온 쪽지', '접수', NOW()
                  FROM tenants t WHERE t.tenant_code = 'W7G2'",
                new { Uid = Guid.NewGuid().ToString() });
        }

        // ── 🔴 화면이 지나는 경로 — 응답을 **직렬화 → 화면 모델로 역직렬화**한다.
        //    컨트롤러 결과를 사전으로 읽으면 이 자리를 아무도 안 지나간다(10/12 사고).
        var listed = await cs.List(null, null, false, null, default);
        Assert.True(StatusOf(listed) == 200, $"{gate} 목록이 200 이 아니다. {Describe(listed)}");

        var json = JsonSerializer.Serialize(ValueOf(listed));
        var page = JsonSerializer.Deserialize<WebListResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(page);
        var rows = page!.Data;
        Assert.NotNull(rows);
        // 🔴 대조 — 역직렬화가 실제로 행을 가져왔나. 0건이면 아래 단언이 「없으니 통과」가 된다.
        Assert.True(rows!.Count == 5,
            $"{gate} 화면 모델로 읽은 행이 {rows.Count} 건이다(5 건이어야 한다) — 읽는 자리가 틀렸다.");

        // ── 🟢 양성 — 화면이 **그 행의 값**으로 받는 낱말. 5종이 서로 갈려야 한다.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["phone"] = "전화", ["email"] = "이메일", ["sns"] = "SNS",
            ["etc"] = "기타", ["erp_message"] = "고객사 쪽지",
        };
        foreach (var (channel, label) in expected)
        {
            var row = rows.SingleOrDefault(r => r.Channel == channel);
            Assert.True(row is not null,
                $"{gate} 화면 모델에 접수경로 {channel} 인 행이 없다 — 서버가 경로를 덮어썼거나 목록이 안 준다.");
            var shown = CsVocab.ChannelLabel(row!.Channel);
            Assert.True(shown == label,
                $"{gate} 경로 「{channel}」이 화면에서 「{shown}」으로 보인다(「{label}」이어야 한다) — "
              + "화면 낱말이 경로를 덮는다.");
        }

        // 🔴 같은 낱말로 뭉개지지 않았나 — 5종이 5개 낱말이어야 한다.
        //    옛 식(`== "erp_message" ? "쪽지" : "전화"`)으로 되돌리면 여기가 2개로 떨어진다.
        var distinct = rows.Select(r => CsVocab.ChannelLabel(r.Channel)).Distinct(StringComparer.Ordinal).Count();
        Assert.True(distinct == 5,
            $"{gate} 다섯 경로가 화면에서 {distinct} 개 낱말로만 보인다 — 서로 다른 길이 한 낱말로 뭉개졌다.");

        // ── 🔴 음성 대조 — 모르는 값을 **아는 척하지 않는다.**
        //    `_ => "기타"` 로 떨어뜨리면 서버가 새 값을 하나 더해도 화면이 「기타」라고 거짓말을 한다.
        Assert.Equal("zzz", CsVocab.ChannelLabel("zzz"));
        Assert.Equal("-", CsVocab.ChannelLabel(null));
    }

    // ══════════════════════════════════════════════════════════════
    // 시험 장치 — 갈래① CsSupportCenterFoundationGateTests 와 같은 모양
    // ══════════════════════════════════════════════════════════════

    private static CsAdminController NewController(ScreenGateDb env) =>
        new(env.Config(), NullLogger<CsAdminController>.Instance, NullLoggerFactory.Instance) { ControllerContext = AdminContext() };

    /// <summary>실물 컨트롤러가 요구하는 `sub` 클레임을 가진 문맥 — 없으면 Forbid 가 돈다.</summary>
    private static ControllerContext AdminContext()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", AdminId),
            new Claim(ClaimTypes.Email, "w7screen@hitpan.test"),
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

    private static string Describe(IActionResult res) =>
        $"status={StatusOf(res)} value={JsonSerializer.Serialize(ValueOf(res))}";

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

    /// <summary>격리 DB 한 벌 — 실물 적용기로 `installer/backoffice/*.sql` 전부 적재.</summary>
    private sealed class ScreenGateDb : IAsyncDisposable
    {
        private readonly string _name;
        private readonly bool _created;

        private ScreenGateDb(string name, bool created) { _name = name; _created = created; }

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

        public static async Task<ScreenGateDb?> TryCreateAsync(string gateName, string defaultName)
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

            var env = new ScreenGateDb(name, created);
            await new SchemaMigrator(env.ConnString, BoSqlDir(), NullLogger<SchemaMigrator>.Instance).ApplyAsync();
            return env;
        }

        /// <summary>고객사 1곳. 반환값은 `tenant_id`(문자열) — 컨트롤러가 그 값으로 쓴다.</summary>
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
                Console.Error.WriteLine($"[작1갈래2게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }
}
