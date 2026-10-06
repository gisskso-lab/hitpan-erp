using System.Reflection;
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
/// 🔴 20261006작8 갈래 가 (Z2) — 백오피스 landing_signups ↔ tenants 키(FK) 연결 게이트.
///
/// G-1 동명 회사 2건 격리(작지 §4): 같은 상호 가입서 2건이 각자 자기 tenant 에만 묶인다.
///   봉합 전(글자 조인)에는 목록·부트스트랩·시리얼 재발급이 동명 타사로 엇갈렸다 —
///   봉합 전 FAIL 재현은 조인 5곳을 봉합 전 코드로 되돌려 실측했다(개발명세서 §4).
/// G-4 backfill 행 수 검산(작지 §4): 전체 행 수 불변 ·
///   tenant_id 채워진 수 + NULL(모호·고아) 수 = 전체 · 모호(동명 2건 이상)는 NULL 유지(반자동 원칙).
///
/// 🔴 글자가 아니라 동작 — 시험은 판정 SQL 을 품지 않는다. 실물을 태운다:
///   · 스키마는 **실제 SchemaMigrator** 가 installer/backoffice/*.sql 을 번호순으로 적용(백오피스 진실원).
///   · 조인·승인·재발급은 **실물 컨트롤러**(SignupsAdminController·InstallerBootstrapController)를 부른다.
///
/// DB 자리:
///   · CI(db-gate 잡 · root): 격리 DB `bo_z2_signup_fk_gate` 를 만들었다 지운다.
///   · 로컬: `hitpan` 은 CREATE DATABASE 거부(4개 DB 이름에만 ALL) ⇒ HITPAN_BO_GATE_DB 로
///     시험 DB(`hitpan_trgtest` — 축B 선례·#39 정합)를 지정해 실측한다. 미지정+서버 없음 = SKIP
///     (CI 에서는 SkipOrFail 이 던진다 — 건너뛴 게이트는 통과가 아니다).
///   · 🔴 override DB 는 안의 표를 전부 지우고 다시 깐다 — 이름에 test/gate 가 없거나
///     운영·실데이터 축(hitpan_erp·hitpan_backoffice)이면 거부한다.
/// </summary>
[Collection(BoSignupTenantKeyGateCollection.Name)]
public sealed class BoSignupTenantKeyGateTests : IDisposable
{
    private const string GateDbDefault = "bo_z2_signup_fk_gate";
    private const string Pepper = "gate-license-pepper";

    private readonly string _dbName;
    private readonly bool _overrideDb;
    private bool _createdDb;

