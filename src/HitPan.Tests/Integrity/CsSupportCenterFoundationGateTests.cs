using System.Security.Claims;
using System.Text.RegularExpressions;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261013작1 갈래① 토대 — 고객지원센터 프론트 재구성이 **서버에 닿는 3건**을 문다
/// (G-W7-1 · G-W7-3 · G-W7-4 · G-W7-7).
///
/// <para><b>글자가 아니라 동작을 잰다.</b> 화이트리스트 배열을 복사해 비교하지 않는다 —
/// 실물 <see cref="CsAdminController"/> 를 호출해 **그가 자기 SQL 을 DB 에 보내게** 하고,
/// 그 다음 <b>DB 의 칸을 직접 읽는다</b>. 낱말을 세는 게이트는 만들지 않는다(글자는 사장님이 바꿀 수 있다).</para>
///
/// <para>🔴 이 게이트가 지키는 약속 넷:
/// <list type="number">
/// <item><b>접수경로가 그대로 앉는다</b> — 서버가 <c>'phone'</c> 리터럴을 박던 자리(S-2)가 풀렸는지.
///       화면이 「이메일」로 적었는데 DB 에 「전화」가 앉으면 경로 낱말 전체가 뜻을 잃는다.</item>
/// <item><b>안 보내면 전화로 떨어진다</b> — 이 칸을 모르는 종전 호출자가 그대로 돌아야 한다(#1 하위호환).</item>
/// <item><b>새 발신 종류가 저장된다</b>(S-3) — 콤보에만 넣고 허용값을 안 늘리면 저장이 400 으로 막힌다.</item>
/// <item>🔴 <b>옛 값이 안 깨진다</b>(#1 감시) — 허용값을 신규 4종으로 <b>교체</b>하는 순간
///       <c>event</c>·<c>billing</c> 이 400 이 된다. 이미 그 값으로 들어간 줄을 다시 저장할 길이 막힌다.</item>
/// </list></para>
///
/// <para>DB 자리: CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 ·
/// 로컬 <c>hitpan</c> 계정은 <c>CREATE DATABASE</c> 가 막혀 있어 <c>HITPAN_BO_GATE_DB</c> 로 지정해 실측한다
/// (이름에 <c>test</c>·<c>gate</c> 가 없으면 거부 — 운영 축 오폭 차단 #39).
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFailStrict"/> — 조용한 초록은 못 낸다.</para>
///
/// <para>🔴 <b>로컬에서는 이 클래스를 하나씩</b> 돌려라. <c>HITPAN_BO_GATE_DB</c> 는 DB 가 하나인데
/// xUnit 은 클래스를 병렬로 돌린다 ⇒ 서로의 표를 지워 애먼 게이트가 빨간불이 된다(2026-10-11 실측).</para>
/// </summary>
public sealed class CsSupportCenterFoundationGateTests
{
    private const string AdminId = "w7-gate-admin";

