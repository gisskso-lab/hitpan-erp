using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Dapper;
using HitPan.API.Services;
using HitPan.Application.DTOs.Auth;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.DTOs.DataReset;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Domain.Common;
using HitPan.Infrastructure.Persistence;
using HitPan.Infrastructure.Security;
using HitPan.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2③ — 걸음 ④ 본 대조 · ④-끝 덧씌움 · ⑤ 교체 <b>DB 게이트</b>
/// (작업지시서 §5-1 G4 ~ G7 · G12 · G13 덧 · G18 · G21 · G22 · §11 D2 · D3 · D4 · D9 · D13 · D15 · CI db-gate 전용).
/// </summary>
/// <remarks>
/// <para>출하 DDL 신규설치본(<see cref="MigrationGateDb"/>) 위 <b>출하 GRANT 흉내 제한 계정</b>(자기 DB 한 개 · 본 §15-9)으로
/// <see cref="RestoreSafePath"/> ① ~ ③ → <see cref="RestoreSwapStep"/> ④ ~ ⑤ 를 직접 부른다(깃발 :639 닫힌 채 · 지시서 §9 ②). root 는 준비 · 씨앗 · 확인 읽기만.</para>
/// <para>🔴 이 PC 3306 은 실물이라 로컬에서 돌리지 않는다(#39) — CI db-gate(HITPAN_REQUIRE_DB)에서만. 환경 변수(DB_*)를 쓰므로 BackupCredentialGate 와 같은 직렬 묶음.</para>
/// <para>점검 상태 · 복원 시각 표지(A-2①)는 이 가지 바닥에 없다 — G21 ⑩ N1-a 칸(복원 전 토큰 401) · 갱신 길(D10)은 합류 때 이 파일에 더한다(명세서 §5).</para>
/// </remarks>
[Collection(BackupCredentialGateCollection.Name)]
public sealed class SafeRestoreSwapGateDbTests : IDisposable
{
    private const string Tenant = "sr3-gate-tenant-0001";
    private const string ParentId = "sr3-gate-parent-0001";
    private const string ChildId = "sr3-gate-child-0001";
    private const string OwnerEmp = "sr3-emp-owner";
    private const string ChildEmp = "sr3-emp-child";
    private const string Company = "안전복원교체게이트";
    private const string RestrictedUser = "hitpan_rs_t";
    private const string PcUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";

    private static readonly string[] ConfigKeys = { "DB_HOST", "DB_PORT", "DB_NAME", "DB_USER", "DB_PASSWORD" };

    private readonly MigrationGateDb _db = new("HITPAN_SR3_ERP_DB", "sr3gate");
    private readonly Dictionary<string, string?> _savedEnv = ConfigKeys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hitpan_sr3_gate_" + Guid.NewGuid().ToString("N")[..12]);
    private readonly string _parentSecret = "Pr-" + Guid.NewGuid().ToString("N")[..16];
    private readonly string _accountSecret = "Rs-" + Guid.NewGuid().ToString("N")[..20];
    private MySqlConnection? _conn;
    private string _appConnString = "";

    public void Dispose()
    {
        _conn?.Dispose();
        foreach (var (k, v) in _savedEnv) Environment.SetEnvironmentVariable(k, v);
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[SR3] 시험 폴더 정리 실패: {ex.Message}"); }   // #15
        _db.Dispose();
    }

    private sealed record Ctx(MySqlConnection Admin, MySqlConnection App, RestoreSafePath Path, BackupService Backup, string AuditPath);

    private async Task<Ctx?> SetupAsync(string gate)
    {
        if (!await _db.ReadyAsync(gate)) return null;
        var admin = await _db.OpenAsync();
        var dbName = admin.Database;

        await admin.ExecuteAsync($"CREATE USER IF NOT EXISTS '{RestrictedUser}'@'%' IDENTIFIED BY @P", new { P = _accountSecret });
        await admin.ExecuteAsync($"ALTER USER '{RestrictedUser}'@'%' IDENTIFIED BY @P", new { P = _accountSecret });
        await admin.ExecuteAsync($"GRANT ALL ON `{dbName}`.* TO '{RestrictedUser}'@'%'");

        var hash = BCrypt.Net.BCrypt.HashPassword(_parentSecret);
        await admin.ExecuteAsync(
            "INSERT INTO local_company (tenant_id, tenant_code, company_name) VALUES (@T, 'SR3', @C)", new { T = Tenant, C = Company });
        await AddUserAsync(admin, ParentId, "owner-id", "대표", isParent: true, hash);
        await AddUserAsync(admin, ChildId, "child-id", "자식", isParent: false, hash);
        await AddEmployeeAsync(admin, OwnerEmp, "0001", "대표", ParentId, "owner-id", "tenant_admin");
        await AddEmployeeAsync(admin, ChildEmp, "0002", "자식", ChildId, "child-id", "hr_manager");

        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        Environment.SetEnvironmentVariable("DB_HOST", host);
        Environment.SetEnvironmentVariable("DB_PORT", port);
        Environment.SetEnvironmentVariable("DB_NAME", dbName);
        Environment.SetEnvironmentVariable("DB_USER", RestrictedUser);
        Environment.SetEnvironmentVariable("DB_PASSWORD", _accountSecret);

        _appConnString = new MySqlConnectionStringBuilder
        {
            Server = host, Port = uint.Parse(port), UserID = RestrictedUser, Password = _accountSecret, Database = dbName,
            DefaultCommandTimeout = 300, GuidFormat = MySqlGuidFormat.None, Pooling = false,
        }.ConnectionString;
        _conn = new MySqlConnection(_appConnString);
        await _conn.OpenAsync();

        Directory.CreateDirectory(_folder);
        var backup = new BackupService(_conn, NullLogger<BackupService>.Instance);
        await backup.UpdateSettingsAsync(Tenant, new UpdateBackupSettingsRequest { PrimaryPath = _folder, RetentionCount = 30 });
        return new Ctx(admin, _conn, new RestoreSafePath(_conn, backup, NullLogger<RestoreSafePath>.Instance), backup,
            Path.Combine(_folder, "audit", "restore-audit.log"));
    }

    private RestoreSwapStep Step(Ctx c, Func<string, CancellationToken, Task>? hook = null, IRestoreBusyProbe? busy = null, Func<DateTime>? localNow = null) =>
        new(c.App, c.Path, new LocalAuditFileWriter(c.AuditPath, NullLogger<LocalAuditFileWriter>.Instance),
            NullLogger<RestoreSwapStep>.Instance, busy, hook, localNow);

    private static Task AddUserAsync(MySqlConnection db, string id, string email, string name, bool isParent, string hash) => db.ExecuteAsync(
        """
        INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent, is_active, created_at, updated_at)
        VALUES (@Id, @T, @E, @H, @N, 'admin', @A, @P, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
        """,
        new { Id = id, T = Tenant, E = email, H = hash, N = name, A = isParent ? "tenant_admin" : "tenant_user", P = isParent ? 1 : 0 });

    private static Task AddEmployeeAsync(MySqlConnection db, string id, string empNo, string name, string? userId, string? loginId, string role, int active = 1) =>
        db.ExecuteAsync(
            """
            INSERT INTO employees (employee_id, tenant_id, emp_no, emp_name, emp_type, join_date, is_active, created_at, updated_at, user_id, login_id, role)
            VALUES (@Id, @T, @No, @N, 'regular', UTC_TIMESTAMP(6), @Act, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @U, @L, @R)
            """,
            new { Id = id, T = Tenant, No = empNo, N = name, U = userId, L = loginId, R = role, Act = active });

    private static async Task AddRoomAsync(MySqlConnection db, string roomId, params string[] members)
    {
        await db.ExecuteAsync("INSERT INTO chat_rooms (room_id, tenant_id, room_type, created_by) VALUES (@R, @T, 'group', @C)",
            new { R = roomId, T = Tenant, C = OwnerEmp });
        foreach (var m in members)
            await db.ExecuteAsync("INSERT INTO chat_room_members (member_id, tenant_id, room_id, employee_id) VALUES (UUID(), @T, @R, @E)",
                new { T = Tenant, R = roomId, E = m });
    }

    private static async Task AddApprovalAsync(MySqlConnection db, string approverEmp, string docType)
    {
        await db.ExecuteAsync(
            "INSERT INTO approval_doc_lines (line_id, tenant_id, doc_type, seq_no, approver_id, approver_name, is_active) VALUES (UUID(), @T, @D, 1, @E, '결재자', 1)",
            new { T = Tenant, D = docType, E = approverEmp });
        await db.ExecuteAsync(
            """
            INSERT INTO approval_documents (approval_id, tenant_id, doc_type, ref_id, title, requester_id, requester_name, status, memo)
            VALUES (UUID(), @T, @D, UUID(), '시험 문서', @Req, '기안자', 'pending', '메모')
            """,
            new { T = Tenant, D = docType, Req = OwnerEmp });
    }

    private static async Task<string> BackupAsync(Ctx c)
    {
        var r = await c.Backup.RunBackupAsync(Tenant, "manual");
        Assert.True(r.Success, $"백업 실패: {r.Error}");
        await Task.Delay(1100);   // 파일 이름 초 단위 — 다음 백업과 겹치지 않게
        return r.BackupId;
    }

    /// <summary>① → ③ → 기록 → ④ → ④-끝 → ⑤.</summary>
    private async Task<(RestoreSwapResult<RestoreSwapReport> R, string RestoreId)> RestoreAsync(Ctx c, string backupId, RestoreSwapStep? step = null)
    {
        var sel = await c.Path.SelectAsync(Tenant, backupId);
        Assert.True(sel.Ok, $"① {sel.DenyCode} {sel.LogDetail}");
        using var selection = sel.Value!;
        var st = await c.Path.StageAsync(Tenant, selection);
        Assert.True(st.Ok, $"③ {st.DenyCode} {st.LogDetail}");
        step ??= Step(c);
        var restoreId = await step.BeginHistoryAsync(Tenant, ParentId, backupId);
        var r = await step.RunAsync(Tenant, ParentId, restoreId, st.Value!);
        return (r, restoreId);
    }