    public BoSignupTenantKeyGateTests()
    {
        var overrideName = Environment.GetEnvironmentVariable("HITPAN_BO_GATE_DB");
        _overrideDb = !string.IsNullOrWhiteSpace(overrideName);
        _dbName = _overrideDb ? overrideName!.Trim() : GateDbDefault;

        if (_overrideDb)
        {
            // 지정 DB 는 표를 전부 비우므로 시험 성격의 이름만 허용 — 운영·실데이터 축 오폭 차단(#39).
            var lower = _dbName.ToLowerInvariant();
            if (lower is "hitpan_erp" or "hitpan_backoffice"
                || (!lower.Contains("test") && !lower.Contains("gate")))
            {
                throw new Xunit.Sdk.XunitException(
                    $"HITPAN_BO_GATE_DB={_dbName} — 시험 DB 가 아니다(이름에 test/gate 필요 · 운영 축 금지).");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 준비물 — 선례(LegacyUnpostedDdlGateTests)와 같은 방식
    // ══════════════════════════════════════════════════════════════

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

    private static string BoScriptsDir() => Path.Combine(RepoRoot(), "installer", "backoffice");

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private string DbConnString() =>
        ServerConnString().Replace("User=", $"Database={_dbName};User=");

    private bool TrySetUpDb(string gateName)
    {
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            if (_overrideDb)
            {
                // 지정 시험 DB — 안의 표를 전부 지운다(모양이 낡은 표가 남으면 시험이 낡은 모양을 잰다).
                var tables = admin.Query<string>(
                    "SELECT table_name FROM information_schema.tables WHERE table_schema = @Db AND table_type = 'BASE TABLE'",
                    new { Db = _dbName }).ToList();
                if (tables.Count > 0)
                {
                    admin.Execute($"USE `{_dbName}`; SET FOREIGN_KEY_CHECKS = 0; "
                        + string.Join(" ", tables.Select(t => $"DROP TABLE IF EXISTS `{t}`;"))
                        + " SET FOREIGN_KEY_CHECKS = 1;");
                }
            }
            else
            {
                admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; "
                            + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
                _createdDb = true;
            }
            return true;
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[BoSignupTenantKeyGate] DB 준비 실패({ex.Number}): {ex.Message}");
            return DbGateEnvironment.SkipOrFail(gateName) && false;
        }
    }

    public void Dispose()
    {
        if (!_createdDb) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
        }
        catch (MySqlException ex)
        {
            // #15 — 뒷정리 실패는 삼키지 않고 찍는다(다음 실행의 DROP IF EXISTS 가 지운다).
            Console.Error.WriteLine($"[BoSignupTenantKeyGate] 격리 DB 뒷정리 실패: {ex.Message}");
        }
    }

    /// <summary>실제 SchemaMigrator 로 installer/backoffice/*.sql 을 번호순 적용(진짜 적용 경로).</summary>
    private async Task RunRealMigratorAsync()
    {
        var migrator = new SchemaMigrator(
            DbConnString(), BoScriptsDir(), NullLogger<SchemaMigrator>.Instance);
        await migrator.ApplyAsync();
    }

    /// <summary>G-4 전 상태 — 00(기존 고객 모양)만 직접 깔아 Z2 적용 전 DB 를 만든다.</summary>
    private async Task ApplyCoreOnlyAsync()
    {
        var corePath = Path.Combine(BoScriptsDir(), "00_backoffice_core.sql");
        Assert.True(File.Exists(corePath), $"백오피스 출하 SQL 이 없다: {corePath}");
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await using var cmd = new MySqlCommand(await File.ReadAllTextAsync(corePath), db)
        { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }

    // ── 실물 컨트롤러 준비 ─────────────────────────────────────────

    private IConfiguration GateConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = DbConnString(),
            ["License:Pepper"] = Pepper,
            ["Release:InstallerVersion"] = "0.0.0-gate",
            ["Release:ManifestUrl"] = "http://127.0.0.1:1/manifest.json", // 즉시 실패 → 설정값 폴백
            ["Bootstrap:TokenKey"] = "gate-bootstrap-token-key",
        })
        .Build();

    private SignupsAdminController NewSignupsAdmin() =>
        WithHttpContext(new SignupsAdminController(
            GateConfig(), new FakeWebhook(), new FakeEmail(), new FakeCfDomain(),
            new FakeHttpFactory(), NullLogger<SignupsAdminController>.Instance));

    private InstallerBootstrapController NewBootstrap() =>
        WithHttpContext(new InstallerBootstrapController(
            GateConfig(), new FakeCfDomain(), NullLogger<InstallerBootstrapController>.Instance));

    private static T WithHttpContext<T>(T c) where T : ControllerBase
    {
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return c;
    }

    // ── 시드 — 동명 회사 2건 (승인 폴백의 「시각 최근접」이 엇갈리는 시각 배치) ──
    //   s1 10:00:00 · s2 10:00:05 · t1 생성 10:00:06 · t2 생성 10:05:00
    //   ⇒ 글자 매칭이면: s2 의 최근접 = t1(1초) ≠ 자기 tenant(t2 · 295초) — 엇갈림 재현 배치.
    private const string Company = "동명상사(주)";
    private const string T1 = "11111111-aaaa-4aaa-8aaa-111111111111";
    private const string T2 = "22222222-bbbb-4bbb-8bbb-222222222222";

