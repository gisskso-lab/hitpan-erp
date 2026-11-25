using Dapper;
using HitPan.API.Controllers;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.DTOs.DataReset;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

// MigrationController 는 [SupportedOSPlatform("windows")] 다(ACE OLEDB 의존). P7 이 부르는 자리는
// 모드 · 확인문구 · 초기화 판정뿐이라 OS 와 무관하고, DB 게이트 잡은 ubuntu 에서 돈다(MdbOverwriteReseedGateTests 와 같은 해제 · #19).
#pragma warning disable CA1416

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>DataResetParentOnlyGate — DB 쪽</b>(20261125작 · 지시서 §5 P1 ~ P8 · CI db-gate 전용).
/// </summary>
/// <remarks>
/// <para>
/// 「모든데이터 초기화」 는 <b>살아 있는 대표 계정(users.is_parent = 1 · is_deleted = 0 · is_active = 1 · tenant_admin)</b> 만 부를 수 있다.
/// 대표 0명 · 2명 이상이면 막고 안내한다(대표 결재 §146). 판정은 비번 대조 · 강제 백업 <b>앞</b>.
/// </para>
/// <para>
/// 시험마다 출하 DDL 로 새 스키마를 깐다(G7-4 준비 꼴 · 기존 파일 도우미 손 0 · 헌법 #36 진실원 위에서 잰다).
/// 이 PC 3306 은 실물 DB 라 로컬에서 돌리지 않는다 — CI db-gate(HITPAN_REQUIRE_DB)에서만.
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class DataResetParentOnlyGateDbTests : IDisposable
{
    private readonly string _dbName = "hitpan_reset_parent_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "61111111-1111-1111-1111-111111111111";
    private const string OtherTenantId = "62222222-2222-2222-2222-222222222222";
    private const string OwnerPw = "Gate-Owner-1234!";
    private const string ChildPw = "Gate-Child-1234!";

    // ══ 준비 — LegacyUnpostedDdlGateTests 와 같은 꼴(출하 DDL 새 스키마) ══

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 출하 DDL 을 읽을 수 없다.");
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

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다
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
            UseShellExecute = false,
            StandardInputEncoding = new System.Text.UTF8Encoding(false)
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

        Assert.True(proc.ExitCode == 0, $"출하 DDL import 가 실패했다:\n{err}");
    }

    /// <summary>users 한 줄. 칸은 출하 DDL users 정의(installer/hitpan_db_clean.sql)로 확인(#13).</summary>
    private static async Task<string> InsertUserAsync(MySqlConnection db, string tenantId, string tag, bool parent, bool admin,
        string password, bool active = true, bool deleted = false)
    {
        var id = Guid.NewGuid().ToString();
        await db.ExecuteAsync(
            """
            INSERT INTO users
              (user_id, tenant_id, email, password_hash, user_name, role,
               account_type, is_parent, is_active, is_deleted, created_at, updated_at)
            VALUES
              (@U, @T, @E, @H, @N, @R, @A, @P, @Act, @Del, NOW(6), NOW(6))
            """,
            new
            {
                U = id, T = tenantId, E = $"{tag}@reset-gate.local", H = BCrypt.Net.BCrypt.HashPassword(password, 4), N = tag,
                R = admin ? "TenantAdmin" : "User",
                A = admin ? "tenant_admin" : "tenant_user",
                P = parent ? 1 : 0,
                Act = active ? 1 : 0,
                Del = deleted ? 1 : 0,
            });
        return id;
    }

    /// <summary>업무 표 시험 줄 1개(초기화 대상 · tenant_id 있음 · 보존 목록 밖). SQL 은 G7 이 출하 DDL 위에서 이미 쓰는 꼴.</summary>
    private static Task InsertBusinessRowAsync(MySqlConnection db, string tenantId, string tag) =>
        db.ExecuteAsync(
            """
            INSERT INTO partner_legacy_balances
              (balance_id, tenant_id, partner_id, legacy_buy_code, base_date, balance_amount, source_type, source_id)
            VALUES
              (UUID(), @T, 'partner-reset-gate', 2147438512, '2026-02-28', -51577479.00, 'migration', @Src)
            """, new { T = tenantId, Src = $"reset-gate-{tag}" });

    private static Task<long> BusinessRowsAsync(MySqlConnection db, string tenantId) =>
        db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM partner_legacy_balances WHERE tenant_id = @T", new { T = tenantId });

    private async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(DbConnString());
        await c.OpenAsync();
        return c;
    }

    private DataResetService NewService(MySqlConnection db, CountingBackup backup) =>
        new(db, backup, new NoOpAudit(), NullLogger<DataResetService>.Instance);

    private async Task AssertDeniedAsync(string tenantId, string userId, string password, string code, string message, string what)
    {
        await using var db = await OpenAsync();
        var backup = new CountingBackup();
        var before = await BusinessRowsAsync(db, tenantId);
        var res = await NewService(db, backup).ResetAllAsync(new DataResetRequest { Password = password, ConfirmText = "초기화" }, tenantId, userId);

        Assert.False(res.Success, $"🔴 {what} — 초기화가 됐다. 대표 아닌/대표 확인 안 된 회사에서 장부 전체가 지워진다.");
        Assert.Equal(code, res.DenyCode);
        Assert.Equal(message, res.Error);
        Assert.True(backup.Count == 0, $"🔴 {what} — 판정 전에 강제 백업이 {backup.Count}번 돌았다(판정은 백업 앞이어야 한다).");
        Assert.Equal(before, await BusinessRowsAsync(db, tenantId));
    }

    // ══ P1 — 대표 1명 = 된다 ══

    [Fact(DisplayName = "P1 🔴 대표 1명 · 자기 비번 → 초기화 됨 · 강제 백업 1회 · 업무 줄 0")]
    public async Task P1_대표_통과()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(P1_대표_통과)); return; }
        SetUpFreshInstall();
        await using var db = await OpenAsync();
        var owner = await InsertUserAsync(db, TenantId, "owner", parent: true, admin: true, OwnerPw);
        await InsertBusinessRowAsync(db, TenantId, "p1");

        var backup = new CountingBackup();
        var res = await NewService(db, backup).ResetAllAsync(new DataResetRequest { Password = OwnerPw, ConfirmText = "초기화" }, TenantId, owner);

        Assert.True(res.Success, $"🔴 대표 본인이 초기화를 못 했다: {res.DenyCode} · {res.Error}");
        Assert.Null(res.DenyCode);
        Assert.Equal(1, backup.Count);
        Assert.Equal(0L, await BusinessRowsAsync(db, TenantId));
        Assert.Equal(DataResetGate.Allowed, await NewService(db, new CountingBackup()).GetResetGateAsync(TenantId, owner));
    }

    // ══ P2 — 관리자 역할 자식 = 막힘 ══

    [Fact(DisplayName = "P2 🔴 대표 1 + 관리자 역할 자식(맞는 비번) → not_parent · 안내 문구 · 백업 0회 · 업무 줄 그대로")]
    public async Task P2_관리자_자식_거절()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(P2_관리자_자식_거절)); return; }
        SetUpFreshInstall();
        string child;
        await using (var db = await OpenAsync())
        {
            await InsertUserAsync(db, TenantId, "owner", parent: true, admin: true, OwnerPw);
            child = await InsertUserAsync(db, TenantId, "mgr", parent: false, admin: true, ChildPw);
            await InsertBusinessRowAsync(db, TenantId, "p2");
        }

        await AssertDeniedAsync(TenantId, child, ChildPw, DataResetGateCodes.NotParent, DataResetService.NotParentMessage, "관리자 역할 자식");
    }

    // ══ P3 — 대표 0명 = 막고 안내 ══

    [Fact(DisplayName = "P3 🔴 대표 0명(관리자 is_parent=0 만) → no_parent · 업무 줄 그대로")]
    public async Task P3_대표_0명()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(P3_대표_0명)); return; }
        SetUpFreshInstall();
        string admin;
        await using (var db = await OpenAsync())
        {
            admin = await InsertUserAsync(db, TenantId, "mgr", parent: false, admin: true, ChildPw);
            await InsertBusinessRowAsync(db, TenantId, "p3");
        }

        await AssertDeniedAsync(TenantId, admin, ChildPw, DataResetGateCodes.NoParent, DataResetService.NoParentMessage, "대표 0명");
    }

    // ══ P4 — 대표 2명 = 대표 본인도 막고 안내 ══

    [Fact(DisplayName = "P4 🔴 대표 2명 → 두 대표 각각 multiple_parents · 업무 줄 그대로")]
    public async Task P4_대표_2명()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(P4_대표_2명)); return; }
        SetUpFreshInstall();
        string a, b;
        await using (var db = await OpenAsync())
        {
            a = await InsertUserAsync(db, TenantId, "owner-a", parent: true, admin: true, OwnerPw);
            b = await InsertUserAsync(db, TenantId, "owner-b", parent: true, admin: true, OwnerPw);
            await InsertBusinessRowAsync(db, TenantId, "p4");
        }

        await AssertDeniedAsync(TenantId, a, OwnerPw, DataResetGateCodes.MultipleParents, DataResetService.MultipleParentsMessage, "대표 2명 중 첫째");
        await AssertDeniedAsync(TenantId, b, OwnerPw, DataResetGateCodes.MultipleParents, DataResetService.MultipleParentsMessage, "대표 2명 중 둘째");
    }

    // ══ P5 — 지운 · 멈춘 대표는 세지 않는다 ══

    [Fact(DisplayName = "P5 🔴 살아 있는 대표 1 + 지운 대표 + 멈춘 대표 → 살아 있는 대표는 초기화 됨(셈 기준 = is_deleted 0 · is_active 1)")]
    public async Task P5_지운_멈춘_대표_안셈()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(P5_지운_멈춘_대표_안셈)); return; }
        SetUpFreshInstall();
        await using var db = await OpenAsync();
        var live = await InsertUserAsync(db, TenantId, "owner-live", parent: true, admin: true, OwnerPw);
        var gone = await InsertUserAsync(db, TenantId, "owner-gone", parent: true, admin: true, OwnerPw, deleted: true);
        var off = await InsertUserAsync(db, TenantId, "owner-off", parent: true, admin: true, OwnerPw, active: false);
        await InsertBusinessRowAsync(db, TenantId, "p5");

        var svc = NewService(db, new CountingBackup());
        Assert.Equal(DataResetGate.NotParent, await svc.GetResetGateAsync(TenantId, gone));
        Assert.Equal(DataResetGate.NotParent, await svc.GetResetGateAsync(TenantId, off));

        var backup = new CountingBackup();
        var res = await NewService(db, backup).ResetAllAsync(new DataResetRequest { Password = OwnerPw, ConfirmText = "초기화" }, TenantId, live);
        Assert.True(res.Success,
            $"🔴 지운 · 멈춘 대표까지 세어 「둘 이상」 으로 막았다: {res.DenyCode} · {res.Error}");
        Assert.Equal(1, backup.Count);
        Assert.Equal(0L, await BusinessRowsAsync(db, TenantId));
    }

    // ══ P6 — 다른 회사 대표는 세지 않는다 ══

    [Fact(DisplayName = "P6 🔴 이 회사 대표 0 · 다른 회사 대표 1 → no_parent(tenant 범위) · 다른 회사 줄 그대로")]
    public async Task P6_다른회사_대표_안셈()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(P6_다른회사_대표_안셈)); return; }
        SetUpFreshInstall();
        string admin;
        await using (var db = await OpenAsync())
        {
            admin = await InsertUserAsync(db, TenantId, "mgr", parent: false, admin: true, ChildPw);
            await InsertUserAsync(db, OtherTenantId, "other-owner", parent: true, admin: true, OwnerPw);
            await InsertBusinessRowAsync(db, TenantId, "p6-mine");
            await InsertBusinessRowAsync(db, OtherTenantId, "p6-other");
        }

        await AssertDeniedAsync(TenantId, admin, ChildPw, DataResetGateCodes.NoParent, DataResetService.NoParentMessage, "다른 회사에만 대표");
        await using var check = await OpenAsync();
        Assert.Equal(1L, await BusinessRowsAsync(check, OtherTenantId));
    }

    // ══ P7 — 자료이관 덮어쓰기 길도 같은 판정으로 닫힌다 ══

    [Fact(DisplayName = "P7 🔴 자료이관 덮어쓰기(legacy-mdb/start · overwrite · 「덮어쓰기」 · 맞는 비번) — 관리자 자식 → 400 · NotParent 안내 · 백업 0 · 줄 그대로")]
    public async Task P7_덮어쓰기_이관도_막힘()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(P7_덮어쓰기_이관도_막힘)); return; }
        SetUpFreshInstall();
        string child;
        await using (var seed = await OpenAsync())
        {
            await InsertUserAsync(seed, TenantId, "owner", parent: true, admin: true, OwnerPw);
            child = await InsertUserAsync(seed, TenantId, "mgr", parent: false, admin: true, ChildPw);
            await InsertBusinessRowAsync(seed, TenantId, "p7");
        }

        await using var db = await OpenAsync();
        var backup = new CountingBackup();
        // 판정에서 멈춰야 하므로 나머지 의존은 넣지 않는다 — 만지면 그 자리에서 터진다(그게 증명이다 · MdbOverwriteReseedGateTests 꼴).
        var c = new MigrationController(
            migrationService: null!, logger: NullLogger<MigrationController>.Instance,
            jobStore: null!, scopeFactory: null!, progress: null!, reconciliation: null!,
            dataReset: NewService(db, backup), provisioner: null!, db: null!);
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = TenantId;
        http.Items["UserId"] = child;
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;   // 메인 PC(자료 보관 컴퓨터)
        c.ControllerContext = new ControllerContext { HttpContext = http };

        var result = await c.StartMigrationJob(new MdbMigrationRequest
        {
            FolderPath = @"C:\HITWIN",
            Mode = "overwrite",
            ConfirmText = "덮어쓰기",
            Password = ChildPw,
        }, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var message = bad.Value?.GetType().GetProperty("message")?.GetValue(bad.Value) as string;
        Assert.Equal(DataResetService.NotParentMessage, message);
        Assert.True(backup.Count == 0, $"🔴 덮어쓰기 길에서 판정 전에 강제 백업이 {backup.Count}번 돌았다.");
        await using var check = await OpenAsync();
        Assert.Equal(1L, await BusinessRowsAsync(check, TenantId));
    }

    // ══ P8 — 판정 직접 ══

    [Fact(DisplayName = "P8 🔴 GetResetGateAsync 직접 — P1 ~ P6 상태를 회사별로 깔고 같은 값")]
    public async Task P8_판정_직접()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(P8_판정_직접)); return; }
        SetUpFreshInstall();
        await using var db = await OpenAsync();
        string T(string n) => $"6800000{n}-0000-0000-0000-000000000000";

        // 회사 1 — 대표 1 + 관리자 자식 + 일반 직원
        var o1 = await InsertUserAsync(db, T("1"), "o1", parent: true, admin: true, OwnerPw);
        var m1 = await InsertUserAsync(db, T("1"), "m1", parent: false, admin: true, ChildPw);
        var u1 = await InsertUserAsync(db, T("1"), "u1", parent: false, admin: false, ChildPw);
        // 회사 2 — 대표 0
        var m2 = await InsertUserAsync(db, T("2"), "m2", parent: false, admin: true, ChildPw);
        // 회사 3 — 대표 2
        var o3a = await InsertUserAsync(db, T("3"), "o3a", parent: true, admin: true, OwnerPw);
        var o3b = await InsertUserAsync(db, T("3"), "o3b", parent: true, admin: true, OwnerPw);
        // 회사 4 — 살아 있는 대표 1 + 지운 대표 + 멈춘 대표
        var o4 = await InsertUserAsync(db, T("4"), "o4", parent: true, admin: true, OwnerPw);
        var o4d = await InsertUserAsync(db, T("4"), "o4d", parent: true, admin: true, OwnerPw, deleted: true);
        var o4i = await InsertUserAsync(db, T("4"), "o4i", parent: true, admin: true, OwnerPw, active: false);
        // 회사 5 — 대표 0 (다른 회사 1 에는 대표가 있다)
        var m5 = await InsertUserAsync(db, T("5"), "m5", parent: false, admin: true, ChildPw);

        var svc = NewService(db, new CountingBackup());
        Assert.Equal(DataResetGate.Allowed, await svc.GetResetGateAsync(T("1"), o1));
        Assert.Equal(DataResetGate.NotParent, await svc.GetResetGateAsync(T("1"), m1));
        Assert.Equal(DataResetGate.NotParent, await svc.GetResetGateAsync(T("1"), u1));
        Assert.Equal(DataResetGate.NoParent, await svc.GetResetGateAsync(T("2"), m2));
        Assert.Equal(DataResetGate.MultipleParents, await svc.GetResetGateAsync(T("3"), o3a));
        Assert.Equal(DataResetGate.MultipleParents, await svc.GetResetGateAsync(T("3"), o3b));
        Assert.Equal(DataResetGate.Allowed, await svc.GetResetGateAsync(T("4"), o4));
        Assert.Equal(DataResetGate.NotParent, await svc.GetResetGateAsync(T("4"), o4d));
        Assert.Equal(DataResetGate.NotParent, await svc.GetResetGateAsync(T("4"), o4i));
        Assert.Equal(DataResetGate.NoParent, await svc.GetResetGateAsync(T("5"), m5));
        // 다른 회사의 대표 아이디를 이 회사 이름으로 물어도 통과하지 않는다(tenant 범위 · #2).
        Assert.Equal(DataResetGate.NoParent, await svc.GetResetGateAsync(T("5"), o1));
    }

    // ══ 대역 ══

    /// <summary>강제 백업이 몇 번 불렸는지 센다 — 판정이 백업 앞인지 재는 자리.</summary>
    private sealed class CountingBackup : IBackupService
    {
        public int Count { get; private set; }

        public Task<RunBackupResponse> RunBackupAsync(string tenantId, string triggeredBy = "manual", CancellationToken ct = default)
        {
            Count++;
            return Task.FromResult(new RunBackupResponse { Success = true, BackupId = "reset-gate-backup" });
        }

        public Task<BackupSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException("이 게이트는 백업 설정을 쓰지 않는다.");
        public Task UpdateSettingsAsync(string tenantId, UpdateBackupSettingsRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException("이 게이트는 백업 설정을 쓰지 않는다.");
        public Task<List<BackupHistoryDto>> GetHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default) =>
            throw new NotSupportedException("이 게이트는 백업 이력을 쓰지 않는다.");
        public Task<RestoreResponse> RestoreAsync(string tenantId, string? userId, RestoreRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException("이 게이트는 복원을 쓰지 않는다.");
        public Task<List<RestoreHistoryDto>> GetRestoreHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default) =>
            throw new NotSupportedException("이 게이트는 복원 이력을 쓰지 않는다.");
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null,
            CancellationToken ct = default) => Task.CompletedTask;
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
            Console.Error.WriteLine($"[DataResetParentOnlyGate] 임시 DB 정리 실패 — 손으로 지워라: {_dbName} ({ex.Message})");
        }
    }
}