    private static Task<long> CountAsync(MySqlConnection c, string sql, object? p = null) => c.ExecuteScalarAsync<long>(sql, p);

    private static Task<long> StagingCountAsync(MySqlConnection c) => c.ExecuteScalarAsync<long>(
        "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND LEFT(TABLE_NAME, 8) = 'rs_new__' AND TABLE_TYPE = 'BASE TABLE'");

    /// <summary>실제 로그인(<c>AuthService.LoginAsync</c>)으로 받은 토큰의 <c>employee_id</c> · <c>role</c> 클레임.</summary>
    /// <remarks>A-2③ 고침 1 — <paramref name="password"/> 덧(G5 대표 「지금 비번」) · 없으면 씨앗 비번.</remarks>
    private async Task<(string? EmployeeId, List<string> Roles)> LoginClaimsAsync(string loginId, string? password = null)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JWT_SECRET")))
            Environment.SetEnvironmentVariable("JWT_SECRET", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(_appConnString, new MariaDbServerVersion(new Version(11, 4, 0))).Options;
        await using var ef = new AppDbContext(opts, Mock.Of<ICurrentTenant>(), Mock.Of<IEncryptionService>());
        await using var conn = new MySqlConnection(_appConnString);
        await conn.OpenAsync();
        var auth = new AuthService(new SwapGateUnitOfWork(conn), new AuthUserLookup(ef));
        var resp = await auth.LoginAsync(new LoginRequest { Email = loginId, Password = password ?? _parentSecret, DeviceType = "pc", UserAgent = PcUa });
        Assert.False(string.IsNullOrEmpty(resp.AccessToken), "로그인 토큰을 못 받았다");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(resp.AccessToken);
        return (jwt.Claims.FirstOrDefault(x => x.Type == "employee_id")?.Value,
                jwt.Claims.Where(x => x.Type == "role").Select(x => x.Value).Distinct().ToList());
    }

