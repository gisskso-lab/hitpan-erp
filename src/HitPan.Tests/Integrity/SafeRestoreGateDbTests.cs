using Dapper;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2② — 안전 복원 걸음 ① ~ ③ <b>DB 게이트</b>(작업지시서 §5-1 G0 · G1 ~ G3 · G8 · G14 · G19 · G20 · CI db-gate 전용).
/// </summary>
/// <remarks>
/// <para>출하 DDL 신규설치본(<see cref="MigrationGateDb"/>) 위에서 <b>출하 GRANT 흉내 제한 계정</b>(자기 DB 한 개 · 본 §15-9)으로
/// <see cref="RestoreSafePath"/> 를 직접 부른다(깃발 :639 닫힌 채 · 작업지시서 §9 ②). root 는 준비 일(DB · 계정 · GRANT · 시드)만.</para>
/// <para>🔴 이 PC 3306 은 실물 DB 라 로컬에서 돌리지 않는다(#39) — CI db-gate(HITPAN_REQUIRE_DB)에서만.
/// 덤프 실행 파일(mariadb-client)은 db-gate 잡이 깐다. 환경 변수(DB_*)를 쓰므로 BackupCredentialGate 와 같은 직렬 묶음.</para>
/// </remarks>
[Collection(BackupCredentialGateCollection.Name)]
public sealed class SafeRestoreGateDbTests : IDisposable
{
    private const string Tenant = "sr-gate-tenant-0001";
    private const string ParentId = "sr-gate-parent-0001";
    private const string ChildId = "sr-gate-child-0001";
    private const string Company = "안전복원게이트";
    private const string RestrictedUser = "hitpan_rs_t";

    private static readonly string[] ConfigKeys = { "DB_HOST", "DB_PORT", "DB_NAME", "DB_USER", "DB_PASSWORD" };

    private readonly MigrationGateDb _db = new("HITPAN_SR_ERP_DB", "srgate");
    private readonly Dictionary<string, string?> _savedEnv = ConfigKeys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hitpan_sr_gate_" + Guid.NewGuid().ToString("N")[..12]);
    private readonly string _parentSecret = "Pr-" + Guid.NewGuid().ToString("N")[..16];
    private readonly string _accountSecret = "Rs-" + Guid.NewGuid().ToString("N")[..20];
    private MySqlConnection? _conn;

    public void Dispose()
    {
        _conn?.Dispose();
        foreach (var (k, v) in _savedEnv) Environment.SetEnvironmentVariable(k, v);
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[SR] 시험 폴더 정리 실패: {ex.Message}"); }   // #15
        _db.Dispose();
    }

    private sealed record Ctx(MySqlConnection Admin, MySqlConnection App, RestoreSafePath Path, BackupService Backup);

