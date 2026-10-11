using System.Security.Claims;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Security;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261011작2 절 A·C — 고객사명 자동조회 · 본사 선발신 쪽지 · 보관기한 · 임의 폐기
/// (G-W2-1 · G-W2-2 · G-W2-3 · G-W2-6 · G-W2-7).
///
/// <para><b>글자가 아니라 동작을 잰다.</b> SQL 문자열을 복사해 비교하지 않는다. 무는 것은 실물뿐이다:
/// <list type="bullet">
/// <item>스키마 = 실물 <see cref="SchemaMigrator"/> 가 <c>installer/backoffice/*.sql</c> 번호순 적용
///       (신설 <c>45</c> 번이 그 흐름에 실제로 끼는지도 여기서 드러난다).</item>
/// <item>판정 과녁 = 실물 <see cref="CsAdminController"/> 를 호출해 **그가 자기 SQL 을 DB 에 보내게** 한다.</item>
/// </list></para>
///
/// <para>🔴 <b>이 게이트가 지키는 약속 셋</b> — 사장님 오더에서 바로 나온다:
/// ① 본사가 쓴 쪽지는 <b>사라지지 않는다</b>(큐에 남는다) ② 화면이 「보냄」이라고 <b>거짓말하지 않는다</b>
/// (<c>delivered_at</c> 가 NULL 이면 대기로 보고한다) ③ 보관기한이 지나도 <b>저절로 지워지지 않는다</b>.</para>
///
/// <para>DB 자리: CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 ·
/// 로컬 <c>hitpan</c> 계정은 <c>CREATE DATABASE</c> 거부라 <c>HITPAN_BO_GATE_DB</c> 로 지정해 실측한다.
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFailStrict"/> — 조용한 초록은 못 낸다.</para>
/// </summary>
public sealed class BackofficeCsOutboundGateTests
{
    private const string AdminId = "w2-gate-admin";

