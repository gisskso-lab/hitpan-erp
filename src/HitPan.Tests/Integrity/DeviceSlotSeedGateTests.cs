using System.Text.RegularExpressions;
using Dapper;
using HitPan.API.Services;
using HitPan.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-19 ~ G-22</b> — 신규 설치 고객에게 기기 슬롯 기준값이 실제로 깔리는가 (20260816작1 R-1 봉합).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 게이트가 왜 생겼나 — 게이트가 "없던" 자리다</b>
/// </para>
/// <para>
/// 1차(20260815작3)는 빌드 0/0 · 시험 504 · ddl-smoke PASS · 반증 13/13 을 <b>전부 통과</b>했다.
/// 그런데도 P0 가 남았다. 게이트가 <b>틀린</b> 게 아니라 <b>없었기</b> 때문이다.
/// 검증팀장 판정: <i>"통과한 게이트의 개수는 안전의 증거가 아니다."</i>
/// </para>
/// <para>
/// 결함은 이랬다 — DB-104 시드가 <c>SELECT DISTINCT tenant_id FROM local_subscription</c> 기준인데
/// <b>신규 설치 시점엔 그 표가 0행</b>이고(회사가 아직 없다), 출하 DDL 이
/// <c>('DB-104','clean-ddl',1)</c> 로 <b>이미 성공 표시</b>를 해서 나중에도 다시 안 돈다.
/// ⇒ <b>신규 고객의 정책 표는 영원히 비어</b> 요금 한도가 늘 코드 상수로 결정됐다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 실제 DB 를 쓰는가 — 글자 검사로는 이 결함을 못 잡는다</b>
/// </para>
/// <para>
/// 검증팀이 기존 <c>P25</c> 를 <b>가짜 게이트</b>로 판정했다. 인덱스 이름 문자열만 봐서
/// <c>NOT NULL DEFAULT ''</c> 로 바꿔도 초록불이었는데, 그 DDL 이면 <b>두 번째 기기부터 1062 로 죽는다.</b>
/// R-1 도 같은 종류다 — 소스에는 시드가 <b>멀쩡히 적혀 있다.</b> 안 도는 것뿐이다.
/// ⇒ 여기서는 <b>격리 DB 에 출하 DDL 을 넣고 실제로 회사를 만들어 표를 읽는다.</b>
/// 검증팀이 R-1 을 잡은 그 방법 그대로다.
/// </para>
///
/// <para>
/// ⚠️ <b>운영 무접촉</b>(헌법 #39) — 임시 DB(<c>hitpan_slotseed_gate_*</c>)만 만들고 <b>반드시 지운다.</b>
/// <c>demo</c>(3306 운영)·<c>hitpan_erp</c> 는 건드리지 않는다.
/// </para>
/// <para>
/// ⚠️ <b>MariaDB 가 없으면 조용히 통과시킨다</b>(맨 앞에서 <c>return</c>).
/// CI·개발 PC 어디서도 <b>거짓 실패</b>를 만들지 않기 위해서다.
/// <b>이 절충의 대가를 정확히 적는다</b> — DB 가 없는 환경에서는 G-19·G-21·G-22 가
/// <b>아무것도 검사하지 않는다.</b> 초록불이 곧 안전이 아니다.
/// ⇒ 🔴 <b>이 게이트의 진짜 실행은 MariaDB 가 있는 자리에서만 일어난다.</b>
/// 개발명세서에 실제 실행 출력을 남기는 이유가 그것이다.
/// </para>
/// <para>
/// 🔴 다만 <b>G-20 의 값 대조(코드 ↔ DB-104)는 DB 없이도 돈다</b> —
/// 파일 대 코드 비교라서 <b>어디서 돌리든 값이 갈라지면 빨간불</b>이 된다.
/// 값 갈라짐만큼은 환경과 무관하게 막힌다.
/// </para>
/// </remarks>
[Collection("DeviceSlotSeedGate")]
public sealed class DeviceSlotSeedGateTests : IDisposable
{
    // ══════════════════════════════════════════════════════════════
    // 준비물 — 격리 DB · 출하 DDL · 실제 Provisioner
    // ══════════════════════════════════════════════════════════════