    // ══════════════════════════════════════════════════════════════
    // G-W7-1 — 접수경로가 **그대로 앉는다** (S-1 + S-2)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W7-1 A/S 접수경로 4종이 DB 에 그대로 앉는다 · 안 보내면 전화 · 목록 밖은 400+행0")]
    public async Task G_W7_1_접수경로가_그대로_앉는다()
    {
        const string gate = "G-W7-1 접수경로 저장";
        await using var env = await CsGateDb.TryCreateAsync(gate, "hitpan_bo_w7_gate_1");
        if (env is null) return;

        await env.SeedTenantAsync("W7G1", "경로 게이트 상사");
        var cs = NewController(env);
        await using var db = await env.OpenAsync();

        // ── 🟢 양성 — 4종 전수. 보낸 값이 **그 값 그대로** 앉아야 한다.
        //    (S-2 를 되돌려 `'phone'` 리터럴로 쓰면 email·sns·etc 세 건이 여기서 FAIL 한다)
        foreach (var channel in new[] { "phone", "email", "sns", "etc" })
        {
            var made = await cs.PhoneIntake(new CsAdminController.PhoneRequest
            {
                TenantCode = "W7G1", Category = "use", SubTag = "etc", Channel = channel,
                Body = $"{channel} 로 들어온 문의입니다",
            }, default);
            Assert.True(StatusOf(made) == 200, $"{gate} 접수경로 {channel} 이 200 이 아니다. {Describe(made)}");

            var id = Field<long>(ValueOf(made), "id");
            var saved = await db.ExecuteScalarAsync<string>(
                "SELECT received_channel FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
            Assert.True(saved == channel,
                $"{gate} 화면이 「{channel}」로 보냈는데 DB 에 「{saved}」가 앉았다 — 서버가 경로를 덮어쓴다.");
        }

        // ── 🟢 하위호환(#1) — 칸을 **안 보내면** 전화로 떨어진다. 종전 호출자가 그대로 돌아야 한다.
        foreach (var blank in new string?[] { null, "   " })
        {
            var legacy = await cs.PhoneIntake(new CsAdminController.PhoneRequest
            {
                TenantCode = "W7G1", Category = "use", SubTag = "etc", Channel = blank,
                Body = "경로 칸을 모르는 종전 화면이 보낸 접수",
            }, default);
            Assert.True(StatusOf(legacy) == 200,
                $"{gate} 경로 칸 없는 종전 호출이 200 이 아니다 — 하위호환이 깨졌다. {Describe(legacy)}");
            Assert.Equal("phone", await db.ExecuteScalarAsync<string>(
                "SELECT received_channel FROM bo_cs_tickets WHERE id = @Id",
                new { Id = Field<long>(ValueOf(legacy), "id") }));
        }

        // ── 🔴 음성 대조 — 목록 밖 값은 400 **그리고** 행이 안 생긴다.
        //    🔴 둘을 **함께** 본다: 「행 0건」의 이유가 둘(거부했다 / 애초에 안 보냈다)이기 때문이다(10/12 교훈).
        //       그래서 ① 400 인지 ② 그 호출 전후로 건수가 **안 늘었는지** 를 같이 센다.
        var before = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_cs_tickets");
        var rejected = await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = "W7G1", Category = "use", SubTag = "etc", Channel = "weird",
            Body = "목록 밖 경로로 들어온 접수",
        }, default);
        Assert.True(StatusOf(rejected) == 400,
            $"{gate} 목록 밖 접수경로가 400 이 아니다 — 자유입력 구멍이 열려 있다. {Describe(rejected)}");
        var after = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_cs_tickets");
        Assert.True(after == before,
            $"{gate} 거부했다면서 행이 늘었다(전 {before} → 후 {after}) — 거부 뒤에 INSERT 가 돈다.");
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bo_cs_tickets WHERE received_channel = 'weird'"));

