using Dapper;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Device;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 2026-10-05 작4 — 슬롯 폐기 · 「접속기기 확인」 게이트의 <b>DB 몫</b>
/// (설계 20261005_설계_접속기기확인_슬롯폐기 §8 · 9/28 §5 · CTO 게시 조건 G-2ⓐ).
/// </summary>
/// <remarks>
/// <para>DB 불필요 몫 14건은 <c>ApprovalRetiredAccessGateTests</c> 에 있다. 이 파일은 실물 <see cref="TenantDeviceService"/> 를
/// 출하 DDL(<c>installer/hitpan_db_clean.sql</c>) 위 격리 DB 에 붙여 잰다 — 흉내가 아니다.</para>
/// <para>클래스 이름에 <c>ApprovalRetiredAccessGate</c> 가 들어 있어 CI <c>db-gate</c> 잡의 작4 필터가 그대로 문다 — 워크플로 0줄.</para>
/// <para>⚠️ 개발 PC 는 SKIP 이 정상(<c>hitpan</c> 은 CREATE DATABASE 거부) — CI <c>HITPAN_REQUIRE_DB</c> 가 FAIL 로 바꾼다.
/// 로컬 초록은 증거가 아니다.</para>
/// <para>🔴 대조군 방식 — 각 시험 안에서 <b>봉합 전 판정이 같은 줄을 어떻게 봤는지</b>를 DB 로 다시 잰다
/// (옛 한도 셈 SQL · 옛 승인 판정 <c>IsDeviceApprovedAsync</c> · 테넌트 조건 없는 원문 조회). 대조군이 「막혔을 것」을
/// 보여 주지 못하면 그 시험의 초록은 아무것도 증명하지 않는다 ⇒ 대조군 단언이 함께 실패한다.
/// 코드를 되돌린 커밋으로 재는 음성대조(설계 §8 「원복 → FAIL」)는 draft PR <c>db-gate</c> 몫이다.</para>
/// <para>연결 문자열은 <see cref="MySqlConnectionStringBuilder"/> 로 만든다 — 시험 파일에 자격증명 모양 글자를 두지 않는다(비밀 스캔).
/// 격리 DB 연결은 풀을 끈다(<c>Pooling=false</c> · 관례 AccountSeatGateSeal1Tests:76).</para>
/// <para>🔴 메인PC 서버줄 갈래(옛 :274-501)·G-M8* 는 재지 않는다 — 모든 기기 줄은 지문 <c>HFPv2-</c> · <c>is_main_pc=0</c> · 터널 접속(<c>IsLocalConsole=false</c>).</para>
/// </remarks>
[Collection("ApprovalRetiredAccessGateDb")]
public sealed class ApprovalRetiredAccessGateDbTests : IDisposable
{
    private readonly string _dbName = "hitpan_arg_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;
    private readonly string _tenantA = Guid.NewGuid().ToString();
    private readonly string _tenantB = Guid.NewGuid().ToString();

    private const string PcUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";
    private const string PhoneUa = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

