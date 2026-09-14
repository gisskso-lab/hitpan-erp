using System.Reflection;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-M1 ~ G-M7</b> — 재기동이 메인PC 표식을 <b>되올리지 않는다</b> · 이미 생긴 2줄은 <b>정리된다</b> ·
/// DB 가 2줄을 <b>물리적으로 거부한다</b> (20260913작2).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇이 났나</b> — 2026-09-13 사장님: <i>"수정안됨. 반려"</i>.
/// 실물 <c>hitpan_erp_t004</c> 에 <c>is_main_pc=1</c> 이 <b>2줄</b>이었다 —
/// ⓐ <c>HFPv2-a341d087</c>(승인 · 사장님이 쓰는 줄) · ⓑ <c>MAINPC-9c1163c</c>(폐기 · 사유 「…(20260818작4)」).
/// </para>
///
/// <para>
/// 🔴 <b>진범은 「내림 실패」가 아니다 — 내려간 표식을 재기동이 되올린다.</b>
/// <c>MainPcRegistrationService</c> 의 되올림 UPDATE 가 <c>WHERE device_id=@Id AND is_main_pc=0</c> 만 보고
/// <b><c>status</c> 를 보지 않는다</b> ⇒ <c>revoked</c> 줄도 되올린다.
/// <c>BackgroundService</c> 라 <b>API 기동마다 1회</b> 돌므로 <b>업데이트마다 재발</b>한다
/// (선행검증 A §8-2 ② · 폐기 줄의 <c>last_seen_at 9/11 23:05</c> 이 그 UPDATE 의 <c>NOW()</c> 흔적).
/// </para>
///
/// <para>
/// 🟢 <b>초록불이 어디서 오나</b> — 격리 DB 에 <b>출하 DDL</b> 을 넣고
/// <b>실제 <c>RegisterAsync</c></b>(reflection · 흉내 아님)와 <b>실제 <c>DB-120</c> 파일</b>을 돌린 뒤
/// <b>표를 읽는다.</b> 글자를 안 본다. 반환값도 판정 근거로 쓰지 않는다.
/// </para>
///
/// <para>
/// 🔴 <b>옛 고객 모양 재현</b> — 출하 DDL 에 UNIQUE 가 들어간 뒤에는 2줄을 만들 수 없다 ⇒
/// 격리 DB 에서 <c>uq_tenant_main_pc</c>·<c>main_pc_key</c> 를 떼어 <b>DB-120 이전 모양</b>을 만든 뒤 시험한다.
/// <b>격리 DB 안에서만</b> 한다(헌법 #39).
/// </para>
///
/// <para>
/// ⚠️ <b>운영 무접촉</b>(헌법 #39) — 임시 DB(<c>hitpan_mainpc_mark_*</c>)만 만들고 반드시 지운다.
/// 3306 운영 · 5257 · 5234 접속 0.
/// ⚠️ MariaDB 가 없으면 건너뛴다(<c>DbGateEnvironment.SkipOrFail</c>) — <b>그 환경에서 이 게이트는
/// 아무것도 검사하지 않는다.</b> 초록불이 곧 안전이 아니다. CI 는 <c>HITPAN_REQUIRE_DB</c> 로 SKIP 을 막는다.
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class MainPcRestartMarkGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_mainpc_mark_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string UserId = "22222222-2222-2222-2222-222222222222";

    /// <summary>실물 ⓑ 의 폐기 사유 원문. <b>이 글자가 보존되는지</b>가 감사기록 보존 판정이다(#1).</summary>
    private const string RevokedReason = "메인PC 표식을 실제 사용 화면으로 옮김 (20260818작4)";

    /// <summary>🔴 <c>DB-120</c> 안에서 ①단(정리)이 끝나는 자리. G-M4 가 여기를 잘라 ②③만 돌린다.</summary>
    private const string Stage1EndMarker = "-- ##DB120-STAGE-1-END##";

    // ══════════════════════════════════════════════════════════════
    // 준비물 — 선례(MainPcLocalConsoleGateTests·DeviceAndKeyGateTests)와 같은 방식
    // ══════════════════════════════════════════════════════════════

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 출하 DDL·마이그를 읽을 수 없다.");
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

    /// <summary>
    /// 🔴 마이그 러너와 <b>같은 연결 성질</b>이다 — <c>AllowUserVariables=true</c>
    /// (<c>MigrationDbConnectionFactory.cs:58</c>). 이것이 없으면 DB-89 식 <c>SET @…</c>/<c>PREPARE</c> 가
    /// MySqlConnector 에서 <b>파라미터로 오해되어</b> 터진다 — 러너에서 되는지를 여기서 잰다.
    /// </summary>
    private string DbConnString() =>
        ServerConnString().Replace("User=", $"Database={_dbName};User=");

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL")
        ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

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

    /// <summary>🔴 신규 설치 그대로 — 빈 DB 에 출하 DDL 한 방(헌법 #36).</summary>
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
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(_dbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddlPath));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0,
            $"출하 DDL import 가 실패했다 — 신규 설치가 같은 자리에서 죽는다:\n{err}");

        SeedCompanyAndUser();
    }

    /// <summary>
    /// 🔴 <c>RegisterAsync</c> 가 회사를 <c>local_company</c> 에서 읽는다
    /// (<c>ORDER BY bootstrap_at LIMIT 1</c>) ⇒ 그 줄이 없으면 서비스가 <b>등록을 미루고 끝난다.</b>
    /// 사람 줄도 심는다 — <c>tenant_devices.user_id</c> 에 <c>fk_device_user</c> 가 걸려 있다.
    /// </summary>
    private void SeedCompanyAndUser()
    {
        using var db = new MySqlConnection(DbConnString());
        db.Open();

        db.Execute(
            """
            INSERT INTO local_company (tenant_id, tenant_code, company_name, bootstrap_at)
            VALUES (@Tid, 'GATE01', '게이트시험회사', NOW())
            """,
            new { Tid = TenantId });

        db.Execute(
            """
            INSERT INTO users
              (user_id, tenant_id, email, password_hash, user_name, role,
               account_type, is_active, created_at, updated_at)
            VALUES
              (@Uid, @Tid, 'mark-gate@hitpan.kr', 'x', '게이트시험자', 'tenant_admin',
               'tenant_admin', 1, NOW(6), NOW(6))
            """,
            new { Uid = UserId, Tid = TenantId });
    }

    /// <summary>
    /// 🔴 <b>DB-120 이전(옛 고객) 모양</b>으로 되돌린다 — 방어선이 없는 상태.
    /// 실물이 그 상태였다(선행검증 A §8-1 Q7·Q8 둘 다 빈 결과).
    /// <c>IF EXISTS</c> 라서 <b>봉합 전(출하 DDL 에 아직 방어선이 없을 때)에도</b> 그대로 돈다.
    /// </summary>
    private static async Task MakeLegacyShapeAsync(MySqlConnection db)
    {
        await db.ExecuteAsync("ALTER TABLE `tenant_devices` DROP INDEX IF EXISTS `uq_tenant_main_pc`;");
        await db.ExecuteAsync("ALTER TABLE `tenant_devices` DROP COLUMN IF EXISTS `main_pc_key`;");
    }

    // ══════════════════════════════════════════════════════════════
    // 실제 코드를 부른다 — 흉내 금지
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>실제 서버 지문</b>을 그 코드에서 얻는다(<c>BuildServerFingerprint</c>).
    /// 여기서 값을 베껴 쓰면 시험이 <b>실물과 다른 줄</b>을 만든다 —
    /// 되올림은 <c>fingerprint</c> 로 기존 줄을 찾기 때문에 지문이 다르면 ①경로 자체를 안 탄다.
    /// </summary>
    private static string ServerFingerprint()
    {
        var m = typeof(HitPan.API.HostedServices.MainPcRegistrationService)
            .GetMethod("BuildServerFingerprint", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(m is not null,
            "BuildServerFingerprint 를 못 찾았다 — 서비스 구조가 바뀌었다. 시험이 실물 지문을 못 만든다.");
        return (string)m!.Invoke(null, null)!;
    }

    /// <summary>
    /// 🔴 <b>실제 되올림 경로를 그대로 부른다.</b>
    /// <c>ExecuteAsync</c>(기동 루프)는 <c>db.conf</c>/환경변수를 읽으므로 격리 DB 를 가리킬 수 없다 ⇒
    /// 그 바로 안쪽인 <c>RegisterAsync(connStr, ct)</c> 를 부른다. <b>SQL 은 실물과 한 글자도 다르지 않다.</b>
    /// </summary>
    private async Task CallRegisterAsync()
    {
        var svc = new HitPan.API.HostedServices.MainPcRegistrationService(
            NullLogger<HitPan.API.HostedServices.MainPcRegistrationService>.Instance);

        var m = typeof(HitPan.API.HostedServices.MainPcRegistrationService)
            .GetMethod("RegisterAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(m is not null,
            "RegisterAsync 를 못 찾았다 — 서비스 구조가 바뀌었다. 이 게이트는 되올림을 재지 못한다.");

        await (Task)m!.Invoke(svc, new object[] { DbConnString(), CancellationToken.None })!;
    }

    private static string Db120Path() =>
        Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL",
                     "DB-120_main_pc_single_row_guard.sql");

    /// <summary>🔴 <b>파일을 실제로 읽는다.</b> 글자검사가 아니라 <b>실행</b>이 판정이다.</summary>
    private static string Db120Sql()
    {
        var path = Db120Path();
        Assert.True(File.Exists(path),
            $"🔴 DB-120 마이그 파일이 없다: {path}\n"
          + "  ⇒ 이미 표식 2줄인 고객(실물 사장님 PC)은 정리되지 않고, 신규 고객엔 UNIQUE 도 안 생긴다.");
        return File.ReadAllText(path);
    }

    /// <summary>🔴 ①단(정리)을 <b>건너뛰고</b> ②③만 남긴다 — G-M4 순서 시험용.</summary>
    private static string Db120Stage23Only()
    {
        var full = Db120Sql();
        var i = full.IndexOf(Stage1EndMarker, StringComparison.Ordinal);
        Assert.True(i >= 0,
            $"🔴 DB-120 에 순서 표식 `{Stage1EndMarker}` 가 없다 — ①단과 ②③단의 경계를 잴 수 없다.\n"
          + "  이 표식이 있어야 「정리가 UNIQUE 보다 먼저」를 동작으로 증명할 수 있다(G-M4).");
        return full[(i + Stage1EndMarker.Length)..];
    }

    /// <summary>🔴 마이그 러너와 같은 방식 — 문장들을 <b>한 번의 <c>ExecuteAsync</c></b> 로 보낸다(<c>MigrationRunner.cs:135-137</c>).</summary>
    private async Task RunMigrationSqlAsync(string sql)
    {
        await using var conn = new MySqlConnection(DbConnString());
        await conn.OpenAsync();
        await conn.ExecuteAsync(new CommandDefinition(sql, commandTimeout: 86400));
    }

    // ══════════════════════════════════════════════════════════════
    // 표를 읽는다 (반환값이 아니라 표가 사실이다)
    // ══════════════════════════════════════════════════════════════

    private sealed class DeviceRow
    {
        public string DeviceId { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsMainPc { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokedReason { get; set; }
        public DateTime? LastSeenAt { get; set; }
    }

    private static Task<DeviceRow?> ReadRowAsync(MySqlConnection db, string deviceId) =>
        db.QueryFirstOrDefaultAsync<DeviceRow>(
            """
            SELECT device_id AS DeviceId, status AS Status, COALESCE(is_main_pc,0) AS IsMainPc,
                   revoked_at AS RevokedAt, revoked_reason AS RevokedReason, last_seen_at AS LastSeenAt
              FROM tenant_devices WHERE device_id = @Id
            """,
            new { Id = deviceId });

    private static Task<long> MarkCountAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tenant_devices WHERE tenant_id=@Tid AND is_main_pc=1",
            new { Tid = TenantId });

    private static Task<long> RowCountAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tenant_devices");

    /// <summary>🔴 <b>요금 계수와 같은 식</b>이다 — <c>status='approved' ∧ device_type='pc'</c>(계수에 <c>is_main_pc</c> 절 없음).</summary>
    private static Task<long> ApprovedPcCountAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tenant_devices WHERE tenant_id=@Tid AND status='approved' AND device_type='pc'",
            new { Tid = TenantId });

    private static Task<long> IndexExistsAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<long>(
            """
            SELECT COUNT(*) FROM information_schema.statistics
             WHERE table_schema = DATABASE() AND table_name='tenant_devices'
               AND index_name='uq_tenant_main_pc'
            """);

    private static Task<long> ColumnExistsAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<long>(
            """
            SELECT COUNT(*) FROM information_schema.columns
             WHERE table_schema = DATABASE() AND table_name='tenant_devices'
               AND column_name='main_pc_key'
            """);

    /// <summary>
    /// 🔴 <b><c>DB-120</c> ③단을 건너뛴 사실이 그 고객 DB 안에 남았는지</b>
    /// (④단 · CTO 결재문 20260913결1 §6 <b>D-3</b>).
    /// <para>이 줄이 없으면 「방어선 없이 남은 회사」를 <b>아무도 다시 못 찾는다</b> — 문서에만 있으면 없는 것이다.</para>
    /// </summary>
    private static Task<long> DupAuditCountAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<long>(
            """
            SELECT COUNT(*) FROM audit_trail
             WHERE tenant_id = @Tid AND action_type = 'db120_mainpc_dup'
            """,
            new { Tid = TenantId });

    // ── 줄 심기 ──────────────────────────────────────────────────

    /// <summary>서버가 만든 줄 그대로 — 지문 <c>MAINPC-…</c> · <c>user_id NULL</c>(브라우저 로그인 경로를 타지 않는 줄).</summary>
    private static Task InsertServerRowAsync(
        MySqlConnection db, string deviceId, string status, bool mark,
        string? lastSeen = "2026-09-11 23:05:01", bool revokedStamps = false) =>
        db.ExecuteAsync(
            """
            INSERT INTO tenant_devices
              (device_id, tenant_id, user_id, device_type, device_name, fingerprint,
               ip_address, status, is_main_pc, registered_at, last_seen_at, revoked_at, revoked_reason)
            VALUES
              (@Id, @Tid, NULL, 'pc', '회사 서버 (자료 보관 컴퓨터)', @Fp,
               NULL, @Status, @Mark, '2026-08-21 01:02:39', @LastSeen,
               CASE WHEN @Stamps THEN '2026-09-11 18:39:31' ELSE NULL END,
               CASE WHEN @Stamps THEN @Reason ELSE NULL END)
            """,
            new
            {
                Id = deviceId, Tid = TenantId, Fp = ServerFingerprint(), Status = status,
                Mark = mark ? 1 : 0, LastSeen = lastSeen, Stamps = revokedStamps, Reason = RevokedReason
            });

    /// <summary>사장님이 실제로 쓰는 줄 ⓐ — 브라우저 지문 <c>HFPv2-…</c> · 사람이 붙어 있다.</summary>
    private static Task InsertBrowserRowAsync(
        MySqlConnection db, string deviceId, string status, bool mark,
        string? lastSeen = "2026-09-13 15:18:21", string registeredAt = "2026-09-10 04:18:32") =>
        db.ExecuteAsync(
            """
            INSERT INTO tenant_devices
              (device_id, tenant_id, user_id, device_type, device_name, fingerprint,
               ip_address, status, is_main_pc, registered_at, last_seen_at)
            VALUES
              (@Id, @Tid, @Uid, 'pc', 'Windows · Edge', @Fp,
               '127.0.0.1', @Status, @Mark, @Reg, @LastSeen)
            """,
            new
            {
                Id = deviceId, Tid = TenantId, Uid = UserId,
                Fp = "HFPv2-" + Guid.NewGuid().ToString("N")[..16],
                Status = status, Mark = mark ? 1 : 0, Reg = registeredAt, LastSeen = lastSeen
            });

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
            // 헌법 #15 — 조용히 넘기지 않는다. 지우기 실패가 시험 결과를 뒤집으면 안 되므로 던지지는 않는다.
            Console.Error.WriteLine($"[정리실패] 임시 DB {_dbName} 삭제 실패 — 사람이 지워야 한다: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-M1 — 재기동이 폐기된 서버줄을 되올리지 않는다  🔴 본체(진범)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-M1. 폐기된 서버줄은 재기동에도 표식을 되찾지 못한다.</b>
    /// <para>[이것이 사장님 증상이다] 8/18 에 표식을 ⓐ 로 옮겼는데, 9/11 업데이트(API 재기동)가
    /// 폐기된 서버줄을 <b>되올려</b> 표식이 2줄이 됐다.</para>
    /// <para>[반증] 되올림 조건 (A)<c>approved</c> 나 (B)「표식 0줄」을 지우면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-M1 🔴 재기동이 폐기된 서버줄의 메인PC 표식을 되올리지 않는다")]
    public async Task GM1_되올림이_폐기줄을_세우지_않는다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM1_되올림이_폐기줄을_세우지_않는다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        // 실물 모양: ⓑ 폐기된 서버줄(표식 내려감) + ⓐ 승인·표식 보유(사장님이 쓰는 줄)
        const string serverRow = "srv-1";
        const string ownerRow = "own-1";
        await InsertServerRowAsync(db, serverRow, "revoked", mark: false, revokedStamps: true);
        await InsertBrowserRowAsync(db, ownerRow, "approved", mark: true);

        await CallRegisterAsync();

        var srv = await ReadRowAsync(db, serverRow);
        var own = await ReadRowAsync(db, ownerRow);
        var marks = await MarkCountAsync(db);

        Assert.True(srv is not null, "서버줄이 표에서 사라졌다.");
        Assert.False(srv!.IsMainPc,
            "🔴 폐기된 서버줄이 다시 메인PC 로 되올려졌다 — 표식이 2줄이 된다(실물 P1 재현). "
          + "되올림 UPDATE 가 status 를 보지 않는다: API 기동마다 1회 도므로 **업데이트마다 재발**한다.");

        Assert.Equal("revoked", srv.Status);
        Assert.Equal(RevokedReason, srv.RevokedReason);

        Assert.Equal(1, marks);

        Assert.True(own is not null && own.IsMainPc && own.Status == "approved",
            "🔴 사장님이 쓰는 줄 ⓐ 가 변했다 — 이 봉합은 ⓐ 를 건드리지 않아야 한다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-M2 — 🟢 대조군: 정당한 목적은 살아 있다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🟢 <b>G-M2(대조군). 표식이 비어 있으면 되올림은 그대로 돈다.</b>
    /// <para>「업데이트로 컬럼이 새로 생긴 기존 고객」을 위한 정당한 목적이다
    /// (<c>MainPcRegistrationService.cs:155</c> 주석). <b>이것이 FAIL 로 바뀌면 조건을 과하게 건 것</b>이다.</para>
    /// </summary>
    [Fact(DisplayName = "G-M2 🟢 대조군 — 표식이 0줄이면 승인된 서버줄은 정상적으로 메인PC 가 된다")]
    public async Task GM2_대조군_표식0줄이면_되올린다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM2_대조군_표식0줄이면_되올린다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        const string serverRow = "srv-2";
        await InsertServerRowAsync(db, serverRow, "approved", mark: false);

        await CallRegisterAsync();

        var srv = await ReadRowAsync(db, serverRow);
        Assert.True(srv is not null, "서버줄이 표에서 사라졌다.");
        Assert.True(srv!.IsMainPc,
            "🔴 정당한 목적이 죽었다 — 회사에 표식이 0줄이고 그 줄이 승인 상태인데 메인PC 표식이 붙지 않았다. "
          + "CS 가 「그 컴퓨터가 본체입니다」를 찾을 수 없게 된다(이 서비스의 존재 이유). "
          + "봉합 조건을 과하게 걸었다는 뜻이다.");
        Assert.Equal(1, await MarkCountAsync(db));
    }

    // ══════════════════════════════════════════════════════════════
    // G-M1a · G-M1b — 두 조건이 **각각** 제 몫을 하는지 따로 잰다
    //
    // 🔴 왜 G-M1 만으로는 부족한가 (20260913 실측으로 드러난 것)
    //   G-M1 의 모양(서버줄 `revoked` + 회사에 표식 1줄)은 (A)·(B) **둘 다** 막는다.
    //   ⇒ 하나만 지워도 나머지가 막아 주므로 **G-M1 은 초록불을 유지한다.**
    //     실제로 무력화 시험에서 (A) 제거·(B) 제거 각각에 G-M1 이 통과했다.
    //   ⇒ 그래서 **조건 하나만이 유일한 방어선인 모양**을 따로 만든다.
    //     설계 §2-2 표의 4행(폐기·표식 0줄)과 3행(승인·표식 이미 있음)이 정확히 그 두 자리다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-M1a. (A) 만이 막는 자리</b> — 서버줄이 <c>revoked</c> 이고 회사에 표식이 <b>0줄</b>.
    /// <para>표식이 0줄로 남는 것은 <b>의도</b>다(설계 §2-2 4행 · 사장님 결재 5
    /// 「폐기된 옛 메인 줄 자동 부활 안 함」). 사람이 목록에서 옛 기기를 폐기하면
    /// 다음 기동에 신규 INSERT 경로가 새 줄을 만든다.</para>
    /// <para>[반증] 되올림 조건 <b>(A) <c>status='approved'</c> 를 지우면 FAIL</b>.</para>
    /// </summary>
    [Fact(DisplayName = "G-M1a 🔴 (A)단독 — 폐기된 서버줄은 회사에 표식이 0줄이어도 부활하지 않는다")]
    public async Task GM1a_A조건_단독_폐기줄은_부활하지_않는다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM1a_A조건_단독_폐기줄은_부활하지_않는다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        // 회사에 표식이 **0줄** — 그래서 (B) 는 통과한다. 막는 것은 (A) 뿐이다.
        const string serverRow = "srv-1a";
        await InsertServerRowAsync(db, serverRow, "revoked", mark: false, revokedStamps: true);
        Assert.Equal(0, await MarkCountAsync(db));

        await CallRegisterAsync();

        var srv = await ReadRowAsync(db, serverRow);
        Assert.False(srv!.IsMainPc,
            "🔴 **폐기된 줄이 되올려졌다.** 회사에 표식이 0줄이라 (B) 는 열려 있으므로 "
          + "이 자리를 막는 것은 (A) `status='approved'` **하나뿐**이다. "
          + "폐기된 옛 메인 줄을 자동 부활시키지 않는다는 사장님 결재 5 가 깨진다.");
        Assert.Equal("revoked", srv.Status);
        Assert.Equal(0, await MarkCountAsync(db));
    }

    /// <summary>
    /// 🔴 <b>G-M1b. (B) 만이 막는 자리</b> — 서버줄이 <c>approved</c> 인데 표식은 이미 ⓐ 에 있다.
    /// <para>8/18작4 의 표식 이동 **결과를 존중한다**(설계 §2-2 3행). 서버줄을 다시 세우면 2줄이 된다.</para>
    /// <para>[반증] 되올림 조건 <b>(B) 「회사에 표식 0줄」을 지우면 FAIL</b>.</para>
    /// </summary>
    [Fact(DisplayName = "G-M1b 🔴 (B)단독 — 승인된 서버줄도 표식이 이미 다른 줄에 있으면 세우지 않는다")]
    public async Task GM1b_B조건_단독_표식이_있으면_안세운다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM1b_B조건_단독_표식이_있으면_안세운다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        // 서버줄이 **승인** 이라 (A) 는 통과한다. 막는 것은 (B) 뿐이다.
        const string serverRow = "srv-1b";
        const string ownerRow = "own-1b";
        await InsertServerRowAsync(db, serverRow, "approved", mark: false);
        await InsertBrowserRowAsync(db, ownerRow, "approved", mark: true);

        await CallRegisterAsync();

        var srv = await ReadRowAsync(db, serverRow);
        var own = await ReadRowAsync(db, ownerRow);

        Assert.False(srv!.IsMainPc,
            "🔴 **표식이 이미 ⓐ 에 있는데 서버줄을 또 세웠다 — 표식이 2줄이 된다.** "
          + "서버줄이 승인이라 (A) 는 열려 있으므로 이 자리를 막는 것은 "
          + "(B) 「회사에 표식 0줄」 **하나뿐**이다. 8/18작4 의 표식 이동 결과가 매 기동 뒤집힌다.");
        Assert.True(own!.IsMainPc, "🔴 사장님이 쓰는 줄 ⓐ 가 표식을 잃었다.");
        Assert.Equal(1, await MarkCountAsync(db));
    }

    // ══════════════════════════════════════════════════════════════
    // G-M3 — 이미 2줄인 고객이 정리된다 (실제 DB-120 파일 실행)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-M3. <c>DB-120</c> 이 표식만 내리고 감사기록을 보존한다.</b>
    /// <para>줄 삭제 0 · <c>status</c>·<c>revoked_at</c>·<c>revoked_reason</c> 무변경(#1) ·
    /// 실행 뒤 <c>uq_tenant_main_pc</c> 존재.</para>
    /// </summary>
    [Fact(DisplayName = "G-M3 🔴 DB-120 이 표식 2줄을 1줄로 정리하고 감사기록을 보존한다")]
    public async Task GM3_DB120이_2줄을_정리한다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM3_DB120이_2줄을_정리한다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        const string serverRow = "srv-3";
        const string ownerRow = "own-3";
        await InsertServerRowAsync(db, serverRow, "revoked", mark: true, revokedStamps: true);
        await InsertBrowserRowAsync(db, ownerRow, "approved", mark: true);

        Assert.Equal(2, await MarkCountAsync(db));
        var rowsBefore = await RowCountAsync(db);

        await RunMigrationSqlAsync(Db120Sql());

        var srv = await ReadRowAsync(db, serverRow);
        var own = await ReadRowAsync(db, ownerRow);

        Assert.True(srv is not null && own is not null, "줄이 사라졌다 — DB-120 은 줄을 지우지 않는다(#1).");

        Assert.False(srv!.IsMainPc, "🔴 폐기 줄의 표식이 안 내려갔다 — 2줄이 그대로 남는다.");
        Assert.Equal("revoked", srv.Status);
        Assert.Equal(RevokedReason, srv.RevokedReason);
        Assert.True(srv.RevokedAt is not null,
            "🔴 revoked_at 이 지워졌다 — 다음 사람이 경로를 읽을 수 없다(감사기록 보존 #1).");

        Assert.True(own!.IsMainPc && own.Status == "approved",
            "🔴 사장님이 쓰는 승인 줄이 표식을 잃었다 — 잘못 고르면 사장님이 갇힌다(8/11·8/16·8/18 계통).");

        Assert.Equal(1, await MarkCountAsync(db));
        Assert.Equal(rowsBefore, await RowCountAsync(db));
        Assert.Equal(1, await IndexExistsAsync(db));

        // 🔴 §9-4 함께 봉합 — 정리된 회사에는 「건너뜀」 기록이 **없어야** 한다(거짓 기록 대조).
        Assert.True(await DupAuditCountAsync(db) == 0,
            "🔴 정리가 끝난 회사에 `db120_mainpc_dup` 기록이 남았다 — ④단이 정상 회사에 거짓 기록을 쓴다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-M4 — 🔴 ③단은 ①단의 성공에 기대지 않는다 (작업지시서 §9-3 · 2026-09-13 사장님 결재)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-M4. ①단(정리)을 건너뛰어 중복이 남아도 — ⓐ마이그가 실패하지 않고 ⓑ방어선이 생기지 않으며 ⓒ기록이 1줄 남는다.</b>
    /// <para>
    /// <b>옛 단언(1·2차)</b>: 「①단을 건너뛰면 UNIQUE 생성이 <b>실패한다</b>」 — (다) 무조건 UNIQUE 를 전제로 한 순서 시험이었다.
    /// (가) 채택으로 ③단이 중복을 보고 <b>스스로 건너뛰므로</b> 순서 의존 자체가 사라졌다.
    /// 이것은 단언을 무른 것이 아니라 <b>재는 대상이 바뀐 것</b>이다(결재 §9-3).
    /// </para>
    /// <para>
    /// 🔴 <b>ⓐⓑⓒ 를 한 시험에 전부 건다</b> — ⓐ만 보면 「UNIQUE 를 걸다 조용히 실패」를, ⓑ만 보면 「기록 누락」을 놓친다.
    /// V4 는 ①단이 <b>규칙대로 무처리</b>한 모양(승인 표식 0줄)이고, 이 시험은 <b>①단 자체가 없는</b> 모양(승인 1 + 폐기 1)이다 — 겹치지 않는다.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-M4 🔴 정리를 건너뛰어 중복이 남아도 마이그는 멈추지 않고 · UNIQUE 는 안 생기며 · 기록이 1줄 남는다")]
    public async Task GM4_정리를_건너뛰어도_마이그는_멈추지_않고_기록이_남는다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM4_정리를_건너뛰어도_마이그는_멈추지_않고_기록이_남는다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        await InsertServerRowAsync(db, "srv-4", "revoked", mark: true, revokedStamps: true);
        await InsertBrowserRowAsync(db, "own-4", "approved", mark: true);
        Assert.Equal(2, await MarkCountAsync(db));
        var srvBefore = (await ReadRowAsync(db, "srv-4"))!;

        var stage23 = Db120Stage23Only();

        var ex = await Record.ExceptionAsync(() => RunMigrationSqlAsync(stage23));

        // ⓐ 마이그가 실패하지 않는다 — 실패하면 러너가 return 해 그 고객의 이후 마이그가 전부 멈춘다.
        Assert.True(ex is null,
            "🔴 **ⓐ 위반 — ①단 없이 ②③④단을 돌렸더니 마이그가 실패했다.**\n"
          + $"  실제 오류: {ex?.Message}\n"
          + "  ⇒ ③단이 중복을 보고 스스로 건너뛰지 못했다(D-1 조건 `@dup_tenants = 0` 누락 의심).\n"
          + "  ⇒ `MigrationRunner.cs:143-162` 는 실패 시 `return` — 그 고객은 다음 업데이트마다 같은 자리에서 멈춘다.");

        // ⓑ 방어선이 생기지 않는다 — 중복이 남은 표에 UNIQUE 가 걸렸다면 조건부가 거르지 않은 것이다.
        var idx = await IndexExistsAsync(db);
        Assert.True(idx == 0,
            $"🔴 **ⓑ 위반 — 표식이 2줄 남았는데 `uq_tenant_main_pc` 가 생겼다(idx_exists={idx}).**\n"
          + "  ⇒ ⓐ 가 통과한 이유가 「조건부가 걸렀다」가 아니라 다른 무엇이다.");

        // ⓒ 건너뛴 사실이 그 고객 DB 안에 1줄 남는다 (D-3).
        var audit = await DupAuditCountAsync(db);
        Assert.True(audit == 1,
            $"🔴 **ⓒ 위반 — 건너뛴 기록(`db120_mainpc_dup`)이 {audit}줄이다(1줄이어야 한다).**\n"
          + "  ⇒ 방어선 없이 남은 회사를 다음 사람이 찾을 방법이 없다.");

        // 곁 단언 — 무처리가 무처리로 남는다 · 부활 0 · 원문 보존 · D-2 컬럼 유지
        Assert.Equal(2, await MarkCountAsync(db));
        var srvAfter = (await ReadRowAsync(db, "srv-4"))!;
        Assert.Equal("revoked", srvAfter.Status);
        Assert.Equal(srvBefore.RevokedReason, srvAfter.RevokedReason);
        Assert.Equal(1, await ColumnExistsAsync(db));
    }

    // ══════════════════════════════════════════════════════════════
    // G-M5 — DB 가 2줄을 물리적으로 거부한다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-M5. <c>DB-120</c> 적용 뒤에는 손으로도 2줄을 만들 수 없다.</b>
    /// <para>코드를 되돌려도 DB 가 거부한다 — 방어선을 화면·서비스에만 두면 입구가 늘 때마다 다시 뚫린다(DB-89 머리말).</para>
    /// </summary>
    [Fact(DisplayName = "G-M5 🔴 DB-120 적용 뒤 두 번째 표식은 DB 가 거부한다")]
    public async Task GM5_UNIQUE가_2줄을_거부한다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM5_UNIQUE가_2줄을_거부한다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        await InsertBrowserRowAsync(db, "own-5", "approved", mark: true);
        await InsertServerRowAsync(db, "srv-5", "approved", mark: false);

        await RunMigrationSqlAsync(Db120Sql());
        Assert.Equal(1, await IndexExistsAsync(db));

        var ex = await Record.ExceptionAsync(() => db.ExecuteAsync(
            "UPDATE tenant_devices SET is_main_pc = 1 WHERE device_id = 'srv-5'"));

        Assert.True(ex is not null,
            "🔴 DB 가 두 번째 메인PC 표식을 **받아 줬다** — UNIQUE 가 실제로 걸리지 않았다. "
          + "코드에 구멍이 하나 생기면 그대로 2줄이 된다(이번 사고 그 자체).");
        Assert.Equal(1, await MarkCountAsync(db));
    }

    // ══════════════════════════════════════════════════════════════
    // G-M6 — 신규설치(출하 DDL)도 태어날 때부터 방어선을 갖는다 (#36)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-M6. 출하 DDL <b>만</b> 적재한 새 DB 에 방어선이 이미 있다.</b>
    /// <para>🔴 <b>이것이 DB-89 가 죽은 자리다</b> — 시드에만 넣고 본문에 방어선을 안 실어서
    /// 신규설치 고객은 <c>DB-89</c> 를 <b>영원히 건너뛴다</b>(<c>clean-ddl</c>·success=1).
    /// 시드와 본문 <b>둘 다</b> 있어야 한다.</para>
    /// </summary>
    [Fact(DisplayName = "G-M6 🔴 출하 DDL 만으로도 main_pc_key·uq_tenant_main_pc·DB-120 시드가 있다")]
    public async Task GM6_출하DDL이_방어선을_품는다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM6_출하DDL이_방어선을_품는다)); return; }
        SetUpFreshInstall();   // 🔴 옛 모양으로 되돌리지 않는다 — 신규설치 그대로를 본다

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        Assert.Equal(1, await ColumnExistsAsync(db));
        Assert.Equal(1, await IndexExistsAsync(db));

        var seeded = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='DB-120' AND app_version='clean-ddl' AND success=1");
        Assert.True(seeded == 1,
            "🔴 출하 DDL 시드에 ('DB-120','clean-ddl',1) 이 없다 — `ddl-smoke` 게이트 5(파일 수 == 시드 수)가 "
          + "빨간불이 되고, 신규 고객 자동업데이트가 전량 차단된다.");

        // 🔴 ddl-smoke 게이트 5 와 같은 산수를 여기서도 잰다 — 한쪽만 갱신하는 사고를 막는다.
        var migDir = Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL");
        var fileIds = Directory.GetFiles(migDir, "DB-*.sql")
            .Select(Path.GetFileName)
            .Select(n => System.Text.RegularExpressions.Regex.Match(n!, @"^DB-(\d+)([a-zA-Z]*)_"))
            .Where(m => m.Success)
            .Select(m => $"DB-{int.Parse(m.Groups[1].Value):D2}{m.Groups[2].Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var seedCount = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM schema_migrations WHERE success=1");

        Assert.True(fileIds == seedCount,
            $"🔴 소스 마이그 파일({fileIds}) != 출하 DDL 시드 success=1({seedCount}) — "
          + "어긋나면 신규 고객 자동업데이트가 전량 차단된다(20260722 실측 사고).");
    }

    // ══════════════════════════════════════════════════════════════
    // G-M7 — 🟢 대조군(요금): 계수가 움직이지 않는다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🟢 <b>G-M7(대조군). 정리 전후 <c>status='approved' ∧ device_type='pc'</c> 줄 수가 같다.</b>
    /// <para>🔴 DB-89 꼬리 UPDATE(폐기된 메인PC 를 <c>approved</c> 로 부활)를 DB-120 에 넣으면
    /// 이 숫자가 <b>4 → 5</b> 로 늘어 <b>고객 요금이 움직인다.</b> 그래서 그 UPDATE 를 싣지 않는다.</para>
    /// </summary>
    [Fact(DisplayName = "G-M7 🟢 대조군(요금) — 정리 전후 승인 PC 줄 수가 같고 부활이 0 이다")]
    public async Task GM7_대조군_요금이_움직이지_않는다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM7_대조군_요금이_움직이지_않는다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        // 실물 모양: 승인 PC 4줄(그중 ⓐ 가 표식) + 폐기 서버줄 1줄(표식 보유) ⇒ 화면 PC 4/5
        await InsertBrowserRowAsync(db, "own-7", "approved", mark: true);
        await InsertBrowserRowAsync(db, "pc-7b", "approved", mark: false, registeredAt: "2026-09-01 00:00:00");
        await InsertBrowserRowAsync(db, "pc-7c", "approved", mark: false, registeredAt: "2026-09-02 00:00:00");
        await InsertBrowserRowAsync(db, "pc-7d", "approved", mark: false, registeredAt: "2026-09-03 00:00:00");
        await InsertServerRowAsync(db, "srv-7", "revoked", mark: true, revokedStamps: true);

        var before = await ApprovedPcCountAsync(db);
        Assert.Equal(4, before);

        await RunMigrationSqlAsync(Db120Sql());

        var after = await ApprovedPcCountAsync(db);
        Assert.True(before == after,
            $"🔴 요금 계수가 움직였다 ({before} → {after}). 폐기 줄을 `approved` 로 부활시키면 "
          + "고객이 **쓰지도 않는 자리에 돈을 낸다**(DB-89 꼬리 UPDATE 를 실었다는 뜻). "
          + "DB-120 은 `is_main_pc` 한 컬럼만 쓴다.");

        var srv = await ReadRowAsync(db, "srv-7");
        Assert.Equal("revoked", srv!.Status);
    }

    // ══════════════════════════════════════════════════════════════
    // 변형 — 같은 게이트 안에 넣는다 (§6-2 마지막 줄)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>V1. 표식 3줄 · <c>approved</c> 2줄</b> — <c>approved</c> 중 <c>last_seen_at</c> 최신 1줄만 남는다.
    /// </summary>
    [Fact(DisplayName = "V1 🔴 표식 3줄·승인 2줄 — 승인 중 최근 접속 1줄만 남는다")]
    public async Task V1_표식3줄_승인2줄()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(V1_표식3줄_승인2줄)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        await InsertBrowserRowAsync(db, "v1-old", "approved", mark: true, lastSeen: "2026-09-01 00:00:00");
        await InsertBrowserRowAsync(db, "v1-new", "approved", mark: true, lastSeen: "2026-09-13 15:18:21");
        await InsertServerRowAsync(db, "v1-rev", "revoked", mark: true, revokedStamps: true);
        Assert.Equal(3, await MarkCountAsync(db));

        await RunMigrationSqlAsync(Db120Sql());

        Assert.Equal(1, await MarkCountAsync(db));
        var kept = await ReadRowAsync(db, "v1-new");
        Assert.True(kept!.IsMainPc,
            "🔴 승인 줄 중 **최근 접속이 가장 최신인** 줄이 남아야 한다 — 「그 컴퓨터에서 히트판이 돈다」는 사실 축.");
    }

    /// <summary>
    /// 🔴 <b>V2 (조건 C-2). 폐기 줄의 <c>last_seen_at</c> 이 승인 줄보다 <b>최신</b>이어도 승인 줄이 남는다.</b>
    /// <para>🔴 <b>이것이 PM 이 추가로 반증한 자리다</b> — <c>last_seen_at</c> 은 <b>되올림 UPDATE 가
    /// <c>NOW()</c> 로 덮는 값</b>이다(실물 ⓑ 의 <c>9/11 23:05</c> 이 그 흔적). 따라서 <b>폐기 줄이 「최신」이 될 수 있다.</b>
    /// 규칙이 <c>approved</c> 로 <b>먼저</b> 거르므로 안전하지만, 이 순서가 뒤집히면 <b>사장님이 갇힌다.</b></para>
    /// </summary>
    [Fact(DisplayName = "V2 🔴 C-2 — 폐기 줄이 더 최근에 접속돼 있어도 승인 줄이 남는다")]
    public async Task V2_C2_폐기줄이_더_최신이어도_승인줄이_남는다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(V2_C2_폐기줄이_더_최신이어도_승인줄이_남는다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        // 🔴 폐기 줄이 **더 최신**이다 — 되올림이 NOW() 로 덮었기 때문에 실제로 이런 값이 나온다.
        await InsertBrowserRowAsync(db, "v2-app", "approved", mark: true, lastSeen: "2026-09-11 18:00:00");
        await InsertServerRowAsync(db, "v2-rev", "revoked", mark: true,
            lastSeen: "2026-09-30 23:59:59", revokedStamps: true);

        await RunMigrationSqlAsync(Db120Sql());

        var app = await ReadRowAsync(db, "v2-app");
        var rev = await ReadRowAsync(db, "v2-rev");

        Assert.True(app!.IsMainPc,
            "🔴 **폐기 줄이 진짜로 선택됐다** — 사장님이 갇힌다(8/11·8/16·8/18 3회 재발한 그 모양). "
          + "`last_seen_at` 은 되올림이 NOW() 로 덮는 값이라 폐기 줄이 최신일 수 있다 ⇒ "
          + "`approved` 로 **먼저** 걸러야 한다. 그 순서가 뒤집혔다.");
        Assert.False(rev!.IsMainPc, "🔴 폐기 줄이 표식을 들고 있다.");
        Assert.Equal(1, await MarkCountAsync(db));
    }

    /// <summary>
    /// 🔴 <b>V3. <c>last_seen_at</c> 동값·NULL 에서도 <b>결정적</b>으로 1줄을 고른다</b>(사람·랜덤 판정 금지).
    /// </summary>
    [Fact(DisplayName = "V3 🔴 최근 접속이 동값·NULL 이어도 결정적으로 1줄만 남는다")]
    public async Task V3_동값과_NULL에서도_결정적()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(V3_동값과_NULL에서도_결정적)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        // 동값 2줄 + 접속기록 없는 줄 1줄. 규칙: 접속기록 있는 줄 우선 → last_seen DESC → registered_at DESC → device_id ASC
        await InsertBrowserRowAsync(db, "v3-tie-a", "approved", mark: true,
            lastSeen: "2026-09-05 10:00:00", registeredAt: "2026-09-01 00:00:00");
        await InsertBrowserRowAsync(db, "v3-tie-b", "approved", mark: true,
            lastSeen: "2026-09-05 10:00:00", registeredAt: "2026-09-02 00:00:00");
        await InsertBrowserRowAsync(db, "v3-null", "approved", mark: true,
            lastSeen: null, registeredAt: "2026-09-03 00:00:00");

        await RunMigrationSqlAsync(Db120Sql());

        Assert.Equal(1, await MarkCountAsync(db));

        var nullRow = await ReadRowAsync(db, "v3-null");
        Assert.False(nullRow!.IsMainPc,
            "🔴 접속기록이 **없는** 줄이 메인PC 로 남았다 — 「그 컴퓨터에서 히트판이 돈다」는 근거가 없는 줄이다.");

        var tieB = await ReadRowAsync(db, "v3-tie-b");
        Assert.True(tieB!.IsMainPc,
            "🔴 동값에서 고르는 규칙이 결정적이지 않다 — 같은 입력에 같은 결과가 나오지 않으면 "
          + "고객마다 다른 줄이 남고, 무엇이 남았는지 아무도 예측할 수 없다. "
          + "규칙: last_seen_at 동값이면 registered_at 이 더 최신인 줄.");

        // 🔴 두 번 돌려도 같은 결과여야 한다(멱등 — 검증팀 독립반증 ⓑ)
        await RunMigrationSqlAsync(Db120Sql());
        Assert.Equal(1, await MarkCountAsync(db));
        Assert.True((await ReadRowAsync(db, "v3-tie-b"))!.IsMainPc, "🔴 두 번째 실행이 결과를 바꿨다 — 멱등이 아니다.");
    }

    /// <summary>
    /// 🔴 <b>V4. <c>approved</c> 표식 줄이 0개면 <b>아무것도 건드리지 않는다</b></b>(K3 결재).
    /// <para>표식을 0줄로 만들면 CS 가 메인PC 를 식별할 수 없고 <c>!isMainPc</c> 구제도 사라진다 ⇒
    /// <b>사람이 판정할 자리</b>로 남긴다.</para>
    /// <para>
    /// 🔴 <b>그리고 이 줄기가 ③단 UNIQUE 와 충돌하는지를 같이 잰다</b> — 정리가 무처리로 끝나면
    /// 중복이 남고, 그 상태에서 UNIQUE 를 걸면 <b>마이그가 실패해 그 고객의 업데이트가 멈춘다</b>
    /// (<c>MigrationRunner.cs:143-162</c> 는 실패 시 <c>return</c> 이라 이후 마이그 전부 중단).
    /// </para>
    /// <para>
    /// 🔴 <b>2026-09-13 결재 (가) 채택 — 이 게이트의 단언은 한 글자도 바뀌지 않았다.</b>
    /// 1차에서 이 시험은 <b>빨간불</b>이었고(③단 무조건 UNIQUE), 초록으로 만든 것은 <b>구현 수정</b>이다
    /// (<c>DB-120</c> ③단을 조건부로 · ④단 감사기록 1줄). 아래 <b>①②</b> 단언과 그 실패 메시지 원문은
    /// <b>1차 그대로</b>이고, 그 위에 <b>조이는 단언만</b> 더했다 —
    /// 「초록으로 만들려고 단언을 무르는 것」은 거짓봉합으로 명시 반려됐다(결재문 §4).
    /// </para>
    /// </summary>
    [Fact(DisplayName = "V4 🔴 승인 표식이 0줄이면 무처리 — 그리고 그 고객도 마이그가 멈추지 않는다")]
    public async Task V4_승인표식_0줄이면_무처리()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(V4_승인표식_0줄이면_무처리)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        // 표식 2줄인데 둘 다 approved 가 아니다 (폐기 + 대기)
        await InsertServerRowAsync(db, "v4-rev", "revoked", mark: true, revokedStamps: true);
        await InsertBrowserRowAsync(db, "v4-pend", "pending", mark: true);
        Assert.Equal(2, await MarkCountAsync(db));

        var ex = await Record.ExceptionAsync(() => RunMigrationSqlAsync(Db120Sql()));

        // ① 무처리 — 표식을 사람 대신 고르지 않는다 (K3)
        Assert.Equal(2, await MarkCountAsync(db));
        Assert.True((await ReadRowAsync(db, "v4-rev"))!.Status == "revoked", "폐기 상태가 바뀌었다 — 부활 0 이어야 한다.");

        // ② 🔴 그러나 마이그 자체가 실패해서는 안 된다 — 실패하면 그 고객의 업데이트가 영구히 멈춘다.
        Assert.True(ex is null,
            "🔴 **`approved` 표식이 0줄인 고객에게서 DB-120 이 실패했다.**\n"
          + $"  실제 오류: {ex?.Message}\n"
          + "  ⇒ K3(무처리)과 ③단(무조건 UNIQUE)이 **서로 충돌한다**: 정리가 중복을 남기는데 UNIQUE 를 걸면 중복 키로 터진다.\n"
          + "  ⇒ `MigrationRunner.cs:143-162` 은 실패 시 `return` 이라 **이후 마이그가 전부 중단**되고,\n"
          + "     success=0 으로 기록돼 다음 업데이트마다 같은 자리에서 또 실패한다(영구 정지).\n"
          + "  ⇒ 이것이 [CTO-2] 초안 ②(**중복 0 일 때만 UNIQUE**)와 이번 설계(**①정리 후 ③UNIQUE**)의\n"
          + "     접근 차이가 실제로 갈리는 자리다 — [5] CTO 결재 안건(조건 C-4).");

        // ══════════════════════════════════════════════════════════
        // 🔴 여기까지 위 ①② 는 **한 글자도 무르지 않았다** (CTO 결재문 §4 · 조건 D-4).
        //    (가) 가 옳다는 증거는 「단언을 고쳐서 초록이 됐다」가 아니라
        //    「**단언을 그대로 두고** 초록이 됐다」는 사실 자체다.
        //    위 실패 메시지 원문도 그대로 둔다 — 1차가 무엇에 걸렸는지 남는 기록이다.
        //    아래는 **조이는 단언만** 더한 것이다.
        // ══════════════════════════════════════════════════════════

        // ③ UNIQUE 가 **안** 생겼다 (D-1) — 조건부가 조용히 걸어버리지 않았음을 동작으로 못 박는다.
        var idxAfter = await IndexExistsAsync(db);
        Assert.True(idxAfter == 0,
            $"🔴 **UNIQUE 가 생겼다(idx_exists={idxAfter})** — 표식이 2줄 남은 회사에서 ③단이 걸렸다는 뜻이다.\n"
          + "  그렇다면 위 ② 단언이 통과한 이유는 「조건부가 걸렀다」가 아니라 다른 무엇이고,\n"
          + "  실제 고객 DB 에서는 같은 자리에서 `Duplicate entry` 로 마이그가 멈춘다(결재 (가) 위반).");

        // ④ ②단 생성컬럼은 **무조건 유지**된다 (D-2).
        var colAfter = await ColumnExistsAsync(db);
        Assert.True(colAfter == 1,
            $"🔴 **`main_pc_key` 가 없다(col_exists={colAfter})** — D-2 위반. UNIQUE 를 건너뛴 회사에도\n"
          + "  이 컬럼은 남아야 한다 — 유일성이 없어 중복 DB 에서도 무해하고, 다음 재정합(DB-122+)의 **발판**이다.");

        // ⑤ 건너뛴 사실이 **그 고객 DB 안에** 1줄 남았다 (D-3).
        var audit1 = await DupAuditCountAsync(db);
        Assert.True(audit1 == 1,
            $"🔴 **건너뛴 사실이 고객 DB 에 남지 않았다(`db120_mainpc_dup` 줄 수={audit1})** — D-3 위반.\n"
          + "  이 줄이 없으면 「방어선 없이 남은 회사」를 다음 사람이 **찾을 방법이 없다** — 문서에만 있으면 없는 것이다.");

        // ⑥ 🔴 2회 실행해도 1줄 (멱등). 러너는 성공 뒤 skip 하지만 복원·재설치·재정합 경로에서 다시 돌 수 있다.
        var ex2 = await Record.ExceptionAsync(() => RunMigrationSqlAsync(Db120Sql()));
        Assert.True(ex2 is null, $"🔴 두 번째 실행이 실패했다 — 멱등이 아니다: {ex2?.Message}");

        var audit2 = await DupAuditCountAsync(db);
        Assert.True(audit2 == 1,
            $"🔴 **2회 실행 뒤 감사기록이 {audit2}줄이다** — 멱등이 아니다. 같은 사실이 쌓이면 "
          + "감사표를 읽어 대상을 찾는 재정합이 **무엇이 몇 건인지 셀 수 없게** 된다.");

        // 2회 돌려도 표식은 그대로 2줄이고 UNIQUE 도 여전히 없다 — 무처리가 무처리로 남는다.
        Assert.Equal(2, await MarkCountAsync(db));
        Assert.Equal(0, await IndexExistsAsync(db));
    }

    /// <summary>
    /// 🔴 <b>V5. 표식이 0줄인 회사는 <c>DB-120</c> 이 아무것도 바꾸지 않는다</b>(정상 고객 무영향).
    /// </summary>
    [Fact(DisplayName = "V5 🟢 대조군 — 표식 1줄인 정상 회사는 DB-120 이 건드리지 않는다")]
    public async Task V5_대조군_정상회사_무영향()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(V5_대조군_정상회사_무영향)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);

        await InsertBrowserRowAsync(db, "v5-main", "approved", mark: true);
        await InsertBrowserRowAsync(db, "v5-other", "approved", mark: false, registeredAt: "2026-09-02 00:00:00");

        await RunMigrationSqlAsync(Db120Sql());

        Assert.Equal(1, await MarkCountAsync(db));
        Assert.True((await ReadRowAsync(db, "v5-main"))!.IsMainPc,
            "🔴 정상 회사(표식 1줄)의 표식이 내려갔다 — 정리가 과하게 돌았다.");
        Assert.Equal(2, await ApprovedPcCountAsync(db));

        // 🔴 §9-4 함께 봉합 — 정상 회사에는 「건너뜀」 기록이 0줄이다(거짓 기록 대조).
        Assert.True(await DupAuditCountAsync(db) == 0,
            "🔴 정상 회사에 `db120_mainpc_dup` 기록이 남았다 — 고객 [로그] 화면에 없는 문제가 뜬다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-M8 — 🔴 표식 내린 서버줄로는 브라우저 로그인이 표를 못 바꾼다
    //   (작업지시서 §9-4 · 병렬이슈33 · 사장님 결재 (a) 2026-09-14)
    // ══════════════════════════════════════════════════════════════

    private const string GuardMessage = HitPan.Application.DTOs.Device.DeviceMessages.StaleServerRow;

    /// <summary>🔴 실제 서비스 — 승인제를 <b>켠 채로</b>(선례 <c>MainPcLocalConsoleGateTests.NewService</c>).</summary>
    private static HitPan.Application.Services.TenantDeviceService NewDeviceService(MySqlConnection db)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceApproval:Enabled"] = "true" })
            .Build();
        return new HitPan.Application.Services.TenantDeviceService(
            db, new NoOpAudit(), config, NullLogger<HitPan.Application.Services.TenantDeviceService>.Instance);
    }

    private sealed class NoOpAudit : HitPan.Application.Interfaces.IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>합류 화면의 로그인 — 브라우저 지문은 자기 것, <b>장비넘버는 서버줄 것</b>(DeviceAuthGate 합류 2-1).</summary>
    private static HitPan.Application.DTOs.Device.RegisterDeviceRequest JoinedLogin(string deviceId, bool local) => new()
    {
        Fingerprint = "HFPv2-joined0000000001",
        DeviceId = deviceId,
        DeviceType = "pc",
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/128.0 Safari/537.36 Edg/128.0",
        IsLocalConsole = local
    };

    private sealed record Snap(string SrvStatus, bool SrvMark, string? SrvReason, string OwnStatus, bool OwnMark, long Approved);

    private static async Task<Snap> SnapAsync(MySqlConnection db, string srv, string own)
    {
        var s = (await ReadRowAsync(db, srv))!;
        var o = (await ReadRowAsync(db, own))!;
        return new Snap(s.Status, s.IsMainPc, s.RevokedReason, o.Status, o.IsMainPc, await ApprovedPcCountAsync(db));
    }

    /// <summary>
    /// 🟢 <b>G-M8a 대조군 — DB-120 적용 전</b>(표식 든 폐기 서버줄 + 사장님 줄)에 합류 화면이 로그인해도 표는 그대로다.
    /// <para>이것이 실물의 지금 모양이고, 가드가 <b>만들어야 하는 기대값</b>이다. 여기가 깨지면 가드가 아니라 다른 것이 움직인 것이다.</para>
    /// </summary>
    [Theory(DisplayName = "G-M8a 🟢 대조군 — DB-120 전에는 합류 화면 로그인이 표를 안 바꾼다")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GM8a_대조군_DB120전_무변화(bool local)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8a_대조군_DB120전_무변화)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);
        await InsertServerRowAsync(db, "m8-srv", "revoked", mark: true, revokedStamps: true);
        await InsertBrowserRowAsync(db, "m8-own", "approved", mark: true);

        var before = await SnapAsync(db, "m8-srv", "m8-own");
        await NewDeviceService(db).RegisterOrRefreshAsync(TenantId, UserId, JoinedLogin("m8-srv", local), "127.0.0.1");
        Assert.Equal(before, await SnapAsync(db, "m8-srv", "m8-own"));
    }

    /// <summary>
    /// 🔴 <b>G-M8b. DB-120 이 서버줄 표식을 내린 뒤</b> — 합류 화면 로그인은 거부되고 <b>표는 그대로</b>이며,
    /// 사장님 줄이 이어 로그인하고 다시 합류 화면이 와도 <b>핑퐁 0</b>.
    /// </summary>
    [Theory(DisplayName = "G-M8b 🔴 표식 내린 폐기 서버줄로 로그인 — 거부 · 부활 0 · 사장님 줄 유지 · 핑퐁 0")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GM8b_폐기서버줄_로그인_거부_핑퐁0(bool local)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8b_폐기서버줄_로그인_거부_핑퐁0)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakeLegacyShapeAsync(db);
        await InsertServerRowAsync(db, "m8-srv", "revoked", mark: true, revokedStamps: true);
        await InsertBrowserRowAsync(db, "m8-own", "approved", mark: true);
        await RunMigrationSqlAsync(Db120Sql());

        var afterMig = await SnapAsync(db, "m8-srv", "m8-own");
        Assert.True(!afterMig.SrvMark && afterMig.OwnMark, "전제 — DB-120 이 서버줄 표식을 내리고 사장님 줄을 남겨야 한다.");

        var svc = NewDeviceService(db);
        var (allowed, reason, deviceId, _) = await svc.RegisterOrRefreshAsync(TenantId, UserId, JoinedLogin("m8-srv", local), "127.0.0.1");

        // 표가 먼저다 — 반환값보다 표가 사실이다
        var s1 = await SnapAsync(db, "m8-srv", "m8-own");
        Assert.True(s1 == afterMig,
            $"🔴 **합류 화면 로그인이 표를 바꿨다**(local={local}) — 전: {afterMig} / 후: {s1}\n"
          + "  localhost 면 서버줄 부활·사장님 줄 폐기(결재 5 위반), 원격이면 대기 전환·사유 소거(병렬이슈33).");
        Assert.False(allowed);
        Assert.Equal(GuardMessage, reason);
        Assert.Equal("m8-srv", deviceId);   // null 이면 401 — 그 화면이 관문에도 못 간다

        // 사장님 줄 로그인 → 다시 합류 화면 로그인: 서로 뒤집지 않는다
        var ownLogin = JoinedLogin("m8-own", local);
        ownLogin.Fingerprint = "HFPv2-owner00000000001";
        await svc.RegisterOrRefreshAsync(TenantId, UserId, ownLogin, "127.0.0.1");
        await svc.RegisterOrRefreshAsync(TenantId, UserId, JoinedLogin("m8-srv", local), "127.0.0.1");

        var s2 = await SnapAsync(db, "m8-srv", "m8-own");
        Assert.True(s2.SrvStatus == "revoked" && !s2.SrvMark && s2.SrvReason == RevokedReason && s2.OwnStatus == "approved" && s2.OwnMark,
            $"🔴 **핑퐁** — 사장님 줄과 서버줄이 로그인마다 뒤집힌다: {s2}");
        Assert.Equal(afterMig.Approved, s2.Approved);
    }

    /// <summary>
    /// 🔴 <b>G-M8c. 승인 상태인 표식 없는 서버줄</b>(①단이 승인 2줄 중 하나를 내린 모양 · V1)로 localhost 로그인 —
    /// 표식이 서버줄로 <b>옮겨가지 않는다</b>(가드 ②). 사장님 줄 유지.
    /// </summary>
    [Fact(DisplayName = "G-M8c 🔴 표식 없는 승인 서버줄로 localhost 로그인 — 표식 이전 0 · 사장님 줄 유지")]
    public async Task GM8c_승인서버줄_표식이전0()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8c_승인서버줄_표식이전0)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertServerRowAsync(db, "m8-srv", "approved", mark: false);
        await InsertBrowserRowAsync(db, "m8-own", "approved", mark: true);

        await NewDeviceService(db).RegisterOrRefreshAsync(TenantId, UserId, JoinedLogin("m8-srv", local: true), "127.0.0.1");

        var s = await SnapAsync(db, "m8-srv", "m8-own");
        Assert.True(!s.SrvMark && s.SrvStatus == "approved" && s.OwnMark && s.OwnStatus == "approved",
            $"🔴 **표식이 서버줄로 옮겨갔다** — `:269` 표식 이전이 서버줄에 돌았다: {s}\n"
          + "  사장님 줄이 폐기되고 다음 로그인에 되뒤집힌다(병렬이슈33 · 가드 ②).");
    }

    /// <summary>
    /// 🔴 <b>G-M8e. 반려(rejected) 상태인 표식 없는 서버줄</b>로 합류 화면 로그인 — 거부 · 표 무변화(대기 전환 0).
    /// <para>[4] 재검증 적발(병렬이슈34 ①): 가드 ①에서 <c>|| status == "rejected"</c> 만 지워도 G-M8a~d 는 초록이었고
    /// 서버줄이 <c>rejected → pending</c> 으로 바뀌었다. 반려 갈래를 따로 잰다.</para>
    /// </summary>
    [Theory(DisplayName = "G-M8e 🔴 표식 없는 반려 서버줄로 로그인 — 거부 · 대기 전환 0")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GM8e_반려서버줄_로그인_거부(bool local)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8e_반려서버줄_로그인_거부)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertServerRowAsync(db, "m8-srv", "rejected", mark: false);
        await InsertBrowserRowAsync(db, "m8-own", "approved", mark: true);

        var before = await SnapAsync(db, "m8-srv", "m8-own");
        var (allowed, reason, deviceId, _) = await NewDeviceService(db).RegisterOrRefreshAsync(
            TenantId, UserId, JoinedLogin("m8-srv", local), "127.0.0.1");

        var after = await SnapAsync(db, "m8-srv", "m8-own");
        Assert.True(after == before,
            $"🔴 **반려 서버줄이 합류 화면 로그인으로 바뀌었다**(local={local}) — 전: {before} / 후: {after}\n"
          + "  반려 → 대기 전환이면 대표가 승인할 때 슬롯 +1(병렬이슈34).");
        Assert.False(allowed);
        Assert.Equal(GuardMessage, reason);
        Assert.Equal("m8-srv", deviceId);
    }

    /// <summary>
    /// 🟢 <b>G-M8d 대조군 — 표식 든 서버줄</b>은 가드에 안 걸린다(8/11 폐기 구제 축 보존).
    /// </summary>
    [Fact(DisplayName = "G-M8d 🟢 대조군 — 표식 든 폐기 서버줄은 가드 문구로 막히지 않는다(8/11 구제 보존)")]
    public async Task GM8d_대조군_표식든서버줄_가드무관()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8d_대조군_표식든서버줄_가드무관)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertServerRowAsync(db, "m8-srv", "revoked", mark: true, revokedStamps: true);

        var (_, reason, _, _) = await NewDeviceService(db).RegisterOrRefreshAsync(
            TenantId, UserId, JoinedLogin("m8-srv", local: true), "127.0.0.1");

        Assert.NotEqual(GuardMessage, reason);
    }

    // ══════════════════════════════════════════════════════════════
    // G-M8f~h — 🔴 표식 없는 서버줄은 승인되지 않는다 (작업지시서 §9-5 · 병렬이슈34 ③)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-M8f. 대기·반려 상태의 표식 없는 서버줄 승인 → 거부 · 승인 PC 수 불변.</b>
    /// <para>[4] 재검증 실측: 승인하면 같은 컴퓨터가 슬롯을 하나 더 먹었다(승인 PC 1→2 · 요금 이동).</para>
    /// </summary>
    [Theory(DisplayName = "G-M8f 🔴 표식 없는 서버줄(대기·반려) 승인 — 거부 · 슬롯 무이동")]
    [InlineData("pending")]
    [InlineData("rejected")]
    public async Task GM8f_표식없는_서버줄_승인거부(string status)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8f_표식없는_서버줄_승인거부)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertServerRowAsync(db, "m8-srv", status, mark: false);
        await InsertBrowserRowAsync(db, "m8-own", "approved", mark: true);

        var approvedBefore = await ApprovedPcCountAsync(db);
        var ex = await Record.ExceptionAsync(() => NewDeviceService(db).ApproveAsync("m8-srv", TenantId, UserId));

        var srv = (await ReadRowAsync(db, "m8-srv"))!;
        Assert.True(srv.Status == status && await ApprovedPcCountAsync(db) == approvedBefore,
            $"🔴 **표식 없는 서버줄({status})이 승인됐다** — 상태 {srv.Status} · 승인 PC {approvedBefore}→{await ApprovedPcCountAsync(db)}\n"
          + "  같은 컴퓨터가 슬롯을 하나 더 먹는다(병렬이슈34 ③).");
        Assert.True(ex is InvalidOperationException, $"승인 거부 예외가 아니다: {ex?.GetType().Name} {ex?.Message}");
        Assert.Equal(HitPan.Application.DTOs.Device.DeviceMessages.StaleServerRowApprove, ex!.Message);
    }

    /// <summary>
    /// 🔴 <b>G-M8i. 표식 없는 서버줄은 인증키 재발급·키 대조로도 승인되지 않는다</b>(병렬이슈35 우회로).
    /// <para>검증자 탐침: 재발급 → 키 대조로 대기 서버줄이 approved · 승인 PC 1→2.</para>
    /// </summary>
    [Theory(DisplayName = "G-M8i 🔴 표식 없는 서버줄 — 재발급 거부 · 키 대조 거부 · 슬롯 무이동")]
    [InlineData("pending")]
    [InlineData("rejected")]
    public async Task GM8i_재발급_키대조_우회차단(string status)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8i_재발급_키대조_우회차단)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertServerRowAsync(db, "m8-srv", status, mark: false);
        await InsertBrowserRowAsync(db, "m8-own", "approved", mark: true);
        var svc = NewDeviceService(db);
        var approvedBefore = await ApprovedPcCountAsync(db);

        var reissue = await Record.ExceptionAsync(() => svc.ReissueAuthKeyAsync("m8-srv", TenantId, UserId));
        Assert.True(reissue is InvalidOperationException, $"🔴 표식 없는 서버줄에 인증키가 재발급됐다: {reissue?.GetType().Name}");

        // 옛 키가 이미 심겨 있던 모양(8/20 합류 흔적) — 키 대조가 승인하면 안 된다
        await db.ExecuteAsync("UPDATE tenant_devices SET auth_key_hash = SHA2('old-join-key', 256) WHERE device_id = 'm8-srv'");
        var secret = await svc.VerifyAuthKeyAsync("old-join-key", TenantId, "m8-srv");

        var srv = (await ReadRowAsync(db, "m8-srv"))!;
        Assert.True(secret is null && srv.Status == status && await ApprovedPcCountAsync(db) == approvedBefore,
            $"🔴 **키 대조로 표식 없는 서버줄이 승인됐다** — 비밀 발급={(secret is not null)} · 상태 {srv.Status} · 승인 PC {approvedBefore}→{await ApprovedPcCountAsync(db)}");
    }

    /// <summary>🟢 <b>G-M8l 대조군 — 표식 든 서버줄(합류 대상)은 재발급·키 대조가 된다</b>(20260820작2 합류 보존).</summary>
    [Fact(DisplayName = "G-M8l 🟢 대조군 — 표식 든 서버줄 재발급·키 대조는 그대로 된다(합류 보존)")]
    public async Task GM8l_대조군_합류경로_보존()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8l_대조군_합류경로_보존)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertServerRowAsync(db, "m8-srv", "approved", mark: true);
        var svc = NewDeviceService(db);

        var key = await svc.ReissueAuthKeyAsync("m8-srv", TenantId, UserId);
        Assert.False(string.IsNullOrEmpty(key));
        var secret = await svc.VerifyAuthKeyAsync(key!, TenantId, "m8-srv");
        Assert.False(string.IsNullOrEmpty(secret), "🔴 표식 든 서버줄 합류가 막혔다 — 가드가 과하다(8/20작2).");
    }

    /// <summary>🔴 <b>G-M8k. 대기 상태 표식 없는 서버줄로 로그인</b> — 가드 사유(대표 알림 생략 표지) · 표 무변화.</summary>
    [Theory(DisplayName = "G-M8k 🔴 표식 없는 대기 서버줄로 로그인 — 가드 사유 · 표 무변화")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GM8k_대기서버줄_로그인(bool local)
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8k_대기서버줄_로그인)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertServerRowAsync(db, "m8-srv", "pending", mark: false);
        await InsertBrowserRowAsync(db, "m8-own", "approved", mark: true);

        var before = await SnapAsync(db, "m8-srv", "m8-own");
        var (_, reason, deviceId, _) = await NewDeviceService(db).RegisterOrRefreshAsync(
            TenantId, UserId, JoinedLogin("m8-srv", local), "127.0.0.1");

        Assert.Equal(before, await SnapAsync(db, "m8-srv", "m8-own"));
        Assert.True(reason == GuardMessage,
            $"🔴 대기 서버줄 로그인 사유가 가드 사유가 아니다('{reason}') — AuthController 가 대표에게 로그인마다 알림을 보낸다(병렬이슈35).");
        Assert.Equal("m8-srv", deviceId);
    }

    /// <summary>🟢 <b>G-M8g 대조군 — 일반 대기 브라우저 줄은 승인된다</b>(승인 가드가 과하지 않다).</summary>
    [Fact(DisplayName = "G-M8g 🟢 대조군 — 일반 대기 브라우저 줄 승인은 그대로 된다")]
    public async Task GM8g_대조군_일반기기_승인()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8g_대조군_일반기기_승인)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertBrowserRowAsync(db, "m8-main", "approved", mark: true);
        await InsertBrowserRowAsync(db, "m8-staff", "pending", mark: false, registeredAt: "2026-09-12 00:00:00");

        await NewDeviceService(db).ApproveAsync("m8-staff", TenantId, UserId);
        Assert.Equal("approved", (await ReadRowAsync(db, "m8-staff"))!.Status);
    }

    /// <summary>🔴 <b>G-M8h. 판정 메서드</b> — 서버줄 표식0 = 참 · 서버줄 표식1 = 거짓 · 브라우저 줄 = 거짓 · 없는 번호 = 거짓.</summary>
    [Fact(DisplayName = "G-M8h 🔴 IsServerRowWithoutMarkAsync 판정 — 서버줄 표식0 만 참")]
    public async Task GM8h_서버줄_판정()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GM8h_서버줄_판정)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertServerRowAsync(db, "m8-srv0", "revoked", mark: false, revokedStamps: true);
        await InsertBrowserRowAsync(db, "m8-own", "pending", mark: false);
        var svc = NewDeviceService(db);

        Assert.True(await svc.IsServerRowWithoutMarkAsync("m8-srv0", TenantId), "표식 없는 서버줄을 못 알아봤다.");
        Assert.False(await svc.IsServerRowWithoutMarkAsync("m8-own", TenantId), "브라우저 줄을 서버줄로 봤다.");
        Assert.False(await svc.IsServerRowWithoutMarkAsync("nope", TenantId));

        await db.ExecuteAsync("UPDATE tenant_devices SET is_main_pc=1, status='approved' WHERE device_id='m8-srv0'");
        Assert.False(await svc.IsServerRowWithoutMarkAsync("m8-srv0", TenantId), "표식 든 서버줄을 옛 서버줄로 봤다 — 8/11 구제가 막힌다.");
    }
}
