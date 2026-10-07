using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261007작12 1차수 B3-1 — 설치 부트스트랩 <b>ⓑ 시도 제한 · ⓒ 응답 최소화</b> 게이트.
///
/// <para>작업지시서 <c>docs/운영기록/20261007작12_백오피스_익명노출_전수_트랙개설_작업지시서.md</c> §8·§10 ·
/// 설계 <c>docs/설계/백오피스/20261007_설계_작12_1차수_설치부트스트랩_선검증.md</c> §4·§5·§6.</para>
///
/// <para><b>전부 동작 게이트다.</b> 글자를 세지 않는다 — 실물을 태운다:
/// <list type="bullet">
/// <item>스키마 = 실물 <see cref="SchemaMigrator"/> 가 <c>installer/backoffice/*.sql</c> 번호순 적용(백오피스 진실원).</item>
/// <item>판정 = 실물 <see cref="InstallerBootstrapController"/> 를 불러 <b>상태코드와 표의 행</b>을 본다.</item>
/// <item>G-9 = 실물 <c>UseAuthentication → UseAuthorization → MVC</c> 파이프라인에 <b>토큰 0 으로</b> 요청을 보낸다.</item>
/// </list></para>
///
/// <para>🔴 <b>이 파일이 담는 게이트는 G-3·G-4·G-5·G-6·G-8·G-9 다.</b>
/// G-1·G-2·G-2b 는 ⓐ 선검증(갈래 A) 게이트이고 그건 [3]-2(C-1 결과 뒤)다 — 여기 없다.
/// <b>G-7(워치독 실파서 투입)은 이 파일에 없다</b> — 이유는 아래 「G-7 이 왜 없나」.</para>
///
/// <para><b>G-7 이 왜 없나 (숨기지 않고 적는다 · 개발명세서 §5)</b>:
/// G-7 은 축소 응답 JSON 을 <c>TunnelTokenRecovery</c> 의 <b>실제 파싱 경로</b>에 먹이라고 한다.
/// 그러려면 이 시험 프로젝트가 <c>HitPan.Watchdog</c> 를 참조해야 하는데
/// <c>HitPan.Watchdog.csproj</c> 는 <c>net8.0-windows</c>, <c>HitPan.Tests.csproj</c> 는 <c>net8.0</c> 이다.
/// 플랫폼 TFM 이 더 좁은 프로젝트는 넓은 TFM 프로젝트가 참조할 수 없다(NU1201).
/// ⇒ 4파일 범위(제품 1 · 게이트 1 · 워치독 1 · CI 1줄) 안에서는 만들 수 없다. PM 결재 사항이다.
/// 대신 <c>C1_</c> 시험이 축소 응답의 <b>항목 이름 집합</b>을 실측해 「워치독이 읽는 셋이 그대로 남았나」를 잠근다 —
/// 🔴 그것은 G-7 이 아니다. 실파서를 돌린 것이 아니므로 G-7 을 통과했다고 읽지 마라.</para>
///
/// <para>DB 자리(선례 <see cref="BoSignupTenantKeyGateTests"/>):
/// CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 · 로컬 <c>hitpan</c> 계정은 <c>CREATE DATABASE</c> 거부라
/// <c>HITPAN_BO_GATE_DB</c> 로 시험 DB 를 지정해 실측한다. 둘 다 안 되면
/// <see cref="DbGateEnvironment.SkipOrFailStrict"/> — <b>선언 없는 건너뛰기는 빨간불</b>이다.</para>
///
/// <para>🔴 Collection 을 <see cref="BoSignupTenantKeyGateCollection"/> 과 <b>공유</b>한다.
/// <c>HITPAN_BO_GATE_DB</c> 를 주면 두 게이트가 <b>같은 시험 DB</b> 의 표를 전부 지우고 다시 깐다 —
/// 병렬로 돌면 서로의 스키마를 날려 깜빡인다. 같은 collection 에 넣어 직렬화한다(상태 공유는 0).</para>
/// </summary>
[Collection(BoSignupTenantKeyGateCollection.Name)]
public sealed class InstallerBootstrapPreverifyGateTests
    : IClassFixture<HitPan.Tests.BackofficeAnonymousAdminApiGate.Factory>, IDisposable
{
    private const string GateDbDefault = "bo_b31_installer_bootstrap_gate";
    private const string Pepper = "b31-gate-license-pepper";

    /// <summary>유효 시리얼 — 실물 컨트롤러가 정규화(Trim·대문자·공백제거)한 뒤 HMAC 비교한다.</summary>
    private const string ValidKey = "B31-GATE-VALID-KEY";
    private const string WrongKeyPrefix = "B31-GATE-WRONG-KEY-";
    private const string Fingerprint = "B31GATE-PC-b31gate-user";
    private const string TenantId = "b31aaaaa-0000-4000-8000-00000000b31a";

    private readonly string _dbName;
    private readonly bool _overrideDb;
    private bool _createdDb;
    private readonly HitPan.Tests.BackofficeAnonymousAdminApiGate.Factory _pipeline;

    public InstallerBootstrapPreverifyGateTests(HitPan.Tests.BackofficeAnonymousAdminApiGate.Factory pipeline)
    {
        _pipeline = pipeline;

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
    // 준비물 — 선례 BoSignupTenantKeyGateTests 와 같은 방식
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
            Console.Error.WriteLine($"[InstallerBootstrapPreverifyGate] DB 준비 실패({ex.Number}): {ex.Message}");
            return DbGateEnvironment.SkipOrFailStrict(gateName) && false;
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
            Console.Error.WriteLine($"[InstallerBootstrapPreverifyGate] 격리 DB 뒷정리 실패: {ex.Message}");
        }
    }

    private async Task RunRealMigratorAsync()
    {
        var migrator = new SchemaMigrator(
            DbConnString(), BoScriptsDir(), NullLogger<SchemaMigrator>.Instance);
        await migrator.ApplyAsync();
    }

    // ── 실물 컨트롤러 준비 ─────────────────────────────────────────

    private IConfiguration GateConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = DbConnString(),
            ["License:Pepper"] = Pepper,
            ["Bootstrap:TokenKey"] = "b31-gate-bootstrap-token-key",
        })
        .Build();

    private InstallerBootstrapController NewBootstrap(ICloudflareDomainService? cf = null)
    {
        var c = new InstallerBootstrapController(
            GateConfig(), cf ?? new OffCfDomain(), NullLogger<InstallerBootstrapController>.Instance);
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return c;
    }

    private static InstallerBootstrapController.BootstrapRequest Req(string key, string? purpose = null) =>
        new()
        {
            LicenseKey = key,
            MachineFingerprint = Fingerprint,
            Hostname = "B31GATE-PC",
            InstallerVersion = "0.0.0-gate",
            Purpose = purpose,
        };

    private static string Hmac(string data) =>
        Convert.ToHexString(
            new HMACSHA256(Encoding.UTF8.GetBytes(Pepper)).ComputeHash(Encoding.UTF8.GetBytes(data)))
        .ToLowerInvariant();

    private static string FpHash => Hmac(Fingerprint);

    /// <summary>유효 키로 묶인 active 테넌트 1건. (ⓐ 2중 재료는 [3]-2 과녁 — 여기서 재지 않는다.)</summary>
    private async Task SeedTenantAsync(MySqlConnection db)
    {
        var at = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
        await db.ExecuteAsync(@"
            INSERT INTO tenants (tenant_id, tenant_code, company_name, tel, status, db_host, db_name,
                                 license_key_hash, reseller_tier, created_at, updated_at)
            VALUES (@T, 'T-B31', '비삼일상사(주)', '010-0000-0031', 'active', '', '',
                    @H, 0, @At, @At);",
            new { T = TenantId, H = Hmac(ValidKey), At = at });
    }

    private async Task<(int FailedCount, int IsLocked)?> ReadLockAsync(MySqlConnection db) =>
        await db.QueryFirstOrDefaultAsync<(int, int)?>(@"
            SELECT failed_count, is_locked FROM serial_verify_locks WHERE client_fingerprint = @Fp",
            new { Fp = FpHash });

    private static int StatusOf(IActionResult r) =>
        Assert.IsAssignableFrom<IStatusCodeActionResult>(r).StatusCode ?? 0;

    private static object OkValue(IActionResult r)
    {
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.NotNull(ok.Value);
        return ok.Value!;
    }

    /// <summary>응답 묶음의 항목 **이름**을 점 경로로 전수 수집한다(값은 한 글자도 보지 않는다).</summary>
    private static SortedSet<string> NamePaths(object o, string prefix = "")
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var path = prefix.Length == 0 ? p.Name : prefix + "." + p.Name;
            set.Add(path);
            // 익명 묶음(tenant·domain·bootstrap)만 한 단계 더 들어간다. string·bool·int 는 잎이다.
            var t = p.PropertyType;
            if (t.IsClass && t != typeof(string) && t.Name.Contains("AnonymousType", StringComparison.Ordinal))
            {
                var child = p.GetValue(o);
                if (child is not null) set.UnionWith(NamePaths(child, path));
            }
        }
        return set;
    }

    // 🔴 전체 응답의 이름 전수 — 설계 §6 G-8 은 「18개」라 적었는데 **실측은 19개**다(묶음 이름 3개 포함).
    //   숫자를 받아쓰지 않고 이름 집합 자체를 대조한다(개발명세서 §3 에 이 차이를 적었다).
    private static readonly string[] FullNames =
    {
        "success", "source",
        "tenant", "tenant.tenantCode", "tenant.companyName", "tenant.tel", "tenant.email", "tenant.planType",
        "domain", "domain.primary", "domain.api", "domain.tunnelTokenIssued", "domain.tunnelToken", "domain.tunnelId",
        "bootstrap", "bootstrap.token", "bootstrap.expiresInSec", "bootstrap.backofficeUrl", "bootstrap.tokenKey",
    };

    // 단계 2 축소 — 전체에서 **빼기만** 한 결과. 새 이름은 하나도 없다(설계 §5-3).
    private static readonly string[] ReducedNames =
    {
        "success", "source",
        "domain", "domain.primary", "domain.api", "domain.tunnelTokenIssued", "domain.tunnelToken", "domain.tunnelId",
        "bootstrap", "bootstrap.expiresInSec", "bootstrap.backofficeUrl",
    };

    // ══════════════════════════════════════════════════════════════
    // G-3 잠금 — 틀린 키 5회 → 1~4회 401 · 5회째 423 · 표에 failed_count=5 · is_locked=1
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// G-3. 봉합 전엔 ⑤ 가 <c>LogWarning</c> 한 줄만 남겼다 ⇒ 5회 전부 401 이고 lock 행이 <b>0</b> 이었다.
    /// (봉합 빼면 FAIL 재현 — 개발명세서 §4 에 실측 결과를 적었다.)
    /// </summary>
    [Fact]
    public async Task G3_틀린키_5회면_5회째_423_이고_지문단위_잠금행이_생긴다()
    {
        if (!TrySetUpDb(nameof(G3_틀린키_5회면_5회째_423_이고_지문단위_잠금행이_생긴다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedTenantAsync(db);

        var controller = NewBootstrap();
        for (var i = 1; i <= 4; i++)
        {
            var r = await controller.Bootstrap(Req(WrongKeyPrefix + i), CancellationToken.None);
            Assert.Equal(401, StatusOf(r));                       // 1~4회 — 기존 401 그대로(워치독 계약)
            var mid = await ReadLockAsync(db);
            Assert.NotNull(mid);
            Assert.Equal(i, mid!.Value.FailedCount);               // 가산이 실제로 올라간다
            Assert.Equal(0, mid.Value.IsLocked);
        }

        var fifth = await controller.Bootstrap(Req(WrongKeyPrefix + 5), CancellationToken.None);
        Assert.Equal(423, StatusOf(fifth));                        // 5회째 — 잠금

        var after = await ReadLockAsync(db);
        Assert.NotNull(after);
        Assert.Equal(5, after!.Value.FailedCount);
        Assert.Equal(1, after.Value.IsLocked);

        // 잠긴 뒤 **틀린 키**를 또 보내면 423 (무차별 대입은 항상 틀린 키를 낸다)
        Assert.Equal(423, StatusOf(await controller.Bootstrap(Req(WrongKeyPrefix + 6), CancellationToken.None)));
        // 🔴 유효 키는 423 이 아니다 — 그 축은 G-4a·G-10 이 따로 문다(P0-1 처방)

        // 시도 기록이 **구분되어** 남는다 — 같은 표를 브라우저 시리얼 검증이 공유한다
        Assert.Equal(5, await db.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM serial_verify_attempts WHERE result = 'installer-mismatch'"));
        Assert.Equal(1, await db.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM serial_verify_attempts WHERE result = 'installer-locked'"));
        // result 는 varchar(20) — 잘리지 않았다(잘리면 추적이 섞인다)
        foreach (var v in await db.QueryAsync<string>(
            "SELECT DISTINCT result FROM serial_verify_attempts"))
            Assert.True(v.Length <= 20, $"result 값이 20자를 넘었다: {v} ({v.Length}자)");
    }

    // ══════════════════════════════════════════════════════════════
    // G-4 🔴 자동 해제 — 재설치 영구차단 P0 방어선
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 G-4a (P0-1 방어선 · 사장님 결재 2026-10-07) — 잠긴 지문 + <b>유효 키</b> → <b>즉시 200</b>.
    /// <para>61분을 기다리지 않는다. 잠금 판독이 키 조회 <b>앞</b>으로 가면 423 이 되어 FAIL ⇒
    /// 워치독 터널 자가복구와 고객 재설치가 60분 멈춘다(#27·#28·#30).</para>
    /// </summary>
    [Fact]
    public async Task G4a_잠긴지문도_유효키면_즉시_200_이다()
    {
        if (!TrySetUpDb(nameof(G4a_잠긴지문도_유효키면_즉시_200_이다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedTenantAsync(db);

        var controller = NewBootstrap();
        for (var i = 1; i <= 5; i++)
            await controller.Bootstrap(Req(WrongKeyPrefix + i), CancellationToken.None);
        Assert.Equal(1, (await ReadLockAsync(db))!.Value.IsLocked);       // 잠겼다(틀린 키 5회)

        // 🔴 시각을 밀지 않는다 — 잠긴 **그 순간** 유효 키로 들어온다
        Assert.Equal(200, StatusOf(await controller.Bootstrap(Req(ValidKey), CancellationToken.None)));
    }

    /// <summary>
    /// G-4b 자동 해제 — 잠긴 지문 + <b>틀린 키</b> + <c>last_failed_at</c> 을 61분 전으로 밀면 <b>401</b>.
    /// <para>읽기 쪽 60분 창 조건(<c>WithinWindow</c>)이 없으면 <b>423</b> 으로 남는다 ⇒ FAIL.
    /// 기존 기계는 영구 잠금 + 본사 수동 해제다 — 이 조건이 그걸 되돌려 푼다.</para>
    /// </summary>
    [Fact]
    public async Task G4b_잠금은_60분_지나면_틀린키에도_423이_아니다()
    {
        if (!TrySetUpDb(nameof(G4b_잠금은_60분_지나면_틀린키에도_423이_아니다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedTenantAsync(db);

        var controller = NewBootstrap();
        for (var i = 1; i <= 5; i++)
            await controller.Bootstrap(Req(WrongKeyPrefix + i), CancellationToken.None);
        Assert.Equal(1, (await ReadLockAsync(db))!.Value.IsLocked);
        // 잠긴 창 안에서는 틀린 키가 423
        Assert.Equal(423, StatusOf(await controller.Bootstrap(Req(WrongKeyPrefix + 6), CancellationToken.None)));

        // 시계를 못 돌리므로 표의 시각을 뒤로 민다 — 「60분 지난 상태」를 실제로 만든다
        await db.ExecuteAsync(@"
            UPDATE serial_verify_locks
            SET last_failed_at = DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 61 MINUTE)
            WHERE client_fingerprint = @Fp", new { Fp = FpHash });

        // 423 이 아니다 — 창이 지나 스스로 열렸고, 틀린 키라서 401 + 카운터는 1 에서 다시 시작
        Assert.Equal(401, StatusOf(await controller.Bootstrap(Req(WrongKeyPrefix + 7), CancellationToken.None)));
        var row = await ReadLockAsync(db);
        Assert.Equal(1, row!.Value.FailedCount);
        Assert.Equal(0, row.Value.IsLocked);
    }

    /// <summary>
    /// 🔴🔴 G-10 (P0-1 · 사장님 결재 2026-10-07) — <b>워치독 자가복구가 잠기지 않는다</b>를 동작으로 재는 유일한 게이트.
    /// <para>잠긴 지문이 유효 키로 <b>200</b> 을 받고 <b>그 뒤 <c>failed_count=0</c>·<c>is_locked=0</c></b>.
    /// 그래서 바로 다음 요청도 정상이다(잠금이 실제로 풀렸다).</para>
    /// <para><b>봉합을 빼면</b>(= 잠금 검사를 키 조회보다 앞으로 옮기면) <b>423</b> 이 나와 FAIL 한다.</para>
    /// <para>반증 근거: 워치독은 60분에 최대 10회 부트스트랩을 부를 수 있고(키별 독립 큐 ·
    /// <c>Worker.cs:448</c>·<c>:1051</c> 두 키), <c>t.status='active'</c> 조건 때문에 구독 정지·미승인
    /// 고객은 <b>유효 키로도 401</b> 이다 ⇒ 지문이 잠길 길이 실재한다.</para>
    /// </summary>
    [Fact]
    public async Task G10_잠긴지문이_유효키로_200을_받고_잠금이_실제로_풀린다()
    {
        if (!TrySetUpDb(nameof(G10_잠긴지문이_유효키로_200을_받고_잠금이_실제로_풀린다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedTenantAsync(db);

        var controller = NewBootstrap();
        for (var i = 1; i <= 5; i++)
            await controller.Bootstrap(Req(WrongKeyPrefix + i), CancellationToken.None);
        var locked = await ReadLockAsync(db);
        Assert.Equal(5, locked!.Value.FailedCount);
        Assert.Equal(1, locked.Value.IsLocked);                 // 전제: 정말 잠겼다

        // 워치독·고객 재설치가 쓰는 길 — 유효 키
        Assert.Equal(200, StatusOf(await controller.Bootstrap(Req(ValidKey), CancellationToken.None)));

        var after = await ReadLockAsync(db);
        Assert.Equal(0, after!.Value.FailedCount);              // 리셋됐다
        Assert.Equal(0, after.Value.IsLocked);                  // 잠금이 실제로 풀렸다
        // 풀렸으니 다음 요청도 정상이다(「한 번은 통과하지만 표는 잠긴 채」가 아니다)
        Assert.Equal(200, StatusOf(await controller.Bootstrap(Req(ValidKey), CancellationToken.None)));
    }

    // ══════════════════════════════════════════════════════════════
    // G-5 성공 리셋
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// G-5. 실패 4회 → 유효 키 → 200 <b>그리고</b> <c>failed_count=0</c>.
    /// 리셋이 없으면 4 로 남아 다음 설치에서 오타 1회로 잠긴다.
    /// </summary>
    [Fact]
    public async Task G5_성공하면_실패카운터가_0으로_리셋된다()
    {
        if (!TrySetUpDb(nameof(G5_성공하면_실패카운터가_0으로_리셋된다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedTenantAsync(db);

        var controller = NewBootstrap();
        for (var i = 1; i <= 4; i++)
            Assert.Equal(401, StatusOf(await controller.Bootstrap(Req(WrongKeyPrefix + i), CancellationToken.None)));
        Assert.Equal(4, (await ReadLockAsync(db))!.Value.FailedCount);

        Assert.Equal(200, StatusOf(await controller.Bootstrap(Req(ValidKey), CancellationToken.None)));
        Assert.Equal(0, (await ReadLockAsync(db))!.Value.FailedCount);

        // 다음 오타 1회가 다시 1 에서 시작한다 — 이전 오타가 넘어오지 않는다
        Assert.Equal(401, StatusOf(await controller.Bootstrap(Req(WrongKeyPrefix + 9), CancellationToken.None)));
        Assert.Equal(1, (await ReadLockAsync(db))!.Value.FailedCount);
    }

    // ══════════════════════════════════════════════════════════════
    // G-6 🔴 장애 분리 — 본사가 아파도 고객이 잠기지 않는다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// G-6a. Cloudflare 가 터지는 경우 — 대역이 <b>실제로 예외를 던진다</b>(호출 카운터로 호출됐음을 확인).
    /// 응답은 200(DNS 폴백 설계 그대로)이고 <c>failed_count</c> 증가는 <b>0</b>.
    /// <para>🔴 「CF 를 막았다/태웠다」는 전부 <b>대역 카운터 기준</b>이다 — 외부 실호출 0(C-2).</para>
    /// </summary>
    [Fact]
    public async Task G6a_CF가_예외를_던져도_실패카운터는_0이다()
    {
        if (!TrySetUpDb(nameof(G6a_CF가_예외를_던져도_실패카운터는_0이다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedTenantAsync(db);

        var cf = new ThrowingCfDomain();
        Assert.Equal(200, StatusOf(await NewBootstrap(cf).Bootstrap(Req(ValidKey), CancellationToken.None)));

        Assert.True(cf.IssueCalls >= 1, "CF 대역이 한 번도 안 불렸다 — 이 시험이 장애를 재현하지 못했다.");

        // 🔴 교정 2026-10-07 (FAIL 재현 I2 가 이 게이트를 뚫었다 — 실측으로 알았다):
        //   종전 판정은 「failed_count == 0 이면 통과」였다. 그런데 이 요청은 **성공(200)** 이라
        //   ⑧-b 리셋이 뒤에서 돌아 **0 으로 덮어 준다** ⇒ CF 장애 catch 에 가산을 심어도 초록이었다.
        //   ⇒ 판정을 「행이 아예 없다」로 바꾼다. 가산은 UPSERT 로 **행을 만들고**, 리셋 UPDATE 는
        //      행을 지우지 않으므로 심은 가산이 흔적으로 남는다. (I2 재측정에서 FAIL 확인)
        Assert.Null(await ReadLockAsync(db));
        Assert.Equal(0, await db.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM serial_verify_attempts WHERE result IN ('installer-mismatch','installer-locked')"));
    }

    /// <summary>
    /// G-6b. 본사 DB 가 터지는 경우 — <c>tenants</c> 표를 떨어뜨려 테넌트 조회 자체를 깨뜨린다.
    /// 응답은 500 이고 <c>serial_verify_locks</c> 에 행이 <b>안 생긴다</b>.
    /// <para>가산을 바깥 <c>catch</c> 나 공통 경로에 넣으면 여기서 행이 생긴다 ⇒ FAIL.</para>
    /// </summary>
    [Fact]
    public async Task G6b_DB조회가_깨져도_잠금행이_생기지_않는다()
    {
        if (!TrySetUpDb(nameof(G6b_DB조회가_깨져도_잠금행이_생기지_않는다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 잠금 표는 살려 두고 테넌트 조회만 깨뜨린다 — 그래야 「가산이 닿았나」를 볼 수 있다
        await db.ExecuteAsync("SET FOREIGN_KEY_CHECKS = 0; DROP TABLE IF EXISTS tenants; SET FOREIGN_KEY_CHECKS = 1;");

        Assert.Equal(500, StatusOf(await NewBootstrap().Bootstrap(Req(ValidKey), CancellationToken.None)));
        Assert.Null(await ReadLockAsync(db));
        Assert.Equal(0, await db.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM serial_verify_attempts WHERE client_fingerprint = @Fp", new { Fp = FpHash }));
    }

    // ══════════════════════════════════════════════════════════════
    // G-8 🔴 하위호환 — 선택 필드 없으면 지금과 똑같은 전체 응답
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// G-8. <c>purpose</c> <b>없는</b> 요청 → 응답 항목 이름 <b>전수 일치</b>.
    /// <para>기본값을 축소로 잡으면 고객 PC 의 구 설치본·구 워치독이 전부 깨진다. 이 시험이 그 자리를 잠근다.</para>
    /// </summary>
    [Fact]
    public async Task G8_선택필드_없으면_응답_항목이름이_전수_그대로다()
    {
        if (!TrySetUpDb(nameof(G8_선택필드_없으면_응답_항목이름이_전수_그대로다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedTenantAsync(db);

        var value = OkValue(await NewBootstrap().Bootstrap(Req(ValidKey), CancellationToken.None));
        var names = NamePaths(value);

        // 🔴 숫자를 세지 않는다 — **이름 집합 전수 대조**다([4] 교정 2026-10-07).
        //   설계 §6 G-8 의 「18개」는 어느 셈법으로도 안 나온다(실측 = 이름 19 · 말단 16).
        //   숫자를 기대값으로 쓰면 「개수는 같은데 이름이 바뀐」 하위호환 파괴를 못 잡는다.
        Assert.Equal(FullNames.OrderBy(x => x, StringComparer.Ordinal), names);
        Assert.Empty(names.Except(FullNames));       // 더해진 이름 0개
        Assert.Empty(FullNames.Except(names));       // 빠진 이름 0개
    }

    /// <summary>
    /// G-8b 음성 대조군 — <b>모르는 purpose 값</b>도 전체 응답이다. 오타·옛 값이 축소로 떨어지면 안 된다.
    /// </summary>
    [Fact]
    public async Task G8b_모르는_purpose_값이면_전체_응답이다()
    {
        if (!TrySetUpDb(nameof(G8b_모르는_purpose_값이면_전체_응답이다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedTenantAsync(db);

        var value = OkValue(await NewBootstrap().Bootstrap(
            Req(ValidKey, "install"), CancellationToken.None));
        Assert.Equal(FullNames.OrderBy(x => x, StringComparer.Ordinal), NamePaths(value));
    }

    // ══════════════════════════════════════════════════════════════
    // ⓒ-1 축소 응답 — 🔴 **G-7 이 아니다**(실파서 미투입 · 위 클래스 주석)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// ⓒ-1. <c>purpose="tunnel-recovery"</c> → 전체에서 <b>8개를 뺀 것</b>과 정확히 같다.
    /// <list type="bullet">
    /// <item>빠진 것: tenant 묶음 6 + bootstrap.token + bootstrap.tokenKey</item>
    /// <item>🔴 더해진 이름은 <b>0개</b> — 구 설치본 <c>ExtractJsonValue</c> 평면 검색 보호(설계 §5-3)</item>
    /// <item>🔴 워치독이 읽는 셋(<c>domain.tunnelToken·tunnelId·tunnelTokenIssued</c>)은 <b>그대로</b></item>
    /// </list>
    /// <para>⚠️ 이것은 이름 집합 실측이다. <b>워치독 실파서를 돌린 G-7 이 아니다</b> — 그렇게 읽지 마라.</para>
    /// </summary>
    [Fact]
    public async Task C1_단계2_축소응답은_빼기만_하고_워치독이_읽는_셋은_남긴다()
    {
        if (!TrySetUpDb(nameof(C1_단계2_축소응답은_빼기만_하고_워치독이_읽는_셋은_남긴다))) return;
        await RunRealMigratorAsync();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedTenantAsync(db);

        var value = OkValue(await NewBootstrap().Bootstrap(
            Req(ValidKey, "tunnel-recovery"), CancellationToken.None));
        var names = NamePaths(value);

        Assert.Equal(ReducedNames.OrderBy(x => x, StringComparer.Ordinal), names);
        Assert.Empty(names.Except(FullNames));                        // 더한 이름 0개
        Assert.Equal(8, FullNames.Except(names).Count());             // 뺀 이름 8개
        // 🔴 [4] 교정 2026-10-07 — 워치독이 **실제로 읽는 것은 이 둘뿐**이다.
        //   설계 §5-1 은 domain.tunnelTokenIssued 도 「필요」로 적었지만 그건 TunnelTokenRecovery.cs:128
        //   **주석**이고 TryGetProperty 호출이 0건이다. 그래서 기준은 두 개로 세운다(주석은 코드가 아니다).
        foreach (var kept in new[] { "domain.tunnelToken", "domain.tunnelId" })
            Assert.Contains(kept, names);                             // 워치독이 읽는 둘은 못 뺀다
        // tunnelTokenIssued 는 **빼도 워치독이 안 깨지지만** 변경 최소로 그대로 둔다(그 사실을 고정한다)
        Assert.Contains("domain.tunnelTokenIssued", names);
        foreach (var gone in new[] { "bootstrap.token", "bootstrap.tokenKey", "tenant", "tenant.email" })
            Assert.DoesNotContain(gone, names);

        // 성공 기록은 축소 응답에서도 그대로 남는다(감사 추적 · ⑨ 무접촉)
        Assert.Equal(1, await db.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM serial_verify_attempts WHERE result = 'installer-bootstrap'"));
    }

    // ══════════════════════════════════════════════════════════════
    // G-9 음성 대조군 — 익명 그대로다 (DB 불필요 · 실물 파이프라인)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// G-9. 토큰 <b>0</b> 으로 보낸 요청이 여전히 <b>컨트롤러 안쪽</b>에 닿는다.
    /// <para>판정: 빈 시리얼 요청의 응답이 <c>400</c> 이고 본문이 <b>컨트롤러 자신의 문구</b>를 담는다.
    /// <c>[Authorize]</c> 가 붙으면 인증 파이프라인이 <b>401</b> 을 내고 그 문구는 사라진다 ⇒ FAIL.</para>
    /// <para>🔴 신설 423·401 은 컨트롤러가 낸 것이어야 한다. 이 게이트는 실제 호스트를 띄워
    /// <c>UseAuthentication → UseAuthorization → MVC</c> 를 <b>지나가 본다</b>(메인PC ② 사고 교훈).</para>
    /// <para>DB 불필요 — 400 은 Dapper 접속 앞에서 갈린다(호스트의 연결문자열은 닫힌 포트로 덮여 있다).</para>
    /// </summary>
    [Fact]
    public async Task G9_토큰_0으로도_부트스트랩이_컨트롤러에_닿는다()
    {
        using var http = _pipeline.CreateClient();
        Assert.Null(http.DefaultRequestHeaders.Authorization);   // 토큰 0 — 헤더를 달지 않았다

        using var res = await http.PostAsJsonAsync("/api/installer/bootstrap",
            new { licenseKey = "", machineFingerprint = "" });

        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);   // 파이프라인이 자르지 않았다
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("시리얼 키를 입력해주세요", await res.Content.ReadAsStringAsync());
    }

    // ══════════════════════════════════════════════════════════════
    // 대역 — 바깥 가장자리만 막는다. 🔴 외부 실호출 0 · CF 자격증명 0
    // ══════════════════════════════════════════════════════════════

    /// <summary>CF 꺼진 경우 — 컨트롤러가 <c>IsConfigured</c> 를 먼저 보므로 아래는 불리지 않는다.</summary>
    private sealed class OffCfDomain : ICloudflareDomainService
    {
        public bool IsConfigured => false;
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

    /// <summary>
    /// CF 가 「설정돼 있지만 터지는」 경우 — G-6a 가 쓰는 장애 대역.
    /// <para>🔴 <see cref="ICloudflareDomainService"/> 를 <b>확장하지 않는다</b>(#12 — 구현체 전수 grep 비용 0).
    /// 바깥으로 나가는 요청은 0건이고 자격증명은 어디에도 없다.</para>
    /// </summary>
    private sealed class ThrowingCfDomain : ICloudflareDomainService
    {
        public int IssueCalls;
        public bool IsConfigured => true;
        public Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, CancellationToken ct)
            => IssueAsync(tenantId, tenantCode, null, ct);
        public Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, string? domainAlias, CancellationToken ct)
        {
            Interlocked.Increment(ref IssueCalls);
            throw new HttpRequestException("게이트 장애 대역 — 본사 CF 가 터진 경우(외부 호출 0건)");
        }
        public Task<TunnelIssueResult> IssueTunnelAsync(string tenantId, string tenantCode, CancellationToken ct)
            => throw new HttpRequestException("게이트 장애 대역 — 터널 발급 실패");
        public Task UpdateDnsTunnelTargetAsync(string recordId, string domain, string tunnelId, CancellationToken ct)
            => throw new HttpRequestException("게이트 장애 대역");
        public Task UpdateTunnelIngressAsync(string tunnelId, string hostname, string originUrl, CancellationToken ct)
            => throw new HttpRequestException("게이트 장애 대역");
        public Task<bool> RevokeAsync(string cfZoneId, string cfRecordId, string? cfTunnelId, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
        public Task<bool> RevokeByDomainAsync(string subdomain, CancellationToken ct)
            => throw new NotSupportedException("게이트에서 CF 호출 금지");
    }
}