    /// <summary>A-2③ 고침 1 — 실제 로그인이 거절되는가(<c>UnauthorizedAccessException</c> · 토큰 0).</summary>
    private async Task<bool> LoginRejectedAsync(string loginId)
    {
        try
        {
            await LoginClaimsAsync(loginId);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"[SR3] 로그인 거절 확인 {loginId}: {ex.GetType().Name}");
            return true;
        }
    }

    private Task<long> EmployeeCountAsync(Ctx c) => CountAsync(c.Admin, "SELECT COUNT(*) FROM employees WHERE tenant_id = @T", new { T = Tenant });

    private static Task<long> UncommittedWritesAsync(MySqlConnection admin, long connId) => admin.ExecuteScalarAsync<long>(
        "SELECT COUNT(*) FROM information_schema.INNODB_TRX WHERE trx_mysql_thread_id = @Id AND trx_rows_modified > 0", new { Id = connId });

    private static Mock<IRestoreBusyProbe> Busy(string? code)
    {
        var busy = new Mock<IRestoreBusyProbe>();
        busy.Setup(b => b.BusyCode(Tenant)).Returns(code);
        return busy;
    }

    // ══ G4 · G5 · G7 · D2 · D3 · D9 · G16 ══

    [Fact(DisplayName = "G4 · G5 · G7 🔴 백업 뒤 거래 사라짐 · 백업 전 그대로 · 계정 표 그대로 · 기록 success 1줄(지역 시각 · D9) · 감사 끝 줄 · 격리 RR(D2) · 임시 표 0")]
    public async Task G4_G5_G7_교체_기록_격리()
    {
        var c = await SetupAsync("SR3-G4");
        if (c is null) return;
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-a', @T, '전')", new { T = Tenant });
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-b', @T, '뒤')", new { T = Tenant });
        await c.Admin.ExecuteAsync("UPDATE users SET is_active = 0, password_hash = 'changed' WHERE user_id = @U", new { U = ChildId });   // G5 — 지금 값

        await c.App.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED");   // D2 — 세션이 RC 여도 거래는 RR
        var before = DateTime.UtcNow;
        // D9 — 시험이 시간대를 UTC 아닌 값(+9)으로 고정(주입 시계 · 러너가 UTC 여도 어긋남이 보임)
        var kst = TimeSpan.FromHours(9);
        var (r, rid) = await RestoreAsync(c, id, Step(c, localNow: () => DateTime.SpecifyKind(DateTime.UtcNow + kst, DateTimeKind.Local)));
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.Equal(RestoreSwapStep.ExpectedIsolation, r.Value!.Isolation);
        Console.WriteLine($"[SR3-G16] ④-끝 {r.Value.Counts.Elapsed.TotalMilliseconds:F0}ms · ⑤ {r.Value.Elapsed.TotalMilliseconds:F0}ms · 지움 {r.Value.DeletedRows} · 부음 {r.Value.InsertedRows}");
        // 🔴 A-2③ 고침 1 G16 — 기록만이 아니라 문턱 X(= 10분 · 리뷰서 §6 G16 줄) 넘으면 빨강 1줄(크기 표본 · 1206 · undo · 강제 종료 재기동 = 시험 PC 몫 · 명세서 넘김)
        Assert.True(r.Value.Elapsed < TimeSpan.FromMinutes(10), $"G16 — ⑤ 교체 {r.Value.Elapsed} ≥ 문턱 10분");
        Assert.True(r.Value.Counts.Elapsed < TimeSpan.FromMinutes(10), $"G16 — ④-끝 {r.Value.Counts.Elapsed} ≥ 문턱 10분");

        Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-a'"));
        Assert.Equal(0, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-b'"));
        var u = await c.Admin.QuerySingleAsync<(int Active, string Hash)>("SELECT is_active, password_hash FROM users WHERE user_id = @U", new { U = ChildId });
        Assert.Equal(0, u.Active);
        Assert.Equal("changed", u.Hash);

        var h = await c.Admin.QuerySingleAsync<(string Status, DateTime Finished)>(
            "SELECT status, finished_at FROM restore_history WHERE restore_id = @R", new { R = rid });
        Assert.Equal("success", h.Status);
        // D3 · D9 — 기록 시각 = 주입한 지역 시각(+9) · 기대값은 구현 식과 따로 셈 · UTC 로 쓰면 9시간 이르게 읽혀 빨강
        var expectLocal = before + kst;
        Assert.InRange(h.Finished, expectLocal.AddMinutes(-5), expectLocal.AddMinutes(10));
        Assert.True(h.Finished - before > TimeSpan.FromHours(8), $"D9 — 기록 시각이 UTC 꼴({h.Finished:O})");
        var started = await c.Admin.ExecuteScalarAsync<DateTime>("SELECT started_at FROM restore_history WHERE restore_id = @R", new { R = rid });
        Assert.True(started - before > TimeSpan.FromHours(8), $"D9 — 시작 시각이 UTC 꼴({started:O})");

        Assert.True(r.Value.AuditCompleted);
        var log = await File.ReadAllTextAsync(c.AuditPath);
        Assert.Contains("SwapCompleted", log, StringComparison.Ordinal);
        Assert.Contains(rid, log, StringComparison.Ordinal);
        Assert.Equal(0, await StagingCountAsync(c.Admin));
    }

    // ══ G6 — 교체 중 실패 · 연결 끊김 ══

    [Fact(DisplayName = "G6 🔴 ⑤ 거래 안 지우기 뒤 강제 실패 = 되돌림 · 라이브 그대로 · 기록 failed(글자만) · 자료 그대로 응답")]
    public async Task G6_교체중_실패_되돌림()
    {
        var c = await SetupAsync("SR3-G6");
        if (c is null) return;
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-a', @T, '전')", new { T = Tenant });
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-b', @T, '뒤')", new { T = Tenant });

        var (r, rid) = await RestoreAsync(c, id, Step(c, hook: (_, _) => throw new InvalidOperationException("시험 걸이 — 강제 실패")));
        Assert.False(r.Ok);
        Assert.Equal(SafeRestoreDenyCodes.SwapFailed, r.DenyCode);
        Assert.True(r.DataUnchanged);
        Assert.Equal(2, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts"));
        Assert.Equal(2, await CountAsync(c.Admin, "SELECT COUNT(*) FROM employees"));
        var h = await c.Admin.QuerySingleAsync<(string Status, string? Err)>("SELECT status, error_message FROM restore_history WHERE restore_id = @R", new { R = rid });
        Assert.Equal("failed", h.Status);
        Assert.Equal(SafeRestoreDenyCodes.SwapFailed, h.Err);
    }

    [Fact(DisplayName = "G6b 🔴 ⑤ 거래 안 지우기 뒤 연결 끊김(KILL) = 서버가 되돌림 · 라이브 그대로")]
    public async Task G6b_교체중_연결끊김()
    {
        var c = await SetupAsync("SR3-G6b");
        if (c is null) return;
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-a', @T, '전')", new { T = Tenant });
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-b', @T, '뒤')", new { T = Tenant });
        var appId = await c.App.ExecuteScalarAsync<long>("SELECT CONNECTION_ID()");

        var (r, _) = await RestoreAsync(c, id, Step(c, hook: async (_, _) => await c.Admin.ExecuteAsync($"KILL {appId}")));
        Assert.False(r.Ok);
        Assert.Equal(2, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts"));
        Assert.Equal(2, await CountAsync(c.Admin, "SELECT COUNT(*) FROM employees"));
    }

    // ══ D4 · D13 · D15 ══

    [Fact(DisplayName = "D4 🔴 자료이관 도는 중 = 걸음 ④ 앞 멈춤(migration_running) · 쉬운 말 · 자료 그대로 / D13 업데이트 도는 중 = update_running")]
    public async Task D4_D13_바쁨_멈춤()
    {
        var c = await SetupAsync("SR3-D4");
        if (c is null) return;
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-b', @T, '뒤')", new { T = Tenant });
        var store = new MigrationJobStore(c.App);
        var job = store.Restore(new MigrationJob { JobId = "sr3-job-" + Guid.NewGuid().ToString("N")[..8], TenantId = Tenant, Status = "running", StartedAt = DateTime.Now });
        try
        {
            var (r, rid) = await RestoreAsync(c, id);
            Assert.False(r.Ok);
            Assert.Equal(SafeRestoreDenyCodes.MigrationRunning, r.DenyCode);
            Assert.Equal(RestoreOverlayRules.BusyMigrationMessage, r.Message);
            Assert.True(r.DataUnchanged);
            Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-b'"));
            Assert.Equal("failed", await c.Admin.ExecuteScalarAsync<string>("SELECT status FROM restore_history WHERE restore_id = @R", new { R = rid }));
        }
        finally
        {
            store.Update(job.JobId, j => j.Status = "completed");   // 정적 사전 — 다른 시험에 새지 않게
        }

        var busy = new Mock<IRestoreBusyProbe>();
        busy.Setup(b => b.BusyCode(Tenant)).Returns(SafeRestoreDenyCodes.UpdateRunning);
        var (u, _) = await RestoreAsync(c, id, Step(c, busy: busy.Object));
        Assert.Equal(SafeRestoreDenyCodes.UpdateRunning, u.DenyCode);
        Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-b'"));
    }

    [Fact(DisplayName = "D15 🔴 세션 autocommit = 0(전역 0 인 고객 PC 꼴) 이어도 붓기 뒤 열린 거래 0 · 다른 연결이 임시 표 줄을 다 봄 · 교체 성공")]
    public async Task D15_autocommit0()
    {
        var c = await SetupAsync("SR3-D15");
        if (c is null) return;
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-a', @T, '전')", new { T = Tenant });
        // 🔴 A-2③ 고침 1 D15 ③ — 붓기 차례 끝 쪽 표에 씨앗(DDL 은 암묵 커밋 → ① 을 빼도 앞 표는 커밋됨 · 끝 표 INSERT 만 열린 채 남음 · 리뷰서 §2)
        await c.Admin.ExecuteAsync(
            "INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at) VALUES ('sr3-wh-1', @T, 'W1', '끝표', 'normal', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
            new { T = Tenant });
        var id = await BackupAsync(c);
        var appConnId = await c.App.ExecuteScalarAsync<long>("SELECT CONNECTION_ID()");
        await c.App.ExecuteAsync("SET SESSION autocommit = 0");

        var sel = await c.Path.SelectAsync(Tenant, id);
        using var selection = sel.Value!;
        var st = await c.Path.StageAsync(Tenant, selection);
        Assert.True(st.Ok, st.DenyCode);
        // D15 ①(고침 1) — 붓기 연결에 커밋 안 된 쓰기 0(다른 연결이 INNODB_TRX 로 봄 · 읽기 거래는 셈 밖)
        Assert.Equal(0, await UncommittedWritesAsync(c.Admin, appConnId));
        Assert.Equal(1, await c.Admin.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM `{st.Value!.StagedTables["warehouses"]}` WHERE warehouse_id = 'sr3-wh-1'"));
        Assert.Equal(0, await c.App.ExecuteScalarAsync<long>("SELECT @@SESSION.autocommit"));   // D15 ② — 붓기 전 값(0)으로 되돌림
        foreach (var (src, staged) in st.Value!.StagedTables)
        {
            var viaApp = await c.App.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM `{staged}`");
            var viaAdmin = await c.Admin.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM `{staged}`");
            Assert.True(viaApp == viaAdmin, $"{src}: 붓기 연결 {viaApp} · 다른 연결 {viaAdmin}(커밋 안 된 줄)");
        }
        var step = Step(c);
        var rid = await step.BeginHistoryAsync(Tenant, ParentId, id);
        var r = await step.RunAsync(Tenant, ParentId, rid, st.Value);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.Equal("success", await c.Admin.ExecuteScalarAsync<string>("SELECT status FROM restore_history WHERE restore_id = @R", new { R = rid }));
    }

    // ══ G21 — 권한 칸 덧씌움 경우표(보1 §2-3 · 덧1-7 · 덧2) ══

    [Fact(DisplayName = "G21 ① ② 🔴 권한 내림 · 올림 = 지금 값 그대로 — 실제 로그인 토큰 role 클레임")]
    public async Task G21_1_2_권한()
    {
        var c = await SetupAsync("SR3-G21-1");
        if (c is null) return;
        await AddEmployeeAsync(c.Admin, "sr3-emp-x", "0003", "올림", null, null, "sales_user");
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("UPDATE employees SET role = 'sales_user' WHERE employee_id = @E", new { E = ChildEmp });   // 내림
        await c.Admin.ExecuteAsync("UPDATE employees SET role = 'hr_manager' WHERE employee_id = 'sr3-emp-x'");            // 올림
        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.Equal("sales_user", await c.Admin.ExecuteScalarAsync<string>("SELECT role FROM employees WHERE employee_id = @E", new { E = ChildEmp }));
        Assert.Equal("hr_manager", await c.Admin.ExecuteScalarAsync<string>("SELECT role FROM employees WHERE employee_id = 'sr3-emp-x'"));
        Assert.True(r.Value!.Counts.RoleDiff >= 2, $"권한 다름 셈 {r.Value.Counts.RoleDiff}");
        var (emp, roles) = await LoginClaimsAsync("child-id");
        Assert.Equal(ChildEmp, emp);
        Assert.Contains("sales_user", roles);
        Assert.DoesNotContain("hr_manager", roles);
    }

    [Fact(DisplayName = "G21 ③ ④ ⑤ ⑥ 🔴 퇴사 · 재입사 그대로 · 새 직원(계정 없음) 사라짐 · 멈춤 0 · 백업 뒤 이은 계정 = 지금 연결")]
    public async Task G21_3_4_5_6()
    {
        var c = await SetupAsync("SR3-G21-3");
        if (c is null) return;
        var hash = BCrypt.Net.BCrypt.HashPassword(_parentSecret);
        await AddUserAsync(c.Admin, "sr3-user-x", "x-id", "퇴사자", false, hash);
        await AddEmployeeAsync(c.Admin, "sr3-emp-x", "0003", "퇴사자", "sr3-user-x", "x-id", "sales_user");
        await AddEmployeeAsync(c.Admin, "sr3-emp-y", "0004", "재입사", null, null, "sales_user", active: 0);
        await c.Admin.ExecuteAsync("UPDATE employees SET is_resigned = 1 WHERE employee_id = 'sr3-emp-y'");
        await AddEmployeeAsync(c.Admin, "sr3-emp-z", "0005", "나중계정", null, null, "sales_user");
        var id = await BackupAsync(c);

        // ③ 퇴사(사원 4칸 · 연결 끊음 · 계정 표식 — 퇴사 뒤처리와 같은 꼴)
        await c.Admin.ExecuteAsync("UPDATE employees SET is_active = 0, is_resigned = 1, resign_date = UTC_TIMESTAMP(6), resign_reason = '개인', user_id = NULL, login_id = NULL WHERE employee_id = 'sr3-emp-x'");
        await c.Admin.ExecuteAsync("UPDATE users SET is_active = 0, is_deleted = 1, email = 'resigned+x-id' WHERE user_id = 'sr3-user-x'");
        // ④ 재입사
        await c.Admin.ExecuteAsync("UPDATE employees SET is_active = 1, is_resigned = 0 WHERE employee_id = 'sr3-emp-y'");
        // ⑤ 새 직원 · 계정 없음
        await AddEmployeeAsync(c.Admin, "sr3-emp-n", "0006", "새직원", null, null, "sales_user");
        // ⑥ 백업에 있던 사원에 계정 새로 이음
        await AddUserAsync(c.Admin, "sr3-user-z", "z-id", "나중계정", false, hash);
        await c.Admin.ExecuteAsync("UPDATE employees SET user_id = 'sr3-user-z', login_id = 'z-id' WHERE employee_id = 'sr3-emp-z'");

        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        var x = await c.Admin.QuerySingleAsync<(int Active, int Resigned, string? U)>("SELECT is_active, is_resigned, user_id FROM employees WHERE employee_id = 'sr3-emp-x'");
        Assert.Equal((0, 1, (string?)null), x);
        Assert.Equal(1, await c.Admin.ExecuteScalarAsync<int>("SELECT is_active FROM employees WHERE employee_id = 'sr3-emp-y'"));
        Assert.Equal(0, await CountAsync(c.Admin, "SELECT COUNT(*) FROM employees WHERE employee_id = 'sr3-emp-n'"));
        Assert.Equal("sr3-user-z", await c.Admin.ExecuteScalarAsync<string?>("SELECT user_id FROM employees WHERE employee_id = 'sr3-emp-z'"));
        Assert.Equal(1, r.Value!.Counts.LiveOnlyEmployees);   // ⑤ 라이브에만 1(멈추지 않음 · C1′ ⓒ 새 셈)

        // 🔴 A-2③ 고침 1 §12-4 — 실제 로그인 토큰으로: ③ 퇴사자 로그인 거절 · ⑥ 이은 계정 토큰 employee_id = 백업 행 · 로그인 뒤 백필 새 행 0
        var empAfter = await EmployeeCountAsync(c);
        Assert.True(await LoginRejectedAsync("x-id"), "③ 퇴사자 x-id 로그인이 통과했다");
        var (zEmp, _) = await LoginClaimsAsync("z-id");
        Assert.Equal("sr3-emp-z", zEmp);
        Assert.Equal(empAfter, await EmployeeCountAsync(c));
    }

    [Fact(DisplayName = "G21 ⑦ ⑯ 🔴 사원 행이 라이브에서 사라짐 = 되살리되 연결은 ⓐ″(계정 살아 있고 겹침 0 이면 이음) · 기동 재맞춤 FillSql · ClearSql 0행")]
    public async Task G21_7_16_백업에만()
    {
        var c = await SetupAsync("SR3-G21-7");
        if (c is null) return;
        var hash = BCrypt.Net.BCrypt.HashPassword(_parentSecret);
        await AddUserAsync(c.Admin, "sr3-user-w", "w-id", "되살림", false, hash);
        await AddEmployeeAsync(c.Admin, "sr3-emp-w", "0003", "되살림", "sr3-user-w", "w-id", "sales_user");
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("DELETE FROM employees WHERE employee_id = 'sr3-emp-w'");
        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        var w = await c.Admin.QuerySingleAsync<(string? U, string? L)>("SELECT user_id, login_id FROM employees WHERE employee_id = 'sr3-emp-w'");
        Assert.Equal(("sr3-user-w", "w-id"), w);
        var sync = await EmployeeLoginIdSync.ResyncAsync(c.App, null);
        Assert.Equal(0, sync.Filled);
        Assert.Equal(0, sync.Cleared);

        // 🔴 A-2③ 고침 1 §12-4 — ⑦ 되살린 행 = 실제 로그인 토큰 employee_id(⑩ 과 같은 백필 사고 꼴 막음) · 로그인 뒤 행 수 같음
        var empAfter = await EmployeeCountAsync(c);
        var (wEmp, _) = await LoginClaimsAsync("w-id");
        Assert.Equal("sr3-emp-w", wEmp);
        Assert.Equal(empAfter, await EmployeeCountAsync(c));
    }

    [Fact(DisplayName = "G21 ⑧ ⑪ ⑭ ⑰ 🔴 방에서 뺌 = 빠진 그대로 · 뺐다 다시 넣음 = 참여 · 새 방에 넣음 = 줄 없음(멈춤 0) · C4 방 있음 갈래 = 나감 표시")]
    public async Task G21_8_11_14_17_방참여()
    {
        var c = await SetupAsync("SR3-G21-8");
        if (c is null) return;
        await AddEmployeeAsync(c.Admin, "sr3-emp-a", "0003", "에이", null, null, "sales_user");
        await AddRoomAsync(c.Admin, "room-1", OwnerEmp, ChildEmp, "sr3-emp-a");
        await AddRoomAsync(c.Admin, "room-2", OwnerEmp, ChildEmp);
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("UPDATE chat_room_members SET left_at = UTC_TIMESTAMP(6) WHERE room_id = 'room-1' AND employee_id = @E", new { E = ChildEmp });   // ⑧
        await c.Admin.ExecuteAsync("UPDATE chat_room_members SET left_at = UTC_TIMESTAMP(6) WHERE room_id = 'room-2' AND employee_id = @E", new { E = ChildEmp });
        await c.Admin.ExecuteAsync("UPDATE chat_room_members SET left_at = NULL WHERE room_id = 'room-2' AND employee_id = @E", new { E = ChildEmp });               // ⑪
        await AddRoomAsync(c.Admin, "room-3", OwnerEmp, ChildEmp);                                                                                                   // ⑭
        await c.Admin.ExecuteAsync("DELETE FROM chat_room_members WHERE room_id = 'room-1' AND employee_id = 'sr3-emp-a'");                                        // ⑰ 시험 씨앗(제품 길 0)

        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.NotNull(await c.Admin.ExecuteScalarAsync<DateTime?>("SELECT left_at FROM chat_room_members WHERE room_id = 'room-1' AND employee_id = @E", new { E = ChildEmp }));
        Assert.Null(await c.Admin.ExecuteScalarAsync<DateTime?>("SELECT left_at FROM chat_room_members WHERE room_id = 'room-2' AND employee_id = @E", new { E = ChildEmp }));
        Assert.Equal(0, await CountAsync(c.Admin, "SELECT COUNT(*) FROM chat_room_members WHERE room_id = 'room-3'"));
        Assert.NotNull(await c.Admin.ExecuteScalarAsync<DateTime?>("SELECT left_at FROM chat_room_members WHERE room_id = 'room-1' AND employee_id = 'sr3-emp-a'"));
        Assert.True(r.Value!.Counts.LiveOnlyChat >= 2);   // ⑭ 라이브에만 있는 방 참여 줄(셈 · 감사)
        Assert.Equal(1, r.Value.Counts.ChatNarrowed);

        // 🔴 A-2③ 고침 1 §12-4 — 실제 로그인 토큰 employee_id 로 방 관문(IsMemberAsync)을 부름
        var (childEmp, _) = await LoginClaimsAsync("child-id");
        Assert.Equal(ChildEmp, childEmp);
        var chat = new ChatService(c.App, Mock.Of<IChatFileStore>());
        Assert.False(await chat.IsMemberAsync(Tenant, childEmp!, "room-1"));   // ⑧ 뺀 그대로
        Assert.True(await chat.IsMemberAsync(Tenant, childEmp!, "room-2"));    // ⑪ 다시 넣음 = 참여
        Assert.False(await chat.IsMemberAsync(Tenant, childEmp!, "room-3"));   // ⑭ 새 방 줄 없음
    }

    [Fact(DisplayName = "G21 ⑨ ⑨′ 🔴 결재자가 백업 뒤 퇴사 = 줄 끔 · 진행 문서 회수(퇴사 뒤처리와 같은 memo) · 계정 지워진 백업에만 있는 결재자(R′)도")]
    public async Task G21_9_결재정리()
    {
        var c = await SetupAsync("SR3-G21-9");
        if (c is null) return;
        var hash = BCrypt.Net.BCrypt.HashPassword(_parentSecret);
        await AddUserAsync(c.Admin, "sr3-user-x", "x-id", "결재자", false, hash);
        await AddEmployeeAsync(c.Admin, "sr3-emp-x", "0003", "결재자", "sr3-user-x", "x-id", "sales_user");
        await AddUserAsync(c.Admin, "sr3-user-v", "v-id", "지운결재자", false, hash);
        await AddEmployeeAsync(c.Admin, "sr3-emp-v", "0004", "지운결재자", "sr3-user-v", "v-id", "sales_user");
        await AddApprovalAsync(c.Admin, "sr3-emp-x", "expense");
        await AddApprovalAsync(c.Admin, "sr3-emp-v", "leave");
        var id = await BackupAsync(c);

        // ⑨ X 퇴사(라이브 — 사원 4칸 · 결재 두 문장 · 퇴사 뒤처리 꼴)
        await c.Admin.ExecuteAsync("UPDATE employees SET is_active = 0, is_resigned = 1, user_id = NULL, login_id = NULL WHERE employee_id = 'sr3-emp-x'");
        await c.Admin.ExecuteAsync("UPDATE users SET is_active = 0, is_deleted = 1, email = 'resigned+x-id' WHERE user_id = 'sr3-user-x'");
        // ⑨′ V — 사원 행 사라짐 + 계정 지움(백업에만 · R′)
        await c.Admin.ExecuteAsync("DELETE FROM employees WHERE employee_id = 'sr3-emp-v'");
        await c.Admin.ExecuteAsync("UPDATE users SET is_deleted = 1, is_active = 0 WHERE user_id = 'sr3-user-v'");

        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        foreach (var (emp, dt) in new[] { ("sr3-emp-x", "expense"), ("sr3-emp-v", "leave") })
        {
            Assert.Equal(0, await c.Admin.ExecuteScalarAsync<int>("SELECT is_active FROM approval_doc_lines WHERE approver_id = @E", new { E = emp }));
            var d = await c.Admin.QuerySingleAsync<(string Status, string Memo)>("SELECT status, memo FROM approval_documents WHERE doc_type = @D", new { D = dt });
            Assert.Equal("cancelled", d.Status);
            Assert.EndsWith(" / 결재자 퇴사로 회수", d.Memo, StringComparison.Ordinal);
        }
        Assert.Null(await c.Admin.ExecuteScalarAsync<string?>("SELECT user_id FROM employees WHERE employee_id = 'sr3-emp-v'"));
        Assert.Equal(1, await c.Admin.ExecuteScalarAsync<int>("SELECT is_active FROM employees WHERE employee_id = 'sr3-emp-v'"));   // 사원 칸 손 0(C8)
        Assert.Equal(2, r.Value!.Counts.LinesOff);
        Assert.Equal(2, r.Value.Counts.DocsRecalled);

        // 🔴 A-2③ 고침 1 §12-4 — 결재함(서비스 호출): 결재자 쪽 회수 문서 0 · 기안자(대표 토큰 employee_id) 쪽 둘 다 cancelled · 퇴사자 로그인 거절
        var approval = new ApprovalService(c.App, new NoOpAudit());
        Assert.Empty(await approval.GetPendingAsync(Tenant, "sr3-emp-x"));
        Assert.Empty(await approval.GetPendingAsync(Tenant, "sr3-emp-v"));
        var (ownerEmp, _) = await LoginClaimsAsync("owner-id");
        Assert.Equal(OwnerEmp, ownerEmp);
        var sent = await approval.GetSentAsync(Tenant, ownerEmp!);
        Assert.Equal(2, sent.Count);
        Assert.All(sent, d => Assert.Equal("cancelled", d.Status));
        Assert.True(await LoginRejectedAsync("x-id"), "⑨ 퇴사자 x-id 로그인이 통과했다");
    }

    // ══ G21 ⑩ · ⑩-neg · G12 · G18 — 초기화 뒤 복원 · ⓑ′ ══

    private DataResetService ResetService(Ctx c) => new(c.App, c.Backup, new NoOpAudit(), NullLogger<DataResetService>.Instance);

    private CompanyBootstrapProvisioner Provisioner() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = _appConnString }).Build(),
        NullLogger<CompanyBootstrapProvisioner>.Instance);

    [Fact(DisplayName = "G21 ⑩ · G12 🔴 초기화(실제 서비스 · 강제 백업) → 재시드(새 대표 행) → 백필 꼴 행 → 초기화 직전 백업 복원 = 멈춤 0(ⓑ′) · 대표 토큰 employee_id = 백업 대표 행 · 행 수 같음 · 방 참여 = 백업 값")]
    public async Task G21_10_초기화뒤_복원()
    {
        var c = await SetupAsync("SR3-G21-10");
        if (c is null) return;
        await AddRoomAsync(c.Admin, "room-1", OwnerEmp, ChildEmp);
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-a', @T, '전')", new { T = Tenant });
        var empBefore = await CountAsync(c.Admin, "SELECT COUNT(*) FROM employees WHERE tenant_id = @T", new { T = Tenant });

        var reset = await ResetService(c).ResetAllAsync(new DataResetRequest { Password = _parentSecret, ConfirmText = "초기화" }, Tenant, ParentId);
        Assert.True(reset.Success, "초기화 실패");
        Assert.False(string.IsNullOrEmpty(reset.BackupId));
        await Provisioner().ReseedCompanySkeletonAsync(Tenant, default);
        Assert.Equal(0, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts"));
        var reseeded = await c.Admin.ExecuteScalarAsync<string>("SELECT employee_id FROM employees WHERE user_id = @U", new { U = ParentId });
        Assert.NotEqual(OwnerEmp, reseeded);   // N1 — 재시드 L 행(새 employee_id)
        // 로그인 백필 꼴(자식 계정 · 새 employee_id) — 백필은 EF 저장소 길이라 시험은 같은 꼴 행을 씨앗으로 넣음
        await AddEmployeeAsync(c.Admin, "sr3-emp-backfill", "0099", "자식", ChildId, "child-id", "sales_user");

        var (r, _) = await RestoreAsync(c, reset.BackupId!);
        Assert.True(r.Ok, $"⑩ 멈춤 — {r.DenyCode} {r.LogDetail} {r.Message}");
        Assert.Equal(2, r.Value!.Counts.BPrimeExempted);   // 대표 · 자식 둘 다 ⓑ′ 로 빠짐
        Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-a'"));   // G12 업무 자료 돌아옴
        Assert.Equal(empBefore, await CountAsync(c.Admin, "SELECT COUNT(*) FROM employees WHERE tenant_id = @T", new { T = Tenant }));
        Assert.Equal(OwnerEmp, await c.Admin.ExecuteScalarAsync<string>("SELECT employee_id FROM employees WHERE user_id = @U", new { U = ParentId }));
        Assert.Equal(2, await CountAsync(c.Admin, "SELECT COUNT(*) FROM chat_room_members WHERE room_id = 'room-1' AND left_at IS NULL"));   // C4 — 방 0 이면 백업 값

        var (emp, _) = await LoginClaimsAsync("owner-id");
        Assert.Equal(OwnerEmp, emp);
        Assert.Equal(empBefore, await CountAsync(c.Admin, "SELECT COUNT(*) FROM employees WHERE tenant_id = @T", new { T = Tenant }));   // 로그인 뒤 백필 새 행 0
        var sync = await EmployeeLoginIdSync.ResyncAsync(c.App, null);
        Assert.Equal(0, sync.Filled + sync.Cleared);   // ⑯
    }

    [Fact(DisplayName = "G21 ⑩-neg · G18 🔴 초기화 뒤 새 자식 계정(백업에 행 없음) = 멈춤(newer_accounts_missing) · D1 문구에 이름 · 라이브 그대로")]
    public async Task G21_10neg_G18_새계정_멈춤()
    {
        var c = await SetupAsync("SR3-G21-10n");
        if (c is null) return;
        var reset = await ResetService(c).ResetAllAsync(new DataResetRequest { Password = _parentSecret, ConfirmText = "초기화" }, Tenant, ParentId);
        Assert.True(reset.Success);
        await Provisioner().ReseedCompanySkeletonAsync(Tenant, default);
        var hash = BCrypt.Net.BCrypt.HashPassword(_parentSecret);
        await AddUserAsync(c.Admin, "sr3-user-new", "new-id", "새직원갑", false, hash);
        await AddEmployeeAsync(c.Admin, "sr3-emp-new", "0007", "새직원갑", "sr3-user-new", "new-id", "sales_user");
        var liveEmp = await CountAsync(c.Admin, "SELECT COUNT(*) FROM employees");

        var (r, rid) = await RestoreAsync(c, reset.BackupId!);
        Assert.False(r.Ok);
        Assert.Equal(SafeRestoreDenyCodes.NewerAccountsMissing, r.DenyCode);
        Assert.True(r.DataUnchanged);
        Assert.Contains("새직원갑", r.Message, StringComparison.Ordinal);
        Assert.Contains("[멈춤]", r.Message, StringComparison.Ordinal);
        Assert.Equal(liveEmp, await CountAsync(c.Admin, "SELECT COUNT(*) FROM employees"));
        Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM employees WHERE employee_id = 'sr3-emp-new'"));
        Assert.Equal(0, await StagingCountAsync(c.Admin));
        Assert.Equal(SafeRestoreDenyCodes.NewerAccountsMissing, await c.Admin.ExecuteScalarAsync<string>("SELECT error_message FROM restore_history WHERE restore_id = @R", new { R = rid }));

        // 비활성 계정은 셈만(멈춤 대상 아님) — 그 계정을 [멈춤] 하면 되살아남(D1 안내대로)
        await c.Admin.ExecuteAsync("UPDATE users SET is_active = 0 WHERE user_id = 'sr3-user-new'");
        var (ok, _) = await RestoreAsync(c, reset.BackupId!);
        Assert.True(ok.Ok, $"{ok.DenyCode} {ok.LogDetail}");
    }

    // ══ G21 ⑫ ⑬ — C1′ · C3′ ══

    [Fact(DisplayName = "G21 ⑫ 🔴 ④-끝 뒤 · ⑤ 앞 라이브 role 바꿈 = C1′ 재대조 멈춤(다시 시도 문구) · 라이브 바뀐 값 그대로")]
    public async Task G21_12_C1_재대조()
    {
        var c = await SetupAsync("SR3-G21-12");
        if (c is null) return;
        var id = await BackupAsync(c);
        var sel = await c.Path.SelectAsync(Tenant, id);
        using var selection = sel.Value!;
        var st = await c.Path.StageAsync(Tenant, selection);
        Assert.True(st.Ok, st.DenyCode);
        var step = Step(c);
        var rid = await step.BeginHistoryAsync(Tenant, ParentId, id);
        var v = await step.VerifyAsync(Tenant, st.Value!);
        Assert.True(v.Ok, v.LogDetail);
        var o = await step.OverlayAsync(Tenant, step.LocalNow());
        Assert.True(o.Ok, o.LogDetail);
        await c.Admin.ExecuteAsync("UPDATE employees SET role = 'sales_user' WHERE employee_id = @E", new { E = ChildEmp });
        var s = await step.SwapAsync(Tenant, ParentId, rid, v.Value!, o.Value!);
        Assert.False(s.Ok);
        Assert.Equal(SafeRestoreDenyCodes.VerifyFailed, s.DenyCode);
        Assert.Equal(RestoreOverlayRules.DataChangedMessage, s.Message);
        Assert.True(s.DataUnchanged);
        Assert.Equal("sales_user", await c.Admin.ExecuteScalarAsync<string>("SELECT role FROM employees WHERE employee_id = @E", new { E = ChildEmp }));
    }

    [Fact(DisplayName = "G21 ⑬ 🔴 계정만 멈춘(users.is_active = 0) 재직 사원 = 복원 됨 · 연결 = 라이브(옛 「멈춘 계정과 이은 행 0」 문장 버림)")]
    public async Task G21_13_계정만멈춤()
    {
        var c = await SetupAsync("SR3-G21-13");
        if (c is null) return;
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("UPDATE users SET is_active = 0 WHERE user_id = @U", new { U = ChildId });
        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.Equal(ChildId, await c.Admin.ExecuteScalarAsync<string?>("SELECT user_id FROM employees WHERE employee_id = @E", new { E = ChildEmp }));
        // 🔴 A-2③ 고침 1 §12-4 — ⑬ 실제 로그인 거절(계정 멈춤 = 지금 값 그대로)
        Assert.True(await LoginRejectedAsync("child-id"), "⑬ 멈춘 계정 child-id 로그인이 통과했다");
    }

    // ══ G13 덧 · G22 ①③ (시험 DB information_schema) ══

    [Fact(DisplayName = "G13 덧 🔴 덧씌움 칸 · 열쇠 실재 + 사원 · 방 참여 · 결재 두 표 외래키 0 · 트리거 0(시험 DB)")]
    public async Task G13덧_시험DB()
    {
        var c = await SetupAsync("SR3-G13");
        if (c is null) return;
        foreach (var (t, cols) in new[] { ("employees", RestoreOverlayRules.EmployeeOverlayColumns.Concat(RestoreOverlayRules.EmployeeKey)),
                                          ("chat_room_members", RestoreOverlayRules.ChatMemberOverlayColumns.Concat(RestoreOverlayRules.ChatMemberKey)) })
            foreach (var col in cols)
                Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @T AND COLUMN_NAME = @C", new { T = t, C = col }));
        string[] four = ["employees", "chat_room_members", "approval_doc_lines", "approval_documents"];
        Assert.Equal(0, await CountAsync(c.Admin,
            "SELECT COUNT(*) FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL AND (TABLE_NAME IN @F OR REFERENCED_TABLE_NAME IN @F)", new { F = four }));
        Assert.Equal(0, await CountAsync(c.Admin, "SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = DATABASE() AND EVENT_OBJECT_TABLE IN @F", new { F = four }));
    }

    [Fact(DisplayName = "G22 ① ③ 🔴 지금 값 표 _id 꼴 칸 전부 = 두 목록 중 정확히 하나(시험 DB) · 왕복 — 무른 참조 users.dept_id 값 그대로")]
    public async Task G22_시험DB_왕복()
    {
        var c = await SetupAsync("SR3-G22");
        if (c is null) return;
        var cols = (await c.Admin.QueryAsync<(string T, string C)>(
            "SELECT TABLE_NAME, COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND NOT (COLUMN_NAME = 'tenant_id')")).ToList();
        var listed = RestoreTableClassification.SoftReferences.Concat(RestoreTableClassification.NotReferences).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outside = cols.Where(x => RestoreTableClassification.Classify(x.T) == RestoreTableClass.KeepCurrent
                                      && System.Text.RegularExpressions.Regex.IsMatch(x.C, "(_id|_code|_no|_by|_key)$")
                                      && !listed.Contains($"{x.T}.{x.C}")).Select(x => $"{x.T}.{x.C}").ToList();
        Assert.True(outside.Count == 0, "🔴 목록 밖: " + string.Join(", ", outside));

        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("UPDATE users SET dept_id = 'dept-after-backup' WHERE user_id = @U", new { U = ChildId });
        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.Equal("dept-after-backup", await c.Admin.ExecuteScalarAsync<string?>("SELECT dept_id FROM users WHERE user_id = @U", new { U = ChildId }));
    }

    // ══ A-2③ 고침 1 — 지시서 §13 · 리뷰서 §2 · §3 · §5 · §6 · [3-V] P2-1 · P3-1 ~ 4 ══

    private const string OtherTenant = "sr3-gate-tenant-0002";

    private async Task<(RestoreStaging Staging, string RestoreId, RestoreSwapStep Step)> StageAndBeginAsync(Ctx c, string backupId, RestoreSwapStep? step = null)
    {
        var sel = await c.Path.SelectAsync(Tenant, backupId);
        Assert.True(sel.Ok, $"① {sel.DenyCode} {sel.LogDetail}");
        using var selection = sel.Value!;
        var st = await c.Path.StageAsync(Tenant, selection);
        Assert.True(st.Ok, $"③ {st.DenyCode} {st.LogDetail}");
        step ??= Step(c);
        return (st.Value!, await step.BeginHistoryAsync(Tenant, ParentId, backupId), step);
    }

    [Fact(DisplayName = "P2-1 🔴 두 회사 — ④ 라이브에 다른 회사 줄 = 멈춤(verify_failed) · 그 줄 그대로 / ④ 뒤 ⑤ 앞 들어온 다른 회사 줄 = 지우기 WHERE 가 지킴(그 줄 그대로)")]
    public async Task P2_1_두회사_다른회사줄()
    {
        var c = await SetupAsync("SR3-P21");
        if (c is null) return;
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-a', @T, '전')", new { T = Tenant });
        var id = await BackupAsync(c);
        // ⓐ 라이브에만 둘째 회사 줄(백업엔 없음 → ③ G3 는 통과 · ④ 가 막아야 함)
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-o', @T, '남')", new { T = OtherTenant });
        var (r, rid) = await RestoreAsync(c, id);
        Assert.False(r.Ok);
        Assert.Equal(SafeRestoreDenyCodes.VerifyFailed, r.DenyCode);
        Assert.True(r.DataUnchanged);
        Assert.Contains("다른 회사", r.LogDetail, StringComparison.Ordinal);
        Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-o' AND tenant_id = @O", new { O = OtherTenant }));
        Assert.Equal("failed", await c.Admin.ExecuteScalarAsync<string>("SELECT status FROM restore_history WHERE restore_id = @R", new { R = rid }));
        Assert.Equal(0, await StagingCountAsync(c.Admin));

        // ⓑ ④ 를 지난 뒤(⑤ 앞) 둘째 회사 줄이 들어옴 → ⑤ 지우기는 이 회사 줄만(WHERE) · 둘째 회사 줄 그대로
        await c.Admin.ExecuteAsync("DELETE FROM partner_contacts WHERE contact_id = 'pc-o'");
        var (staging, rid2, step) = await StageAndBeginAsync(c, id);
        var v = await step.VerifyAsync(Tenant, staging);
        Assert.True(v.Ok, v.LogDetail);
        var o = await step.OverlayAsync(Tenant, step.LocalNow());
        Assert.True(o.Ok, o.LogDetail);
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-o2', @T, '남')", new { T = OtherTenant });
        var s = await step.SwapAsync(Tenant, ParentId, rid2, v.Value!, o.Value!);
        Assert.True(s.Ok, $"{s.DenyCode} {s.LogDetail}");
        Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-o2' AND tenant_id = @O", new { O = OtherTenant }));
        Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-a' AND tenant_id = @T", new { T = Tenant }));
    }

    [Fact(DisplayName = "G21 ⑬b 🔴 C3′ 단언 — 덧씌움을 틀리게 만든 임시 표(시험 전용 트리거 role ← evil) = ④-끝 멈춤(verify_failed · C3′) · 라이브 role 그대로")]
    public async Task G21_13b_C3단언_어긋남_멈춤()
    {
        var c = await SetupAsync("SR3-G21-13b");
        if (c is null) return;
        var id = await BackupAsync(c);
        var (staging, _, step) = await StageAndBeginAsync(c, id);
        var v = await step.VerifyAsync(Tenant, staging);
        Assert.True(v.Ok, v.LogDetail);
        // 시험 전용 — 임시 표에만 트리거(제품 손 0 · 임시 표와 함께 사라짐)
        await c.Admin.ExecuteAsync("CREATE TRIGGER sr3_evil_role BEFORE UPDATE ON rs_new__employees FOR EACH ROW SET NEW.role = 'evil'");
        var o = await step.OverlayAsync(Tenant, step.LocalNow());
        Assert.False(o.Ok);
        Assert.Equal(SafeRestoreDenyCodes.VerifyFailed, o.DenyCode);
        Assert.Contains("C3′", o.LogDetail, StringComparison.Ordinal);
        Assert.Equal("hr_manager", await c.Admin.ExecuteScalarAsync<string>("SELECT role FROM employees WHERE employee_id = @E", new { E = ChildEmp }));
    }

    [Fact(DisplayName = "G21 ⑨b 🔴 오래된 퇴사자(백업 전 퇴사 · 결재 줄 이미 꺼짐)는 R 밖 — 남의 진행 문서 회수 0 · pending 그대로")]
    public async Task G21_9b_오래된퇴사자_결재그대로()
    {
        var c = await SetupAsync("SR3-G21-9b");
        if (c is null) return;
        await AddEmployeeAsync(c.Admin, "sr3-emp-q", "0003", "오래된퇴사자", null, null, "sales_user", active: 0);
        await c.Admin.ExecuteAsync("UPDATE employees SET is_resigned = 1 WHERE employee_id = 'sr3-emp-q'");
        await AddApprovalAsync(c.Admin, OwnerEmp, "leave");   // 대표 줄 켜짐 + pending 문서
        await c.Admin.ExecuteAsync(
            "INSERT INTO approval_doc_lines (line_id, tenant_id, doc_type, seq_no, approver_id, approver_name, is_active) VALUES (UUID(), @T, 'leave', 2, 'sr3-emp-q', '퇴사자', 0)",
            new { T = Tenant });
        var id = await BackupAsync(c);
        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.Equal("pending", await c.Admin.ExecuteScalarAsync<string>("SELECT status FROM approval_documents WHERE doc_type = 'leave'"));
        Assert.Equal(0, r.Value!.Counts.DocsRecalled);
        Assert.Equal(0, r.Value.Counts.LinesOff);
    }

    [Fact(DisplayName = "G21 ⑦b 🔴 되살림 겹침 — 백업에만 있는 W 의 계정 U 를 라이브에서 다른 행 K 가 가짐 = W 안 이음 · K 그대로 · 실제 로그인 토큰 = K")]
    public async Task G21_7b_되살림_겹침_안이음()
    {
        var c = await SetupAsync("SR3-G21-7b");
        if (c is null) return;
        var hash = BCrypt.Net.BCrypt.HashPassword(_parentSecret);
        await AddUserAsync(c.Admin, "sr3-user-w", "w-id", "되살림", false, hash);
        await AddEmployeeAsync(c.Admin, "sr3-emp-w", "0003", "되살림", "sr3-user-w", "w-id", "sales_user");
        await AddEmployeeAsync(c.Admin, "sr3-emp-k", "0004", "새주인", null, null, "sales_user");
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("DELETE FROM employees WHERE employee_id = 'sr3-emp-w'");
        await c.Admin.ExecuteAsync("UPDATE employees SET user_id = 'sr3-user-w', login_id = 'w-id' WHERE employee_id = 'sr3-emp-k'");
        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.Null(await c.Admin.ExecuteScalarAsync<string?>("SELECT user_id FROM employees WHERE employee_id = 'sr3-emp-w'"));
        Assert.Equal("sr3-user-w", await c.Admin.ExecuteScalarAsync<string?>("SELECT user_id FROM employees WHERE employee_id = 'sr3-emp-k'"));
        var (emp, _) = await LoginClaimsAsync("w-id");
        Assert.Equal("sr3-emp-k", emp);
    }

    [Fact(DisplayName = "G21 ⑦c 🔴 되살림 · 계정만 멈춤(users.is_active = 0 · 지움 · 표식 아님) = W 안 이음(ⓐ″ 길 is_active 낱)")]
    public async Task G21_7c_되살림_멈춘계정_안이음()
    {
        var c = await SetupAsync("SR3-G21-7c");
        if (c is null) return;
        var hash = BCrypt.Net.BCrypt.HashPassword(_parentSecret);
        await AddUserAsync(c.Admin, "sr3-user-w", "w-id", "되살림", false, hash);
        await AddEmployeeAsync(c.Admin, "sr3-emp-w", "0003", "되살림", "sr3-user-w", "w-id", "sales_user");
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("DELETE FROM employees WHERE employee_id = 'sr3-emp-w'");
        await c.Admin.ExecuteAsync("UPDATE users SET is_active = 0 WHERE user_id = 'sr3-user-w'");
        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        var w = await c.Admin.QuerySingleAsync<(string? U, string? L)>("SELECT user_id, login_id FROM employees WHERE employee_id = 'sr3-emp-w'");
        Assert.Equal(((string?)null, (string?)null), w);
    }

    [Fact(DisplayName = "G7b 🔴 기록 success 갱신이 0줄(running 아님) = 교체 실패 · 되돌림 · 백업 뒤 줄 그대로")]
    public async Task G7b_기록0줄_실패()
    {
        var c = await SetupAsync("SR3-G7b");
        if (c is null) return;
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-b', @T, '뒤')", new { T = Tenant });
        var (staging, rid, step) = await StageAndBeginAsync(c, id);
        await c.Admin.ExecuteAsync("UPDATE restore_history SET status = 'failed' WHERE restore_id = @R", new { R = rid });
        var r = await step.RunAsync(Tenant, ParentId, rid, staging);
        Assert.False(r.Ok);
        Assert.Equal(SafeRestoreDenyCodes.SwapFailed, r.DenyCode);
        Assert.True(r.DataUnchanged);
        Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-b'"));
    }

    [Fact(DisplayName = "G15 🔴 트리거 2표 — ⑤ 도중 다른 연결의 발행 계산서 UPDATE = 잠금 오류 · 교체 뒤에도 발행 잠금(45000) 그대로 · partner_special_prices 붓기 전후 값 같음(BEFORE INSERT 재계산 뒤)")]
    public async Task G15_교체중_다른연결()
    {
        var c = await SetupAsync("SR3-G15");
        if (c is null) return;
        await c.Admin.ExecuteAsync(
            """
            INSERT INTO tax_invoices (invoice_id, tenant_id, invoice_no, issued_at, issued_by, amount_total, vat_total, status)
            VALUES ('sr3-ti-1', @T, 'SR3-0001', UTC_TIMESTAMP(6), @U, 10000.00, 1000.00, 'etax_confirmed')
            """, new { T = Tenant, U = ParentId });
        // 🔴 고침 1-2(PM 결재) — psp id = UUID 꼴(제품이 넣는 꼴 = PartnerService Guid.NewGuid().ToString()).
        //    출하 트리거 trg_psp_ratio_insert 의 IF(…, UUID(), NEW.id) 가 UUID 꼴 아닌 id 를 NULL 로 바꾸는지는
        //    아래 PSP_관찰_트리거_UUID형변환 이 따로 찍는다(덮지 않음 · 트리거 고침은 별건).
        // ⬛ 옛 줄: VALUES ('sr3-psp-1', @T, 'p-1', 'i-1', 8000.00, 10000.00) · VALUES ('sr3-psp-2', @T, 'p-1', 'i-2', 5000.00, NULL, 55.55)
        await c.Admin.ExecuteAsync(
            "INSERT INTO partner_special_prices (id, tenant_id, partner_id, item_id, special_price, std_price) VALUES (@Id, @T, 'p-1', 'i-1', 8000.00, 10000.00)", new { Id = Psp1, T = Tenant });
        await c.Admin.ExecuteAsync(
            "INSERT INTO partner_special_prices (id, tenant_id, partner_id, item_id, special_price, std_price, vs_ratio) VALUES (@Id, @T, 'p-1', 'i-2', 5000.00, NULL, 55.55)", new { Id = Psp2, T = Tenant });
        const string PspSql = "SELECT id, special_price, std_price, vs_ratio FROM partner_special_prices WHERE tenant_id = @T ORDER BY id";
        var pspBefore = (await c.Admin.QueryAsync<(string Id, decimal Special, decimal? Std, decimal? Ratio)>(PspSql, new { T = Tenant })).ToList();
        Assert.Equal(2, pspBefore.Count);
        Assert.Equal(new[] { Psp1, Psp2 }, pspBefore.Select(p => p.Id));   // 트리거가 넣은 id 그대로 둠(양성 대조)
        var id = await BackupAsync(c);
        // ⬛ 옛 줄: "UPDATE partner_special_prices SET special_price = 9000.00 WHERE id = 'sr3-psp-1'"
        await c.Admin.ExecuteAsync("UPDATE partner_special_prices SET special_price = 9000.00 WHERE id = @Id", new { Id = Psp1 });   // 백업 뒤 바꿈(복원 = 백업 값)

        await using var other = await _db.OpenAsync();
        await other.ExecuteAsync("SET SESSION innodb_lock_wait_timeout = 1");
        MySqlException? during = null;
        var (r, _) = await RestoreAsync(c, id, Step(c, hook: async (_, _) =>
        {
            try { await other.ExecuteAsync("UPDATE tax_invoices SET remark1 = 'during' WHERE invoice_id = 'sr3-ti-1'"); }
            catch (MySqlException ex) { during = ex; }
        }));
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.NotNull(during);   // ⑤ 도중 = 잠금 오류(교체 거래가 줄을 쥠 · 1205)
        Assert.Equal(1205, during!.Number);   // 🔴 고침 1-3(§13-3 P3-C) — 잠금 기다림 시간 넘김만 초록(연결 끊김 등 다른 오류 = 빨강)
        Console.WriteLine($"[SR3-G15] ⑤ 도중 다른 연결 오류 {during!.ErrorCode} {during.SqlState}");

        var after = await Assert.ThrowsAsync<MySqlException>(() => other.ExecuteAsync("UPDATE tax_invoices SET remark1 = 'after' WHERE invoice_id = 'sr3-ti-1'"));
        Assert.Equal("45000", after.SqlState);   // 발행 잠금 트리거 그대로(떼지도 다시 걸지도 않음)
        Assert.Null(await c.Admin.ExecuteScalarAsync<string?>("SELECT remark1 FROM tax_invoices WHERE invoice_id = 'sr3-ti-1'"));
        var pspAfter = (await c.Admin.QueryAsync<(string Id, decimal Special, decimal? Std, decimal? Ratio)>(PspSql, new { T = Tenant })).ToList();
        Assert.Equal(pspBefore, pspAfter);
    }

    // 고침 1-2 — G15 psp id(UUID 꼴 · 소문자 대시 = Guid.ToString() 꼴)
    private const string Psp1 = "5a3e0c1e-0f15-4000-8000-000000000001";
    private const string Psp2 = "5a3e0c1e-0f15-4000-8000-000000000002";

    [Fact(DisplayName = "관찰(판정 아님) 🔵 PSP 트리거 — IF(0, UUID(), 글자) 값 · 경고 · UUID 꼴 아닌 id 로 partner_special_prices INSERT 결과를 한 줄씩 찍음(결과가 어느 쪽이든 초록)")]
    public async Task PSP_관찰_트리거_UUID형변환()
    {
        const string Gate = "SR3-PSP-관찰";
        if (!await _db.ReadyAsync(Gate)) return;
        await using var db = await _db.OpenAsync();
        Console.WriteLine($"[SR3-PSP-관찰] 서버 판 = {await db.ExecuteScalarAsync<string>("SELECT VERSION()")}");

        // ⓐ 트리거 식과 같은 꼴 — IF(거짓, UUID(), 글자) 의 값과 바로 뒤 경고
        var a = await db.ExecuteScalarAsync<object?>("SELECT IF(0, UUID(), 'sr3-psp-x')");
        var aText = a is null or DBNull ? "NULL" : $"'{a}'({a.GetType().Name})";
        var aWarn = await WarningsAsync(db);
        Console.WriteLine($"[SR3-PSP-관찰] ⓐ IF(0, UUID(), 'sr3-psp-x') = {aText} · 경고 {aWarn.Count}건"
            + string.Concat(aWarn.Select(w => " | " + w)));

        // ⓑ UUID 꼴 아닌 id 로 실제 INSERT(출하 트리거를 지남) — 오류 번호 또는 들어간 id
        const string ObsTenant = "sr3-psp-obs-tenant";
        try
        {
            await db.ExecuteAsync(
                "INSERT INTO partner_special_prices (id, tenant_id, partner_id, item_id, special_price, std_price) VALUES ('sr3-psp-x', @T, 'p-obs', 'i-obs', 8000.00, 10000.00)",
                new { T = ObsTenant });
            var bWarn = await WarningsAsync(db);
            var ids = (await db.QueryAsync<string>("SELECT id FROM partner_special_prices WHERE tenant_id = @T", new { T = ObsTenant })).ToList();
            Console.WriteLine($"[SR3-PSP-관찰] ⓑ UUID 꼴 아닌 id INSERT = 성공 · 들어간 id {string.Join(", ", ids.Select(i => $"'{i}'"))} · 경고 {bWarn.Count}건"
                + string.Concat(bWarn.Select(w => " | " + w)));
        }
        catch (MySqlException ex)
        {
            Console.WriteLine($"[SR3-PSP-관찰] ⓑ UUID 꼴 아닌 id INSERT = 오류 {ex.Number} {ex.SqlState} · {ex.Message}");   // #15 — 관찰 줄로 남김
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM partner_special_prices WHERE tenant_id = @T", new { T = ObsTenant });   // 시험 데이터 정리
        }
    }

    /// <summary>바로 앞 문장의 경고 — 칸 형(Code = 부호 없는 정수)에 기대지 않게 글자로 이어 붙임.</summary>
    private static async Task<List<string>> WarningsAsync(MySqlConnection db)
    {
        var rows = await db.QueryAsync("SHOW WARNINGS");
        return rows.Cast<IDictionary<string, object?>>()
            .Select(r => string.Join(" ", r.Values.Select(v => v?.ToString() ?? "NULL")))
            .ToList();
    }

    [Fact(DisplayName = "G5 🔴 백업 뒤 토큰 폐기 · 기기 차단 · 대표 비번 바꿈 → 복원 = 셋 다 지금 값 그대로 · 대표 「지금 비번」 실제 로그인 성공")]
    public async Task G5_토큰_기기_대표비번()
    {
        var c = await SetupAsync("SR3-G5");
        if (c is null) return;
        await c.Admin.ExecuteAsync(
            "INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked) VALUES ('sr3-rt-1', @U, 'sr3-hash', UTC_TIMESTAMP(6) + INTERVAL 7 DAY, 0)", new { U = ChildId });
        await c.Admin.ExecuteAsync(
            "INSERT INTO tenant_devices (device_id, tenant_id, user_id, device_type, fingerprint, status) VALUES ('sr3-dev-1', @T, @U, 'pc', 'sr3-fp', 'approved')", new { T = Tenant, U = ChildId });
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("UPDATE refresh_tokens SET is_revoked = 1 WHERE token_id = 'sr3-rt-1'");
        await c.Admin.ExecuteAsync("UPDATE tenant_devices SET status = 'revoked', revoked_at = UTC_TIMESTAMP(6) WHERE device_id = 'sr3-dev-1'");
        var newSecret = "Nw-" + Guid.NewGuid().ToString("N")[..16];
        await c.Admin.ExecuteAsync("UPDATE users SET password_hash = @H WHERE user_id = @U", new { H = BCrypt.Net.BCrypt.HashPassword(newSecret), U = ParentId });

        var (r, _) = await RestoreAsync(c, id);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.Equal(1, await c.Admin.ExecuteScalarAsync<int>("SELECT is_revoked FROM refresh_tokens WHERE token_id = 'sr3-rt-1'"));
        Assert.Equal("revoked", await c.Admin.ExecuteScalarAsync<string>("SELECT status FROM tenant_devices WHERE device_id = 'sr3-dev-1'"));
        var (emp, _) = await LoginClaimsAsync("owner-id", newSecret);
        Assert.Equal(OwnerEmp, emp);
    }

    [Fact(DisplayName = "D9b 🔴 기본 시계(주입 0) — restore_history 시각 = 서버 지역 시각 · 이 클래스만 도는 CI 단계 TZ=Asia/Seoul 에서 UTC+9(UTC 로 쓰면 빨강)")]
    public async Task D9b_기본시계_지역시각()
    {
        var c = await SetupAsync("SR3-D9b");
        if (c is null) return;
        var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
        Assert.True(offset == TimeSpan.FromHours(9),
            $"D9b — 이 시험은 TZ=Asia/Seoul 단계에서만 돈다(지금 시간대 차 {offset}) · CI db-gate 「복원 교체 게이트(TZ=Asia/Seoul)」 단계");
        var id = await BackupAsync(c);
        var (r, rid) = await RestoreAsync(c, id);   // Step(c) = 주입 0 · 제품 기본 시계
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        var h = await c.Admin.QuerySingleAsync<(DateTime Started, DateTime Finished)>(
            "SELECT started_at, finished_at FROM restore_history WHERE restore_id = @R", new { R = rid });
        var nowUtc = DateTime.UtcNow;
        foreach (var t in new[] { h.Started, h.Finished })
            Assert.InRange(t - nowUtc, TimeSpan.FromHours(9) - TimeSpan.FromMinutes(10), TimeSpan.FromHours(9) + TimeSpan.FromMinutes(1));
    }

    [Fact(DisplayName = "D15b 🔴 세션 autocommit = 0 — 기록 쓰기(running · 사전 백업 · 멈춤 failed)가 다른 연결에 바로 보임(명시 커밋 P3-4) · 붓기 연결 커밋 안 된 쓰기 0")]
    public async Task D15b_autocommit0_멈춤기록()
    {
        var c = await SetupAsync("SR3-D15b");
        if (c is null) return;
        var id = await BackupAsync(c);
        var sel = await c.Path.SelectAsync(Tenant, id);
        using var selection = sel.Value!;
        var st = await c.Path.StageAsync(Tenant, selection);
        Assert.True(st.Ok, st.DenyCode);
        var appConnId = await c.App.ExecuteScalarAsync<long>("SELECT CONNECTION_ID()");
        await c.App.ExecuteAsync("SET SESSION autocommit = 0");

        var step = Step(c, busy: Busy(SafeRestoreDenyCodes.UpdateRunning).Object);
        var rid = await step.BeginHistoryAsync(Tenant, ParentId, id);
        Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM restore_history WHERE restore_id = @R AND status = 'running'", new { R = rid }));
        await step.RecordPreBackupAsync(Tenant, rid, "sr3-pre-1");
        Assert.Equal("sr3-pre-1", await c.Admin.ExecuteScalarAsync<string?>("SELECT pre_restore_backup FROM restore_history WHERE restore_id = @R", new { R = rid }));
        var r = await step.RunAsync(Tenant, ParentId, rid, st.Value!);
        Assert.Equal(SafeRestoreDenyCodes.UpdateRunning, r.DenyCode);
        var h = await c.Admin.QuerySingleAsync<(string Status, string? Err)>("SELECT status, error_message FROM restore_history WHERE restore_id = @R", new { R = rid });
        Assert.Equal(("failed", (string?)SafeRestoreDenyCodes.UpdateRunning), h);
        Assert.Equal(0, await UncommittedWritesAsync(c.Admin, appConnId));
    }

    [Fact(DisplayName = "P3-1 🔴 끝 그물 — 이미 취소된 요청(대조 중 예외) = 던지지 않고 멈춤 · 기록 failed · 임시 표 0 · 라이브 그대로")]
    public async Task P3_1_그물_취소_뒷정리()
    {
        var c = await SetupAsync("SR3-P31");
        if (c is null) return;
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-b', @T, '뒤')", new { T = Tenant });
        var (staging, rid, step) = await StageAndBeginAsync(c, id);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var r = await step.RunAsync(Tenant, ParentId, rid, staging, cts.Token);
        Assert.False(r.Ok);
        Assert.True(r.DataUnchanged);
        Assert.Equal("failed", await c.Admin.ExecuteScalarAsync<string>("SELECT status FROM restore_history WHERE restore_id = @R", new { R = rid }));
        Assert.Equal(0, await StagingCountAsync(c.Admin));
        Assert.Equal(1, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-b'"));
    }

    [Fact(DisplayName = "P3-2 🔴 기동 청소 — 앞 프로세스가 남긴 running 줄 = failed(restore_interrupted) · 끝난 줄(success) 손 0")]
    public async Task P3_2_기동청소_running닫음()
    {
        var c = await SetupAsync("SR3-P32");
        if (c is null) return;
        var step = Step(c, localNow: () => DateTime.Now.AddMinutes(-5));
        var stale = await step.BeginHistoryAsync(Tenant, ParentId, "sr3-src");
        var done = await step.BeginHistoryAsync(Tenant, ParentId, "sr3-src");
        await c.Admin.ExecuteAsync("UPDATE restore_history SET status = 'success', finished_at = NOW() WHERE restore_id = @R", new { R = done });

        var closed = await RestoreHistoryRecovery.CloseInterruptedAsync(c.App, DateTime.Now);
        Assert.Equal(1, closed);
        var h = await c.Admin.QuerySingleAsync<(string Status, string? Err, DateTime? Finished)>(
            "SELECT status, error_message, finished_at FROM restore_history WHERE restore_id = @R", new { R = stale });
        Assert.Equal("failed", h.Status);
        Assert.Equal(SafeRestoreDenyCodes.RestoreInterrupted, h.Err);
        Assert.NotNull(h.Finished);
        Assert.Equal("success", await c.Admin.ExecuteScalarAsync<string>("SELECT status FROM restore_history WHERE restore_id = @R", new { R = done }));
        Assert.Equal(0, await RestoreHistoryRecovery.CloseInterruptedAsync(c.App, DateTime.Now));   // 두 번째 = 0줄(멱등)
    }

    [Fact(DisplayName = "P3-3 🔴 ⑤ 도중 부르는 쪽 요청이 취소돼도(브라우저 닫음) 교체는 끝까지 · 커밋 · 결과 = 성공")]
    public async Task P3_3_교체중_취소_무시()
    {
        var c = await SetupAsync("SR3-P33");
        if (c is null) return;
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-a', @T, '전')", new { T = Tenant });
        var id = await BackupAsync(c);
        await c.Admin.ExecuteAsync("INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-b', @T, '뒤')", new { T = Tenant });
        using var cts = new CancellationTokenSource();
        var (staging, rid, step) = await StageAndBeginAsync(c, id, Step(c, hook: async (_, _) => await cts.CancelAsync()));
        var r = await step.RunAsync(Tenant, ParentId, rid, staging, cts.Token);
        Assert.True(cts.IsCancellationRequested);
        Assert.True(r.Ok, $"{r.DenyCode} {r.LogDetail}");
        Assert.Equal(0, await CountAsync(c.Admin, "SELECT COUNT(*) FROM partner_contacts WHERE contact_id = 'pc-b'"));
        Assert.Equal("success", await c.Admin.ExecuteScalarAsync<string>("SELECT status FROM restore_history WHERE restore_id = @R", new { R = rid }));
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>격리 DB 연결을 운영 <c>AuthService</c> 에 넘겨주는 통로(선례 <c>EmployeeAccountLinkGateDbTests.LinkGateUnitOfWork</c>).</summary>
    private sealed class SwapGateUnitOfWork : IUnitOfWork
    {
        private readonly System.Data.Common.DbConnection _conn;
        public SwapGateUnitOfWork(System.Data.Common.DbConnection conn) => _conn = conn;
        public System.Data.Common.DbConnection GetDbConnection() => _conn;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public IRepository<T> Repository<T>() where T : BaseEntity =>
            throw new NotSupportedException("게이트는 EF 리포지토리를 태우지 않는다(사원 백필 길 아님).");
        public Task<ISharedTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("게이트는 EF 공유 트랜잭션을 태우지 않는다.");
        public void Dispose() { }
    }
}
