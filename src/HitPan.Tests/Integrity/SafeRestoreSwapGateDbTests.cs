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
    private async Task<(string? EmployeeId, List<string> Roles)> LoginClaimsAsync(string loginId)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JWT_SECRET")))
            Environment.SetEnvironmentVariable("JWT_SECRET", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(_appConnString, new MariaDbServerVersion(new Version(11, 4, 0))).Options;
        await using var ef = new AppDbContext(opts, Mock.Of<ICurrentTenant>(), Mock.Of<IEncryptionService>());
        await using var conn = new MySqlConnection(_appConnString);
        await conn.OpenAsync();
        var auth = new AuthService(new SwapGateUnitOfWork(conn), new AuthUserLookup(ef));
        var resp = await auth.LoginAsync(new LoginRequest { Email = loginId, Password = _parentSecret, DeviceType = "pc", UserAgent = PcUa });
        Assert.False(string.IsNullOrEmpty(resp.AccessToken), "로그인 토큰을 못 받았다");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(resp.AccessToken);
        return (jwt.Claims.FirstOrDefault(x => x.Type == "employee_id")?.Value,
                jwt.Claims.Where(x => x.Type == "role").Select(x => x.Value).Distinct().ToList());
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
        var id = await BackupAsync(c);
        await c.App.ExecuteAsync("SET SESSION autocommit = 0");

        var sel = await c.Path.SelectAsync(Tenant, id);
        using var selection = sel.Value!;
        var st = await c.Path.StageAsync(Tenant, selection);
        Assert.True(st.Ok, st.DenyCode);
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