    private async Task<(long s1, long s2)> SeedHomonymAsync(MySqlConnection db)
    {
        var baseAt = new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc);
        await db.ExecuteAsync(@"
            INSERT INTO landing_signups (signup_token, biz_no_hash, company_name, email, phone, plan_type,
                                         agree_terms, agree_privacy, status, submitted_at)
            VALUES ('sgn-g1-s1', 'bizhash-s1', @C, 's1@gate.test', '010-0000-0001', 'basic', 1, 1, 'submitted', @A1),
                   ('sgn-g1-s2', 'bizhash-s2', @C, 's2@gate.test', '010-0000-0002', 'pro',   1, 1, 'submitted', @A2);
            INSERT INTO tenants (tenant_id, tenant_code, company_name, tel, status, db_host, db_name,
                                 license_key_hash, reseller_tier, created_at, updated_at)
            VALUES (@T1, 'T-901', @C, '010-0000-0001', 'pending', '', '', '', 0, @C1, @C1),
                   (@T2, 'T-902', @C, '010-0000-0002', 'pending', '', '', '', 0, @C2, @C2);",
            new
            {
                C = Company, T1, T2,
                A1 = baseAt, A2 = baseAt.AddSeconds(5),
                C1 = baseAt.AddSeconds(6), C2 = baseAt.AddMinutes(5),
            });
        var ids = (await db.QueryAsync<long>(
            "SELECT signup_id FROM landing_signups ORDER BY signup_id")).ToList();
        return (ids[0], ids[1]);
    }

    private static object? Prop(object? o, string name) =>
        o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);

    private static object OkValue(IActionResult r)
    {
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.NotNull(ok.Value);
        return ok.Value!;
    }

    // ══════════════════════════════════════════════════════════════
    // G-1 동명 회사 2건 격리 — 실물 컨트롤러 동작
    // ══════════════════════════════════════════════════════════════

    /// <summary>G-1a 승인 2건 → 각 가입서에 서로 다른 tenant_id 가 **기록**된다(승인 시점 기록 · Z2).</summary>
    [Fact]
    public async Task G1a_승인하면_가입서에_서로다른_tenant_키가_기록된다()
    {
        if (!TrySetUpDb(nameof(G1a_승인하면_가입서에_서로다른_tenant_키가_기록된다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var (s1, s2) = await SeedHomonymAsync(db);

        var admin = NewSignupsAdmin();
        OkValue(await admin.Approve(s1, CancellationToken.None));
        OkValue(await admin.Approve(s2, CancellationToken.None));

        var links = (await db.QueryAsync<(long SignupId, string? TenantId)>(
            "SELECT signup_id, CAST(tenant_id AS CHAR) FROM landing_signups ORDER BY signup_id")).ToList();
        Assert.Equal(T1, links.Single(l => l.SignupId == s1).TenantId);   // s1 → 자기 tenant(t1)
        Assert.Equal(T2, links.Single(l => l.SignupId == s2).TenantId);   // s2 → 자기 tenant(t2)
    }

    /// <summary>
    /// G-1b 관리 목록이 각자 자기 tenant 로 묶인다.
    /// 봉합 전 글자 조인(created_at >= submitted_at 최초 1건)이면 s2 도 t1 로 보였다(엇갈림).
    /// </summary>
    [Fact]
    public async Task G1b_목록이_가입서마다_자기_tenant_로_묶인다()
    {
        if (!TrySetUpDb(nameof(G1b_목록이_가입서마다_자기_tenant_로_묶인다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var (s1, s2) = await SeedHomonymAsync(db);

        var admin = NewSignupsAdmin();
        OkValue(await admin.Approve(s1, CancellationToken.None));
        OkValue(await admin.Approve(s2, CancellationToken.None));

        var rows = ((IEnumerable<SignupsAdminController.SignupRow>)Prop(
            OkValue(await admin.List(null, CancellationToken.None)), "data")!).ToList();
        Assert.Equal("T-901", rows.Single(r => r.SignupId == s1).TenantCode);
        Assert.Equal("T-902", rows.Single(r => r.SignupId == s2).TenantCode);
    }

    /// <summary>
    /// G-1c 설치 부트스트랩 응답이 **자기 가입서**의 이메일·요금제를 싣는다.
    /// 봉합 전 글자 조인(ls.company_name = t.company_name)이면 동명 타사의 가입서가 실릴 수 있었다.
    /// </summary>
    [Fact]
    public async Task G1c_부트스트랩_응답이_자기_가입서에_묶인다()
    {
        if (!TrySetUpDb(nameof(G1c_부트스트랩_응답이_자기_가입서에_묶인다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var (s1, s2) = await SeedHomonymAsync(db);

        var admin = NewSignupsAdmin();
        OkValue(await admin.Approve(s1, CancellationToken.None));
        var licenseKey2 = (string)Prop(OkValue(await admin.Approve(s2, CancellationToken.None)), "licenseKey")!;

        var boot = OkValue(await NewBootstrap().Bootstrap(
            new InstallerBootstrapController.BootstrapRequest
            { LicenseKey = licenseKey2, MachineFingerprint = "gate-machine-fp" },
            CancellationToken.None));
        var tenant = Prop(boot, "tenant");
        Assert.Equal("s2@gate.test", (string?)Prop(tenant, "email"));   // 봉합 전엔 s1 의 이메일이 실렸다
        Assert.Equal("pro", (string?)Prop(tenant, "planType"));
    }

    /// <summary>
    /// G-1d 시리얼 재발급이 자기 tenant 의 해시만 바꾼다 — 동명 타사(t1) 무접촉.
    /// 봉합 전 글자 매칭(시각 최근접)이면 s2 재발급이 t1 의 시리얼을 갈아끼웠다(실키 무효화 사고 모양).
    /// </summary>
    [Fact]
    public async Task G1d_재발급이_동명_타사의_시리얼을_건드리지_않는다()
    {
        if (!TrySetUpDb(nameof(G1d_재발급이_동명_타사의_시리얼을_건드리지_않는다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var (s1, s2) = await SeedHomonymAsync(db);

        var admin = NewSignupsAdmin();
        OkValue(await admin.Approve(s1, CancellationToken.None));
        OkValue(await admin.Approve(s2, CancellationToken.None));
        var t1HashBefore = await db.QueryFirstAsync<string>(
            "SELECT license_key_hash FROM tenants WHERE tenant_id = @T1", new { T1 });
        var t2HashBefore = await db.QueryFirstAsync<string>(
            "SELECT license_key_hash FROM tenants WHERE tenant_id = @T2", new { T2 });

        OkValue(await admin.ResendLicense(s2, CancellationToken.None));

        var t1HashAfter = await db.QueryFirstAsync<string>(
            "SELECT license_key_hash FROM tenants WHERE tenant_id = @T1", new { T1 });
        var t2HashAfter = await db.QueryFirstAsync<string>(
            "SELECT license_key_hash FROM tenants WHERE tenant_id = @T2", new { T2 });
        Assert.Equal(t1HashBefore, t1HashAfter);      // 남의 회사 무접촉 — 봉합 전엔 여기가 바뀌었다
        Assert.NotEqual(t2HashBefore, t2HashAfter);   // 자기 회사는 재발급으로 갱신
    }

    /// <summary>
    /// G-1e 폴백 대조군(#20) — tenant_id NULL 인 옛 행(동명 아님)은 기존 글자 경로로 여전히 읽힌다.
    /// 키 조인 전환이 옛 데이터를 끊으면 이 시험이 빨간불이 된다.
    /// </summary>
    [Fact]
    public async Task G1e_키없는_옛행은_글자_폴백으로_계속_읽힌다()
    {
        if (!TrySetUpDb(nameof(G1e_키없는_옛행은_글자_폴백으로_계속_읽힌다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var at = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
        await db.ExecuteAsync(@"
            INSERT INTO landing_signups (signup_token, biz_no_hash, company_name, email, phone, plan_type,
                                         agree_terms, agree_privacy, status, submitted_at)
            VALUES ('sgn-g1e', 'bizhash-e', '외길상사', 'old@gate.test', '010-0000-0009', 'basic', 1, 1, 'submitted', @At);
            INSERT INTO tenants (tenant_id, tenant_code, company_name, tel, status, db_host, db_name,
                                 license_key_hash, reseller_tier, created_at, updated_at)
            VALUES ('33333333-cccc-4ccc-8ccc-333333333333', 'T-903', '외길상사', '010-0000-0009',
                    'pending', '', '', '', 0, @C, @C);",
            new { At = at, C = at.AddSeconds(1) });
        var sId = await db.QueryFirstAsync<long>("SELECT signup_id FROM landing_signups LIMIT 1");

        var admin = NewSignupsAdmin();

        // 목록: 키 없이도(backfill 전 모양 그대로 NULL) 글자+시각 폴백으로 tenant 가 보인다
        var rows = ((IEnumerable<SignupsAdminController.SignupRow>)Prop(
            OkValue(await admin.List(null, CancellationToken.None)), "data")!).ToList();
        Assert.Equal("T-903", rows.Single(r => r.SignupId == sId).TenantCode);

        // 승인: 폴백 매칭으로 승인되고, 그 순간 tenant_id 가 기록된다(이후 행은 전부 키)
        OkValue(await admin.Approve(sId, CancellationToken.None));
        Assert.Equal("33333333-cccc-4ccc-8ccc-333333333333", await db.QueryFirstAsync<string>(
            "SELECT CAST(tenant_id AS CHAR) FROM landing_signups WHERE signup_id = @Id", new { Id = sId }));
    }

    // ══════════════════════════════════════════════════════════════
    // G-4 backfill 행 수 검산 — 실제 마이그 경로(SchemaMigrator)로 적용
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// G-4 기존 고객 DB 모양(00 만 적용 + 데이터)에 마이그를 돌리면:
    ///   행 수 불변 · 유일 1건만 연결 · 모호(동명 2건)는 NULL 유지(자동 추정 금지) · 고아 NULL ·
    ///   linked + ambiguous + orphan = total · 건수가 bo_audit_log 에 남는다 · 재실행 멱등 ·
    ///   FK 는 varchar(36) 실키로 걸린다(ON DELETE SET NULL 동작 포함).
    /// </summary>
    [Fact]
    public async Task G4_backfill_행수검산_모호는_NULL유지_건수기록()
    {
        if (!TrySetUpDb(nameof(G4_backfill_행수검산_모호는_NULL유지_건수기록))) return;
        await ApplyCoreOnlyAsync();   // Z2 적용 전 — 기존 고객 모양
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var at = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        await db.ExecuteAsync(@"
            INSERT INTO landing_signups (signup_token, biz_no_hash, company_name, email, phone, plan_type,
                                         agree_terms, agree_privacy, status, submitted_at)
            VALUES ('sgn-g4-a',  'h-a',  '유일상사',  'a@gate.test',  '010', 'basic', 1, 1, 'approved',  @At),
                   ('sgn-g4-b1', 'h-b1', '동명상사', 'b1@gate.test', '010', 'basic', 1, 1, 'approved',  @At),
                   ('sgn-g4-b2', 'h-b2', '동명상사', 'b2@gate.test', '010', 'basic', 1, 1, 'approved',  @At),
                   ('sgn-g4-c',  'h-c',  '고아상사',  'c@gate.test',  '010', 'basic', 1, 1, 'submitted', @At);
            INSERT INTO tenants (tenant_id, tenant_code, company_name, tel, status, db_host, db_name,
                                 license_key_hash, reseller_tier, created_at, updated_at)
            VALUES ('aaaaaaaa-0000-4000-8000-00000000000a', 'T-911', '유일상사',  '010', 'active', '', '', 'h', 0, @At, @At),
                   ('bbbbbbbb-0000-4000-8000-00000000000b', 'T-912', '동명상사', '010', 'active', '', '', 'h', 0, @At, @At),
                   ('cccccccc-0000-4000-8000-00000000000c', 'T-913', '동명상사', '010', 'active', '', '', 'h', 0, @At, @At);",
            new { At = at });
        var totalBefore = await db.QueryFirstAsync<int>("SELECT COUNT(*) FROM landing_signups");

        await RunRealMigratorAsync();   // 00(멱등 재적용)→10→20→30(Z2)→90/91(env 없으면 건너뜀)

        // 행 수 불변 + 검산식
        var totalAfter = await db.QueryFirstAsync<int>("SELECT COUNT(*) FROM landing_signups");
        Assert.Equal(totalBefore, totalAfter);
        var linked = await db.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM landing_signups WHERE tenant_id IS NOT NULL");
        var nulls = await db.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM landing_signups WHERE tenant_id IS NULL");
        Assert.Equal(totalAfter, linked + nulls);
        Assert.Equal(1, linked);   // 유일상사만
        Assert.Equal(3, nulls);    // 모호 2(동명) + 고아 1

        // 유일 1건은 자기 tenant 로 — 모호 2건은 NULL 그대로(자동 추정 금지 · 음성 대조군)
        Assert.Equal("aaaaaaaa-0000-4000-8000-00000000000a", await db.QueryFirstAsync<string>(
            "SELECT CAST(tenant_id AS CHAR) FROM landing_signups WHERE signup_token = 'sgn-g4-a'"));
        Assert.Equal(2, await db.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM landing_signups WHERE company_name = '동명상사' AND tenant_id IS NULL"));

        // 건수 기록 — bo_audit_log 한 줄에 total/linked/ambiguous/orphan
        var detail = await db.QueryFirstAsync<string>(@"
            SELECT detail_json FROM bo_audit_log
            WHERE action = 'z2.signup_tenant_backfill' ORDER BY log_id DESC LIMIT 1");
        using (var doc = System.Text.Json.JsonDocument.Parse(detail))
        {
            Assert.Equal(4, doc.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("linked").GetInt32());
            Assert.Equal(2, doc.RootElement.GetProperty("ambiguous_null_kept").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("orphan_null").GetInt32());
        }

        // FK 실물 — varchar(36) 컬럼 + 제약이 실제로 걸려 있다 (#13 DESCRIBE 축)
        Assert.Equal("varchar(36)", await db.QueryFirstAsync<string>(@"
            SELECT COLUMN_TYPE FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @Db AND TABLE_NAME = 'landing_signups' AND COLUMN_NAME = 'tenant_id'",
            new { Db = _dbName }));
        Assert.Equal(1, await db.QueryFirstAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = @Db AND CONSTRAINT_NAME = 'fk_landing_signups_tenant'",
            new { Db = _dbName }));

        // 재실행 멱등 — 같은 내용은 1회만(_schema_migrations 해시) · 행도 그대로
        await RunRealMigratorAsync();
        Assert.Equal(totalAfter, await db.QueryFirstAsync<int>("SELECT COUNT(*) FROM landing_signups"));
        Assert.Equal(1, await db.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM bo_audit_log WHERE action = 'z2.signup_tenant_backfill'"));

        // ON DELETE SET NULL 동작 — pending tenant 물리삭제(기존 Delete 경로)가 FK 에 막히지 않는다
        await db.ExecuteAsync("DELETE FROM tenants WHERE tenant_id = 'aaaaaaaa-0000-4000-8000-00000000000a'");
        Assert.Null(await db.QueryFirstAsync<string?>(
            "SELECT CAST(tenant_id AS CHAR) FROM landing_signups WHERE signup_token = 'sgn-g4-a'"));
    }

    // ══════════════════════════════════════════════════════════════
    // 페이크 — 실물 컨트롤러의 바깥 가장자리만 막는다(판정 로직은 전부 실물)
    // ══════════════════════════════════════════════════════════════

    private sealed class FakeWebhook : IWebhookOutboundService
    {
        public Task EmitSubscriptionChangedAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
        public Task EmitDeviceSlotChangedAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
        // [4]·[3-V] 교정 2026-10-07 — 같은 사이클 갈래 다(Z5)가 인터페이스에 더한 구현체(#12 전수).
        //   이 대역이 없어 4갈래 합류 빌드가 CS0535 로 끊겼다. 이 게이트는 웹훅을 재지 않으므로 빈 대역.
        public Task EmitAccountChangedAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeEmail : IEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<bool> SendWithAttachmentAsync(string toEmail, string subject, string htmlBody,
            byte[] attachment, string attachmentFileName, string attachmentMimeType, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class FakeCfDomain : ICloudflareDomainService
    {
        public bool IsConfigured => false;   // 컨트롤러가 IsConfigured 를 먼저 보므로 아래는 호출되지 않는다
        public Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, string? domainAlias, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task<TunnelIssueResult> IssueTunnelAsync(string tenantId, string tenantCode, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task UpdateDnsTunnelTargetAsync(string recordId, string domain, string tunnelId, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task UpdateTunnelIngressAsync(string tunnelId, string hostname, string originUrl, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task<bool> RevokeAsync(string cfZoneId, string cfRecordId, string? cfTunnelId, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task<bool> RevokeByDomainAsync(string subdomain, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
    }

    /// <summary>manifest 조회가 바깥으로 나가지 않게 즉시 404 — 컨트롤러는 설정값으로 폴백한다.</summary>
    private sealed class FakeHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Always404Handler());
        private sealed class Always404Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}

/// <summary>격리 DB 이름이 고정이라 병렬 금지 — 같은 DB 를 두 시험이 동시에 갈아엎지 않게.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BoSignupTenantKeyGateCollection
{
    public const string Name = "BoSignupTenantKeyGate";
}