    // ══════════════════════════════════════════════════════════════
    // G-W2-1 — 고객사명으로 찾고 두 칸만 돌려준다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2-1 고객사명 일부로 찾는다 · 코드·이름 두 칸만 나간다 (+음성 대조)")]
    public async Task G_W2_1_고객사명으로_찾는다()
    {
        const string gate = "G-W2-1 고객사명 조회";
        await using var env = await CsGateDb.TryCreateAsync(gate, "hitpan_bo_w2_gate_1");
        if (env is null) return;

        await env.SeedTenantAsync("W2G1", "히트판 게이트 상사");

        var cs = NewController(env);
        var found = await cs.TenantLookup("게이트", default);
        Assert.True(StatusOf(found) == 200, $"{gate} 조회가 200 이 아니다. {Describe(found)}");

        var items = Field<IEnumerable<CsAdminController.TenantLookupRow>>(ValueOf(found), "items").ToList();
        Assert.True(items.Count == 1, $"{gate} 회사명 일부로 1건이 나와야 한다(나온 건수 {items.Count}).");
        Assert.Equal("W2G1", items[0].TenantCode);
        Assert.Equal("히트판 게이트 상사", items[0].CompanyName);

        // 코드 일부로도 찾는다(전화 중에 코드만 아는 경우)
        var byCode = await cs.TenantLookup("W2G", default);
        Assert.Single(Field<IEnumerable<CsAdminController.TenantLookupRow>>(ValueOf(byCode), "items"));

        // 🔴 음성 대조 — 없는 이름은 0건이어야 한다(아무거나 돌려주면 담당자가 엉뚱한 고객사에 접수한다)
        var none = await cs.TenantLookup("없는회사이름zzz", default);
        Assert.Empty(Field<IEnumerable<CsAdminController.TenantLookupRow>>(ValueOf(none), "items"));

        // 🔴 음성 대조 — 빈 질의에 전체 목록을 쏟지 않는다(#22 본사 데이터 최소주의)
        var blank = await cs.TenantLookup("  ", default);
        Assert.Empty(Field<IEnumerable<object>>(ValueOf(blank), "items"));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W2-2 — 선발신 쪽지는 큐에 남고, 「대기」로 보고된다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2-2 선발신 쪽지가 큐에 쌓이고 delivered_at 은 NULL · 목록이 「대기」로 보고한다")]
    public async Task G_W2_2_선발신이_큐에_쌓인다()
    {
        const string gate = "G-W2-2 선발신 큐";
        await using var env = await CsGateDb.TryCreateAsync(gate, "hitpan_bo_w2_gate_2");
        if (env is null) return;

        var tenantId = await env.SeedTenantAsync("W2G2", "큐 게이트 상사");

        var cs = NewController(env);
        var sent = await cs.CreateOutbound(new CsAdminController.OutboundRequest
        {
            TenantCode = "W2G2", Purpose = "billing", Title = "구독요금 안내", Body = "결제가 확인되지 않았습니다.",
        }, default);

        Assert.True(StatusOf(sent) == 200, $"{gate} 쪽지 넣기가 200 이 아니다. {Describe(sent)}");

        // 🔴 「보냈다」고 말하지 않는다 — 전달 여부는 false 로 나가야 한다.
        Assert.False(Field<bool>(ValueOf(sent), "delivered"),
            $"{gate} 응답이 delivered=true 다 — ERP 수신 통로가 아직 없는데 보냈다고 말하면 안 된다.");

        await using var db = await env.OpenAsync();
        var row = await db.QueryFirstOrDefaultAsync(@"
            SELECT tenant_id AS TenantId, purpose AS Purpose, delivered_at AS DeliveredAt, canceled_at AS CanceledAt
              FROM bo_cs_outbound");
        Assert.NotNull(row);
        Assert.Equal(tenantId, (string)row!.TenantId);
        Assert.Equal("billing", (string)row.Purpose);
        Assert.Null(row.DeliveredAt);

        // 목록이 「대기」로 센다
        var pending = await cs.ListOutbound("pending", default);
        Assert.True(StatusOf(pending) == 200, $"{gate} 목록이 200 이 아니다. {Describe(pending)}");
        Assert.Single(Field<System.Collections.IEnumerable>(ValueOf(pending), "items").Cast<object>());

        // 취소하면 대기에서 빠진다(발신측 임의 삭제)
        var outboundId = await db.ExecuteScalarAsync<string>("SELECT outbound_id FROM bo_cs_outbound");
        var canceled = await cs.CancelOutbound(outboundId!, default);
        Assert.True(StatusOf(canceled) == 200, $"{gate} 취소가 200 이 아니다. {Describe(canceled)}");
        Assert.Empty(Field<System.Collections.IEnumerable>(
            ValueOf(await cs.ListOutbound("pending", default)), "items").Cast<object>());

        // 🔴 음성 대조 — 이미 취소된 것을 또 취소하면 400 이어야 한다(조용히 성공하면 상태가 거짓이 된다)
        Assert.True(StatusOf(await cs.CancelOutbound(outboundId!, default)) == 400,
            $"{gate} 중복 취소가 400 이 아니다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W2-3 — 내보내는 글에도 금지필드 문이 선다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2-3 선발신 쪽지도 식별정보 모양을 거부한다 (+정상 글은 통과)")]
    public async Task G_W2_3_금지필드를_거부한다()
    {
        const string gate = "G-W2-3 선발신 금지필드";
        await using var env = await CsGateDb.TryCreateAsync(gate, "hitpan_bo_w2_gate_3");
        if (env is null) return;

        await env.SeedTenantAsync("W2G3", "금지 게이트 상사");
        var cs = NewController(env);

        // 금지필드 검사가 실제로 거는 입력을 그 검사기에서 직접 구한다 — 시험이 규칙을 베껴 쓰지 않는다.
        const string dirty = "주민번호 880101-1234567 확인 부탁드립니다";
        Assert.NotNull(BoForbiddenFieldScanner.Scan(dirty));

        var rejected = await cs.CreateOutbound(new CsAdminController.OutboundRequest
        {
            TenantCode = "W2G3", Purpose = "promo", Body = dirty,
        }, default);
        Assert.True(StatusOf(rejected) == 400,
            $"{gate} 식별정보가 든 쪽지가 400 이 아니다 — 내보내는 글에 문이 안 서 있다. {Describe(rejected)}");

        await using var db = await env.OpenAsync();
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_cs_outbound"));

        // 🟢 양성 축 — 깨끗한 글은 들어간다(아무것도 안 통과시키는 문은 문이 아니다)
        Assert.True(StatusOf(await cs.CreateOutbound(new CsAdminController.OutboundRequest
        {
            TenantCode = "W2G3", Purpose = "promo", Body = "이번 달 프로모션 안내입니다.",
        }, default)) == 200);
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_cs_outbound"));

        // 🔴 음성 대조 — 용도가 목록 밖이면 거부(자유입력 금지)
        Assert.True(StatusOf(await cs.CreateOutbound(new CsAdminController.OutboundRequest
        {
            TenantCode = "W2G3", Purpose = "아무거나", Body = "정상 글",
        }, default)) == 400);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W2-6 — 폐기: 몸통은 지우고, 지웠다는 사실은 남는다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2-6 폐기하면 본문은 비고 감사기록 1줄이 남는다 · 행과 이력은 남는다")]
    public async Task G_W2_6_폐기가_사실을_남긴다()
    {
        const string gate = "G-W2-6 쪽지 폐기";
        await using var env = await CsGateDb.TryCreateAsync(gate, "hitpan_bo_w2_gate_6");
        if (env is null) return;

        await env.SeedTenantAsync("W2G6", "폐기 게이트 상사");
        var cs = NewController(env);

        // 실물 전화접수로 쪽지 1건을 만든다(시험이 INSERT 를 흉내내지 않는다)
        var made = await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = "W2G6", Category = "use", SubTag = "etc", Body = "지워질 내용입니다",
        }, default);
        Assert.True(StatusOf(made) == 200, $"{gate} 전화접수가 200 이 아니다. {Describe(made)}");
        var ticketId = Field<long>(ValueOf(made), "id");

        // 🔴 사유 없이는 폐기되지 않는다
        Assert.True(StatusOf(await cs.PurgeTicket(ticketId, new CsAdminController.PurgeRequest { Reason = " " }, default)) == 400,
            $"{gate} 사유 없는 폐기가 400 이 아니다 — 기록 없이 지워지는 길이 열려 있다.");

        var purged = await cs.PurgeTicket(ticketId, new CsAdminController.PurgeRequest { Reason = "고객 요청" }, default);
        Assert.True(StatusOf(purged) == 200, $"{gate} 폐기가 200 이 아니다. {Describe(purged)}");

        await using var db = await env.OpenAsync();
        var t = await db.QueryFirstOrDefaultAsync(@"
            SELECT body AS Body, purged_at AS PurgedAt, purged_by AS PurgedBy, purge_reason AS PurgeReason
              FROM bo_cs_tickets WHERE id = @Id", new { Id = ticketId });

        Assert.NotNull(t);                                  // 🔴 행은 남는다
        Assert.Null(t!.Body);                               // 몸통은 지워졌다
        Assert.NotNull(t.PurgedAt);
        Assert.Equal(AdminId, (string)t.PurgedBy);
        Assert.Equal("고객 요청", (string)t.PurgeReason);

        // 🔴 지웠다는 사실은 안 지운다 — 감사기록 1줄
        var audits = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bo_audit_log WHERE action = 'cs.purge' AND target_id = @Id",
            new { Id = ticketId.ToString() });
        Assert.True(audits == 1, $"{gate} bo_audit_log 의 cs.purge 가 {audits}건이다(기대 1건) — 누가 왜 지웠는지가 없다.");

        // 🔴 두 번 지우지 않는다(첫 기록이 덮이면 안 된다)
        Assert.True(StatusOf(await cs.PurgeTicket(ticketId, new CsAdminController.PurgeRequest { Reason = "두번째" }, default)) == 400);
        Assert.Equal("고객 요청", await db.ExecuteScalarAsync<string>(
            "SELECT purge_reason FROM bo_cs_tickets WHERE id = @Id", new { Id = ticketId }));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W2-7 — 보관기한은 설정될 뿐, 아무것도 지우지 않는다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2-7 보관기한 기본 무기한 · 설정 후에도 자동삭제 0건 · 후보만 센다")]
    public async Task G_W2_7_보관기한은_지우지_않는다()
    {
        const string gate = "G-W2-7 보관기한";
        await using var env = await CsGateDb.TryCreateAsync(gate, "hitpan_bo_w2_gate_7");
        if (env is null) return;

        await env.SeedTenantAsync("W2G7", "기한 게이트 상사");
        var cs = NewController(env);

        // ① 기본값 = 무기한(45번 SQL 의 초기 1행). 후보도 0건이어야 한다.
        var policy = await cs.GetRetention(default);
        Assert.True(StatusOf(policy) == 200, $"{gate} 기한 조회가 200 이 아니다. {Describe(policy)}");

        var first = await cs.PurgeCandidates(default);
        Assert.Null(Field<int?>(ValueOf(first), "retainDays"));
        Assert.False(Field<bool>(ValueOf(first), "autoDelete"));
        Assert.Empty(Field<System.Collections.IEnumerable>(ValueOf(first), "items").Cast<object>());

        // ② 오래된 쪽지 1건을 만든다 — 실물 접수 뒤 접수시각만 과거로 옮긴다.
        var made = await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = "W2G7", Category = "use", SubTag = "etc", Body = "오래된 쪽지",
        }, default);
        var ticketId = Field<long>(ValueOf(made), "id");

        await using var db = await env.OpenAsync();
        await db.ExecuteAsync(
            "UPDATE bo_cs_tickets SET created_at = DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 40 DAY) WHERE id = @Id",
            new { Id = ticketId });

        // ③ 기한 30일 설정
        Assert.True(StatusOf(await cs.SetRetention(new CsAdminController.RetentionRequest { RetainDays = 30 }, default)) == 200);

        var after = await cs.PurgeCandidates(default);
        Assert.Equal(30, Field<int?>(ValueOf(after), "retainDays"));
        Assert.Single(Field<System.Collections.IEnumerable>(ValueOf(after), "items").Cast<object>());

        // 🔴 가장 중요한 단언 — **아무것도 지워지지 않았다.**
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_cs_tickets WHERE id = @Id", new { Id = ticketId }));
        Assert.Null(await db.ExecuteScalarAsync<DateTime?>("SELECT purged_at FROM bo_cs_tickets WHERE id = @Id", new { Id = ticketId }));
        Assert.NotNull(await db.ExecuteScalarAsync<string?>("SELECT body FROM bo_cs_tickets WHERE id = @Id", new { Id = ticketId }));
        Assert.False(Field<bool>(ValueOf(after), "autoDelete"));

        // ④ 음성 대조 — 기한 범위 밖 값은 거부
        Assert.True(StatusOf(await cs.SetRetention(new CsAdminController.RetentionRequest { RetainDays = 0 }, default)) == 400);
        Assert.True(StatusOf(await cs.SetRetention(new CsAdminController.RetentionRequest { RetainDays = 99999 }, default)) == 400);
    }

    // ══════════════════════════════════════════════════════════════
    // 시험 장치
    // ══════════════════════════════════════════════════════════════

    private static CsAdminController NewController(CsGateDb env) =>
        new(env.Config(), NullLogger<CsAdminController>.Instance) { ControllerContext = AdminContext() };

    /// <summary>실물 컨트롤러가 요구하는 `sub` 클레임을 가진 문맥 — 없으면 Forbid 가 돈다.</summary>
    private static ControllerContext AdminContext()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", AdminId),
            new Claim(ClaimTypes.Email, "w2gate@hitpan.test"),
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

    /// <summary>격리 DB 한 벌 — 실물 적용기로 `installer/backoffice/*.sql` 전부 적재(45번 포함).</summary>
    private sealed class CsGateDb : IAsyncDisposable
    {
        private readonly string _name;
        private readonly bool _created;

        private CsGateDb(string name, bool created) { _name = name; _created = created; }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
                dir = dir.Parent;
            }
            throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — installer/backoffice 를 읽을 수 없다.");
        }

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
                Console.Error.WriteLine($"[작2게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }
}
