using Dapper;
using HitPan.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>기기 종류 · QR 봉합 게이트</b> (20260818작2 — 2-1 · 2-3 · 2-4 · 2-6 · V-05 · DP-1 · DP-2).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>글자를 안 본다 — 표를 본다.</b> 8/15~16 에 가짜 게이트가 다섯 번 나왔고 원인이 같았다:
/// <c>Assert.Contains</c> 로 소스 글자를 검사하면 <b>주석만 고쳐도 초록불</b>이 된다.
/// 여기서는 <b>격리 DB 에 출하 DDL 을 넣고 실제 서비스를 불러</b> 표에 남은 값을 읽는다.
/// </para>
///
/// <para>
/// 🔴 <b>반환값으로 판정하지 않는다</b> (2026-08-18 신종 가짜).
/// 반환값은 <b>함수가 마음대로 정하는 값</b>이라, 내부가 틀려도 반환값만 맞으면 초록불이 된다 —
/// 실제로 8/18 에 간판 봉합을 통째로 빼도 통과한 게이트가 있었다.
/// ⇒ 매번 물었다: <b>"이 값을 누가 정하는가 — 시험하려는 동작인가, 함수가 그냥 주는 값인가?"</b>
/// </para>
///
/// <para>
/// 🔴 <b>초록불이 어디서 오는지 물었다.</b> <c>tenant_devices</c> 에는
/// <c>UNIQUE uq_tenant_fp(tenant_id, fingerprint)</c> 가 <b>실재한다.</b>
/// 지문을 고정한 채 두 번째 줄을 만들려 하면 <b>내 코드가 아니라 DB 가 막아</b> 초록불이 된다(8/16 G-21 사고).
/// ⇒ 🔴 아래 게이트는 <b>지문을 매번 다르게</b> 준다.
/// </para>
///
/// <para>
/// 🔴 <b>막는 게이트마다 "통하는 짝"을 뒀다.</b> <c>return false</c> 로 전부 막아도 초록불이 되는
/// 가짜를 방지한다 — 막혀야 할 것이 막히고 <b>통해야 할 것이 통해야</b> 통과다.
/// </para>
///
/// <para>
/// ⚠️ <b>운영 무접촉</b>(헌법 #39) — 임시 DB(<c>hitpan_qrtype_gate_*</c>)만 만들고 <b>반드시 지운다.</b>
/// <c>demo</c>(3306 운영)·<c>hitpan_erp</c> 는 건드리지 않는다.
/// </para>
/// <para>
/// ⚠️ <b>MariaDB 가 없으면 조용히 통과시킨다</b> — CI·개발 PC 어디서도 거짓 실패를 만들지 않기 위해서다.
/// 🔴 <b>그 절충의 대가를 정확히 적는다</b>: DB 가 없는 환경에서 이 게이트는 <b>아무것도 검사하지 않는다.</b>
/// 초록불이 곧 안전이 아니다. 개발명세서에 실제 실행 출력을 남기는 이유가 그것이다.
/// </para>
/// </remarks>
[Collection("DeviceTypeQrGate")]
public sealed class DeviceTypeQrGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_qrtype_gate_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "22222222-2222-2222-2222-222222222222";

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
    private static TenantDeviceService NewService(MySqlConnection db, bool approvalEnabled = true)
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

    private sealed class NoOpAudit : HitPan.Application.Interfaces.IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// 🔴 <b>슬롯 한도를 시험이 정확히 지정한다</b> — 한도가 넉넉하면 가드가 없어도 초록불이다([3-V] V-07).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>이것이 2-1 게이트의 생명줄이다.</b> 한도를 안 정하면 안전망 숫자(basic = pc 5)가 쓰여
    /// <b>승격이 그냥 통과</b>하고, 가드를 빼도 FAIL 이 안 난다.
    /// ⇒ <b>한도를 꽉 채운 상태</b>를 만들어야 내 코드를 태운다.
    /// </remarks>
    private static async Task SetSlotPolicyAsync(MySqlConnection db, int pcLimit, int mobileLimit)
    {
        // ⚠️ `policy_id`(PK)·`label` 은 기본값이 없다 — 실측(헌법 #13 DESCRIBE 선행).
        //   안 채우면 "Field 'policy_id' doesn't have a default value" 로 시험이 죽는다.
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

    /// <summary>
    /// 실제 사용자 한 줄 — <c>tenant_devices.user_id</c> 가 <c>users</c> 를 참조하는 <b>FK</b> 라
    /// 신규 등록 경로를 태우려면 그 사람이 <b>실재해야</b> 한다(<c>fk_device_user</c>).
    /// </summary>
    private static async Task InsertUserAsync(MySqlConnection db, string userId, string email)
    {
        await db.ExecuteAsync(
            """
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role,
                               account_type, is_active, created_at, updated_at)
            VALUES (@Uid, @Tid, @Email, 'x', '김직원', 'user',
                    'tenant_user', 1, NOW(6), NOW(6))
            """,
            new { Uid = userId, Tid = TenantId, Email = email });
    }

    /// <summary>기기 한 줄을 직접 넣는다 — 상태·종류·지문을 시험이 정확히 지정하기 위해서다.</summary>
    private static async Task InsertDeviceAsync(
        MySqlConnection db, string deviceId, string status, string fingerprint,
        string deviceType = "pc", bool isMainPc = false, string? authKeyHash = null)
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
                Name = "종류게이트시험기기",
                Fp = fingerprint,
                Status = status,
                Hash = authKeyHash,
                Main = isMainPc ? 1 : 0
            });
    }

    /// <summary>🔴 <b>그 줄이 표에서 지금 어떤 값인가</b> — 판정은 전부 여기서 나온다.</summary>
    private static async Task<(string type, string status, string? userId, string? authKeyHash)> ReadRowAsync(
        MySqlConnection db, string deviceId)
    {
        return await db.QueryFirstAsync<(string, string, string?, string?)>(
            """
            SELECT device_type AS type, status AS status,
                   user_id AS userId, auth_key_hash AS authKeyHash
            FROM tenant_devices WHERE device_id = @Id
            """,
            new { Id = deviceId });
    }

    /// <summary>표에 실제로 몇 줄이 생겼나 — 슬롯 중복(사장님 증상②)은 이 숫자로만 잡힌다.</summary>
    private static async Task<int> CountDevicesAsync(MySqlConnection db) =>
        await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM tenant_devices WHERE tenant_id = @Tid", new { Tid = TenantId });

    /// <summary>QR 토큰을 발급해 <b>평문</b>을 돌려준다 — 실제 발급 경로를 그대로 탄다.</summary>
    private static async Task<string> IssueTokenAsync(TenantDeviceService svc) =>
        await svc.IssueMobileRegisterTokenAsync(TenantId, "issuer-user-0000");

    private static string Sha256Hex(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
    // G-1a — QR 을 두 번 찍어도 줄이 안 는다  🔴 2-3 본체 · 사장님 증상②
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-1a. 같은 폰이 지문이 바뀐 채 다시 찍어도 줄이 하나다.</b>
    ///
    /// <para>
    /// [무엇을 태우나] 사장님 8/16 증상② — <i>"한 기기에서 슬롯 중복으로 잡힘"</i>.
    /// 폰이 자기 번호를 들고 다시 오는데 <b>지문은 달라져 있다</b>(브라우저를 바꿨거나 새로 깔았다).
    /// </para>
    ///
    /// <para>
    /// 🔴 [초록불이 어디서 오나 — UNIQUE 함정을 피했다] <c>uq_tenant_fp</c> 는
    /// <b>(tenant_id, fingerprint)</b> 를 막는다. 그래서 <b>지문을 고정</b>하면 DB 가 두 번째 INSERT 를
    /// 막아 <c>COUNT==1</c> 이 되고, <b>내 코드를 빼도 초록불</b>이 된다.
    /// ⇒ 여기서는 <b>지문을 매회 다르게</b> 준다. DB 는 이것을 <b>서로 다른 줄로 허용</b>하므로
    /// 막는 것은 <b>오직 2-3 의 device_id 조회</b>뿐이다.
    /// </para>
    ///
    /// <para>[반증] 2-3(device_id 1순위 조회)을 빼면 지문이 달라 새 줄이 생겨 <c>COUNT==2</c> 로 FAIL 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-1a 🔴 같은 폰이 지문 바뀐 채 다시 찍어도 줄이 하나다 (증상②)")]
    public async Task G1a_QR_재등록시_지문이_바뀌어도_줄이_안_는다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 3, mobileLimit: 3);

        var svc = NewService(db);

        // ① 처음 등록 — 폰은 아직 자기 번호가 없다(null).
        var token1 = await IssueTokenAsync(svc);
        var first = await svc.RegisterMobileByTokenAsync(
            token1, "직원 폰", "FP-처음-" + Guid.NewGuid().ToString("N"), "127.0.0.1",
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)", null);

        Assert.True(first.ok);
        var deviceId = first.deviceId!;
        Assert.Equal(1, await CountDevicesAsync(db));

        // ② 같은 폰이 다시 찍는다 — 🔴 **번호는 갖고 있고 지문은 달라졌다.**
        //    (브라우저를 바꿨거나 새로 깔았다 — 실제로 흔한 일이다)
        for (var i = 0; i < 5; i++)
        {
            var tokenN = await IssueTokenAsync(svc);
            var again = await svc.RegisterMobileByTokenAsync(
                tokenN, "직원 폰", "FP-매번다름-" + Guid.NewGuid().ToString("N"), "127.0.0.1",
                "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)",
                deviceId);   // 🔴 폰이 보관해 둔 자기 번호

            Assert.True(again.ok);
        }

        // 🔴 판정은 **표**로 한다 — 반환값이 아니라 실제로 몇 줄이 생겼나.
        var rows = await CountDevicesAsync(db);
        Assert.True(rows == 1,
            $"같은 폰이 여섯 번 찍었는데 표에 {rows} 줄이 생겼다 — 슬롯 중복(사장님 증상②)이다. "
            + "2-3(device_id 1순위 조회)이 빠졌거나 동작하지 않는다.");
    }

    /// <summary>
    /// 🔴 <b>G-1a-짝. 진짜 다른 폰은 제대로 새 줄이 된다.</b>
    ///
    /// <para>
    /// ⚠️ <b>막는 게이트에는 반드시 통하는 짝을 둔다.</b> 이 짝이 없으면
    /// <i>"무조건 기존 줄을 돌려준다"</i> 는 가짜 구현으로도 G-1a 가 초록불이 된다 —
    /// 그러면 <b>두 번째 직원이 등록을 못 한다.</b>
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-1a-짝 🔴 진짜 다른 폰은 새 줄이 된다 (전부 막는 가짜 방지)")]
    public async Task G1a_짝_다른_폰은_새_줄이_된다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 3, mobileLimit: 3);

        var svc = NewService(db);

        var t1 = await IssueTokenAsync(svc);
        var a = await svc.RegisterMobileByTokenAsync(
            t1, "A 폰", "FP-A-" + Guid.NewGuid().ToString("N"), "127.0.0.1", "Android", null);

        // 🔴 번호를 **안 갖고** 온다 — 처음 오는 폰이다.
        var t2 = await IssueTokenAsync(svc);
        var b = await svc.RegisterMobileByTokenAsync(
            t2, "B 폰", "FP-B-" + Guid.NewGuid().ToString("N"), "127.0.0.1", "Android", null);

        Assert.True(a.ok && b.ok);
        Assert.NotEqual(a.deviceId, b.deviceId);

        var rows = await CountDevicesAsync(db);
        Assert.True(rows == 2,
            $"서로 다른 폰 두 대인데 표에 {rows} 줄이다 — 두 번째 직원이 등록을 못 한다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-QR-승인 — QR 은 스위치와 무관하게 항상 대기줄  🔴 2-4
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-QR-승인. 승인제 스위치를 꺼도 QR 등록은 <c>pending</c> 이다.</b>
    ///
    /// <para>
    /// [무엇이 문제였나] 종전 <c>_approvalEnabled ? "pending" : "approved"</c> 는
    /// <b><c>[AllowAnonymous]</c> 경로를 개발 편의로 열어 둔 자리</b>였다.
    /// PC 는 로그인을 통과한 뒤 관문에 서는데 QR 은 <b>로그인 없이</b> 들어온다 —
    /// 더 엄해야 할 자리가 더 느슨했다.
    /// </para>
    ///
    /// <para>
    /// 🔴 [반환값이 아니라 표를 본다] 반환 메시지는 함수가 마음대로 정하는 값이다.
    /// <b>실제로 그 줄이 대기줄에 섰는지</b>는 <c>status</c> 컬럼만이 안다.
    /// </para>
    ///
    /// <para>[반증] <c>qrStatus</c> 를 스위치 삼항으로 되돌리면 <c>approved</c> 가 되어 FAIL 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-QR-승인 🔴 스위치를 꺼도 QR 은 대기줄에 선다")]
    public async Task GQr승인_스위치를_꺼도_QR은_pending이다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 3, mobileLimit: 3);

        // 🔴 승인제를 **끈** 상태 — 종전이라면 여기서 즉시 approved 였다.
        var svc = NewService(db, approvalEnabled: false);

        var token = await IssueTokenAsync(svc);
        var r = await svc.RegisterMobileByTokenAsync(
            token, "직원 폰", "FP-" + Guid.NewGuid().ToString("N"), "127.0.0.1", "Android", null);

        Assert.True(r.ok);

        // 🔴 판정 = 표. status 가 진짜로 대기줄인가.
        var (_, status, _, _) = await ReadRowAsync(db, r.deviceId!);
        Assert.True(status == "pending",
            $"승인제를 껐더니 QR 등록이 '{status}' 로 들어갔다 — [AllowAnonymous] 경로가 "
            + "개발 편의로 열려 있다. 옆 사람 폰이 찍어도 바로 쓸 수 있다는 뜻이다.");

        // 승인자도 비어 있어야 한다 — 아직 아무도 승인 안 했다.
        var approvedBy = await db.ExecuteScalarAsync<string?>(
            "SELECT approved_by FROM tenant_devices WHERE device_id = @Id", new { Id = r.deviceId });
        Assert.True(approvedBy is null,
            "아무도 승인 안 했는데 승인자가 적혀 있다 — QR 을 띄운 사람이 승인자로 둔갑했다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-13b — 종류 승격은 방향별로 갈린다  🔴 2-1 본체
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-13b. <c>mobile → pc</c> 승격은 컴퓨터 칸이 찼으면 <b>안 바뀐다</b> — 그러나 <b>막히지도 않는다.</b></b>
    ///
    /// <para>
    /// 🔴 <b>이 게이트는 두 가지를 동시에 본다.</b> 한쪽만 보면 사고 하나를 반드시 놓친다:
    /// ① 요금이 안 샌다(<c>device_type</c> 이 pc 로 안 바뀐다)
    /// ② <b>쓰던 사람이 안 막힌다</b>(<c>allowed</c> 가 여전히 참) — 2026-08-10 사고 계통.
    /// </para>
    ///
    /// <para>
    /// 🔴 [한도를 꽉 채웠다 — V-07] 한도가 넉넉하면 <b>가드가 없어도 승격이 통과</b>해
    /// 봉합을 빼도 FAIL 이 안 난다. 그래서 <c>pcLimit=1</c> 로 두고 그 한 자리를
    /// <b>다른 승인된 PC 가 이미 차지</b>하게 만들었다.
    /// </para>
    ///
    /// <para>[반증] 방향별 분기를 빼면 <c>device_type</c> 이 <c>pc</c> 로 바뀌어 FAIL 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-13b 🔴 컴퓨터 칸이 차면 승격이 '안 바뀐다' — 그러나 막히지 않는다")]
    public async Task G13b_모바일에서_PC_승격은_한도가_차면_안_바뀐다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 🔴 컴퓨터 칸은 **1자리뿐**이고, 그 자리는 이미 찼다.
        await SetSlotPolicyAsync(db, pcLimit: 1, mobileLimit: 5);

        var svc = NewService(db);

        // 이미 승인된 PC 한 대 — 이것이 유일한 컴퓨터 자리를 먹는다.
        await InsertDeviceAsync(db, "pc-already-0000-0000-000000000001", "approved",
            "FP-이미쓰는PC-" + Guid.NewGuid().ToString("N"), deviceType: "pc");

        // 승격을 시도할 폰 — 지금은 휴대기기 칸에 있고 승인돼 있다.
        const string phoneId = "phone-0000-0000-0000-000000000002";
        await InsertDeviceAsync(db, phoneId, "approved",
            "FP-폰-" + Guid.NewGuid().ToString("N"), deviceType: "mobile");

        // 🔴 그 폰이 이번엔 **자기가 컴퓨터라고 신고**한다 — 싸게 등록해 두고 올라타려는 그림이다.
        //   ⚠️ UserAgent 도 Windows 로 준다. 서버 교차검증(V-05)에 걸려 되돌려지면
        //     2-1 을 시험하는 게 아니라 V-05 를 시험하게 되기 때문이다.
        var result = await svc.RegisterOrRefreshAsync(
            TenantId, "user-0001", 
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                DeviceId = phoneId,
                Fingerprint = "FP-폰-다시-" + Guid.NewGuid().ToString("N"),
                DeviceType = "pc",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
            }, "127.0.0.1");

        // ══════════════════════════════════════════════════════════════
        // 🔴 판정 ① — 요금이 새지 않는다. **표**의 종류가 안 바뀌었나.
        var (type, status, _, _) = await ReadRowAsync(db, phoneId);
        Assert.True(type != "pc",
            $"컴퓨터 칸이 1자리인데 이미 찼고, 그런데도 폰이 '{type}' 로 승격했다 — "
            + "컴퓨터 한도가 0이어도 무제한으로 늘어난다는 뜻이다(요금 구멍).");

        // 🔴 판정 ② — **쓰던 사람이 안 막힌다.** 이것을 안 보면 8/10 사고를 놓친다.
        Assert.True(result.allowed,
            "승격을 못 했다고 그 기기를 막아버렸다 — 2026-08-10 사고 그 자체다. "
            + "'막는다' 가 아니라 '안 바꾼다' 여야 한다.");
        Assert.True(status == "approved",
            $"승격 실패가 상태를 '{status}' 로 떨어뜨렸다 — 쓰던 사람이 쫓겨난다.");
    }

    /// <summary>
    /// 🔴 <b>G-13b-짝. 컴퓨터 칸이 남아 있으면 승격이 <b>실제로 된다</b>.</b>
    ///
    /// <para>
    /// ⚠️ 이 짝이 없으면 <i>"승격을 전부 무시한다"</i> 는 가짜로도 G-13b 가 초록불이 된다.
    /// 그러면 <b>진짜 컴퓨터가 영영 휴대기기 칸에 갇힌다.</b>
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-13b-짝 🔴 컴퓨터 칸이 남으면 승격은 실제로 된다")]
    public async Task G13b_짝_자리가_있으면_승격된다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 🔴 이번엔 컴퓨터 칸이 넉넉하다.
        await SetSlotPolicyAsync(db, pcLimit: 5, mobileLimit: 5);

        var svc = NewService(db);

        const string phoneId = "phone-0000-0000-0000-000000000003";
        await InsertDeviceAsync(db, phoneId, "approved",
            "FP-폰2-" + Guid.NewGuid().ToString("N"), deviceType: "mobile");

        await svc.RegisterOrRefreshAsync(
            TenantId, "user-0001", 
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                DeviceId = phoneId,
                Fingerprint = "FP-폰2-다시-" + Guid.NewGuid().ToString("N"),
                DeviceType = "pc",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
            }, "127.0.0.1");

        var (type, _, _, _) = await ReadRowAsync(db, phoneId);
        Assert.True(type == "pc",
            $"컴퓨터 칸이 남았는데도 종류가 '{type}' 그대로다 — 진짜 컴퓨터가 휴대기기 칸에 갇혔다.");
    }

    /// <summary>
    /// 🔴 <b>G-13b-역방향. <c>pc → mobile</c> 은 휴대기기 칸이 꽉 차 있어도 <b>무검사로 통과</b>한다.</b>
    ///
    /// <para>
    /// 🔴 <b>이것이 2026-08-10 사고를 막는 게이트다.</b> 아이패드가 컴퓨터로 잘못 잡혀 있다가
    /// 판정이 고쳐져 제자리(휴대기기)를 찾아가는 경우다 — 그쪽은 <b>요금 위험이 없으므로</b>
    /// 한도를 보면 안 된다. 여기에 검사를 넣으면 <b>쓰던 사람이 막힌다.</b>
    /// </para>
    ///
    /// <para>[반증] <c>pc → mobile</c> 에도 한도 검사를 넣으면 종류가 안 바뀌어 FAIL 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-13b-역방향 🔴 pc→mobile 은 칸이 꽉 차도 제자리를 찾아간다 (8/10 사고 방지)")]
    public async Task G13b_역방향_pc에서_mobile은_무검사로_바뀐다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 🔴 휴대기기 칸은 1자리뿐이고 **이미 찼다.** 그래도 옮겨와야 한다.
        await SetSlotPolicyAsync(db, pcLimit: 5, mobileLimit: 1);

        var svc = NewService(db);

        await InsertDeviceAsync(db, "mob-already-0000-0000-00000000004", "approved",
            "FP-이미쓰는폰-" + Guid.NewGuid().ToString("N"), deviceType: "mobile");

        // 컴퓨터로 잘못 잡혀 있던 아이패드.
        const string padId = "pad-0000-0000-0000-000000000005";
        await InsertDeviceAsync(db, padId, "approved",
            "FP-패드-" + Guid.NewGuid().ToString("N"), deviceType: "pc");

        var result = await svc.RegisterOrRefreshAsync(
            TenantId, "user-0001", 
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                DeviceId = padId,
                Fingerprint = "FP-패드-다시-" + Guid.NewGuid().ToString("N"),
                DeviceType = "mobile",
                UserAgent = "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)"
            }, "127.0.0.1");

        var (type, status, _, _) = await ReadRowAsync(db, padId);

        Assert.True(type == "mobile",
            $"휴대기기 칸이 찼다고 아이패드가 컴퓨터 칸('{type}')에 묶여 있다 — "
            + "고객이 쓰지도 않는 비싼 자리에 계속 돈을 낸다.");
        Assert.True(result.allowed && status == "approved",
            "제자리를 찾아가려는 기기를 막았다 — 2026-08-10 사고 재발이다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-V05 — 서버가 신고값을 그대로 믿지 않는다  🔴 최초 등록의 구멍
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-V05. 컴퓨터가 <c>mobile</c> 이라고 <b>신고해도</b> 컴퓨터 칸으로 세어진다.</b>
    ///
    /// <para>
    /// 🔴 <b>진짜 구멍은 승격이 아니라 최초 등록이었다.</b> 공격자는 승격할 이유가 없다 —
    /// 처음부터 <c>mobile</c> 이라 신고하고 <b>안 바꾸면 그만</b>이다.
    /// ⇒ <b>2-1 만 하면 게이트는 초록이고 구멍은 잔존한다</b>(거짓봉합).
    /// </para>
    ///
    /// <para>
    /// [무엇을 태우나] 신고값은 <c>mobile</c> 인데 <c>User-Agent</c> 는 Windows 다.
    /// <b>서버가 자기 눈으로 읽은 값</b>이 이기는지 본다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>완벽을 약속하지 않는다</b> — User-Agent 도 위조할 수 있다.
    /// 이 게이트가 지키는 것은 <b>"신고값을 그대로 믿지 않는다"</b> 하나다.
    /// </para>
    ///
    /// <para>[반증] <c>ResolveDeviceType</c> 을 <c>NormalizeDeviceType</c> 으로 되돌리면 mobile 로 저장돼 FAIL 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-V05 🔴 컴퓨터가 'mobile' 이라 신고해도 컴퓨터 칸으로 세어진다")]
    public async Task GV05_신고값을_그대로_믿지_않는다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 5, mobileLimit: 5);
        await InsertUserAsync(db, "user-0001", "v05@example.com");

        var svc = NewService(db);

        // 🔴 거짓 신고: 나는 휴대기기다. 그러나 User-Agent 는 Windows 컴퓨터다.
        var r = await svc.RegisterOrRefreshAsync(
            TenantId, "user-0001", 
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                Fingerprint = "FP-사칭-" + Guid.NewGuid().ToString("N"),
                DeviceType = "mobile",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120"
            }, "127.0.0.1");

        var (type, _, _, _) = await ReadRowAsync(db, r.deviceId!);
        Assert.True(type == "pc",
            $"Windows 컴퓨터가 'mobile' 로 신고했더니 표에 '{type}' 로 저장됐다 — "
            + "화면 조작만으로 싼 칸을 고를 수 있다는 뜻이다. 승격을 막아도 여기서 그냥 샌다.");
    }

    /// <summary>
    /// 🔴 <b>G-V05-짝. 진짜 휴대기기는 <b>휴대기기로 남는다</b>.</b>
    ///
    /// <para>
    /// ⚠️ 이 짝이 없으면 <i>"전부 pc 로 본다"</i> 는 가짜로도 G-V05 가 초록불이 된다 —
    /// 그러면 <b>모든 폰이 비싼 칸을 먹어 고객이 돈을 더 낸다.</b>
    /// </para>
    /// <para>
    /// 🔴 <b>아이패드도 함께 본다.</b> 서버는 손가락 터치를 못 봐서 아이패드를 Mac 으로 읽는다 —
    /// 그래서 <b>Mac 계열은 판정하지 않고 비켜서게</b> 했다. 그것이 실제로 지켜지는지 본다.
    /// 안 지켜지면 <b>2026-08-10 사고(아이패드가 컴퓨터 칸을 먹던 일)가 재발</b>한다.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-V05-짝 🔴 진짜 폰·아이패드는 휴대기기로 남는다 (전부 pc 로 보는 가짜 방지)")]
    public async Task GV05_짝_진짜_휴대기기는_그대로다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 5, mobileLimit: 5);
        await InsertUserAsync(db, "user-0001", "v05pair@example.com");

        var svc = NewService(db);

        // ① 진짜 안드로이드 폰.
        var phone = await svc.RegisterOrRefreshAsync(
            TenantId, "user-0001", 
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                Fingerprint = "FP-폰진짜-" + Guid.NewGuid().ToString("N"),
                DeviceType = "mobile",
                UserAgent = "Mozilla/5.0 (Linux; Android 14; SM-S918N) AppleWebKit/537.36"
            }, "127.0.0.1");

        var (phoneType, _, _, _) = await ReadRowAsync(db, phone.deviceId!);
        Assert.True(phoneType == "mobile",
            $"진짜 안드로이드 폰이 '{phoneType}' 로 저장됐다 — 고객이 비싼 칸에 돈을 낸다.");

        // ② 🔴 아이패드 — 스스로를 Mac 이라 신고한다. 클라이언트만 터치로 그것을 안다.
        //    서버가 Mac 을 pc 로 단정하면 여기서 뒤집혀 8/10 사고가 재발한다.
        var pad = await svc.RegisterOrRefreshAsync(
            TenantId, "user-0001", 
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                Fingerprint = "FP-패드진짜-" + Guid.NewGuid().ToString("N"),
                DeviceType = "mobile",
                UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15"
            }, "127.0.0.1");

        var (padType, _, _, _) = await ReadRowAsync(db, pad.deviceId!);
        Assert.True(padType == "mobile",
            $"아이패드가 '{padType}' 로 저장됐다 — 서버가 Mac 을 컴퓨터로 단정했다. "
            + "2026-08-10 사고(아이패드가 컴퓨터 칸을 먹던 일) 재발이다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-13+ — 태블릿은 따로 저장되고, 과금은 휴대기기와 한 칸  🔴 2-6
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-13+. <c>tablet</c> 은 <c>tablet</c> 으로 저장되고, <b>슬롯은 휴대기기 칸에 합산</b>된다.</b>
    ///
    /// <para>
    /// 사장님 결재 3 — <i>"테블렛,모바일 같이 씀"</i> 은 <b>과금</b> 이야기다.
    /// ⇒ <b>저장은 세밀하게, 과금은 단순하게.</b>
    /// </para>
    ///
    /// <para>
    /// 🔴 [총합 항등식] 칸을 셋으로 늘리지 않았다는 것을 <b>숫자로</b> 확인한다:
    /// <c>mobileUsed</c> 가 <c>mobile</c> + <c>tablet</c> 을 <b>함께</b> 세는가.
    /// 안 세면 태블릿이 <b>공짜로 쓰인다.</b>
    /// </para>
    ///
    /// <para>[반증] 계수에서 tablet 을 빼면 <c>MobileUsed</c> 가 1 이 되어 FAIL 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-13+ 🔴 태블릿은 따로 저장되고 과금은 휴대기기와 한 칸이다")]
    public async Task G13플러스_태블릿은_따로_저장되고_과금은_한_칸이다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 5, mobileLimit: 5);

        var svc = NewService(db);

        await InsertUserAsync(db, "user-0001", "tablet@example.com");

        // 🔴 [2026-08-18 이 게이트는 처음에 **가짜였다** — 그 사실을 여기 적어 둔다]
        //
        //   종전엔 `tablet` 을 InsertDeviceAsync 로 **표에 직접 넣고** 그 값을 다시 읽었다.
        //   ⇒ 그것은 **NormalizeDeviceType 을 통째로 건너뛴다.** 넣은 값이 그대로 나오는 것은
        //     내 코드가 아니라 **표가 값을 보관한다는 사실**을 시험한 것이다.
        //   🔴 실제로 2-6 봉합을 빼고(`tablet → mobile` 로 되돌리고) 돌려도 **초록불이었다.**
        //
        //   [고침] **실제 등록 경로를 태운다.** 클라이언트가 'tablet' 을 신고하면
        //     그 값이 정규화를 지나 표에 어떻게 남는지를 본다 — 그래야 내 코드가 탄다.
        //   ⚠️ UserAgent 는 iPad 로 준다. 서버 교차검증(V-05)이 Mac 계열에서 비켜서므로
        //     신고값 'tablet' 이 존중되는지가 여기서 갈린다.
        await svc.RegisterOrRefreshAsync(
            TenantId, "user-0001",
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                Fingerprint = "FP-모바일-" + Guid.NewGuid().ToString("N"),
                DeviceType = "mobile",
                UserAgent = "Mozilla/5.0 (Linux; Android 14; SM-S918N)"
            }, "127.0.0.1");

        var tabletReg = await svc.RegisterOrRefreshAsync(
            TenantId, "user-0001",
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                Fingerprint = "FP-태블릿-" + Guid.NewGuid().ToString("N"),
                DeviceType = "tablet",
                UserAgent = "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)"
            }, "127.0.0.1");

        // 승인제가 켜져 있어 pending 으로 들어온다 — 계수는 approved 만 세므로 승인시킨다.
        //   ⚠️ 스위치로 우회하지 않고 **승인 API 를 부른다**(작업지시서 §6).
        foreach (var id in await db.QueryAsync<string>(
                     "SELECT device_id FROM tenant_devices WHERE tenant_id = @Tid", new { Tid = TenantId }))
        {
            await svc.ApproveAsync(id, TenantId, "admin-user");
        }

        // 🔴 판정 ① — 저장이 갈려 있나. tablet 이 mobile 로 뭉개지지 않았나.
        var (tabletType, _, _, _) = await ReadRowAsync(db, tabletReg.deviceId!);
        Assert.True(tabletType == "tablet",
            $"태블릿이 '{tabletType}' 로 저장됐다 — 나중에 '태블릿은 따로 받자' 하실 때 과거 자료가 없다.");

        // 🔴 판정 ② — **과금은 한 칸.** 둘이 합쳐 2 로 세어지는가.
        var quota = await svc.GetQuotaAsync(TenantId);
        Assert.True(quota.MobileUsed == 2,
            $"휴대기기 칸이 {quota.MobileUsed} 대로 세어졌다 — 2 여야 한다. "
            + "태블릿이 어느 칸에도 안 잡혀 **공짜로 쓰인다**는 뜻이다.");
        Assert.True(quota.PcUsed == 0,
            $"태블릿이 컴퓨터 칸({quota.PcUsed})으로 샜다 — 가격표 2칸 구조가 깨진다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-DP1 — 폐기하면 열쇠도 사라진다  🔴 검증팀 [4] 적발
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-DP1. 기기를 폐기하면 <c>auth_key_hash</c> 가 <b>표에서 사라진다.</b></b>
    ///
    /// <para>
    /// [무엇이 문제였나] 폐기는 <c>status</c> 만 바꾸고 <b>열쇠를 그대로 뒀다.</b>
    /// 막는 것은 <c>VerifyAuthKeyAsync</c> 안의 <b>한 줄뿐</b>이었고,
    /// 🔴 검증팀이 <b>그 한 줄을 빼도 12/12 초록불</b>이었다 — 지켜보는 눈이 0개였다.
    /// </para>
    ///
    /// <para>
    /// 🔴 [왜 표를 보는가] <c>VerifyAuthKeyAsync</c> 의 <b>반환값</b>이 null 인지만 보면,
    /// 그것은 <b>상태 검사 한 줄</b>을 시험하는 것이다. 그 줄이 사라지면 그대로 뚫린다.
    /// ⇒ <b>근본은 "열쇠가 있느냐"</b> 다. 열쇠가 없으면 방어할 것도 없다.
    /// </para>
    ///
    /// <para>[반증] <c>RevokeAsync</c> 에서 <c>auth_key_hash = NULL</c> 을 빼면 해시가 남아 FAIL 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-DP1 🔴 폐기하면 인증키가 표에서 사라진다")]
    public async Task GDp1_폐기하면_인증키가_사라진다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 5, mobileLimit: 5);

        var svc = NewService(db);

        const string deviceId = "rev-0000-0000-0000-000000000008";
        const string authKey = "REVOKE1111TEST2222KEY3333VALUE44";
        await InsertDeviceAsync(db, deviceId, "approved",
            "FP-폐기대상-" + Guid.NewGuid().ToString("N"),
            deviceType: "pc", authKeyHash: Sha256Hex(authKey));

        await svc.RevokeAsync(deviceId, TenantId, "admin-user", "시험 폐기");

        // 🔴 판정 = 표. 열쇠가 진짜로 없어졌나.
        var (_, status, _, hash) = await ReadRowAsync(db, deviceId);
        Assert.True(status == "revoked", $"폐기했는데 상태가 '{status}' 다.");
        Assert.True(hash is null,
            "폐기했는데 인증키 해시가 표에 그대로 남아 있다 — "
            + "상태 검사 한 줄이 사라지거나 우회되면 그 키로 다시 들어온다.");

        // 🔴 겹쳐 확인 — 실제로 그 키가 안 통하는가(상태 검사 + 키 소거 두 겹).
        var opened = await svc.VerifyAuthKeyAsync(authKey, TenantId, deviceId);
        Assert.True(opened is null, "폐기된 기기의 옛 키로 문이 열렸다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-DP2 — 메인PC 는 반려되지 않고, 반려됐어도 빠져나온다  🔴 검증팀 [4] 적발
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-DP2-a. 메인PC 는 <b>반려할 수 없다</b> (막는 자리).</b>
    ///
    /// <para>
    /// [무엇이 문제였나] 폐기(<c>RevokeAsync</c>)에는 메인PC 가드가 있는데 <b>반려에는 없었다.</b>
    /// 그리고 <b>메인PC 도 <c>pending</c> 일 수 있다</b> ⇒ 검증팀이 실제로 메인PC 를 <c>rejected</c> 로 만들었다.
    /// </para>
    /// <para>
    /// 🔴 [왜 P0 인가] 8/16 에 <b>대표가 자기 화면에서 막혀 스스로 못 빠져나온</b> 사고가 있었다(커밋 30e3873).
    /// 메인PC 가 <c>rejected</c> 가 되면 <b>승인해 줄 수 있는 유일한 사람이 승인 화면에 못 들어간다.</b>
    /// </para>
    /// <para>[반증] <c>RejectAsync</c> 의 메인PC 가드를 빼면 상태가 <c>rejected</c> 로 바뀌어 FAIL 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-DP2-a 🔴 메인PC 는 반려되지 않는다 (막는 자리)")]
    public async Task GDp2a_메인PC는_반려되지_않는다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string mainPcId = "main-0000-0000-0000-000000000009";
        await InsertDeviceAsync(db, mainPcId, "pending",
            "FP-메인PC-" + Guid.NewGuid().ToString("N"), deviceType: "pc", isMainPc: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RejectAsync(mainPcId, TenantId, "admin-user", "실수로 누름"));

        // 🔴 판정 = 표. 예외가 났다고 끝이 아니라 **상태가 안 바뀌었나**를 본다.
        var (_, status, _, _) = await ReadRowAsync(db, mainPcId);
        Assert.True(status == "pending",
            $"메인PC 가 '{status}' 가 됐다 — 회사 서버가 반려되면 대표가 스스로 못 빠져나온다.");
    }

    /// <summary>
    /// 🔴 <b>G-DP2-a-짝. 일반 기기는 <b>종전대로 반려된다</b>.</b>
    ///
    /// <para>
    /// ⚠️ 이 짝이 없으면 <i>"전부 못 반려하게 막는다"</i> 는 가짜로도 초록불이 된다 —
    /// 그러면 <b>작1 의 1-4(거절→재신청)가 통째로 죽는다.</b>
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-DP2-a-짝 🔴 일반 기기는 종전대로 반려된다 (전부 막는 가짜 방지)")]
    public async Task GDp2a_짝_일반기기는_반려된다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var svc = NewService(db);

        const string normalId = "norm-0000-0000-0000-00000000000a";
        await InsertDeviceAsync(db, normalId, "pending",
            "FP-일반기기-" + Guid.NewGuid().ToString("N"), deviceType: "pc", isMainPc: false);

        await svc.RejectAsync(normalId, TenantId, "admin-user", "모르는 기기");

        var (_, status, _, _) = await ReadRowAsync(db, normalId);
        Assert.True(status == "rejected",
            $"일반 기기를 반려했는데 상태가 '{status}' 다 — 대표가 모르는 기기를 거부할 수 없다.");
    }

    /// <summary>
    /// 🔴 <b>G-DP2-b. 메인PC 가 어떤 이유로든 <c>rejected</c> 면 <b>스스로 빠져나온다</b> (빠져나오는 자리).</b>
    ///
    /// <para>
    /// 🔴 <b>막는 것과 빠져나오는 것은 다른 역할이다.</b> 막는 자리(G-DP2-a)만 만들면
    /// <b>이미 <c>rejected</c> 인 기존 행</b>이 영영 못 나온다 — 그 회사는 이미 갇혀 있다.
    /// </para>
    /// <para>
    /// 🔴 <b>왜 <c>pending</c> 이 아니라 <c>approved</c> 인가</b> — <c>pending</c> 으로 되돌리면
    /// <b>아무것도 안 고친 것</b>이다. 승인해 줄 유일한 사람이 <b>자기가 승인 대기에 갇혀</b>
    /// 승인 화면에 못 들어간다(8/16 P0 그 자체).
    /// </para>
    /// <para>
    /// [교훈] 8/16 구제책이 <c>"revoked"</c> <b>문자열 하나</b>에 걸려 있었고,
    /// 작1 이 <c>rejected</c> 를 새로 만들자 <b>조용히 그 상태를 안 덮게</b> 됐다(헌법 #12).
    /// </para>
    /// <para>[반증] <c>rejected &amp;&amp; isMainPc</c> 갈래를 빼면 대기 상태로 남아 FAIL 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-DP2-b 🔴 반려된 메인PC 는 다시 접속하면 스스로 회복한다")]
    public async Task GDp2b_반려된_메인PC는_스스로_회복한다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 1, mobileLimit: 1);

        var svc = NewService(db);

        // 🔴 이미 rejected 인 메인PC — 옛 봉합이 없던 시절에 만들어진 행이다.
        const string mainPcId = "main-0000-0000-0000-00000000000b";
        await InsertDeviceAsync(db, mainPcId, "rejected",
            "FP-갇힌메인PC-" + Guid.NewGuid().ToString("N"), deviceType: "pc", isMainPc: true);

        var result = await svc.RegisterOrRefreshAsync(
            TenantId, "owner-user", 
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                DeviceId = mainPcId,
                Fingerprint = "FP-갇힌메인PC-다시-" + Guid.NewGuid().ToString("N"),
                DeviceType = "pc",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
            }, "127.0.0.1");

        // 🔴 판정 = 표. 진짜로 되살아났나.
        var (_, status, _, _) = await ReadRowAsync(db, mainPcId);
        Assert.True(status == "approved",
            $"반려됐던 메인PC 가 '{status}' 에 머물러 있다 — 대표가 승인 화면에 못 들어간다. "
            + "pending 으로 되돌리는 것으로는 부족하다(승인해 줄 사람이 자기 자신이다).");

        Assert.True(result.allowed,
            "메인PC 가 자기 화면에서 막혔다 — 2026-08-16 P0(커밋 30e3873) 재발이다.");
    }

    /// <summary>
    /// 🔴 <b>G-DP2-b-짝. 일반 기기가 <c>rejected</c> 면 <b>승인으로 건너뛰지 않는다</b>.</b>
    ///
    /// <para>
    /// ⚠️ 이 짝이 없으면 <i>"rejected 는 전부 approved 로 되살린다"</i> 는 가짜로도 초록불이 된다 —
    /// 그러면 <b>대표가 거부한 기기가 스스로 승인되어</b> 승인제가 통째로 무의미해진다.
    /// 🔴 일반 기기는 <c>pending</c> 으로 돌아가 <b>대표의 판단을 다시 받아야</b> 한다(작1 1-4).
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-DP2-b-짝 🔴 반려된 일반 기기는 대기줄로만 돌아간다 (자동승인 가짜 방지)")]
    public async Task GDp2b_짝_반려된_일반기기는_대기줄로_간다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 5, mobileLimit: 5);

        var svc = NewService(db);

        const string normalId = "norm-0000-0000-0000-00000000000c";
        await InsertDeviceAsync(db, normalId, "rejected",
            "FP-거부된일반-" + Guid.NewGuid().ToString("N"), deviceType: "pc", isMainPc: false);

        await svc.RegisterOrRefreshAsync(
            TenantId, "user-0001", 
            new HitPan.Application.DTOs.Device.RegisterDeviceRequest
            {
                DeviceId = normalId,
                Fingerprint = "FP-거부된일반-다시-" + Guid.NewGuid().ToString("N"),
                DeviceType = "pc",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
            }, "127.0.0.1");

        var (_, status, _, _) = await ReadRowAsync(db, normalId);
        Assert.True(status == "pending",
            $"거부된 일반 기기가 '{status}' 가 됐다 — 대표가 거부한 기기가 스스로 되살아나면 "
            + "승인제가 통째로 무의미해진다. 'pending' 으로 돌아가 다시 판단받아야 한다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-2-5 — QR 승인 시 대표가 사람을 지정한다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-2-5. 대표가 승인하며 고른 사람이 <b>표에 실제로 붙는다.</b></b>
    ///
    /// <para>
    /// QR 로 들어온 폰은 <c>user_id</c> 가 <c>NULL</c> 이다 — 등록 시점엔 누구 폰인지 모른다.
    /// ⇒ <b>아는 사람이 아는 자리에서</b> 채운다(대표가 승인하는 그 순간).
    /// </para>
    /// <para>
    /// ⚠️ <b>안 고르면 기존 주인을 안 지운다</b> — 그것도 함께 본다.
    /// 안 그러면 PC 경로에서 승인 한 번에 주인이 날아간다.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-2-5 🔴 승인하며 고른 사람이 표에 붙고, 안 고르면 기존 주인이 안 지워진다")]
    public async Task G25_승인시_사람지정이_표에_붙는다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SetSlotPolicyAsync(db, pcLimit: 5, mobileLimit: 5);

        var svc = NewService(db);

        // 실제 사용자 한 명 — FK(users) 가 있어 존재해야 한다.
        const string userId = "user-real-0000-0000-00000000000d";
        await db.ExecuteAsync(
            """
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role,
                               account_type, is_active, created_at, updated_at)
            VALUES (@Uid, @Tid, 'staff@example.com', 'x', '김직원', 'user',
                    'tenant_user', 1, NOW(6), NOW(6))
            """,
            new { Uid = userId, Tid = TenantId });

        // ① QR 로 들어온 주인 없는 폰.
        const string qrPhoneId = "qrp-0000-0000-0000-00000000000e";
        await InsertDeviceAsync(db, qrPhoneId, "pending",
            "FP-QR폰-" + Guid.NewGuid().ToString("N"), deviceType: "mobile");

        await svc.ApproveAsync(qrPhoneId, TenantId, "admin-user", userId);

        var (_, status, assigned, _) = await ReadRowAsync(db, qrPhoneId);
        Assert.True(status == "approved", $"승인했는데 상태가 '{status}' 다.");
        Assert.True(assigned == userId,
            $"대표가 사람을 골랐는데 표의 주인이 '{assigned}' 다 — 나중에 누구 폰인지 알 수 없다.");

        // ② 🔴 짝 — 안 고르면 **기존 주인을 안 지운다.**
        const string pcId = "pcx-0000-0000-0000-00000000000f";
        await InsertDeviceAsync(db, pcId, "pending",
            "FP-주인있는PC-" + Guid.NewGuid().ToString("N"), deviceType: "pc");
        await db.ExecuteAsync(
            "UPDATE tenant_devices SET user_id = @Uid WHERE device_id = @Id",
            new { Uid = userId, Id = pcId });

        await svc.ApproveAsync(pcId, TenantId, "admin-user", null);

        var (_, _, stillAssigned, _) = await ReadRowAsync(db, pcId);
        Assert.True(stillAssigned == userId,
            "사람을 안 골랐더니 기존 주인이 지워졌다 — 승인 한 번에 주인이 날아간다.");
    }
}