    /// <summary>준비 — 시험 DB · 제한 계정(GRANT ALL ON 그DB.*) · 회사 · 대표 · 자식 관리자 · 백업 폴더.</summary>
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
            "INSERT INTO local_company (tenant_id, tenant_code, company_name) VALUES (@T, 'SRG', @C)", new { T = Tenant, C = Company });
        const string userSql =
            """
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent, is_active, created_at, updated_at)
            VALUES (@Id, @T, @E, @H, @N, 'admin', 'tenant_admin', @P, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            """;
        await admin.ExecuteAsync(userSql, new { Id = ParentId, T = Tenant, E = "owner-id", H = hash, N = "대표", P = 1 });
        await admin.ExecuteAsync(userSql, new { Id = ChildId, T = Tenant, E = "child-id", H = hash, N = "자식", P = 0 });

        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        Environment.SetEnvironmentVariable("DB_HOST", host);
        Environment.SetEnvironmentVariable("DB_PORT", port);
        Environment.SetEnvironmentVariable("DB_NAME", dbName);
        Environment.SetEnvironmentVariable("DB_USER", RestrictedUser);
        Environment.SetEnvironmentVariable("DB_PASSWORD", _accountSecret);

        _conn = new MySqlConnection(new MySqlConnectionStringBuilder
        {
            Server = host, Port = uint.Parse(port), UserID = RestrictedUser, Password = _accountSecret, Database = dbName,
            DefaultCommandTimeout = 300, GuidFormat = MySqlGuidFormat.None,
        }.ConnectionString);
        await _conn.OpenAsync();

        Directory.CreateDirectory(_folder);
        var backup = new BackupService(_conn, NullLogger<BackupService>.Instance);
        await backup.UpdateSettingsAsync(Tenant, new UpdateBackupSettingsRequest { PrimaryPath = _folder, RetentionCount = 30 });
        return new Ctx(admin, _conn, new RestoreSafePath(_conn, backup, NullLogger<RestoreSafePath>.Instance), backup);
    }

    private static SafeRestoreRequest Req(string pw) =>
        new() { BackupId = "x", AcknowledgedLaterDataLoss = true, ConfirmCompanyName = Company, Password = pw };

    private static async Task<string> BackupAsync(Ctx c, string trigger = "manual")
    {
        var r = await c.Backup.RunBackupAsync(Tenant, trigger);
        Assert.True(r.Success, $"백업 실패: {r.Error}");
        return r.BackupId;
    }

    private static Task<long> CountAsync(MySqlConnection c, string table) => c.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM `{table}`");

    [Fact(DisplayName = "G0 🔴 제한 계정 — CREATE DATABASE = 1044 · GRANT 가 자기 DB 한 개(root 꼴로 돌면 빨강)")]
    public async Task G0_제한계정_자기증명()
    {
        var c = await SetupAsync("SR-G0");
        if (c is null) return;
        var ex = await Assert.ThrowsAsync<MySqlException>(() => c.App.ExecuteAsync("CREATE DATABASE `sr_gate_should_fail`"));
        Assert.Equal(1044, ex.Number);
        var grants = (await c.App.QueryAsync<string>("SHOW GRANTS")).ToList();
        Assert.Contains(grants, g => g.Contains($"ON `{c.App.Database}`.*", StringComparison.Ordinal));
        Assert.DoesNotContain(grants, g => g.Contains("ON *.*", StringComparison.Ordinal) && g.Contains("ALL PRIVILEGES", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "G1 🔴 자식 관리자 = 거절(not_parent) · 대표 = 통과")]
    public async Task G1_자식관리자_거절()
    {
        var c = await SetupAsync("SR-G1");
        if (c is null) return;
        var child = await c.Path.CheckDoorAsync(Tenant, ChildId, Req(_parentSecret));
        Assert.False(child.Ok);
        Assert.Equal(SafeRestoreDenyCodes.NotParent, child.DenyCode);
        var parent = await c.Path.CheckDoorAsync(Tenant, ParentId, Req(_parentSecret));
        Assert.True(parent.Ok, parent.DenyCode);
        Assert.Equal(SafeRestoreDenyCodes.CompanyMismatch,
            (await c.Path.CheckDoorAsync(Tenant, ParentId, new SafeRestoreRequest { AcknowledgedLaterDataLoss = true, ConfirmCompanyName = "다른회사", Password = _parentSecret })).DenyCode);
    }

    [Fact(DisplayName = "G2 🔴 대표 · 틀린 비번 = 거절 · 실패 칸 +1 · 5회면 잠김")]
    public async Task G2_틀린비번_실패칸()
    {
        var c = await SetupAsync("SR-G2");
        if (c is null) return;
        var r = await c.Path.CheckDoorAsync(Tenant, ParentId, Req("wrong-" + Guid.NewGuid().ToString("N")[..6]));
        Assert.Equal(SafeRestoreDenyCodes.WrongPassword, r.DenyCode);
        Assert.Equal(1, await c.Admin.ExecuteScalarAsync<int>("SELECT failed_login_count FROM users WHERE user_id = @U", new { U = ParentId }));
        for (var i = 0; i < 4; i++) await c.Path.CheckDoorAsync(Tenant, ParentId, Req("wrong"));
        var locked = await c.Path.CheckDoorAsync(Tenant, ParentId, Req(_parentSecret));
        Assert.Equal(SafeRestoreDenyCodes.LockedOut, locked.DenyCode);
        Assert.True(locked.LockoutRemainingSeconds > 0);
    }

    [Fact(DisplayName = "G3 · G19 🔴 목록 밖 · 폴더 밖 · 지문 바뀐 파일 · 지문 칸 빈 옛 백업 · 판 다른 백업 = 거절 · 라이브 그대로")]
    public async Task G3_G19_고르기_거절()
    {
        var c = await SetupAsync("SR-G3");
        if (c is null) return;
        var id = await BackupAsync(c);
        var ok = await c.Path.SelectAsync(Tenant, id);
        Assert.True(ok.Ok, ok.DenyCode);
        ok.Value?.Dispose();
        var file = await c.Admin.ExecuteScalarAsync<string>("SELECT primary_file FROM backup_history WHERE backup_id = @I", new { I = id })
                   ?? throw new InvalidOperationException("백업 파일 칸 빔");

        Assert.Equal(SafeRestoreDenyCodes.BackupNotInList, (await c.Path.SelectAsync(Tenant, Guid.NewGuid().ToString())).DenyCode);
        Assert.Equal(SafeRestoreDenyCodes.BackupNotInList, (await c.Path.SelectAsync("other-tenant", id)).DenyCode);

        var outside = Path.Combine(Path.GetTempPath(), "sr_outside_" + Guid.NewGuid().ToString("N")[..8] + ".sql");
        File.Copy(file, outside);
        try
        {
            await c.Admin.ExecuteAsync("UPDATE backup_history SET primary_file = @F WHERE backup_id = @I", new { F = outside, I = id });
            Assert.Equal(SafeRestoreDenyCodes.BackupNotInList, (await c.Path.SelectAsync(Tenant, id)).DenyCode);
        }
        finally { File.Delete(outside); }
        await c.Admin.ExecuteAsync("UPDATE backup_history SET primary_file = @F WHERE backup_id = @I", new { F = file, I = id });

        await c.Admin.ExecuteAsync("UPDATE backup_history SET schema_fingerprint = REPEAT('0', 64) WHERE backup_id = @I", new { I = id });
        Assert.Equal(SafeRestoreDenyCodes.VersionMismatch, (await c.Path.SelectAsync(Tenant, id)).DenyCode);
        var live = await RestoreSafePath.ComputeSchemaFingerprintAsync(c.App, default);
        await c.Admin.ExecuteAsync("UPDATE backup_history SET schema_fingerprint = @S WHERE backup_id = @I", new { S = live, I = id });

        File.AppendAllText(file, "-- tampered\n");
        Assert.Equal(SafeRestoreDenyCodes.FingerprintMismatch, (await c.Path.SelectAsync(Tenant, id)).DenyCode);
        await c.Admin.ExecuteAsync("UPDATE backup_history SET file_sha256 = NULL WHERE backup_id = @I", new { I = id });
        Assert.Equal(SafeRestoreDenyCodes.FingerprintMismatch, (await c.Path.SelectAsync(Tenant, id)).DenyCode);

        var cands = await c.Path.ListCandidatesAsync(Tenant);
        Assert.Contains(cands, x => x.BackupId == id && !x.Selectable && x.DenyCode == SafeRestoreDenyCodes.FingerprintMismatch);
        Assert.Equal(0, await c.App.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND LEFT(TABLE_NAME, 8) = 'rs_new__'"));
    }

    [Fact(DisplayName = "G8 🔴 사전 백업 = 점검 상태 켠 뒤에만 · 목록에 pre_restore 로 오름 · 고를 수 있음 · 보관정책 뒤에도 남음")]
    public async Task G8_사전백업_목록등록()
    {
        var c = await SetupAsync("SR-G8");
        if (c is null) return;
        var off = await c.Path.PreBackupAsync(Tenant, "r-1", maintenanceActive: false);
        Assert.Equal(SafeRestoreDenyCodes.PreBackupFailed, off.DenyCode);
        Assert.Equal(0, await CountAsync(c.Admin, "backup_history"));

        var pre = await c.Path.PreBackupAsync(Tenant, "r-1", maintenanceActive: true);
        Assert.True(pre.Ok, pre.DenyCode);
        var file = await c.Admin.ExecuteScalarAsync<string>(
            "SELECT primary_file FROM backup_history WHERE backup_id = @I AND triggered_by = 'pre_restore'", new { I = pre.Value!.BackupId })
            ?? throw new InvalidOperationException("사전 백업 파일 칸 빔");
        Assert.StartsWith("hitpan_pre_restore_", Path.GetFileName(file), StringComparison.Ordinal);

        await c.Backup.UpdateSettingsAsync(Tenant, new UpdateBackupSettingsRequest { PrimaryPath = _folder, RetentionCount = 1 });
        await BackupAsync(c);
        await Task.Delay(1100);
        await BackupAsync(c);
        Assert.True(File.Exists(file), "보관정책이 복원 전 자동 백업을 지웠다");
        var cands = await c.Path.ListCandidatesAsync(Tenant);
        Assert.Contains(cands, x => x.BackupId == pre.Value.BackupId && x.Selectable && x.TriggeredBy == "pre_restore");
    }

    [Fact(DisplayName = "G14 · G20 🔴 출하 판 덤프 실물(golden) 통과 — rs_new__ 줄 수 = 라이브 · 256 바이트 왕복 같음 · 라이브 손 0 · 지우기 한 곳")]
    public async Task G14_G20_정상덤프_붓기_왕복()
    {
        var c = await SetupAsync("SR-G14");
        if (c is null) return;
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        await c.Admin.ExecuteAsync(
            "INSERT INTO partner_contacts (contact_id, tenant_id, contact_name, hp_encrypted, memo) VALUES ('pc-1', @T, '홍길동', @B, 'a''b;\\\\c'), ('pc-2', @T, '둘째', NULL, NULL)",
            new { T = Tenant, B = bytes });
        var id = await BackupAsync(c);
        var liveBefore = await CountAsync(c.Admin, "partner_contacts");

        var sel = await c.Path.SelectAsync(Tenant, id);
        using var selValue = sel.Value;
        Assert.True(sel.Ok, sel.DenyCode);
        var st = await c.Path.StageAsync(Tenant, sel.Value!);
        Assert.True(st.Ok, $"{st.DenyCode} {st.LogDetail}");
        Assert.True(st.Value!.StagedTables.ContainsKey("partner_contacts"));
        Assert.False(st.Value.StagedTables.ContainsKey("users"), "지금 값 표를 부었다");
        Assert.Contains("schema_migrations", st.Value.VersionOnlyTables, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(liveBefore, await CountAsync(c.Admin, "rs_new__partner_contacts"));
        Assert.Equal(liveBefore, await CountAsync(c.Admin, "partner_contacts"));
        var back = await c.Admin.ExecuteScalarAsync<byte[]>("SELECT hp_encrypted FROM rs_new__partner_contacts WHERE contact_id = 'pc-1'");
        Assert.Equal(bytes, back);
        Assert.Equal("a'b;\\c", await c.Admin.ExecuteScalarAsync<string>("SELECT memo FROM rs_new__partner_contacts WHERE contact_id = 'pc-1'"));
        Assert.Equal(0, await c.Admin.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = DATABASE() AND LEFT(TABLE_NAME, 8) = 'rs_new__' AND REFERENCED_TABLE_NAME IS NOT NULL"));

        var dropped = await c.Path.DropStagingTablesAsync();
        Assert.Equal(st.Value.StagedTables.Count, dropped);
        Assert.Equal(liveBefore, await CountAsync(c.Admin, "partner_contacts"));
    }

    [Fact(DisplayName = "G3 🔴 다른 회사 표지 줄이 든 백업 = ③ 에서 멈춤(other_company_backup) · 임시 표 0 · 라이브 그대로")]
    public async Task G3_다른회사_백업_멈춤()
    {
        var c = await SetupAsync("SR-G3b");
        if (c is null) return;
        await c.Admin.ExecuteAsync(
            "INSERT INTO partner_contacts (contact_id, tenant_id, contact_name) VALUES ('pc-o', 'other-company-0001', '남의회사')");
        var id = await BackupAsync(c);
        var sel = await c.Path.SelectAsync(Tenant, id);
        using var selValue = sel.Value;
        Assert.True(sel.Ok, sel.DenyCode);
        var st = await c.Path.StageAsync(Tenant, sel.Value!);
        Assert.Equal(SafeRestoreDenyCodes.OtherCompanyBackup, st.DenyCode);
        Assert.True(st.DataUnchanged);
        Assert.Equal(0, await c.App.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND LEFT(TABLE_NAME, 8) = 'rs_new__'"));
        Assert.Equal(1, await CountAsync(c.Admin, "partner_contacts"));
    }

    [Fact(DisplayName = "G8-S2 🔴 사전 백업이 지문 칸 빈 채 목록에 오름(등록 실패) = 거절(pre_backup_failed) — 등록 확인(:227) 뺌 변이 = 빨강")]
    public async Task G8_S2_사전백업_등록실패_거절()
    {
        var c = await SetupAsync("SR-G8b");
        if (c is null) return;
        // 지문 기록만 실패시킴 — backup_history 를 고치는 UPDATE 마다 지문 칸을 비움(덤프 · 성공 표시는 그대로 · 시험 DB 는 매 시험 새로 만듦)
        await c.Admin.ExecuteAsync(
            "CREATE TRIGGER gate_sr_g8b_no_sha BEFORE UPDATE ON backup_history FOR EACH ROW SET NEW.file_sha256 = NULL");

        var pre = await c.Path.PreBackupAsync(Tenant, "r-2", maintenanceActive: true);

        // 준비가 먹었나 — 덤프 성공 · pre_restore 1줄 · 지문 칸 빔(아니면 「덤프 실패」 같은 다른 길로 초록이 나와 변이를 못 잡음)
        var rows = (await c.Admin.QueryAsync<(string Status, string? Sha)>(
            "SELECT status, file_sha256 FROM backup_history WHERE tenant_id = @T AND triggered_by = 'pre_restore'",
            new { T = Tenant })).ToList();
        Assert.Single(rows);
        Assert.Equal("success", rows[0].Status);
        Assert.Null(rows[0].Sha);

        Assert.False(pre.Ok);
        Assert.Equal(SafeRestoreDenyCodes.PreBackupFailed, pre.DenyCode);
        Assert.True(pre.DataUnchanged);
    }

    [Fact(DisplayName = "G1-S2 🔴 대표(is_parent=1) 2명인 회사 = 대표 본인도 거절(not_parent) — 첫 판정(:64)만 막는 꼴 · 부모 판정 뺌 변이 = 빨강")]
    public async Task G1_S2_대표2명_거절()
    {
        var c = await SetupAsync("SR-G1b");
        if (c is null) return;
        // 대표 1명이면 본인 통과(늘 거절하는 판정 = 초록 구멍 막음)
        var one = await c.Path.CheckDoorAsync(Tenant, ParentId, Req(_parentSecret));
        Assert.True(one.Ok, one.DenyCode);

        // 대표 하나 더 — 본인(ParentId)은 대표 행 그대로라 비번 질의(:82 is_parent = 1)는 통과하는 사람 → 막는 것은 「대표 수 = 1」 첫 판정뿐
        await AddSecondParentAsync(c.Admin);
        var two = await c.Path.CheckDoorAsync(Tenant, ParentId, Req(_parentSecret));
        Assert.False(two.Ok);
        Assert.Equal(SafeRestoreDenyCodes.NotParent, two.DenyCode);
        Assert.Equal(0, await c.Admin.ExecuteScalarAsync<int>("SELECT failed_login_count FROM users WHERE user_id = @U", new { U = ParentId }));

        static Task<int> AddSecondParentAsync(MySqlConnection admin) => admin.ExecuteAsync(
            """
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent, is_active, created_at, updated_at)
            SELECT 'sr-gate-parent-0002', tenant_id, 'owner2-id', password_hash, '대표둘', 'admin', 'tenant_admin', 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
            FROM users WHERE user_id = @P
            """,
            new { P = ParentId });
    }
}