        // 🔴 `erp_message` 는 손으로 고를 값이 아니다 — 본사 담당자가 「고객사가 보낸 것」으로 꾸미는 길을 막는다.
        Assert.True(StatusOf(await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = "W7G1", Category = "use", SubTag = "etc", Channel = "erp_message",
            Body = "고객사 쪽지로 꾸민 수동 접수",
        }, default)) == 400, $"{gate} 수동 접수가 erp_message 를 고를 수 있다 — 경로가 거짓이 된다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W7-3 — 새 발신 종류가 **저장된다** (S-3)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W7-3 발신 종류 update·notice 가 저장된다 · 목록 밖은 400+행0")]
    public async Task G_W7_3_새_발신종류가_저장된다()
    {
        const string gate = "G-W7-3 새 발신 종류";
        await using var env = await CsGateDb.TryCreateAsync(gate, "hitpan_bo_w7_gate_3");
        if (env is null) return;

        await env.SeedTenantAsync("W7G3", "공지 게이트 상사");
        var cs = NewController(env);
        await using var db = await env.OpenAsync();

        // 🟢 양성 — 콤보 신규 2종. 허용값을 안 늘리면 여기서 400 으로 FAIL 한다.
        foreach (var purpose in new[] { "update", "notice" })
        {
            var sent = await cs.CreateOutbound(new CsAdminController.OutboundRequest
            {
                TenantCode = "W7G3", Purpose = purpose, Title = "안내", Body = $"{purpose} 종류의 안내입니다.",
            }, default);
            Assert.True(StatusOf(sent) == 200,
                $"{gate} 종류 {purpose} 가 200 이 아니다 — 콤보에 넣어도 저장이 막힌다. {Describe(sent)}");

            var id = Field<string>(ValueOf(sent), "outboundId");
            Assert.Equal(purpose, await db.ExecuteScalarAsync<string>(
                "SELECT purpose FROM bo_cs_outbound WHERE outbound_id = @Id", new { Id = id }));
        }

        // 🔴 음성 대조 — 목록 밖은 400 **그리고** 행 0건(둘을 함께 본다)
        var before = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_cs_outbound");
        var rejected = await cs.CreateOutbound(new CsAdminController.OutboundRequest
        {
            TenantCode = "W7G3", Purpose = "xxx", Body = "목록 밖 종류",
        }, default);
        Assert.True(StatusOf(rejected) == 400,
            $"{gate} 목록 밖 발신 종류가 400 이 아니다 — 자유입력 구멍이 열려 있다. {Describe(rejected)}");
        var after = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_cs_outbound");
        Assert.True(after == before,
            $"{gate} 거부했다면서 행이 늘었다(전 {before} → 후 {after}).");
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bo_cs_outbound WHERE purpose = 'xxx'"));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W7-4 — 🔴 **옛 값이 안 깨진다** (#1 감시)
    //   전수 자체가 대조군이다. 허용값을 신규 4종으로 **교체**하면 event·billing 이 400 으로 FAIL 한다.
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W7-4 🔴 #1 감시 — 옛 발신 종류 promo·event·billing·etc 4값 전수가 그대로 저장된다")]
    public async Task G_W7_4_옛_발신종류가_안_깨진다()
    {
        const string gate = "G-W7-4 옛 발신 종류 보존";
        await using var env = await CsGateDb.TryCreateAsync(gate, "hitpan_bo_w7_gate_4");
        if (env is null) return;

        await env.SeedTenantAsync("W7G4", "옛 값 게이트 상사");
        var cs = NewController(env);
        await using var db = await env.OpenAsync();

        var legacy = new[] { "promo", "event", "billing", "etc" };
        foreach (var purpose in legacy)
        {
            var sent = await cs.CreateOutbound(new CsAdminController.OutboundRequest
            {
                TenantCode = "W7G4", Purpose = purpose, Title = "옛 값", Body = $"{purpose} 로 보낸 글입니다.",
            }, default);
            Assert.True(StatusOf(sent) == 200,
                $"{gate} 옛 값 {purpose} 가 400 이다 — 허용값을 **교체**했다(#1 위반). "
              + $"이미 그 값으로 들어간 줄을 다시 저장·재전송할 길이 막힌다. {Describe(sent)}");
            Assert.Equal(purpose, await db.ExecuteScalarAsync<string>(
                "SELECT purpose FROM bo_cs_outbound WHERE outbound_id = @Id",
                new { Id = Field<string>(ValueOf(sent), "outboundId") }));
        }

        // 4값이 **각각 한 건씩** 남았다 — 하나라도 다른 값으로 바뀌어 앉으면 여기서 드러난다.
        var saved = (await db.QueryAsync<string>("SELECT purpose FROM bo_cs_outbound")).ToList();
        Assert.Equal(legacy.Length, saved.Count);
        foreach (var purpose in legacy)
        {
            Assert.True(saved.Count(p => p == purpose) == 1,
                $"{gate} 옛 값 {purpose} 가 1건이 아니다(저장된 값: {string.Join(",", saved)}).");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W7-7 — 라벨만 바꿔도 **라우트가 산다**
    //   🔴 DB 불필요. 간판 글자를 바꾸다 `@page` 를 함께 건드려 같은 길이 두 화면에 걸리면
    //   Blazor 는 **런타임**에 모호한 경로로 터진다 ⇒ 빌드로는 안 잡힌다. 그 자리를 문다.
    //   🔴 낱말은 세지 않는다 — 라벨 글자는 사장님이 바꿀 수 있다.
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W7-7 /admin/cs · /admin/kb 가 각각 정확히 1개 컴포넌트에만 걸린다")]
    public void G_W7_7_라우트가_유일하다()
    {
        const string gate = "G-W7-7 라우트 유일성";
        var root = Path.Combine(RepoRoot(), "src", "HitPan.Backoffice");
        Assert.True(Directory.Exists(root), $"{gate} 백오피스 폴더를 못 찾았다: {root}");

        // 모든 .razor 의 `@page` 선언을 걷는다(한 파일이 여러 길을 가질 수 있다).
        var byRoute = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var files = Directory.GetFiles(root, "*.razor", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            foreach (var m in Regex.Matches(File.ReadAllText(file), @"@page\s+""([^""]+)""").Cast<Match>())
            {
                var route = m.Groups[1].Value.Trim();
                if (!byRoute.TryGetValue(route, out var owners)) byRoute[route] = owners = new List<string>();
                owners.Add(Path.GetFileName(file));
            }
        }

        // 🔴 대조 — 훑기가 실제로 돌았나. 0건이면 아래 단언이 「없으니 통과」가 될 수 있다.
        Assert.True(files.Length >= 20, $"{gate} .razor 를 {files.Length} 개만 읽었다 — 훑는 자리가 틀렸다.");
        Assert.True(byRoute.Count >= 20, $"{gate} @page 를 {byRoute.Count} 개만 찾았다 — 정규식이 안 문다.");

        // 🔴 대조 — 늘 1 을 돌려주는 검사가 아니다. 없는 길은 0 이어야 한다.
        Assert.False(byRoute.ContainsKey("/admin/__gate_nonexistent__"),
            $"{gate} 없는 길이 잡혔다 — 이 검사는 아무 값이나 1 로 센다.");

        foreach (var route in new[] { "/admin/cs", "/admin/kb" })
        {
            Assert.True(byRoute.TryGetValue(route, out var owners),
                $"{gate} {route} 를 가진 화면이 0 개다 — 간판을 바꾸다 길을 잃었다.");
            Assert.True(owners!.Count == 1,
                $"{gate} {route} 가 {owners.Count} 개 화면에 걸렸다({string.Join(", ", owners)}) — "
              + "같은 길이 둘이면 화면이 런타임에 터진다.");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 시험 장치 — 작2 게이트(BackofficeCsOutboundGateTests)와 같은 모양
    // ══════════════════════════════════════════════════════════════

    private static CsAdminController NewController(CsGateDb env) =>
        new(env.Config(), NullLogger<CsAdminController>.Instance, NullLoggerFactory.Instance) { ControllerContext = AdminContext() };

    /// <summary>실물 컨트롤러가 요구하는 `sub` 클레임을 가진 문맥 — 없으면 Forbid 가 돈다.</summary>
    private static ControllerContext AdminContext()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", AdminId),
            new Claim(ClaimTypes.Email, "w7gate@hitpan.test"),
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
        $"status={StatusOf(res)} value={System.Text.Json.JsonSerializer.Serialize(ValueOf(res))}";

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

    /// <summary>격리 DB 한 벌 — 실물 적용기로 `installer/backoffice/*.sql` 전부 적재(45번 포함).</summary>
    private sealed class CsGateDb : IAsyncDisposable
    {
        private readonly string _name;
        private readonly bool _created;

        private CsGateDb(string name, bool created) { _name = name; _created = created; }

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

        public static async Task<CsGateDb?> TryCreateAsync(string gateName, string defaultName)
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

            var env = new CsGateDb(name, created);
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
                Console.Error.WriteLine($"[작1게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }
}
