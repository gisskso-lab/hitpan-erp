using Dapper;
using HitPan.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-31 · G-32 · G-33 · G-34</b> — 기기 승인 봉합 3차 (20260818작1).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 게이트가 왜 생겼나</b> — 설계서가 <i>"통과 못 하면 배포 금지"</i> 라 한 게이트가 <b>없어서</b>
/// 사장님 증상을 <b>세 번 게시하도록 아무도 못 잡았다.</b> 그래서 고치기 <b>전에</b> 세운다.
/// </para>
///
/// <para>
/// 🔴 <b>글자를 안 본다 — 값을 본다.</b> 8/15~16 에 가짜 게이트가 다섯 번 나왔고 원인이 전부 같았다:
/// <c>Assert.Contains</c> 로 소스 글자를 검사하면 <b>주석만 고쳐도 초록불</b>이 된다.
/// 여기서는 <b>격리 DB 에 출하 DDL 을 넣고 실제 서비스를 불러</b> 돌아온 값을 본다.
/// </para>
///
/// <para>
/// 🔴 <b>초록불이 어디서 오는지 물었다.</b> 8/16 G-21 이 <c>UNIQUE(tenant_id, policy_key)</c> 때문에
/// <b>코드 가드를 둘 다 빼도 초록불</b>이었다 — 내 코드가 아니라 <b>DB 제약조건</b>을 시험하고 있었다.
/// </para>
/// <para>
/// ⚠️ <b>이 표에도 같은 덫이 있다</b> — <c>tenant_devices</c> 에 <c>UNIQUE uq_tenant_fp(tenant_id, fingerprint)</c> 가
/// <b>실재한다.</b> 그래서 <b>지문을 고정한 채</b> 두 번째 기기를 만들려 하면
/// <b>내 코드가 아니라 DB 가 막아</b> 초록불이 된다.
/// ⇒ 🔴 아래 게이트는 <b>전부 지문을 매번 다르게</b> 준다. 막는 것이 <b>코드여야</b> 하기 때문이다.
/// </para>
///
/// <para>
/// 🔴 <b>게이트는 "두 번째 접속"을 센다.</b> 한 번 넣어서 맞는지가 아니라 —
/// <b>남의 열쇠를 들고 왔을 때 남의 줄이 열리는가</b>, <b>승인 안 난 줄의 번호로 문이 열리는가.</b>
/// </para>
///
/// <para>
/// ⚠️ <b>운영 무접촉</b>(헌법 #39) — 임시 DB(<c>hitpan_andkey_gate_*</c>)만 만들고 <b>반드시 지운다.</b>
/// <c>demo</c>(3306 운영)·<c>hitpan_erp</c> 는 건드리지 않는다.
/// </para>
/// <para>
/// ⚠️ <b>MariaDB 가 없으면 조용히 통과시킨다.</b> CI·개발 PC 어디서도 <b>거짓 실패</b>를 만들지 않기 위해서다.
/// <b>이 절충의 대가를 정확히 적는다</b> — DB 가 없는 환경에서 이 게이트는 <b>아무것도 검사하지 않는다.</b>
/// 초록불이 곧 안전이 아니다. ⇒ 🔴 <b>진짜 실행은 MariaDB 가 있는 자리에서만 일어난다.</b>
/// 개발명세서에 실제 실행 출력을 남기는 이유가 그것이다.
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class DeviceAndKeyGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_andkey_gate_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "11111111-1111-1111-1111-111111111111";

    // ══════════════════════════════════════════════════════════════
    // 준비물 — 격리 DB · 출하 DDL · 실제 서비스
    // ══════════════════════════════════════════════════════════════

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

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL")
        ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    /// <summary>MariaDB 서버 + <c>mysql</c> 실행파일이 다 있는가. 없으면 건너뛴다(거짓 실패 금지).</summary>
    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다 (작14 W1)
        if (!File.Exists(MysqlExe())) return false;
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    /// <summary>🔴 신규 설치를 그대로 재현한다 — 빈 DB 에 출하 DDL 한 방(헌법 #36).</summary>
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

    /// <summary>🔴 실제 <see cref="TenantDeviceService"/> — 시험용 흉내가 아니다.</summary>
    private TenantDeviceService NewService(MySqlConnection db, bool approvalEnabled = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeviceApproval:Enabled"] = approvalEnabled ? "true" : "false"
            })
            .Build();

        return new TenantDeviceService(
            db, new NoOpAudit(), config, NullLogger<TenantDeviceService>.Instance);
    }

    /// <summary>감사기록은 이 게이트의 관심사가 아니다 — 표에 안 쓰고 조용히 받는다.</summary>
    private sealed class NoOpAudit : HitPan.Application.Interfaces.IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>기기 한 줄을 직접 넣는다 — 상태·메인PC 여부·지문을 시험이 정확히 지정하기 위해서다.</summary>
    private static async Task InsertDeviceAsync(
        MySqlConnection db, string deviceId, string status,
        string fingerprint, bool isMainPc = false, string deviceType = "pc",
        string? authKeyHash = null)
    {
        await db.ExecuteAsync(
            """
            INSERT INTO tenant_devices
              (device_id, tenant_id, device_type, device_name, fingerprint,
               ip_address, status, auth_key_hash, registered_at, is_main_pc)
            VALUES
              (@Id, @Tid, @Type, @Name, @Fp, '127.0.0.1', @Status, @Hash, NOW(6), @Main)
            """,
            new
            {
                Id = deviceId,
                Tid = TenantId,
                Type = deviceType,
                Name = "게이트시험기기",
                Fp = fingerprint,
                Status = status,
                Hash = authKeyHash,
                Main = isMainPc ? 1 : 0
            });
    }

    /// 🔴 <b>그 줄이 지금 어떤 상태인가 — 표에서 직접 읽는다.</b>
    ///
    /// <remarks>
    /// 🔴 <b>왜 반환값을 안 보고 표를 보는가</b> (2026-08-18 PM 실측 적발).
    /// <para>
    /// 종전 G-31 은 <c>VerifyAuthKeyAsync</c> 의 <b>반환값</b>이 A 인지 아닌지를 봤다.
    /// 그런데 그 함수는 반환값을 <c>sessionDeviceId</c>(=B)로 <b>고정</b>해 돌려준다.
    /// ⇒ 내부에서 <b>A 의 줄을 집어 A 의 해시로 대조해도</b> 돌아오는 값은 B 라서
    /// <c>!= deviceA</c> 가 성립한다 — <b>결함이 있는데 초록불</b>이었다.
    /// </para>
    /// <para>
    /// 🔴 <b>반환값은 함수가 마음대로 정하는 값이다.</b> 그걸 시험하면
    /// <b>동작이 아니라 함수의 성실성</b>을 시험하는 것이다.
    /// </para>
    /// <para>
    /// 🔴 <b>표는 속일 수 없다.</b> 1회용 소거(<c>auth_key_hash=NULL</c>)와 승인(<c>status</c>)은
    /// <b>실제로 열린 줄에만</b> 일어난다 ⇒ <b>누구의 키가 사라졌는지가 곧 누구의 줄이 열렸는지</b>다.
    /// </para>
    /// </remarks>
    private static async Task<(string? hash, string status)> ReadRowAsync(MySqlConnection db, string deviceId)
    {
        var row = await db.QueryFirstOrDefaultAsync<(string? hash, string status)>(
            "SELECT auth_key_hash AS hash, status AS status FROM tenant_devices WHERE device_id = @Id",
            new { Id = deviceId });
        return row;
    }

    private static string Sha256Hex(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// 🔴 슬롯 한도를 시험이 정확히 지정한다 — 승인(ApproveAsync)이 한도를 다시 보기 때문이다.
    ///   (DeviceTypeQrGateTests 의 같은 헬퍼와 같은 모양 — 갈리면 한쪽만 고쳐지는 사고가 난다)
    private static async Task SetSlotPolicyAsync(MySqlConnection db, int pcLimit, int mobileLimit)
    {
        // ⚠️ `policy_id`(PK)·`label` 은 기본값이 없다 — 실측(헌법 #13 DESCRIBE 선행).
        await db.ExecuteAsync(
            """
            INSERT INTO device_slot_policy_settings
              (policy_id, tenant_id, policy_key, policy_value, label)
            VALUES
              (@PcId, @Tid, 'tier.basic.pc_limit', @Pc, '컴퓨터 한도(시험)'),
              (@MobId, @Tid, 'tier.basic.mobile_limit', @Mobile, '휴대기기 한도(시험)')
            ON DUPLICATE KEY UPDATE policy_value = VALUES(policy_value)
            """,
            new
            {
                PcId = Guid.NewGuid().ToString(),
                MobId = Guid.NewGuid().ToString(),
                Tid = TenantId,
                Pc = pcLimit,
                Mobile = mobileLimit
            });
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
            // 지우기 실패가 시험 결과를 뒤집으면 안 된다. 이름에 표식이 있어 사람이 찾을 수 있다.
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-31 — 남의 열쇠로 남의 줄이 열리지 않는다  🔴 1-1 본체
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-31. 기기 B 가 기기 A 의 인증키를 넣어도 A 의 번호를 받지 못한다.</b>
    ///
    /// <para>
    /// [무엇이 문제였나] <c>VerifyAuthKeyAsync</c> 가 <b>키만 보고 줄을 검색</b>했다 —
    /// <c>WHERE tenant_id=@T AND auth_key_hash=@H AND status='approved'</c> 에
    /// <b><c>device_id</c> 조건이 없다.</b> 남의 키를 넣으면 <b>그 남의 줄이 그대로 반환</b>된다.
    /// ⇒ 인증키가 <b>회사 공용 열쇠</b>가 되어 아무 기기나 남의 줄을 열고 통과한다.
    /// <b>요금과 접근통제가 동시에 무너지는 자리다.</b>
    /// </para>
    ///
    /// <para>
    /// [무엇을 보는가 — 글자가 아니다] 실제 격리 DB 에 <b>A(승인·키 보유)</b> 와
    /// <b>B(대기·키 없음)</b> 두 줄을 넣고, <b>B 의 자격으로</b> A 의 키를 넣는다.
    /// 돌아온 <c>device_id</c> 가 <b>A 면 FAIL</b> 이다 — 그것이 남의 줄이 열렸다는 증거다.
    /// </para>
    ///
    /// <para>
    /// 🔴 [초록불이 어디서 오나] <b>DB 제약조건이 아니다.</b> 두 줄은 지문이 서로 달라
    /// <c>uq_tenant_fp</c> 에 걸리지 않고 <b>둘 다 정상 INSERT 된다.</b>
    /// 그래서 여기서 막는 것은 <b>오직 내 코드</b>다.
    /// </para>
    ///
    /// <para>[반증] 1-1 을 되돌려 세션 조건을 빼면 A 의 번호가 반환되어 이 시험이 FAIL 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-31 🔴 남의 인증키를 넣어도 남의 기기 번호가 열리지 않는다")]
    public async Task G31_남의_인증키로_남의_줄이_열리지_않는다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        // A — 승인된 기기. 인증키를 갖고 있다.
        const string deviceA = "aaaaaaaa-0000-0000-0000-00000000000a";
        const string authKeyA = "AAAA1111BBBB2222CCCC3333DDDD4444";
        await InsertDeviceAsync(db, deviceA, "approved", "FP-A-고유지문", authKeyHash: Sha256Hex(authKeyA));

        // B — 승인 대기 기기. 키가 없다. ⚠️ 지문을 A 와 다르게 준다 —
        //   같게 주면 uq_tenant_fp UNIQUE 가 INSERT 를 막아 **DB 를 시험하게 된다**(8/16 G-21 사고).
        const string deviceB = "bbbbbbbb-0000-0000-0000-00000000000b";
        await InsertDeviceAsync(db, deviceB, "pending", "FP-B-고유지문");

        // 🔴 B 가 A 의 키를 넣는다 — 훔쳤거나, 옆에서 봤거나, 회사에 돌아다니는 키다.
        var opened = await svc.VerifyAuthKeyAsync(authKeyA, TenantId, deviceB);

        // ══════════════════════════════════════════════════════════════
        // 🔴 판정은 **표**로 한다. 반환값으로 하지 않는다.
        //
        //   [왜 바꿨나 — 2026-08-18 PM 실측 적발]
        //     종전 판정문은 `Assert.True(opened != deviceA)` 였다.
        //     그런데 이 함수는 반환값을 **sessionDeviceId(=B)로 고정**해 돌려준다.
        //     ⇒ 내부에서 **A 의 줄을 집어 A 의 해시로 대조해도** 돌아오는 값은 B 라서
        //       `!= deviceA` 가 성립한다. **간판 봉합(줄 특정 조건)을 통째로 빼도 초록불**이었다.
        //     🔴 **반환값은 함수가 마음대로 정하는 값**이라 그걸 시험하면
        //       동작이 아니라 **함수의 성실성**을 시험하는 것이다.
        //
        //   [지금] **A 의 줄이 건드려졌는지**를 표에서 직접 본다.
        //     1회용 소거와 승인은 **실제로 열린 줄에만** 일어난다 —
        //     ⇒ **누구의 키가 사라졌는지가 곧 누구의 줄이 열렸는지**다. 이건 못 속인다.
        // ══════════════════════════════════════════════════════════════
        var (aHash, aStatus) = await ReadRowAsync(db, deviceA);
        var (_, bStatus) = await ReadRowAsync(db, deviceB);

        Assert.True(
            aHash is not null,
            "🔴 남의 열쇠로 **남의 줄이 열렸다** — B 가 A 의 키를 넣었더니 " +
            "**A 의 인증키가 소거**됐다(1회용 소거는 열린 줄에만 일어난다). " +
            "인증키가 회사 공용 열쇠가 된 상태다: 아무 기기나 남의 줄을 열고 통과한다. " +
            "키는 '맞나 틀리나' 만 판정해야 하고, 무엇을 열지는 서버가 세션에서 정해야 한다(1-1).");

        Assert.True(
            aStatus == "approved",
            $"A 의 상태가 바뀌었다(지금: {aStatus}). 남의 요청이 A 의 줄을 건드렸다는 뜻이다.");

        Assert.True(
            bStatus == "pending",
            $"🔴 B 가 **남의 키로 승인됐다**(지금: {bStatus}). " +
            "자기 줄의 키가 아닌데 문이 열렸다 — 대조가 자기 줄에서 일어나지 않았다.");

        // 반환값도 함께 본다 — 보조 증거다(이것만으로는 부족하다는 것이 이번 교훈이다).
        Assert.True(
            opened is null,
            $"남의 키를 넣었는데 무언가 열렸다(돌아온 값: {opened}).");
    }

    /// <summary>
    /// <b>G-31-b. 자기 줄의 키를 넣으면 정상적으로 열린다 — 좁히다가 다 막으면 안 된다.</b>
    ///
    /// <para>
    /// 🔴 <b>왜 이 짝이 필요한가</b> — G-31 만 있으면 <c>return null</c> 한 줄로 통과시킬 수 있다.
    /// 그러면 <b>아무도 인증을 못 한다.</b> 8/10 사고(쓰던 사람이 새로 막힘)와 같은 모양이다.
    /// ⇒ <b>막는 게이트와 통하는 게이트를 한 쌍으로 둔다.</b>
    /// </para>
    ///
    /// <para>[반증] <c>VerifyAuthKeyAsync</c> 가 무조건 null 을 주게 하면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-31-b 자기 인증키를 넣으면 자기 기기가 정상 인증된다")]
    public async Task G31b_자기_인증키는_정상_인증된다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string deviceB = "bbbbbbbb-0000-0000-0000-00000000000b";
        const string authKeyB = "BBBB1111CCCC2222DDDD3333EEEE4444";
        await InsertDeviceAsync(db, deviceB, "pending", "FP-B-고유지문", authKeyHash: Sha256Hex(authKeyB));

        var opened = await svc.VerifyAuthKeyAsync(authKeyB, TenantId, deviceB);

        // 🔴 판정은 표로 한다 — 자기 키를 넣었으면 **자기 줄이 실제로 승인되고
        //   사람 키가 기계비밀로 교체**돼야 한다(20260819작1 K-1).
        //   반환값만 보면 "값은 맞게 주면서 표는 안 고치는" 구현도 통과한다.
        var (bHash, bStatus) = await ReadRowAsync(db, deviceB);

        Assert.True(
            bStatus == "approved",
            $"자기 키를 넣었는데 자기 줄이 승인되지 않았다(지금: {bStatus} / 돌아온 값: {opened ?? "null"}). " +
            "좁히는 봉합이 정상 경로까지 막았다 — 8/10 사고(쓰던 사람이 새로 막힘)와 같은 모양이다.");

        // 🔴 K-1 (20260819작1) — 소거(NULL)가 아니라 **교체**다.
        //   NULL 이면 이 기기의 매 요청 통행로(축①)가 함께 사라진다 — 그것이 K-0 잠재 P0 였다.
        Assert.True(
            bHash is not null,
            "🔴 인증에 성공했는데 해시가 **소거(NULL)** 됐다 — 이 기기는 매 요청 통행로(축①)를 잃는다. " +
            "업무 API 전면 403(K-0 잠재 P0)이 되살아난 것이다(20260819작1 K-1).");

        Assert.True(
            bHash != Sha256Hex(authKeyB),
            "🔴 인증에 성공했는데 **사람이 본 키가 표에 그대로 살아 있다** — " +
            "옆에서 본 사람이 나중에 그 키로 들어온다(사장님 결재 4 의도 위반).");

        // 반환값 = 기계비밀 원문. 표의 해시와 맞아야 화면이 보관한 값으로 매 요청이 통한다.
        Assert.False(string.IsNullOrWhiteSpace(opened), "인증에 성공했는데 기계비밀이 돌아오지 않았다.");
        Assert.Equal(Sha256Hex(opened!), bHash);
    }

    /// <summary>
    /// <b>G-31-c. 틀린 키는 자기 줄이어도 안 열린다.</b>
    ///
    /// <para>대조가 실제로 일어나는지 본다. 세션 줄만 잡고 <b>키 대조를 건너뛰면</b> 여기서 걸린다.</para>
    /// <para>[반증] 해시 비교를 지우고 세션 줄을 그냥 돌려주면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-31-c 틀린 인증키는 자기 기기여도 열리지 않는다")]
    public async Task G31c_틀린_키는_자기_줄이어도_안_열린다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string deviceB = "bbbbbbbb-0000-0000-0000-00000000000b";
        await InsertDeviceAsync(db, deviceB, "pending", "FP-B-고유지문",
            authKeyHash: Sha256Hex("진짜키-1111-2222-3333"));

        var opened = await svc.VerifyAuthKeyAsync("아무렇게나-찍은-키", TenantId, deviceB);

        // 🔴 표로 판정한다 — 틀린 키였으면 **줄이 하나도 안 바뀌어야** 한다.
        var (bHash, bStatus) = await ReadRowAsync(db, deviceB);

        Assert.True(
            bStatus == "pending",
            $"틀린 키를 넣었는데 줄이 승인됐다(지금: {bStatus}). 대조가 실제로 일어나지 않았다.");

        Assert.True(
            bHash is not null,
            "틀린 키를 넣었는데 키가 소거됐다 — 대조 없이 소거하면 정상 직원의 키가 날아간다.");

        Assert.Null(opened);
    }

    // ══════════════════════════════════════════════════════════════
    // G-32 — 헤더 통과는 메인PC 만  🔴 1-2 본체
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-32. 메인PC 가 아닌 기기의 번호를 헤더에 넣어도 통과하지 못한다.</b>
    ///
    /// <para>
    /// [무엇이 문제였나] <c>IsDeviceAllowedAsync</c> 가 <c>status='approved'</c> 만 봤다.
    /// <c>device_id</c> 는 <b>비밀이 아니다</b> — 브라우저 저장소에 그대로 있고 화면에도 보인다.
    /// ⇒ 남의 번호를 헤더에 넣으면 <b>무기명으로 통과</b>한다.
    /// </para>
    ///
    /// <para>
    /// [왜 메인PC 만인가] 이 헤더 통과 길은 <b>메인PC 를 구하려고</b> 낸 길이다(8/16 P0).
    /// 메인PC 는 인증키를 받은 적이 없어서 <b>다른 길이 없다.</b>
    /// 나머지 기기는 <b>인증키라는 제 길</b>이 있으므로 이 길을 열어 둘 이유가 없다.
    /// </para>
    ///
    /// <para>
    /// 🔴 [초록불이 어디서 오나] <b>DB 제약조건이 아니다.</b> 승인된 일반 기기 한 줄만 넣으면
    /// 어떤 UNIQUE 도 걸리지 않는다. 막는 것은 <b>오직 내 코드</b>다.
    /// </para>
    ///
    /// <para>[반증] 1-2 의 <c>is_main_pc = 1</c> 조건을 빼면 통과하여 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-32 🔴 메인PC 가 아닌 승인 기기의 번호로는 헤더 통과가 안 된다")]
    public async Task G32_메인PC가_아니면_헤더통과_안된다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        // 승인은 났지만 메인PC 는 아닌 평범한 직원 기기.
        const string employeeDevice = "cccccccc-0000-0000-0000-00000000000c";
        await InsertDeviceAsync(db, employeeDevice, "approved", "FP-직원-고유지문", isMainPc: false);

        var allowed = await svc.IsDeviceAllowedAsync(employeeDevice, TenantId);

        Assert.False(
            allowed,
            "🔴 메인PC 가 아닌 기기의 번호가 **헤더만으로 통과**했다. " +
            "장비넘버는 비밀이 아니다(브라우저 저장소·화면에 그대로 있다) — " +
            "남의 번호를 헤더에 넣으면 무기명으로 들어온다. " +
            "이 길은 인증키를 받은 적 없는 메인PC 를 구하려고 낸 길이고, 거기까지만 열려야 한다(1-2).");
    }

    /// <summary>
    /// <b>G-32-b. 메인PC 는 그대로 통과한다 — 8/16 P0 를 되살리지 않는다.</b>
    ///
    /// <para>
    /// 🔴 <b>왜 이 짝이 필요한가</b> — G-32 만 있으면 <c>return false</c> 로 통과시킬 수 있고,
    /// 그러면 <b>메인PC 가 자기 화면에서 다시 막힌다.</b> 8/16 PR#169 가 닫은 바로 그 자리다.
    /// </para>
    ///
    /// <para>[반증] <c>IsDeviceAllowedAsync</c> 가 무조건 false 면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-32-b 메인PC 는 종전대로 헤더 통과한다 (8/16 P0 무회귀)")]
    public async Task G32b_메인PC는_종전대로_통과한다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string mainPc = "dddddddd-0000-0000-0000-00000000000d";
        await InsertDeviceAsync(db, mainPc, "approved", "MAINPC-고유지문", isMainPc: true);

        var allowed = await svc.IsDeviceAllowedAsync(mainPc, TenantId);

        Assert.True(
            allowed,
            "🔴 메인PC 가 헤더로 통과하지 못한다 — **자기 화면에서 스스로 못 빠져나오는 방**이 되살아났다. " +
            "8/16 PR#169 가 닫은 자리다(사장님 실측 P0).");
    }

    /// <summary>
    /// 🔴 <b>G-32-d. 메인PC 의 번호를 <b>다른 기기</b>가 들고 와도 그것만으로는 못 막는다 — 한계를 명시한다.</b>
    ///
    /// <para>
    /// 🔴 <b>[3-V] V-04 지적 반영</b> — 1-2 를 <i>"무기명 도용 차단"</i> 이라 부르면 <b>과장</b>이다.
    /// <c>device_id</c> 는 <b>비밀이 아니다</b>: 기기 목록 화면에 보이고,
    /// <c>gate-status</c> <b>쿼리스트링(서버 로그에 평문)</b> 에 실리며, localStorage 에 그대로 있다.
    /// </para>
    ///
    /// <para>
    /// ⇒ 1-2 가 하는 일은 <b>"통과 가능한 범위를 메인PC 한 줄로 좁히는 것"</b> 이지
    /// <b>도용을 막는 것이 아니다.</b> 메인PC 의 번호를 손에 넣은 자는 <b>여전히 통과한다.</b>
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>이 시험은 그 한계를 문서가 아니라 값으로 세워 둔다.</b>
    /// 나중에 누군가 <i>"1-2 로 도용이 막혔다"</i> 고 적으면 이 시험이 반증이 된다.
    /// <b>남은 구멍은 다음 차수에서 닫아야 한다</b>(기기별 비밀값 — 이번 범위 밖).
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-32-d ⚠️ 메인PC 번호를 훔치면 여전히 통과한다 — 1-2 는 도용 차단이 아니다(한계 명시)")]
    public async Task G32d_메인PC_번호_도용은_아직_막히지_않는다_한계명시()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string mainPc = "dddddddd-0000-0000-0000-00000000000d";
        await InsertDeviceAsync(db, mainPc, "approved", "MAINPC-고유지문", isMainPc: true);

        // 🔴 다른 기기가 메인PC 의 번호를 헤더에 넣는다.
        //   서버는 헤더에 실린 번호만 볼 뿐, 그것을 **누가** 보냈는지 구분할 수단이 없다.
        var stolenPasses = await svc.IsDeviceAllowedAsync(mainPc, TenantId);

        Assert.True(
            stolenPasses,
            "⚠️ 이 시험이 실패했다면 동작이 바뀐 것이다 — 좋은 쪽일 수도 있다(기기별 비밀값이 생겼거나). " +
            "그 경우 이 시험의 전제를 다시 쓰고 1-2 설명도 함께 고쳐라. " +
            "지금 이 시험이 지키는 것은 '1-2 는 도용을 막지 않는다' 는 **사실의 기록**이다 — " +
            "그것을 '도용 차단' 이라 적는 순간 거짓봉합이 된다([3-V] V-04).");
    }

    /// <summary>
    /// <b>G-32-c. 승인 안 난 메인PC 는 통과하지 못한다.</b>
    ///
    /// <para>메인PC 라는 표식이 <b>승인 검사 자체를 무효화</b>하면 안 된다. 조건은 <b>AND</b> 다.</para>
    /// <para>[반증] 조건을 <c>is_main_pc=1</c> <b>만</b> 으로 바꾸면(상태 검사 누락) FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-32-c 승인 안 난 메인PC 는 통과하지 못한다 (AND 조건)")]
    public async Task G32c_미승인_메인PC는_통과_못한다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string pendingMain = "eeeeeeee-0000-0000-0000-00000000000e";
        await InsertDeviceAsync(db, pendingMain, "pending", "MAINPC-대기-고유지문", isMainPc: true);

        var allowed = await svc.IsDeviceAllowedAsync(pendingMain, TenantId);

        Assert.False(allowed,
            "메인PC 표식이 승인 검사를 덮어썼다 — 조건은 '메인PC **이면서** 승인됨' 이어야 한다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-33 — 거절은 폐기와 다른 칸이다  🔴 1-4 본체
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-33. 거절하면 <c>rejected</c> 로 간다 — <c>revoked</c> 가 아니다.</b>
    ///
    /// <para>
    /// [무엇이 문제였나] <c>RejectAsync</c> 가 <c>SET status='revoked'</c> 였다.
    /// <b>"이번엔 아니다"</b> 와 <b>"폐기"</b> 가 <b>같은 칸</b>에 들어갔다.
    /// ⇒ 거절당한 직원은 <c>RegisterOrRefreshAsync</c> 의 <c>revoked</c> 갈래에 걸려
    /// <b>"폐기된 기기입니다"</b> 로 로그인 자체가 막힌다 — <b>무한 폴링에 갇힌다.</b>
    /// 사장님 오더 <i>"거절하면 첫 화면 회귀"</i> 가 물리적으로 불가능해진다.
    /// </para>
    ///
    /// <para>
    /// [무엇을 보는가] 실제로 거절을 실행하고 <b>표에 적힌 값</b>을 읽는다.
    /// 🟢 <c>status</c> 가 <c>varchar(20)</c> 이라 <c>rejected</c> 에 <b>ALTER 가 필요 없다</b>(실측).
    /// </para>
    ///
    /// <para>[반증] <c>'rejected'</c> 를 <c>'revoked'</c> 로 되돌리면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-33 🔴 거절은 rejected 로 간다 (revoked 와 다른 칸)")]
    public async Task G33_거절은_rejected로_간다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string deviceId = "ffffffff-0000-0000-0000-00000000000f";
        await InsertDeviceAsync(db, deviceId, "pending", "FP-거절대상-고유지문");

        await svc.RejectAsync(deviceId, TenantId, "approver-user", "모르는 기기");

        var status = await db.ExecuteScalarAsync<string>(
            "SELECT status FROM tenant_devices WHERE device_id = @Id", new { Id = deviceId });

        Assert.Equal("rejected", status);
    }

    /// <summary>
    /// 🔴 <b>G-33-b. 거절당한 기기는 다시 신청할 수 있다 — 첫 화면 회귀.</b>
    ///
    /// <para>
    /// <b>이것이 1-4 의 진짜 목적이다.</b> 칸 이름만 바꾸고 <c>RegisterOrRefreshAsync</c> 가
    /// 여전히 막으면 <b>아무것도 안 고친 것</b>이다 —
    /// 사장님이 보시는 증상(<i>거절당한 직원이 갇힌다</i>)은 그대로 남는다.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>이것이 "두 번째 접속" 게이트다.</b> 한 번 거절해서 값이 맞는지가 아니라 —
    /// <b>거절당한 그 기기가 다시 왔을 때 들어올 수 있는가.</b>
    /// </para>
    ///
    /// <para>[반증] <c>rejected</c> 를 <c>revoked</c> 와 같이 막으면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-33-b 🔴 거절당한 기기가 다시 신청할 수 있다 (첫 화면 회귀)")]
    public async Task G33b_거절당한_기기는_다시_신청할_수_있다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string deviceId = "ffffffff-0000-0000-0000-00000000000f";
        const string fp = "FP-거절대상-고유지문";
        await InsertDeviceAsync(db, deviceId, "pending", fp);

        await svc.RejectAsync(deviceId, TenantId, "approver-user", "모르는 기기");

        // 🔴 거절당한 그 기기가 **다시 온다.** 같은 장비넘버·같은 지문이다.
        var (allowed, reason, returnedId, _) = await svc.RegisterOrRefreshAsync(
            TenantId, "user-1",
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                DeviceId = deviceId,
                Fingerprint = fp,
                DeviceType = "pc",
                DeviceName = "직원 PC"
            },
            "127.0.0.1");

        // 아직 승인은 안 났으니 allowed=false 가 맞다. 그러나 **막힌 것과는 다르다** —
        //   다시 대기 줄에 서야 한다. 폐기 갈래로 떨어지면 deviceId 가 null 로 돌아온다.
        Assert.True(
            returnedId == deviceId,
            $"🔴 거절당한 기기가 **다시 신청하지 못한다** (돌아온 값: {returnedId ?? "null"} / 사유: {reason}). " +
            "폐기 갈래에 걸려 로그인 자체가 막힌 상태다 — 직원이 무한 폴링에 갇힌다. " +
            "사장님 오더 '거절하면 첫 화면 회귀' 가 불가능해진다(1-4).");

        var statusNow = await db.ExecuteScalarAsync<string>(
            "SELECT status FROM tenant_devices WHERE device_id = @Id", new { Id = deviceId });

        Assert.True(
            statusNow == "pending",
            $"다시 신청했는데 상태가 대기(pending)로 돌아오지 않았다(지금: {statusNow}). " +
            "대표 화면의 승인 대기 목록에 다시 뜨지 않으면 대표는 승인할 기회조차 없다.");
    }

    /// <summary>
    /// <b>G-33-c. 폐기(<c>revoked</c>)는 종전대로 막힌다 — 폐기의 뜻을 바꾸지 않았다.</b>
    ///
    /// <para>
    /// 🔴 <b>왜 이 짝이 필요한가</b> — <c>rejected</c> 를 통과시키다가 <c>revoked</c> 까지 열면
    /// 대표가 <i>"이 기기 못 쓰게 해"</i> 라고 눌렀던 것이 <b>무효</b>가 된다.
    /// 작업지시서 §8: <b>폐기는 폐기로 둔다.</b>
    /// </para>
    ///
    /// <para>[반증] <c>revoked</c> 갈래를 지우면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-33-c 폐기된 기기는 종전대로 막힌다 (revoked 의미 불변)")]
    public async Task G33c_폐기된_기기는_종전대로_막힌다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string deviceId = "99999999-0000-0000-0000-000000000009";
        const string fp = "FP-폐기대상-고유지문";
        // 메인PC 가 아닌 일반 기기의 폐기 — 메인PC 는 별도 구제 규칙이 있다.
        await InsertDeviceAsync(db, deviceId, "revoked", fp, isMainPc: false);

        var (allowed, _, returnedId, _) = await svc.RegisterOrRefreshAsync(
            TenantId, "user-1",
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                DeviceId = deviceId,
                Fingerprint = fp,
                DeviceType = "pc",
                DeviceName = "폐기된 PC"
            },
            "127.0.0.1");

        // 🔴 2026-08-20 20260820작3 — **뜻은 그대로, 판정 축만 정확히 한다.**
        //
        //   사장님 정정: *"게이트 의미를 재정의 하는게 아니라 **회귀 게이트만 열어두면 되**"*
        //
        //   [이 게이트가 지키는 것] *"대표가 '못 쓰게 해' 라고 누른 것이 무효가 되지 않는다"*
        //     ⇒ 그 뜻은 **`approved` 가 되지 않는다** 는 것이다. 그것은 지금도 참이다.
        //   [바뀐 것] 종전엔 그것을 `allowed == false` **하나로** 봤는데,
        //     사장님 오더로 폐기 기기는 이제 **로그인은 통과하고 대기(pending)로 회귀**한다
        //     (*"관리자에게 문의하세요" 로 끝나면 문의해도 대표가 손쓸 자리가 없다* — 실측).
        //     ⇒ 판정을 **표의 status** 로 옮긴다. 값이 더 정확해질 뿐 **막는 사실은 그대로**다.
        Assert.False(allowed,
            "🔴 폐기된 기기가 **바로 통과**했다 — 대표가 '못 쓰게 해' 라고 누른 것이 무효가 됐다. " +
            "회귀는 대기(pending)까지다. 승인은 대표가 누른다.");

        var statusAfter = await db.ExecuteScalarAsync<string>(
            "SELECT status FROM tenant_devices WHERE device_id = @Id", new { Id = deviceId });

        Assert.True(statusAfter != "approved",
            $"🔴 폐기된 기기가 **스스로 승인 상태가 됐다**(지금: {statusAfter}) — " +
            "재접속만으로 폐기가 풀리면 도난·퇴사 기기를 막을 방법이 사라진다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-42 — 폐기 기기의 회귀 (20260820작3 · 사장님 실측 오더)
    //
    //   사장님: *"'폐기된 기기 입니다. 관리자에게 문의하세요' 가 아닌,
    //             **기기 등록 전 상태로 회귀**하도록"*
    //           *"지금 상황은 … 관리자에게 문의해봐야 **어떻게 못하는 상황**"*
    //   ⇒ 여는 게이트는 **이것 하나**다. 다른 게이트의 뜻은 건드리지 않는다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🟢 <b>G-42. 폐기된 기기가 다시 접속하면 <b>대기(pending)로 회귀</b>한다 — 그리고 거기까지다.</b>
    ///
    /// <para>
    /// [무엇이 문제였나] 종전엔 <b>로그인 자체를 거부</b>했다(<c>deviceId: null</c> ⇒ 401).
    /// 그 기기는 <b>관문에 도달할 길이 없고</b>, 대표 화면에도 폐기 기기를 되살릴 자리가 없다
    /// (<c>DeviceManagePage.razor</c> 는 승인 대기 구역만 그린다).
    /// ⇒ 안내문이 <i>"관리자에게 문의하세요"</i> 인데 <b>문의해도 대표가 손쓸 수 없었다</b>(사장님 실측).
    /// </para>
    /// <para>
    /// 🔴 <b>그러나 폐기의 뜻은 남는다</b> — 회귀 도착지는 <c>pending</c> 이고,
    /// 쓰려면 <b>대표가 [승인]을 눌러야</b> 한다. 옛 인증키도 죽은 채다(폐기 때 소거 · G-DP1).
    /// </para>
    /// <para>[반증] 회귀 갈래를 되돌려 거부(<c>return … null</c>)로 두면 <c>deviceId</c> 가 안 와 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-42 🟢 폐기된 기기는 대기로 회귀한다 (승인은 여전히 대표 몫 · 옛 키는 죽은 채)")]
    public async Task G42_폐기된_기기는_대기로_회귀한다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        // 대표가 폐기한 일반 기기 — 폐기 때 인증키는 이미 지워져 있다(RevokeAsync · G-DP1).
        const string deviceId = "aaaaaaaa-0000-0000-0000-0000000000a1";
        const string fp = "FP-폐기회귀-고유지문";
        await InsertDeviceAsync(db, deviceId, "revoked", fp, isMainPc: false);

        var (allowed, reason, returnedId, _) = await svc.RegisterOrRefreshAsync(
            TenantId, "user-1",
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                DeviceId = deviceId,
                Fingerprint = fp,
                DeviceType = "pc",
                DeviceName = "폐기됐던 PC"
            },
            "127.0.0.1");

        // ① 관문에 도달할 수 있어야 한다 — deviceId 가 와야 AuthController 가 401 을 안 낸다.
        Assert.True(returnedId == deviceId,
            $"🔴 폐기된 기기가 **관문에 도달하지 못한다**(돌아온 값: {returnedId ?? "null"} / 사유: {reason}). " +
            "로그인이 401 로 끝나면 화면은 '관리자에게 문의하세요' 에서 멈추고, " +
            "대표 화면에도 그 기기가 안 떠서 **문의해도 아무도 손쓸 수 없다**(사장님 실측).");

        // ② 표가 대기로 회귀했는가 — 대표의 승인 대기 목록에 다시 떠야 한다.
        var statusNow = await db.ExecuteScalarAsync<string>(
            "SELECT status FROM tenant_devices WHERE device_id = @Id", new { Id = deviceId });
        Assert.True(statusNow == "pending",
            $"폐기 기기가 대기로 회귀하지 않았다(지금: {statusNow}). " +
            "대표 화면 승인 대기 목록에 뜨지 않으면 대표는 승인할 기회조차 없다.");

        // ③ 🔴 그러나 통과는 아니다 — 폐기의 뜻이 남는 자리.
        Assert.False(allowed,
            "🔴 폐기된 기기가 **그대로 통과**했다 — 재접속만으로 폐기가 풀리면 안 된다.");

        // ④ 🔴 옛 인증키는 죽은 채다 — 회귀가 열쇠를 되살리지 않는다(G-DP1 무회귀).
        var (hashNow, _) = await ReadRowAsync(db, deviceId);
        Assert.True(hashNow is null,
            "🔴 회귀하면서 **옛 인증키가 되살아났다** — 폐기 때 지운 열쇠가 돌아오면 안 된다(G-DP1).");
    }

    // ══════════════════════════════════════════════════════════════
    // G-34 — 번호 없는 접속이 무조건 통과하지 않는다  🔴 gate-status 구멍
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-34. 인증키 재발급은 옛 키를 확실히 죽인다.</b>
    ///
    /// <para>
    /// [왜 필요한가] 1-8 재발급은 <b>오타 낸 직원이 영구 차단되는 것</b>을 막는 장치다.
    /// 그런데 재발급이 <b>새 키를 더하기만 하고 옛 키를 안 죽이면</b>
    /// 돌아다니던 옛 키가 <b>계속 유효</b>하다 — 재발급의 의미가 사라진다.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>이것도 "두 번째" 게이트다.</b> 새 키가 되는지가 아니라 — <b>옛 키가 죽었는가.</b>
    /// </para>
    ///
    /// <para>[반증] 재발급이 해시를 덮어쓰지 않게 하면 옛 키가 살아 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-34 🔴 인증키를 재발급하면 옛 키는 죽는다")]
    public async Task G34_재발급하면_옛_키는_죽는다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string deviceId = "77777777-0000-0000-0000-000000000007";
        const string oldKey = "OLD-1111-2222-3333-4444";
        await InsertDeviceAsync(db, deviceId, "pending", "FP-재발급대상-고유지문",
            authKeyHash: Sha256Hex(oldKey));

        // 전제 확인 — 지금 표에 들어 있는 것은 **옛 키의 해시**다.
        //   ⚠️ 여기서 VerifyAuthKeyAsync 를 부르지 않는다. 부르는 순간 1회용 소거가 일어나
        //     "재발급이 옛 키를 죽였는지" 와 "인증이 죽였는지" 가 **구분되지 않는다.**
        var (hashBefore, _) = await ReadRowAsync(db, deviceId);
        Assert.Equal(Sha256Hex(oldKey), hashBefore);

        // 🔴 재발급.
        var newKey = await svc.ReissueAuthKeyAsync(deviceId, TenantId, "approver-user");
        Assert.False(string.IsNullOrWhiteSpace(newKey), "재발급이 새 키를 돌려주지 않았다.");

        // 🔴 표로 판정한다 — 저장된 해시가 **옛 키의 것이면 안 된다.**
        var (hashAfter, _) = await ReadRowAsync(db, deviceId);

        Assert.True(
            hashAfter != Sha256Hex(oldKey),
            "🔴 재발급했는데 표에 **옛 키의 해시가 그대로** 남아 있다 — 돌아다니던 옛 키가 계속 유효하다. " +
            "재발급의 의미가 사라진다.");

        Assert.True(
            hashAfter == Sha256Hex(newKey!),
            "재발급이 돌려준 키와 표에 저장된 해시가 다르다 — 대표가 알려준 번호로 직원이 못 들어간다.");

        // 새 키로 실제 인증이 되는가 — 되살아나야 한다(직원 영구 차단 방지).
        //   ⚠️ K-1(20260819작1) 이후 성공 반환값은 기기 번호가 아니라 **기계비밀**이다.
        var withNew = await svc.VerifyAuthKeyAsync(newKey!, TenantId, deviceId);
        Assert.False(string.IsNullOrWhiteSpace(withNew),
            "재발급한 새 키로 인증이 안 된다 — 재발급이 기기를 되살리지 못했다(직원 영구 차단).");

        // 🔴 옛 키로는 안 된다.
        var withOld = await svc.VerifyAuthKeyAsync(oldKey, TenantId, deviceId);
        Assert.Null(withOld);
    }

    /// <summary>
    /// <b>G-34-b. 1회용 — 한 번 쓴 인증키는 다시 쓰이지 않는다.</b>
    ///
    /// <para>
    /// 사장님 결재 4: <i>"1회용 + 재발급 화면 필요"</i>.
    /// 키가 계속 살아 있으면 <b>옆에서 본 사람이 나중에 그 키로 들어온다.</b>
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>전형적인 "두 번째 접속" 게이트다.</b> 첫 인증이 되는지가 아니라 —
    /// <b>같은 키로 두 번째가 되는가.</b>
    /// </para>
    ///
    /// <para>[반증] 검증 성공 시 해시 소거를 빼면 두 번째도 통과하여 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-34-b 🔴 인증키는 1회용 — 같은 키로 두 번째는 안 된다")]
    public async Task G34b_인증키는_1회용이다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string deviceId = "88888888-0000-0000-0000-000000000008";
        const string key = "ONCE-1111-2222-3333-4444";
        await InsertDeviceAsync(db, deviceId, "pending", "FP-1회용-고유지문",
            authKeyHash: Sha256Hex(key));

        var first = await svc.VerifyAuthKeyAsync(key, TenantId, deviceId);
        Assert.False(string.IsNullOrWhiteSpace(first), "자기 키 첫 인증이 실패했다.");

        // 🔴 표로 판정한다 — 인증이 되는 순간 **사람이 본 키의 해시가 표에서 사라져야** 한다.
        //   이것이 1회용의 실체다. 반환값이 아니라 이 교체가 다음 사람을 막는다.
        //   ⚠️ K-1(20260819작1): 자리가 비는(NULL) 것이 아니라 **기계비밀 해시로 바뀐다** —
        //     소거는 이 기기의 매 요청 통행로까지 없앴다(K-0 잠재 P0).
        var (hashAfterFirst, _) = await ReadRowAsync(db, deviceId);
        Assert.True(
            hashAfterFirst != Sha256Hex(key),
            "🔴 인증에 성공했는데 **사람이 본 키가 표에 그대로 남아 있다** — 1회용이 아니다(사장님 결재 4 위반). " +
            "키가 계속 살아 있으면 옆에서 본 사람이 나중에 그 키로 들어온다.");

        // 🔴 같은 키로 다시 온다 — 옆에서 본 사람이거나, 키가 적힌 쪽지를 주운 사람이다.
        var second = await svc.VerifyAuthKeyAsync(key, TenantId, deviceId);

        Assert.True(second is null,
            "🔴 같은 인증키로 **두 번째 인증**이 됐다 — 1회용이 아니다(사장님 결재 4 위반).");
    }

    // ══════════════════════════════════════════════════════════════
    // G-35 ~ G-38 — 본안 K (20260819작1) · [3-V] 7회차 조건 2
    //
    //   🔴 왜 생겼나 — 8/18 의 두 봉합(1회용 소거 + 축② 좁힘)이 같은 커밋에 실리며
    //     **인증 완료한 직원 기기의 매 요청 통행로가 사라졌다**(K-0 잠재 P0).
    //     그런데 기존 게이트 38/38 이 전부 초록이었다 — *"verify 를 통과한 기기가
    //     그 뒤 요청을 통과한다"* 를 값으로 세운 게이트가 **0개**였다(사각 10번째).
    //   ⇒ 아래 게이트는 그 종단을 값으로 세운다. 축① 판정 본체(VerifyDeviceSecretAsync)를
    //     직접 부른다 — 미들웨어가 같은 메서드를 부르므로 두 자리가 갈릴 수 없다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>G-35. 직원 종단 — verify 를 통과한 기기는 그 뒤 요청도 통과한다 (K-0 닫힘).</b>
    ///
    /// <para>
    /// [무엇이 문제였나] verify 성공이 해시를 <b>소거</b>했다. 화면이 보관한 사람 키는 죽은 키라
    /// 매 요청 축① 이 <c>COUNT=0</c> — <b>인증 직후부터 업무 API 전면 403</b>(스위치 ON 상태).
    /// 사장님 실측 6회 갇힘과 같은 모양이 전 직원 기기에서 나는 자리였다.
    /// </para>
    /// <para>[반증 — G-E 실측 절차] K-1 을 되돌려 <c>auth_key_hash = NULL</c> 소거로 바꾸면
    /// 기계비밀이 없어 이 시험이 FAIL 한다. 코딩 직후 실제로 되돌려 FAIL 을 확인한다(가짜게이트 방지).</para>
    /// </summary>
    [Fact(DisplayName = "G-35 🔴🔴 verify 통과한 직원 기기는 기계비밀로 매 요청을 통과한다 (K-0 닫힘)")]
    public async Task G35_verify_통과한_직원기기는_매요청을_통과한다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        // 평범한 직원 기기 — 대표가 승인해 키를 받았고(해시 보유), 지금 그 키를 넣는다.
        const string deviceB = "bbbbbbbb-0000-0000-0000-00000000000b";
        const string authKeyB = "BBBB1111CCCC2222DDDD3333EEEE4444";
        await InsertDeviceAsync(db, deviceB, "pending", "FP-B-고유지문", authKeyHash: Sha256Hex(authKeyB));

        var secret = await svc.VerifyAuthKeyAsync(authKeyB, TenantId, deviceB);
        Assert.False(string.IsNullOrWhiteSpace(secret), "verify 가 기계비밀을 돌려주지 않았다.");

        // 🔴 종단 — 다음 요청. 화면이 보관한 기계비밀 + 자기 번호로 축① 판정 본체를 지난다.
        var passes = await svc.VerifyDeviceSecretAsync(deviceB, secret!, TenantId);
        Assert.True(passes,
            "🔴 인증을 통과한 직원 기기가 **바로 다음 요청에서 막혔다** — K-0 잠재 P0 그대로다. " +
            "verify 성공이 준 값이 매 요청 통행증이 되지 못하면, 직원은 인증하고도 업무 화면 전부 403 이다.");

        // 🔴 그리고 **사람이 본 키로는 매 요청이 안 통한다** — 결재 4 의도(옆에서 본 사람 차단) 유지.
        var humanKeyPasses = await svc.VerifyDeviceSecretAsync(deviceB, authKeyB, TenantId);
        Assert.False(humanKeyPasses,
            "🔴 **사람이 본 인증키가 매 요청 통행증으로 살아 있다** — 옆에서 본 사람이 " +
            "그 키를 헤더에 넣으면 계속 들어온다(사장님 결재 4 의도 위반).");
    }

    /// <summary>
    /// 🔴 <b>G-36. 교차쌍 차단 — 남의 기계비밀 + 자기 번호(또는 반대)는 0 이다 (K-3).</b>
    ///
    /// <para>
    /// [무엇이 문제였나] 종전 축① 은 해시만 봤다 — <c>device_id</c> 조건이 없어
    /// <b>한 기기의 비밀값이 회사 공용 통행증</b>이 될 수 있었다(8/18 주석의 "다음 차수 몫").
    /// </para>
    /// <para>[반증] K-3 의 <c>device_id</c> 결합을 빼면 남의 비밀로 통과하여 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-36 🔴 남의 기계비밀 + 자기 번호는 통하지 않는다 (K-3 짝 결합)")]
    public async Task G36_교차쌍은_통하지_않는다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        // A·B 두 직원 기기 — 각자 verify 를 통과해 각자의 기계비밀을 가졌다.
        const string deviceA = "aaaaaaaa-0000-0000-0000-00000000000a";
        const string deviceB = "bbbbbbbb-0000-0000-0000-00000000000b";
        const string keyA = "AAAA1111BBBB2222CCCC3333DDDD4444";
        const string keyB = "BBBB1111CCCC2222DDDD3333EEEE4444";
        await InsertDeviceAsync(db, deviceA, "pending", "FP-A-고유지문", authKeyHash: Sha256Hex(keyA));
        await InsertDeviceAsync(db, deviceB, "pending", "FP-B-고유지문", authKeyHash: Sha256Hex(keyB));

        var secretA = await svc.VerifyAuthKeyAsync(keyA, TenantId, deviceA);
        var secretB = await svc.VerifyAuthKeyAsync(keyB, TenantId, deviceB);
        Assert.False(string.IsNullOrWhiteSpace(secretA));
        Assert.False(string.IsNullOrWhiteSpace(secretB));

        // 🔴 교차 — B 가 A 의 비밀을 훔쳐 자기 번호와 함께 낸다 / A 의 번호와 B 의 비밀.
        Assert.False(await svc.VerifyDeviceSecretAsync(deviceB, secretA!, TenantId),
            "🔴 남의 기계비밀 + 자기 번호가 통과했다 — 비밀값이 회사 공용 통행증이다(K-3 미결합).");
        Assert.False(await svc.VerifyDeviceSecretAsync(deviceA, secretB!, TenantId),
            "🔴 자기 번호 + 남의 기계비밀이 통과했다 — 비밀값이 회사 공용 통행증이다(K-3 미결합).");

        // 짝이 맞으면 통한다 — 좁히다가 다 막으면 8/10 모양이다(막는 게이트와 통하는 게이트는 한 쌍).
        Assert.True(await svc.VerifyDeviceSecretAsync(deviceA, secretA!, TenantId));
        Assert.True(await svc.VerifyDeviceSecretAsync(deviceB, secretB!, TenantId));
    }

    /// <summary>
    /// 🟢 <b>G-37. 대표 자기승인 종단 — approve → verify → 통행, 그리고 끊겨도 재발급으로 잇는다 (K-4).</b>
    ///
    /// <para>
    /// 관문의 [이 컴퓨터 승인] 이 거는 문과 같은 문을 서비스 층에서 그대로 건다:
    /// ① <c>ApproveAsync</c>(키 발급) → ② <c>VerifyAuthKeyAsync</c>(기계비밀 수령) → ③ 매 요청 통과.
    /// </para>
    /// <para>
    /// 🔴 [3-V] 7회차 조건 1 — ①과 ② 사이가 끊긴 채 재클릭하면 <c>ApproveAsync</c> 는
    /// <b>null</b>(멱등)이라 verify 할 키가 없다. 그 갈래는 <b>재발급 경유</b>로 이어져야 한다.
    /// </para>
    /// <para>[반증] 재발급 경유가 죽으면(또는 재발급 후 verify 가 안 되면) FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-37 🟢 대표 자기승인 종단 — 끊겨도 재발급 경유로 통행까지 잇는다 (K-4 조건 1)")]
    public async Task G37_자기승인_종단과_재시도_갈래()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 5, mobileLimit: 5);

        var svc = NewService(db);

        // 대표의 두 번째 컴퓨터 — 터널로 접속해 pending 으로 들어와 있다(8/18 사장님 자리).
        const string ownerPc = "ffffffff-0000-0000-0000-00000000000f";
        await InsertDeviceAsync(db, ownerPc, "pending", "FP-대표기기-고유지문");

        // ① 정상 경로: approve → 키 → verify → 기계비밀 → 통행.
        var key1 = await svc.ApproveAsync(ownerPc, TenantId, "owner-user");
        Assert.False(string.IsNullOrWhiteSpace(key1), "승인이 인증키를 돌려주지 않았다.");

        var secret1 = await svc.VerifyAuthKeyAsync(key1!, TenantId, ownerPc);
        Assert.False(string.IsNullOrWhiteSpace(secret1), "승인 키로 verify 가 안 된다.");
        Assert.True(await svc.VerifyDeviceSecretAsync(ownerPc, secret1!, TenantId),
            "🔴 자기승인 완료 직후 업무 요청이 막혔다 — 대표가 승인하고도 갇힌다(실1′ 자리).");

        // ② 🔴 재시도 갈래: approve 를 다시 누르면 null(멱등) — 여기서 멈추면 덫이다.
        var key2 = await svc.ApproveAsync(ownerPc, TenantId, "owner-user");
        Assert.Null(key2);   // 멱등 확인 — 이 null 이 [3-V] 7회차가 잡은 덫의 입구다

        //   ⇒ 화면은 이 갈래에서 reissue-key 를 경유한다. 그 경유가 실제로 잇는지 값으로 본다.
        var key3 = await svc.ReissueAuthKeyAsync(ownerPc, TenantId, "owner-user");
        Assert.False(string.IsNullOrWhiteSpace(key3), "재발급이 키를 돌려주지 않았다 — 재시도 갈래가 죽었다.");

        var secret2 = await svc.VerifyAuthKeyAsync(key3!, TenantId, ownerPc);
        Assert.False(string.IsNullOrWhiteSpace(secret2),
            "🔴 재발급 키로 verify 가 안 된다 — 자동 투입이 한 번 끊긴 대표는 영영 못 들어온다(K-0 과 같은 덫).");
        Assert.True(await svc.VerifyDeviceSecretAsync(ownerPc, secret2!, TenantId));

        // 옛 기계비밀은 재발급 순간 죽는다 — 현행 재발급 의미론 그대로.
        Assert.False(await svc.VerifyDeviceSecretAsync(ownerPc, secret1!, TenantId),
            "재발급했는데 옛 기계비밀이 살아 있다 — 재발급의 의미가 사라진다.");
    }

    /// <summary>
    /// <b>G-38. 승인 안 난(pending) 기기는 기계비밀 모양의 값을 내도 통하지 않는다.</b>
    ///
    /// <para>축① 판정은 <c>status='approved'</c> 를 함께 본다 — 승인제가 살아 있는지(G-P 계열).</para>
    /// <para>[반증] <c>VerifyDeviceSecretAsync</c> 에서 status 조건을 빼면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-38 승인 대기 기기는 해시가 맞아도 매 요청 통행이 안 된다")]
    public async Task G38_대기_기기는_통행이_안된다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        // pending 인데 해시가 있는 줄 — 대표가 승인(키 발급)만 하고 아직 verify 전인 기기.
        const string pendingDev = "99999999-0000-0000-0000-000000000009";
        const string someKey = "PEND1111KEY22223333VALUE4444FIVE";
        await InsertDeviceAsync(db, pendingDev, "pending", "FP-대기-고유지문", authKeyHash: Sha256Hex(someKey));

        Assert.False(await svc.VerifyDeviceSecretAsync(pendingDev, someKey, TenantId),
            "🔴 승인 대기 기기가 매 요청 통행을 지났다 — 승인제가 무너졌다(스위치가 있으나 없으나 같다).");
    }

    // ══════════════════════════════════════════════════════════════
    // G-39 · G-40 — 회사서버 줄 합류 (20260820작2 2-1 · PM·CTO 전결)
    //
    //   사장님 실측: 메인PC 컴퓨터가 서버 슬롯 + 브라우저 슬롯 = 2개.
    //   봉합: 관문의 [회사서버 컴퓨터] 갈래가 서버 줄(is_main_pc=1)에 합류한다 —
    //   reissue → verify → 기계비밀. 아래는 그 종단을 서비스 값으로 세운다.
    //   ⚠️ 관문 UI·신분 유지 가드(AuthService)는 브라우저 동작이라 xUnit 재현 불가 —
    //     Playwright 이월 + TEST1 실측(실4·실4′)이 판정한다(작업지시서 §3에 명시).
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🟢 <b>G-39. 서버 줄 합류 종단 — 서버 줄 재발급 키로 verify 하면 기계비밀로 매 요청이 통한다.</b>
    ///
    /// <para>서버 줄은 원래 인증키를 받은 적이 없다(해시 NULL — MainPcRegistrationService 가 스스로 등록).
    /// 합류는 그 줄에 재발급으로 해시를 만들고, verify 로 기계비밀을 받는 흐름이다.</para>
    /// <para>🔴 합류를 갈아타면(재발급 반복) <b>직전 탑승자의 비밀이 죽는다</b> — 무료 탑승 동시 1개 자기 제한.</para>
    /// </summary>
    [Fact(DisplayName = "G-39 🟢 회사서버 줄 합류 — 재발급→verify→기계비밀 통행 + 직전 탑승자 사멸")]
    public async Task G39_서버줄_합류_종단()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        // 회사서버 줄 — 스스로 등록돼 승인 상태, 해시는 없다(키를 받은 적이 없다).
        const string serverRow = "dddddddd-0000-0000-0000-00000000000d";
        await InsertDeviceAsync(db, serverRow, "approved", "MAINPC-고유지문", isMainPc: true);

        // 1차 합류 — 대표 브라우저.
        var key1 = await svc.ReissueAuthKeyAsync(serverRow, TenantId, "owner-user");
        Assert.False(string.IsNullOrWhiteSpace(key1), "서버 줄 재발급이 키를 돌려주지 않았다 — 합류 갈래가 죽었다.");

        var secret1 = await svc.VerifyAuthKeyAsync(key1!, TenantId, serverRow);
        Assert.False(string.IsNullOrWhiteSpace(secret1), "서버 줄 재발급 키로 verify 가 안 된다.");
        Assert.True(await svc.VerifyDeviceSecretAsync(serverRow, secret1!, TenantId),
            "🔴 합류 직후 매 요청이 막혔다 — [회사서버 컴퓨터] 를 눌러도 대표가 그대로 갇힌다.");

        // 2차 합류 — 다른 브라우저가 갈아탄다. 직전 탑승자의 비밀은 죽어야 한다.
        var key2 = await svc.ReissueAuthKeyAsync(serverRow, TenantId, "owner-user");
        var secret2 = await svc.VerifyAuthKeyAsync(key2!, TenantId, serverRow);
        Assert.False(string.IsNullOrWhiteSpace(secret2));
        Assert.True(await svc.VerifyDeviceSecretAsync(serverRow, secret2!, TenantId));
        Assert.False(await svc.VerifyDeviceSecretAsync(serverRow, secret1!, TenantId),
            "🔴 갈아탔는데 직전 탑승자의 비밀이 살아 있다 — 서버 줄이 무제한 무료 통행증이 된다.");
    }

    /// <summary>
    /// <b>G-40. 합류는 아무것도 승인하지 않는다 — 서버 줄 표식 무변 + pending 줄 그대로 (G-43 무접촉).</b>
    ///
    /// <para>합류 흐름(reissue→verify)이 서버 줄의 <c>is_main_pc</c> 를 건드리거나
    /// 브라우저의 pending 줄을 승인해 버리면, 요금·표식이 함께 무너진다.</para>
    /// </summary>
    [Fact(DisplayName = "G-40 합류해도 서버 줄 표식 무변 + 브라우저 pending 줄은 대기 그대로")]
    public async Task G40_합류는_아무것도_승인하지_않는다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string serverRow = "dddddddd-0000-0000-0000-00000000000d";
        const string browserRow = "bbbbbbbb-0000-0000-0000-00000000000b";
        await InsertDeviceAsync(db, serverRow, "approved", "MAINPC-고유지문", isMainPc: true);
        await InsertDeviceAsync(db, browserRow, "pending", "FP-대표브라우저-고유지문");

        var key = await svc.ReissueAuthKeyAsync(serverRow, TenantId, "owner-user");
        var secret = await svc.VerifyAuthKeyAsync(key!, TenantId, serverRow);
        Assert.False(string.IsNullOrWhiteSpace(secret));

        // 서버 줄: 표식·상태 무변. 브라우저 줄: 여전히 대기(슬롯 0).
        var server = await db.QueryFirstAsync<(string status, bool main)>(
            "SELECT status AS status, is_main_pc AS main FROM tenant_devices WHERE device_id = @Id",
            new { Id = serverRow });
        Assert.Equal("approved", server.status);
        Assert.True(server.main, "합류가 서버 표식을 지웠다 — CS 가 본체를 못 찾는다.");

        var (_, browserStatus) = await ReadRowAsync(db, browserRow);
        Assert.True(browserStatus == "pending",
            $"🔴 합류했는데 브라우저 pending 줄이 '{browserStatus}' 가 됐다 — " +
            "합류는 새 슬롯을 만들지 않는 갈래인데 어딘가가 승인을 흘렸다(G-43 · 요금).");

        // 계수 확인 — 그 기계 합계는 서버 줄 1대뿐이다(pending 은 안 센다).
        var counts = (await db.QueryAsync<(string t, int c)>(
            "SELECT device_type AS t, COUNT(*) AS c FROM tenant_devices WHERE tenant_id = @Tid AND status='approved' GROUP BY device_type",
            new { Tid = TenantId })).ToList();
        Assert.Equal(1, TenantDeviceService.PcUsedFrom(counts));
    }

    /// <summary>
    /// 🔴 <b>G-41. 회사서버 줄의 정체는 갱신이 덮지 못한다 ([3-V] 실재 판정 ②③).</b>
    ///
    /// <para>
    /// [무엇이 문제였나] 로그인 갱신은 장비넘버 1순위 대조인데, 합류(2-1)로 브라우저가
    /// 서버 줄 번호를 신분으로 갖는다 ⇒ 그 브라우저의 매 로그인이 서버 줄의
    /// 이름·UA·IP 를 브라우저 것으로 덮고(CS 가 본체를 못 찾는다), 대표 <b>폰</b>이 합류하면
    /// COALESCE 가 종류를 mobile 로 바꿔 <b>요금 칸이 이동</b>한다(pc 계수 -1).
    /// </para>
    /// <para>[반증] RegisterOrRefresh 의 preserveMainPcIdentity 를 빼면 종류·이름이 덮여 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-41 🔴 회사서버 줄의 이름·종류·UA·IP 는 로그인 갱신이 덮지 못한다")]
    public async Task G41_서버줄_정체는_갱신이_덮지_못한다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string serverRow = "dddddddd-0000-0000-0000-00000000000d";
        await InsertDeviceAsync(db, serverRow, "approved", "MAINPC-고유지문", isMainPc: true);

        // 합류한 대표 폰이 로그인한다 — 장비넘버 = 서버 줄, 신고 종류 = mobile.
        var (allowed, _, _, _) = await svc.RegisterOrRefreshAsync(
            TenantId, "owner-user",
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                Fingerprint = "FP-대표폰-고유지문",
                DeviceId = serverRow,
                DeviceType = "mobile",
                DeviceName = "iPhone 대표폰",
                UserAgent = "Mozilla/5.0 (iPhone)"
            },
            "203.0.113.9");
        Assert.True(allowed, "승인된 서버 줄인데 로그인이 막혔다 — 8/10 사고 계통.");

        var row = await db.QueryFirstAsync<(string type, string? name, string? ua, string? ip)>(
            "SELECT device_type AS type, device_name AS name, user_agent AS ua, ip_address AS ip "
            + "FROM tenant_devices WHERE device_id = @Id", new { Id = serverRow });

        Assert.True(row.type == "pc",
            $"🔴 서버 줄의 종류가 '{row.type}' 로 바뀌었다 — 요금 칸이 pc→mobile 로 이동한다(계수 -1).");
        Assert.True(row.name != "iPhone 대표폰",
            "🔴 서버 줄의 이름이 브라우저 이름으로 덮였다 — CS 가 본체를 못 찾는다(IsMainPc 표식의 존재 이유).");
        Assert.True(row.ua is null || !row.ua.Contains("iPhone"),
            "🔴 서버 줄의 UA 가 브라우저 UA 로 덮였다.");
        Assert.True(row.ip != "203.0.113.9",
            "🔴 서버 줄의 IP 가 합류 브라우저의 IP 로 덮였다.");

        // 짝 확인 — 일반 기기는 종전대로 갱신된다(보존을 넓히다 갱신 자체를 죽이면 D-4 회귀).
        const string normalDev = "cccccccc-0000-0000-0000-00000000000c";
        await InsertDeviceAsync(db, normalDev, "approved", "FP-일반-고유지문");
        await svc.RegisterOrRefreshAsync(
            TenantId, "owner-user",
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                Fingerprint = "FP-일반-고유지문",
                DeviceId = normalDev,
                DeviceType = "pc",
                DeviceName = "새 이름",
                UserAgent = "UA-2"
            },
            "198.51.100.7");
        var normal = await db.QueryFirstAsync<(string? name, string? ip)>(
            "SELECT device_name AS name, ip_address AS ip FROM tenant_devices WHERE device_id = @Id",
            new { Id = normalDev });
        Assert.Equal("새 이름", normal.name);
        Assert.Equal("198.51.100.7", normal.ip);
    }
}