    private readonly string _dbName = "hitpan_slotseed_gate_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 소스를 읽을 수 없다.");
    }

    /// <summary>서버 접속 문자열(DB 지정 없음). env 로 CI 를 덮어쓸 수 있다.</summary>
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

    /// <summary>MariaDB 서버 + <c>mysql</c> 실행파일이 다 있는가. 없으면 건너뛴다(거짓 실패 금지).</summary>
    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다 (작14 W1)
        // DDL 을 실행파일로 넣으므로 둘 다 있어야 시험이 성립한다.
        if (!File.Exists(MysqlExe())) return false;

        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            // ⚠️ 붙지 못한 것은 이 시험의 실패가 아니라 **환경 부재**다.
            //   여기서 던지면 DB 없는 CI 가 전부 빨간불이 되어 게이트가 꺼진다.
            return false;
        }
    }

    /// <summary>
    /// <c>mysql</c> 실행파일 경로. <c>ddl-smoke-test.sh</c> 와 <b>같은 규칙</b>을 쓴다.
    /// </summary>
    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL")
        ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    /// <summary>
    /// 🔴 <b>신규 설치를 그대로 재현한다</b> — 빈 DB 에 출하 DDL 한 방(헌법 #36).
    /// </summary>
    /// <remarks>
    /// ⚠️ DDL 을 <c>mysql</c> 실행파일로 넣는 이유는 <b>고객 설치가 그렇게 하기 때문</b>이다.
    /// 출하 DDL 에는 <c>DELIMITER</c>·주석 등 클라이언트가 해석하는 구문이 섞여 있어,
    /// 드라이버로 한 문장씩 쪼개 넣으면 <b>실제 설치와 다른 것을 시험하게 된다.</b>
    /// <c>ddl-smoke-test.sh</c> 와 같은 방식이다.
    /// </remarks>
    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        Assert.True(File.Exists(ddlPath), $"출하 DDL 이 없다: {ddlPath}");

        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add(_dbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddlPath));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0,
            $"출하 DDL import 가 실패했다 — 신규 설치가 같은 자리에서 죽는다:\n{err}");
    }

    /// <summary>실제 <see cref="CompanyBootstrapProvisioner"/> — 시험용 흉내가 아니다.</summary>
    private CompanyBootstrapProvisioner NewProvisioner()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = DbConnString()
            })
            .Build();

        return new CompanyBootstrapProvisioner(
            config, NullLogger<CompanyBootstrapProvisioner>.Instance);
    }

    private static ProofPayload NewPayload(string tenantId, string tenantCode) => new()
    {
        Sub = tenantId,
        TenantCode = tenantCode,
        CompanyName = "게이트시험(주)",
        Subscription = new ProofSubscription { SubscriptionTier = "basic", Status = "active" }
    };

    /// <summary>회사 생성 종단 — bootstrap → 부모계정. 신규 고객이 겪는 순서 그대로다.</summary>
    private async Task<string> CreateCompanyAsync(string tenantId, string tenantCode, string loginId)
    {
        var p = NewProvisioner();

        var (bootstrap, bootMsg) = await p.BootstrapCompanyAsync(
            NewPayload(tenantId, tenantCode),
            new CompanyBootstrapInput { BizNo = "1234567890", CeoName = "대표" },
            CancellationToken.None);
        Assert.True(bootstrap == CompanyBootstrapProvisioner.BootstrapOutcome.Ok,
            $"회사 자동 반영이 실패했다: {bootMsg}");

        var (parent, parentMsg, _) = await p.CreateParentAsync(
            NewPayload(tenantId, tenantCode),
            new CreateParentInput { LoginId = loginId, Password = "Admin1234!", Name = "대표" },
            CancellationToken.None);
        Assert.True(parent == CompanyBootstrapProvisioner.CreateParentOutcome.Ok,
            $"부모계정 생성이 실패했다: {parentMsg}");

        return tenantId;
    }

    private async Task<List<(string Key, int Value)>> ReadPolicyAsync(string tenantId)
    {
        await using var db = new MySqlConnection(DbConnString());
        var rows = await db.QueryAsync<(string k, int v)>(
            "SELECT policy_key, policy_value FROM device_slot_policy_settings WHERE tenant_id = @T ORDER BY policy_key",
            new { T = tenantId });
        return rows.Select(r => (r.k, r.v)).ToList();
    }

    /// <summary>🔴 임시 DB 는 반드시 지운다 — 검증이 흔적을 남기지 않는다(헌법 #39).</summary>
    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
        }
        catch (MySqlException)
        {
            // 지우기 실패가 시험 결과를 뒤집으면 안 된다. 임시 DB 이름에 표식이 있어 사람이 찾을 수 있다.
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-19 — 회사를 만들면 정책 표가 비어 있지 않다  🔴 R-1 본체
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-19 🔴 신규 설치 후 회사를 만들면 기기 슬롯 기준값 표가 비어 있지 않다")]
    public async Task G19_신규설치_회사생성후_정책표가_비어있지_않다()
    {
        if (!ServerAvailable()) return;   // MariaDB 가 없으면 건너뛴다 (ServerAvailable 주석 참조)

        SetUpFreshInstall();

        var tenantId = Guid.NewGuid().ToString();

        // 🔴 회사를 만들기 전에는 비어 있는 것이 정상이다 — 이것이 R-1 의 출발점이었다.
        //   출하 DDL 만 넣은 상태에서 표가 0행이고, DB-104 는 이미 success=1 이라 다시 안 돈다.
        Assert.Empty(await ReadPolicyAsync(tenantId));

        await CreateCompanyAsync(tenantId, "T-G19", "gate19");

        var rows = await ReadPolicyAsync(tenantId);

        Assert.True(rows.Count > 0,
            "🔴 회사를 만들었는데 기기 슬롯 기준값 표가 비어 있다 — R-1 재발이다. "
            + "신규 고객은 요금 한도가 영원히 코드 상수로 결정되고, "
            + "대표가 설정 화면에서 고쳐도 반영될 표가 없다(헌법 #11 미달성). "
            + "[반증] CompanyBootstrapProvisioner 의 시드 호출을 빼면 이 시험이 FAIL 한다.");

        // 열쇠가 다 왔는가 — 일부만 오면 나머지가 조용히 안전망으로 떨어진다.
        Assert.Equal(SlotPolicyDefaults.Rows.Count, rows.Count);
    }

    // ══════════════════════════════════════════════════════════════
    // G-20 — 시드된 값이 안전망(FallbackLimits)·마이그(DB-104)와 일치한다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-20 🔴 시드된 값이 안전망(FallbackLimits) 숫자와 정확히 같다")]
    public async Task G20_시드값이_안전망과_일치한다()
    {
        if (!ServerAvailable()) return;   // MariaDB 가 없으면 건너뛴다 (ServerAvailable 주석 참조)

        SetUpFreshInstall();

        var tenantId = Guid.NewGuid().ToString();
        await CreateCompanyAsync(tenantId, "T-G20", "gate20");

        var seeded = (await ReadPolicyAsync(tenantId))
            .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

        // 🔴 이 봉합으로 한도가 **하나도 안 바뀌어야** 한다(작지서 §2 "숫자 변경 금지").
        //   설정표를 읽었을 때와 안 읽었을 때(안전망) 결과가 같아야 무회귀다.
        //   ⇒ 실제 판정부를 두 번 불러 값으로 대조한다. 글자 검사가 아니다.
        var empty = new Dictionary<string, int>();

        foreach (var tier in new[] { "basic", "pro", "enterprise", "premium", "trial", "모르는요금제" })
        {
            var withSeed = TenantDeviceService.ResolveLimits(tier, 0, seeded);
            var withFallback = TenantDeviceService.ResolveLimits(tier, 0, empty);

            Assert.True(withSeed == withFallback,
                $"🔴 '{tier}' 의 한도가 시드({withSeed})와 안전망({withFallback})에서 다르다. "
                + "이 봉합은 한도를 하나도 바꾸면 안 된다 — 숫자가 갈리면 "
                + "신규 고객과 기존 고객이 서로 다른 요금 한도로 돈다. "
                + "[반증] SlotPolicyDefaults 의 값을 하나만 바꿔도 FAIL 한다.");
        }

        // 추가슬롯 산식(1+1)도 시드에서 와야 한다.
        Assert.Equal(
            TenantDeviceService.ResolveLimits("basic", 3, empty),
            TenantDeviceService.ResolveLimits("basic", 3, seeded));
    }

    /// <summary>
    /// 🔴 <b>값 갈라짐 방지의 핵심</b> — 코드 정의와 마이그 DB-104 를 <b>전수 대조</b>한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 이제 같은 숫자를 두 표현이 안다 — ①마이그 <c>DB-104</c>(기존 고객이 받는 것) ·
    /// ②<see cref="SlotPolicyDefaults"/>(신규 고객이 받는 것 + 안전망).
    /// <b>DB-104 는 고칠 수 없다</b>(이미 적용된 고객이 있을 수 있어 마이그는 되돌리지 않는다).
    /// 따라서 완전한 단일 정의는 불가능하고, <b>기계가 대조하는 것</b>이 차선이다.
    /// </para>
    /// <para>
    /// ⚠️ 이 시험은 <b>DB 가 없어도 돈다</b> — 파일 대 코드 대조라서다.
    /// 값이 갈라지는 순간 <b>어디서 돌리든 빨간불</b>이 된다.
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "G-20 🔴 코드 시드 정의와 마이그 DB-104 의 값이 한 글자도 다르지 않다")]
    public void G20_코드정의와_마이그DB104가_일치한다()
    {
        var sqlPath = Path.Combine(RepoRoot(),
            "src", "HitPan.API", "Migrations", "SQL", "DB-104_device_slot_policy_settings.sql");
        Assert.True(File.Exists(sqlPath), $"DB-104 마이그가 없다: {sqlPath}");

        var sql = File.ReadAllText(sqlPath);

        // 시드 블록의 '열쇠', 값 짝을 전부 뽑는다.
        //   DB-104 는 첫 줄만 `'키' AS policy_key, 5 AS policy_value` 형태이고
        //   나머지는 `'키', 5` 형태라 둘 다 받는다.
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(sql,
                     @"'((?:tier|extra_slot)\.[a-z_.]+)'\s*(?:AS\s+policy_key\s*)?,\s*(\d+)",
                     RegexOptions.IgnoreCase))
        {
            found[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }

        Assert.True(found.Count > 0,
            "DB-104 에서 시드 값을 하나도 못 읽었다 — 이 게이트의 탐지 방식이 깨졌다(형식이 바뀌었는가).");

        // ① 코드가 아는 열쇠는 전부 마이그에도 같은 값으로 있어야 한다.
        foreach (var row in SlotPolicyDefaults.Rows)
        {
            Assert.True(found.TryGetValue(row.Key, out var sqlValue),
                $"🔴 코드 시드에 '{row.Key}' 가 있는데 마이그 DB-104 에는 없다. "
                + "기존 고객과 신규 고객이 서로 다른 기준값을 받는다.");

            Assert.True(sqlValue == row.Value,
                $"🔴 '{row.Key}' 값이 갈렸다 — 코드 {row.Value} vs 마이그 DB-104 {sqlValue}. "
                + "기존 고객(마이그)과 신규 고객(회사 생성 시드)이 서로 다른 요금 한도로 돈다. "
                + "값은 SlotPolicyDefaults 한 곳에서 정의하고 DB-104 와 같아야 한다.");
        }

        // ② 반대 방향 — 마이그에만 있는 열쇠가 있으면 신규 고객이 그것을 못 받는다.
        var missingInCode = found.Keys
            .Where(k => !SlotPolicyDefaults.Rows.Any(r => r.Key.Equals(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(missingInCode.Count == 0,
            "🔴 마이그 DB-104 에만 있는 열쇠가 있다 — 기존 고객은 받는데 신규 고객은 못 받는다: "
            + string.Join(", ", missingInCode));
    }

    // ══════════════════════════════════════════════════════════════
    // G-21 — 두 번 돌려도 행이 안 늘어난다 (멱등)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-21 🔴 회사 생성 시드를 두 번 돌려도 행이 늘어나지 않는다(멱등)")]
    public async Task G21_두번_돌려도_행이_늘지_않는다()
    {
        if (!ServerAvailable()) return;   // MariaDB 가 없으면 건너뛴다 (ServerAvailable 주석 참조)

        SetUpFreshInstall();

        var tenantId = Guid.NewGuid().ToString();
        await CreateCompanyAsync(tenantId, "T-G21", "gate21");

        var first = await ReadPolicyAsync(tenantId);
        Assert.NotEmpty(first);

        // 🔴 두 번째 — **고객이 실제로 겪는 경로 그대로** 다시 태운다.
        //
        //   ⚠️ 이 자리가 **가짜 게이트였다**(CR2-1 이 잡았다). 종전에는 여기서
        //     `DELETE FROM users/employees` 로 부모계정을 지운 뒤 CreateParentAsync 를 불렀다.
        //     그러면 조기 return(ParentExists)을 **우회**해서 시드까지 내려갔고,
        //     "시드가 두 번 돈다" 는 것은 확인됐지만 **기존 회사가 시드에 닿는가** 는
        //     검사하지 못했다. 오히려 그 DELETE 가 **R-1 절반 봉합을 가려 주고 있었다.**
        //     (게이트가 결함을 숨긴 자리다 — 인수인계서 §5-2 #4)
        //
        //   ⇒ 이제 **아무것도 지우지 않는다.** 이미 설치가 끝난 회사가 다시 오는,
        //     즉 AlreadyLocked / ParentExists 로 돌아가는 **진짜 운영 경로**를 그대로 태운다.
        //     이 경로에서도 기준값이 깔려야 하고(CR2-1), 두 번 와도 안 늘어나야 한다(멱등).
        var p2 = NewProvisioner();

        var (boot2, boot2Msg) = await p2.BootstrapCompanyAsync(
            NewPayload(tenantId, "T-G21"),
            new CompanyBootstrapInput { BizNo = "1234567890", CeoName = "대표" },
            CancellationToken.None);

        // 이미 잠긴 회사이므로 AlreadyLocked 가 정상이다 — 그래도 시드는 지나가야 한다.
        Assert.True(boot2 == CompanyBootstrapProvisioner.BootstrapOutcome.AlreadyLocked,
            $"이미 설치된 회사인데 AlreadyLocked 가 아니다 — 경로 전제가 깨졌다: {boot2Msg}");

        var (again, _, _) = await p2.CreateParentAsync(
            NewPayload(tenantId, "T-G21"),
            new CreateParentInput { LoginId = "gate21b", Password = "Admin1234!", Name = "대표2" },
            CancellationToken.None);

        // 부모계정이 이미 있으므로 ParentExists 가 정상이다 — 시드에는 닿지 못하는 경로다.
        //   그래서 CR2-1 봉합이 bootstrap 쪽에 있어야 한다.
        Assert.True(again == CompanyBootstrapProvisioner.CreateParentOutcome.ParentExists,
            "이미 부모계정이 있는데 ParentExists 가 아니다 — 경로 전제가 깨졌다.");

        var second = await ReadPolicyAsync(tenantId);

        Assert.True(first.Count == second.Count,
            $"🔴 시드를 두 번 돌렸더니 행이 {first.Count} → {second.Count} 로 늘었다 — 멱등이 깨졌다. "
            + "업데이트 재시도로 같은 열쇠가 두 벌이 되면 어느 값이 읽힐지 알 수 없다.");

        // 🔴 값도 그대로여야 한다 — 행 수만 보면 못 잡는 것이 있다.
        //
        //   ⚠️ **행 수 검사만으로는 이 게이트가 아무것도 안 지킨다**(반증으로 확인했다).
        //     `uk_slot_policy_tenant_key` UNIQUE 가 DB 수준에서 중복을 막으므로,
        //     코드의 가드를 **둘 다 빼도** 행은 늘지 않는다(1062 가 나고 catch 가 삼킨다).
        //     즉 종전 assert 는 **코드가 아니라 UNIQUE 제약을 시험**하고 있었다.
        //     (P25·G-8 과 같은 종류의 가짜 게이트다 — 인수인계서 §5-2)
        //
        //   ⇒ 값 대조를 함께 건다. 두 번째 시드가 값을 덮어쓰면(ON DUPLICATE KEY UPDATE 등)
        //     행 수는 그대로여도 여기서 잡힌다.
        Assert.Equal(
            first.OrderBy(r => r.Key).ToList(),
            second.OrderBy(r => r.Key).ToList());
    }

    // ══════════════════════════════════════════════════════════════
    // G-23 — 🔴 이미 설치가 끝난 회사도 기준값을 받는다 (CR2-1 본체)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>CR2-1</b> — R-1 봉합이 <b>절반만</b> 됐던 자리를 지킨다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1차 봉합은 시드를 <c>CreateParentAsync</c> <b>맨 끝</b>에 뒀다. 그런데 그 함수는
    /// <c>ParentExists</c>·<c>BootstrapMissing</c>·<c>DuplicateLogin</c> 로
    /// <b>시드에 닿기 전에 돌아간다.</b> 이미 설치가 끝난 회사는 <b>항상</b> 그 길로 돌아간다 —
    /// 즉 <b>R-1 이 덮으려던 바로 그 집단이 여전히 못 받고 있었다.</b>
    /// </para>
    /// <para>
    /// ⚠️ 종전 G-21 은 이것을 <b>못 잡았다</b>. <c>DELETE FROM users</c> 로 조기 return 을
    /// 우회했기 때문이다 — <b>게이트가 결함을 가렸다.</b> 그래서 이 시험은
    /// <b>아무것도 지우지 않고</b> 기존 회사 그대로 다시 온다.
    /// </para>
    /// <para>
    /// [반증] <c>BootstrapCompanyAsync</c> 의 <c>AlreadyLocked</c> 앞 시드 호출을 빼면 FAIL 한다.
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "G-23 🔴 이미 설치가 끝난 회사(기존 고객)도 기기 슬롯 기준값을 받는다")]
    public async Task G23_기존고객도_기준값을_받는다()
    {
        if (!ServerAvailable()) return;   // MariaDB 가 없으면 건너뛴다 (ServerAvailable 주석 참조)

        SetUpFreshInstall();

        var tenantId = Guid.NewGuid().ToString();

        // ── ① 정책 표가 없던 시절에 설치된 회사를 만든다 ──
        //   R-1 이 살아 있던 때의 고객을 그대로 재현한다: 회사·부모계정은 있는데 기준값은 0행.
        await CreateCompanyAsync(tenantId, "T-G23", "gate23");

        await using (var db = new MySqlConnection(DbConnString()))
        {
            await db.OpenAsync();
            await db.ExecuteAsync(
                "DELETE FROM device_slot_policy_settings WHERE tenant_id = @T", new { T = tenantId });
        }

        // 전제 확인 — R-1 상태(표가 비어 있다)를 정말로 만들었는가.
        //   ⚠️ 이 DELETE 는 **운영 경로를 우회하려는 것이 아니라** 옛 고객 상태를 만드는 것이다.
        //     아래 실제 호출에서는 아무것도 지우지 않는다.
        Assert.Empty(await ReadPolicyAsync(tenantId));

        // ── ② 업데이트를 받은 뒤 그 고객이 평소처럼 다시 온다 ──
        //   아무것도 지우지 않는다. 이미 잠긴 회사라 AlreadyLocked 로 돌아가는 길이다.
        var p = NewProvisioner();
        var (outcome, _) = await p.BootstrapCompanyAsync(
            NewPayload(tenantId, "T-G23"),
            new CompanyBootstrapInput { BizNo = "1234567890", CeoName = "대표" },
            CancellationToken.None);

        Assert.True(outcome == CompanyBootstrapProvisioner.BootstrapOutcome.AlreadyLocked,
            "이미 설치가 끝난 회사인데 AlreadyLocked 가 아니다 — 이 시험의 전제가 깨졌다.");

        // ── ③ 그 길로도 기준값이 깔렸는가 ──
        var rows = await ReadPolicyAsync(tenantId);

        Assert.True(rows.Count > 0,
            "🔴 이미 설치가 끝난 회사가 기기 슬롯 기준값을 못 받았다 — CR2-1 재발이다. "
            + "R-1 이 덮으려던 집단이 바로 이 회사들이다. 시드가 조기 return 뒤에 있으면 "
            + "기존 고객은 영원히 안전망으로만 돌고, 대표가 설정 화면에서 고쳐도 반영될 표가 없다. "
            + "[반증] BootstrapCompanyAsync 의 AlreadyLocked 앞 시드 호출을 빼면 FAIL 한다.");

        Assert.Equal(SlotPolicyDefaults.Rows.Count, rows.Count);
    }

    // ══════════════════════════════════════════════════════════════
    // G-22 — 이미 값을 고친 회사는 덮어쓰지 않는다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-22 🔴 대표가 이미 고친 기준값을 시드가 덮어쓰지 않는다")]
    public async Task G22_이미_고친_회사는_덮어쓰지_않는다()
    {
        if (!ServerAvailable()) return;   // MariaDB 가 없으면 건너뛴다 (ServerAvailable 주석 참조)

        SetUpFreshInstall();

        var tenantId = Guid.NewGuid().ToString();

        // 🔴 대표가 이미 설정 화면에서 고쳐 둔 상태를 만든다.
        //
        //   ⚠️ **마지막 열쇠**를 고른 것은 의도적이다. 시드는 목록 순서대로 도는데,
        //     첫 열쇠를 고르면 시드가 그 열쇠에서 UNIQUE(1062)로 **바로 죽어**
        //     나머지를 시도조차 안 한다. 그러면 "안 덮어썼다" 는 결과가
        //     **막아서가 아니라 죽어서** 나오고, 게이트는 그 차이를 못 본다.
        //     (실제로 반증 중에 이 함정을 밟았다 — 변형을 넣었는데 초록불이었다.)
        //   ⇒ 마지막 열쇠를 고르면 시드가 앞의 14줄을 **먼저 넣으려 시도**하므로
        //     테넌트 단위 무접촉이 깨졌을 때 **행 수로 반드시 드러난다.**
        var 대표가고친열쇠 = SlotPolicyDefaults.Rows[^1].Key;
        const int 대표가정한값 = 99999;

        await using (var db = new MySqlConnection(DbConnString()))
        {
            await db.OpenAsync();
            await db.ExecuteAsync(@"
                INSERT INTO device_slot_policy_settings
                  (policy_id, tenant_id, policy_key, policy_value, value_unit, label, updated_reason)
                VALUES (@Id, @T, @Key, @Val, 'krw', '대표가 정한 값', '대표가 직접 고침')",
                new { Id = Guid.NewGuid().ToString(), T = tenantId, Key = 대표가고친열쇠, Val = 대표가정한값 });
        }

        await CreateCompanyAsync(tenantId, "T-G22", "gate22");

        var rows = (await ReadPolicyAsync(tenantId))
            .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

        // ① 대표가 정한 값이 그대로 살아 있는가
        Assert.True(rows[대표가고친열쇠] == 대표가정한값,
            $"🔴 대표가 {대표가정한값} 로 고쳐 둔 '{대표가고친열쇠}' 를 시드가 {rows[대표가고친열쇠]} 로 덮어썼다 — "
            + "헌법 #1(덮어쓰기 금지) · #11(기준값은 어드민이 설정) 위반이다. "
            + "요금 한도를 우리가 말없이 되돌리면 대표가 화면에서 고친 것이 사라진다. "
            + "[반증] ON DUPLICATE KEY UPDATE 로 바꾸면 FAIL 한다.");

        // ② 🔴 손댄 회사는 **통째로** 무접촉이다 — DB-104 의 의도를 그대로 옮겼다.
        //   대표가 지운 열쇠를 우리가 되살리지 않는다. 그것도 대표의 설정이기 때문이다.
        //   [반증] 테넌트 단위 판단을 없애면 나머지 14줄이 들어와 여기서 FAIL 한다.
        Assert.True(rows.Count == 1,
            $"🔴 이미 값이 있는 회사에 시드가 {rows.Count - 1} 줄을 더 넣었다(총 {rows.Count}줄). "
            + "DB-104 의 WHERE NOT EXISTS 는 **테넌트 단위**로 무접촉이다 — "
            + "대표가 지운 열쇠를 되살리는 것도 설정을 덮어쓰는 것이다.");
    }
}