    // ══ 준비물 — AccountSeatGateSeal1Tests 와 같은 방식(출하 DDL 위 격리 DB) ══
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }

    private static MySqlConnectionStringBuilder ServerBuilder() => new()
    {
        Server = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost",
        Port = uint.TryParse(Environment.GetEnvironmentVariable("HITPAN_DB_PORT"), out var p) ? p : 3306,
        UserID = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root",
        Password = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "",
        DefaultCommandTimeout = 90,
        GuidFormat = MySqlGuidFormat.None,
        AllowUserVariables = true,
    };

    private static string ServerConnString() => ServerBuilder().ConnectionString;

    private string DbConnString()
    {
        var b = ServerBuilder();
        b.Database = _dbName;
        b.Pooling = false;
        return b.ConnectionString;
    }

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private bool Ready(string gate)
    {
        if (!DbGateEnvironment.IsCi)
        {
            var ok = File.Exists(MysqlExe());
            if (ok)
            {
                try
                {
                    using var c = new MySqlConnection(ServerConnString());
                    c.Open();
                    c.Execute($"CREATE DATABASE IF NOT EXISTS `{_dbName}`");
                    c.Execute($"DROP DATABASE IF EXISTS `{_dbName}`");
                }
                catch (MySqlException ex)
                {
                    Console.Error.WriteLine($"[{gate}] 서버 접속·권한 없음: {ex.Message}");
                    ok = false;
                }
            }
            if (!ok) return !DbGateEnvironment.SkipOrFail(gate);
        }
        SetUpFreshInstall();
        return true;
    }

    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(_dbName);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddlPath));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"출하 DDL import 실패:\n{err}");
    }

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[ApprovalRetiredAccessGateDb] 임시 DB 정리 실패 — 손으로 지워라: {_dbName} ({ex.Message})");
        }
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(DbConnString());
        await c.OpenAsync();
        return c;
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// 🔴 실물 서비스 — 출하 appsettings 처럼 <c>DeviceApproval:Enabled=true</c> 를 넣는다(9/28 §3:
    /// 「G-AR1 은 설정을 true 로 넣고 잰다」 — 「읽지 않는다」를 동작으로 증명).
    /// </summary>
    private static TenantDeviceService NewService(MySqlConnection db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceApproval:Enabled"] = "true" })
            .Build();
        return new TenantDeviceService(db, new NoOpAudit(), config, NullLogger<TenantDeviceService>.Instance);
    }

    /// <summary>회사 + 구독(basic) + 옛 한도 설정표. 한도 표는 이제 읽히지 않는다 — 대조군 셈에만 쓴다.</summary>
    private static async Task SeedCompanyAsync(MySqlConnection db, string tenantId, int pcLimit, int mobileLimit)
    {
        await db.ExecuteAsync(
            "INSERT INTO local_subscription (tenant_id, max_users, extra_accounts) VALUES (@T, 8, 0)",
            new { T = tenantId });
        // ⚠️ policy_id(PK)·label 은 기본값이 없다(#13 — 출하 DDL :4051 판독 · DeviceTypeQrGateTests:175 실측).
        await db.ExecuteAsync(@"
            INSERT INTO device_slot_policy_settings (policy_id, tenant_id, policy_key, policy_value, label)
            VALUES (@P1, @T, 'tier.basic.pc_limit', @Pc, '컴퓨터 한도(시험)'),
                   (@P2, @T, 'tier.basic.mobile_limit', @Mo, '휴대기기 한도(시험)')",
            new { P1 = Guid.NewGuid().ToString(), P2 = Guid.NewGuid().ToString(), T = tenantId, Pc = pcLimit, Mo = mobileLimit });
    }

    private static async Task<string> InsertUserAsync(MySqlConnection db, string tenantId, string email, string name, bool parent = false)
    {
        var id = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent,
                               is_active, is_deleted, created_at, updated_at)
            VALUES (@U, @T, @E, 'x', @N, @R, @A, @P, 1, 0, NOW(6), NOW(6))",
            new
            {
                U = id, T = tenantId, E = email, N = name,
                R = parent ? "TenantAdmin" : "User",
                A = parent ? "tenant_admin" : "tenant_user",
                P = parent ? 1 : 0
            });
        return id;
    }

    /// <summary>기기 한 줄 — 상태·종류를 시험이 지정한다. 지문은 브라우저 모양(HFPv2-) · 표식 0(메인PC 갈래 무접촉).</summary>
    private static async Task<(string id, string fp)> InsertDeviceAsync(
        MySqlConnection db, string tenantId, string? userId, string type, string status)
    {
        var id = Guid.NewGuid().ToString();
        var fp = "HFPv2-" + Guid.NewGuid().ToString("N");
        await db.ExecuteAsync(@"
            INSERT INTO tenant_devices (device_id, tenant_id, user_id, device_type, device_name, fingerprint,
                                        ip_address, status, registered_at, is_main_pc)
            VALUES (@Id, @T, @U, @Ty, '시험기기', @Fp, '127.0.0.1', @S, NOW(6), 0)",
            new { Id = id, T = tenantId, U = userId, Ty = type, Fp = fp, S = status });
        return (id, fp);
    }

    private static RegisterDeviceRequest Pc(string fp, string? deviceId = null) => new()
    {
        Fingerprint = fp, DeviceType = "pc", UserAgent = PcUa, DeviceName = "사무실 PC", DeviceId = deviceId, IsLocalConsole = false
    };

    private static RegisterDeviceRequest Phone(string fp, string? deviceId = null) => new()
    {
        Fingerprint = fp, DeviceType = "mobile", UserAgent = PhoneUa, DeviceName = "직원 폰", DeviceId = deviceId, IsLocalConsole = false
    };

    private static string NewFp() => "HFPv2-" + Guid.NewGuid().ToString("N");

    private static async Task<(string status, string type, string? approvedBy)> RowAsync(MySqlConnection db, string deviceId) =>
        await db.QueryFirstAsync<(string, string, string?)>(
            "SELECT status, device_type, approved_by FROM tenant_devices WHERE device_id = @Id", new { Id = deviceId });

    /// <summary>
    /// 🔴 대조군 — <b>봉합 전 한도 셈</b>을 그대로 다시 한다(옛 <c>CountUsedSlotsAsync</c> :1846 SQL + 옛 <c>GetLimitsAsync</c> 설정 열쇠).
    /// 「옛 판이었다면 이 회사는 칸이 찼다」를 DB 로 증명한다 — 이게 거짓이면 「막히지 않았다」는 초록이 아무것도 아니다.
    /// </summary>
    private static async Task<(int pcUsed, int mobileUsed, int pcLimit, int mobileLimit)> OldSlotStateAsync(MySqlConnection db, string tenantId)
    {
        var counts = (await db.QueryAsync<(string t, int c)>(@"
            SELECT device_type AS t, COUNT(*) AS c FROM tenant_devices
            WHERE tenant_id = @T AND status = 'approved' GROUP BY device_type", new { T = tenantId })).ToList();
        var pcLimit = await db.ExecuteScalarAsync<int>(
            "SELECT policy_value FROM device_slot_policy_settings WHERE tenant_id = @T AND policy_key = 'tier.basic.pc_limit'", new { T = tenantId });
        var mobileLimit = await db.ExecuteScalarAsync<int>(
            "SELECT policy_value FROM device_slot_policy_settings WHERE tenant_id = @T AND policy_key = 'tier.basic.mobile_limit'", new { T = tenantId });
        return (TenantDeviceService.PcUsedFrom(counts), TenantDeviceService.MobileUsedFrom(counts), pcLimit, mobileLimit);
    }

    // ══ G-AR1 ⓐ·ⓒ — #20 핵심: 칸이 찬 회사의 새 직원이 들어간다 · 새 줄은 approved ══

    [Fact(DisplayName = "G-AR1ac 🔴 칸 찬 회사(컴퓨터 3/1 · 휴대폰 2/1) 새 직원 첫 로그인 허용 · 새 줄 approved · 승인자 기록 · 거절 기록 0 · 대조군(옛 셈이면 칸 참 · 옛 판정은 pending 줄을 막는다)")]
    public async Task AR1_Full_Company_New_Staff_Allowed_Row_Approved()
    {
        if (!Ready("G-AR1ac")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, _tenantA, pcLimit: 1, mobileLimit: 1);
        var owner = await InsertUserAsync(db, _tenantA, "owner_a", "대표", parent: true);
        for (var i = 0; i < 3; i++) await InsertDeviceAsync(db, _tenantA, owner, "pc", "approved");
        for (var i = 0; i < 2; i++) await InsertDeviceAsync(db, _tenantA, owner, "mobile", "approved");
        var staff = await InsertUserAsync(db, _tenantA, "staff_new", "새직원");

        // 대조군 ① — 옛 셈이면 이 회사는 컴퓨터·휴대폰 칸이 다 찼다(옛 :716·:728 이 401 을 냈을 자리)
        var old = await OldSlotStateAsync(db, _tenantA);
        Assert.True(old.pcUsed >= old.pcLimit, $"대조군 무효 — 컴퓨터 칸이 안 찼다({old.pcUsed}/{old.pcLimit})");
        Assert.True(old.mobileUsed >= old.mobileLimit, $"대조군 무효 — 휴대폰 칸이 안 찼다({old.mobileUsed}/{old.mobileLimit})");

        var svc = NewService(db);
        var before = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tenant_devices WHERE tenant_id = @T", new { T = _tenantA });
        var r = await svc.RegisterOrRefreshAsync(_tenantA, staff, Pc(NewFp()), "127.0.0.1");

        Assert.True(r.allowed, $"칸 찬 회사의 새 직원이 막혔다 — 사유: {r.reason}");
        Assert.Equal("", r.reason);
        Assert.True(r.newlyRegistered);
        Assert.NotNull(r.deviceId);
        Assert.Equal(before + 1, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tenant_devices WHERE tenant_id = @T", new { T = _tenantA }));

        // ⓐ 새 줄은 approved — 설정 DeviceApproval:Enabled=true 를 넣었는데도(읽지 않는다)
        var row = await RowAsync(db, r.deviceId!);
        Assert.Equal("approved", row.status);
        Assert.Equal("pc", row.type);
        Assert.Equal(staff, row.approvedBy);
        Assert.True(await svc.IsDeviceApprovedAsync(r.deviceId!, _tenantA));
        Assert.False(DeviceApprovalRetirement.ApprovalEnabled);

        // 거절 기록(denied_limit)이 남지 않았다
        Assert.Equal(0, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM device_login_logs WHERE tenant_id = @T AND login_result LIKE 'denied%'", new { T = _tenantA }));

        // 대조군 ② — 봉합 전 모양(새 줄이 pending)이었다면 같은 승인 판정이 그 줄을 막는다 ⇒ ⓐ 단언은 빈 단언이 아니다
        var (pendingId, _) = await InsertDeviceAsync(db, _tenantA, staff, "pc", "pending");
        Assert.False(await svc.IsDeviceApprovedAsync(pendingId, _tenantA));
    }

    // ══ G-AR2 — 기다리던 직원이 풀린다 ══

    [Fact(DisplayName = "G-AR2 🔴 pending 이던 옛 직원 줄 — 같은 지문 로그인 허용 · 사유 비움 · 줄 상태는 표시용으로 남음 · 대조군(옛 판정 IsDeviceApproved=false)")]
    public async Task AR2_Pending_Row_Released_On_Login()
    {
        if (!Ready("G-AR2")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, _tenantA, pcLimit: 5, mobileLimit: 5);
        var staff = await InsertUserAsync(db, _tenantA, "staff_wait", "대기직원");
        var (id, fp) = await InsertDeviceAsync(db, _tenantA, staff, "pc", "pending");
        var svc = NewService(db);

        var byFp = await svc.RegisterOrRefreshAsync(_tenantA, staff, Pc(fp), "127.0.0.1");
        Assert.True(byFp.allowed, $"pending 줄 직원이 막혔다 — 사유: {byFp.reason}");
        Assert.Equal("", byFp.reason);
        Assert.Equal(id, byFp.deviceId);
        Assert.False(byFp.newlyRegistered);

        var byId = await svc.RegisterOrRefreshAsync(_tenantA, staff, Pc(NewFp(), id), "127.0.0.1");
        Assert.True(byId.allowed);
        Assert.Equal(id, byId.deviceId);

        // 대조군 — 줄은 여전히 pending(지우지 않는다 · #37) · 옛 승인 판정은 이 줄을 막는다 ⇒ 허용은 바깥 한 자리 몫
        Assert.Equal("pending", (await RowAsync(db, id)).status);
        Assert.False(await svc.IsDeviceApprovedAsync(id, _tenantA));
    }

    // ══ G-AR3 — 예전에 폐기·거부된 컴퓨터 ══

    [Fact(DisplayName = "G-AR3 🔴 revoked·rejected 직원 줄 — 로그인 허용 · 사유 비움 · 줄은 안쪽 규칙대로 pending 으로 돌아감(표시용) · 대조군(옛 판정 막힘)")]
    public async Task AR3_Revoked_And_Rejected_Rows_Allowed()
    {
        if (!Ready("G-AR3")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, _tenantA, pcLimit: 5, mobileLimit: 5);
        var s1 = await InsertUserAsync(db, _tenantA, "staff_rev", "폐기직원");
        var s2 = await InsertUserAsync(db, _tenantA, "staff_rej", "거부직원");
        var (revId, revFp) = await InsertDeviceAsync(db, _tenantA, s1, "pc", "revoked");
        var (rejId, rejFp) = await InsertDeviceAsync(db, _tenantA, s2, "pc", "rejected");
        var svc = NewService(db);

        var rv = await svc.RegisterOrRefreshAsync(_tenantA, s1, Pc(revFp), "127.0.0.1");
        var rj = await svc.RegisterOrRefreshAsync(_tenantA, s2, Pc(rejFp), "127.0.0.1");

        Assert.True(rv.allowed, $"폐기 줄 직원이 막혔다 — 사유: {rv.reason}");
        Assert.True(rj.allowed, $"거부 줄 직원이 막혔다 — 사유: {rj.reason}");
        Assert.Equal("", rv.reason);
        Assert.Equal("", rj.reason);
        Assert.Equal(revId, rv.deviceId);
        Assert.Equal(rejId, rj.deviceId);

        // 안쪽(옛 :458·:538) 규칙은 무접촉 — 둘 다 pending 으로 돌아가 있다. 대조군: 옛 승인 판정은 둘 다 막는다
        Assert.Equal("pending", (await RowAsync(db, revId)).status);
        Assert.Equal("pending", (await RowAsync(db, rejId)).status);
        Assert.False(await svc.IsDeviceApprovedAsync(revId, _tenantA));
        Assert.False(await svc.IsDeviceApprovedAsync(rejId, _tenantA));
        // 줄이 늘지 않았다(새 기기로 잡지 않았다)
        Assert.Equal(2, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tenant_devices WHERE tenant_id = @T", new { T = _tenantA }));
    }

    // ══ G-AR4 — 휴대폰은 막히지 않는다 ══

    [Fact(DisplayName = "G-AR4 🔴 휴대폰 한도 0 — 한 직원 휴대폰 3대 첫 로그인 셋 다 허용 · 3줄 mobile·approved · 대조군(옛 셈이면 첫 대부터 칸 참)")]
    public async Task AR4_Three_Phones_Allowed_With_Zero_Mobile_Limit()
    {
        if (!Ready("G-AR4")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, _tenantA, pcLimit: 1, mobileLimit: 0);
        var staff = await InsertUserAsync(db, _tenantA, "staff_phone", "폰직원");

        var old = await OldSlotStateAsync(db, _tenantA);
        Assert.True(old.mobileUsed >= old.mobileLimit, "대조군 무효 — 휴대폰 칸이 안 찼다");

        var svc = NewService(db);
        var ids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var r = await svc.RegisterOrRefreshAsync(_tenantA, staff, Phone(NewFp()), "127.0.0.1");
            Assert.True(r.allowed, $"{i + 1}번째 휴대폰이 막혔다 — 사유: {r.reason}");
            Assert.True(r.newlyRegistered);
            ids.Add(r.deviceId!);
        }
        foreach (var id in ids)
        {
            var row = await RowAsync(db, id);
            Assert.Equal("approved", row.status);
            Assert.Equal("mobile", row.type);
        }
    }

    // ══ G-AR5 — 현황은 우리 회사만 (user_sessions.tenant_id NULL 행 포함) ══

    private static async Task<string> InsertSessionAsync(MySqlConnection db, string userId, string? sessionTenant, string kind)
    {
        var sid = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO user_sessions (session_id, user_id, tenant_id, login_at, last_active_at, is_active, expires_at, device_kind)
            VALUES (@S, @U, @T, UTC_TIMESTAMP(6) - INTERVAL 5 MINUTE, UTC_TIMESTAMP(6) - INTERVAL 1 MINUTE, 1,
                    UTC_TIMESTAMP(6) + INTERVAL 8 HOUR, @K)",
            new { S = sid, U = userId, T = sessionTenant, K = kind });
        return sid;
    }

    private static async Task InsertLoginTrailAsync(MySqlConnection db, string tenantId, string userId, string sessionId, string ua)
    {
        await db.ExecuteAsync(@"
            INSERT INTO audit_trail (log_id, tenant_id, user_id, action_type, entity_type, entity_id, after_value, created_at)
            VALUES (@L, @T, @U, 'login', 'user_session', @S, JSON_OBJECT('user_agent', @Ua), NOW(6))",
            new { L = Guid.NewGuid().ToString(), T = tenantId, U = userId, S = sessionId, Ua = ua });
    }

    [Fact(DisplayName = "G-AR5 🔴 current — A 대표 조회에 A 사람만 · 세션 tenant_id NULL 행도 사람 기준으로 격리 · 컴퓨터/휴대폰 구분 · 브라우저 칸은 A 기록만 · 대조군(테넌트 조건 없는 원문 조회엔 B 가 섞임 · B 조회엔 B 가 보임)")]
    public async Task AR5_Current_Is_Tenant_Isolated()
    {
        if (!Ready("G-AR5")) return;
        await using var db = await OpenAsync();
        var ownerA = await InsertUserAsync(db, _tenantA, "owner_a", "대표A", parent: true);
        var staffA = await InsertUserAsync(db, _tenantA, "staff_a", "직원A");
        var ownerB = await InsertUserAsync(db, _tenantB, "owner_b", "대표B", parent: true);
        var staffB = await InsertUserAsync(db, _tenantB, "staff_b", "직원B");

        var aPc = await InsertSessionAsync(db, staffA, _tenantA, "pc");
        await InsertSessionAsync(db, staffA, null, "mobile");             // A 사람 · 세션 tenant_id NULL
        var bPc = await InsertSessionAsync(db, staffB, _tenantB, "pc");
        await InsertSessionAsync(db, ownerB, null, "pc");                 // B 사람 · 세션 tenant_id NULL
        await InsertSessionAsync(db, staffB, null, "mobile");             // B 사람 · 세션 tenant_id NULL
        await InsertLoginTrailAsync(db, _tenantA, staffA, aPc, PcUa);
        // 남의 회사 기록이 A 세션 번호를 들고 있어도 A 브라우저 칸에 섞이면 안 된다
        await InsertLoginTrailAsync(db, _tenantB, staffB, aPc, "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Gecko/20100101 Firefox/131.0");
        await InsertLoginTrailAsync(db, _tenantB, staffB, bPc, PhoneUa);

        var svc = NewService(db);
        var a = await svc.GetAccessStatusAsync(_tenantA);

        var aIds = a.People.Select(p => p.UserId).ToHashSet();
        Assert.Equal(new HashSet<string> { ownerA, staffA }, aIds);
        Assert.DoesNotContain(ownerB, aIds);
        Assert.DoesNotContain(staffB, aIds);
        Assert.Equal(1, a.PcUsersNow);
        Assert.Equal(1, a.MobileSessionsNow);
        var pa = a.People.Single(p => p.UserId == staffA);
        Assert.NotNull(pa.Pc);
        Assert.Equal("크롬 · 윈도우", pa.Pc!.Browser);
        Assert.Single(pa.Mobiles);
        Assert.Null(a.People.Single(p => p.UserId == ownerA).Pc);

        // 대조군 ① — 테넌트 조건 없는 원문(봉합 전 모양: WHERE tenant_id 없음)이면 B 사람이 섞인다
        var unfiltered = (await db.QueryAsync<string>(@"
            SELECT DISTINCT s.user_id FROM user_sessions s
            WHERE s.is_active = 1 AND s.expires_at > UTC_TIMESTAMP(6)")).ToHashSet();
        Assert.Contains(staffB, unfiltered);
        Assert.Contains(ownerB, unfiltered);
        // 대조군 ② — 세션 칸(tenant_id)으로 거르면 A 의 NULL 휴대폰 줄이 빠진다 ⇒ 사람 기준 격리가 맞다
        Assert.Equal(1, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM user_sessions WHERE tenant_id = @T", new { T = _tenantA }));
        // 대조군 ③ — 같은 서비스가 B 로 물으면 B 가 보인다(데이터가 실재한다)
        var b = await svc.GetAccessStatusAsync(_tenantB);
        Assert.Equal(new HashSet<string> { ownerB, staffB }, b.People.Select(p => p.UserId).ToHashSet());
        Assert.Equal(2, b.PcUsersNow);
        Assert.Equal(1, b.MobileSessionsNow);
    }

    [Fact(DisplayName = "G-AR5a/AR6d 🔴 alerts — A 대표 조회에 A 기록만 · 고객용 2유형만(session_insert_failed 제외) · 문장에 UTC·원문 없음 · 대조군(원문 조회엔 3유형+B 섞임)")]
    public async Task AR5a_Alerts_Tenant_Isolated_And_Customer_Types_Only()
    {
        if (!Ready("G-AR5a")) return;
        await using var db = await OpenAsync();
        await InsertUserAsync(db, _tenantA, "owner_a", "대표A", parent: true);
        var staffA = await InsertUserAsync(db, _tenantA, "hong", "홍길동");
        var staffB = await InsertUserAsync(db, _tenantB, "kim", "김철수");

        const string raw = "개발문장 — 마지막 사용 2026-10-05 03:12 UTC · session=abc";
        async Task Alert(string? tenant, string user, string type) => await db.ExecuteAsync(@"
            INSERT INTO security_alerts (alert_id, tenant_id, user_id, alert_type, description, created_at)
            VALUES (@Id, @T, @U, @Ty, @D, NOW(6) - INTERVAL 1 HOUR)",
            new { Id = Guid.NewGuid().ToString(), T = tenant, U = user, Ty = type, D = raw });

        await Alert(_tenantA, staffA, "pc_login_blocked");
        await Alert(_tenantA, staffA, "pc_session_forced_out");
        await Alert(_tenantA, staffA, "session_insert_failed");
        await Alert(_tenantB, staffB, "pc_login_blocked");
        await Alert(null, staffB, "pc_login_blocked");                   // 회사 칸이 빈 기록

        var svc = NewService(db);
        var list = await svc.GetLoginConflictAlertsAsync(_tenantA);

        Assert.Equal(2, list.Count);
        Assert.Equal(new HashSet<string> { "pc_login_blocked", "pc_session_forced_out" }, list.Select(x => x.Kind).ToHashSet());
        Assert.All(list, x =>
        {
            Assert.Equal("hong", x.LoginId);
            Assert.Equal("홍길동", x.UserName);
            Assert.DoesNotContain("UTC", x.Message);
            Assert.DoesNotContain("개발문장", x.Message);
            Assert.DoesNotContain("session", x.Message);
            Assert.StartsWith("홍길동(hong) — ", x.Message);
        });
        Assert.DoesNotContain(list, x => x.LoginId == "kim");

        // 대조군 — 원문 조회(유형·회사 조건 없음)엔 기술 경고와 B·빈 회사 기록이 섞여 있다 ⇒ 거르는 것은 조회의 조건이다
        Assert.Equal(5, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM security_alerts"));
        Assert.Equal(3, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM security_alerts WHERE tenant_id = @T", new { T = _tenantA }));
        var b = await svc.GetLoginConflictAlertsAsync(_tenantB);
        Assert.Single(b);
        Assert.Equal("kim", b[0].LoginId);
    }

    // ══ G-AR9 — 칸이 찬 회사에서도 휴대폰 줄이 컴퓨터로 승격 ══

    [Fact(DisplayName = "G-AR9 🔴 컴퓨터 한도 1 꽉 참 · 휴대폰 줄을 컴퓨터로 로그인 → device_type=pc · 허용 · 대조군(옛 셈이면 컴퓨터 칸 참 = 옛 :568 이 종류를 안 바꿨을 자리)")]
    public async Task AR9_Promotion_Always_Happens()
    {
        if (!Ready("G-AR9")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, _tenantA, pcLimit: 1, mobileLimit: 5);
        var owner = await InsertUserAsync(db, _tenantA, "owner_a", "대표", parent: true);
        var staff = await InsertUserAsync(db, _tenantA, "staff_p", "승격직원");
        await InsertDeviceAsync(db, _tenantA, owner, "pc", "approved");
        var (phoneId, _) = await InsertDeviceAsync(db, _tenantA, staff, "mobile", "approved");

        var old = await OldSlotStateAsync(db, _tenantA);
        Assert.True(old.pcUsed >= old.pcLimit, $"대조군 무효 — 컴퓨터 칸이 안 찼다({old.pcUsed}/{old.pcLimit})");

        var svc = NewService(db);
        var r = await svc.RegisterOrRefreshAsync(_tenantA, staff, Pc(NewFp(), phoneId), "127.0.0.1");

        Assert.True(r.allowed, $"승격 로그인이 막혔다 — 사유: {r.reason}");
        Assert.Equal(phoneId, r.deviceId);
        Assert.False(r.newlyRegistered);
        var row = await RowAsync(db, phoneId);
        Assert.Equal("pc", row.type);
        Assert.Equal("approved", row.status);
    }

    // ══ G-AR11 — 승인·QR 등록이 한도로 안 실패 ══

    [Fact(DisplayName = "G-AR11 🔴 한도 0/0 · 칸 가득 — ApproveAsync 인증키 발급·approved · QR 등록 ok · 한도 예외·거절 0 · 대조군(옛 셈이면 두 칸 다 참)")]
    public async Task AR11_Approve_And_Qr_Not_Limited()
    {
        if (!Ready("G-AR11")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, _tenantA, pcLimit: 0, mobileLimit: 0);
        var owner = await InsertUserAsync(db, _tenantA, "owner_a", "대표", parent: true);
        var staff = await InsertUserAsync(db, _tenantA, "staff_q", "큐알직원");
        await InsertDeviceAsync(db, _tenantA, owner, "pc", "approved");
        await InsertDeviceAsync(db, _tenantA, owner, "mobile", "approved");
        var (pendingId, _) = await InsertDeviceAsync(db, _tenantA, staff, "pc", "pending");

        var old = await OldSlotStateAsync(db, _tenantA);
        Assert.True(old.pcUsed >= old.pcLimit && old.mobileUsed >= old.mobileLimit, "대조군 무효 — 칸이 안 찼다");

        var svc = NewService(db);

        // 승인 — 옛 :906-912 이면 「한도 초과」 예외
        var key = await svc.ApproveAsync(pendingId, _tenantA, owner);
        Assert.False(string.IsNullOrEmpty(key));
        Assert.Equal("approved", (await RowAsync(db, pendingId)).status);

        // QR 등록 — 옛 :1526-1535 이면 「인증기기 한도초과」 거절
        var token = await svc.IssueMobileRegisterTokenAsync(_tenantA, owner);
        var qr = await svc.RegisterMobileByTokenAsync(token, "직원 폰", NewFp(), "127.0.0.1", PhoneUa, null);
        Assert.True(qr.ok, $"QR 등록이 거절됐다 — {qr.message}");
        Assert.DoesNotContain("한도", qr.message);
        Assert.NotNull(qr.deviceId);
        Assert.Equal("mobile", (await RowAsync(db, qr.deviceId!)).type);
    }
}
