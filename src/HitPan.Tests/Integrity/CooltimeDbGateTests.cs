using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Dapper;
using HitPan.API.Services.Cooltime;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Auth;
using HitPan.Application.DTOs.Employee;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Domain.Common;
using HitPan.Domain.Entities;
using HitPan.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 쿨타임(설정 시각 자동 로그아웃 · PC 만) — <b>실제 DB</b> 게이트 G-C1 ~ G-C21 (G-C14 는 <c>.Pipeline.cs</c>).
/// </summary>
/// <remarks>
/// <para>근거: 설계 <c>docs/설계/erp/쿨타임_자동로그아웃_설계_20260928.md</c> §7 · §D · §E(「🆕 11/27」 문장) ·
/// 지시서 <c>docs/운영기록/20260928작4_쿨타임_자동로그아웃_작업지시서.md</c> §5 ·
/// 개발명세서 <c>docs/개발/erp/20261127_쿨타임1b_갈래3_게이트_개발명세서.md</c>(게이트 ↔ 시험 이름 표 · 대조군 변이 방법).</para>
/// <para>🔴 <b>판정하는 SQL 이 이 파일에 없다.</b> 판정은 전부 제품 공개 메서드
/// (<see cref="CooltimeEngine.RunOnceAsync"/> · <see cref="CooltimeEngine.CooltimeLogoutAsync"/> · <see cref="CooltimeEngine.ContinueAsync"/> ·
/// <see cref="HrService.CheckOutAsync"/> · <see cref="HrService.EditCheckOutAsync"/> · <see cref="SettingsService.SaveAsync"/> ·
/// <see cref="UserService.SuspendAsync"/> · <see cref="AuthService.LoginAsync"/>)가 한다. 이 파일의 SQL 은 상태를 <b>만드는</b> 준비와 <b>읽는</b> 확인뿐이다.</para>
/// <para>시각(설계 §7 머리 · §D-5): DB 시계는 주입할 수 없다 ⇒ 세션 시각(<c>login_at</c>·<c>cooltime_asked_at</c>·<c>cooltime_snooze_until</c>)은
/// <c>UTC_TIMESTAMP(6) - INTERVAL n</c> 로 과거에 심거나, 고정 날짜(이틀 전 <c>D</c>)의 로컬 시각을 엔진의 <see cref="CooltimeEngine.ServerLocalToUtc"/> 로 바꿔 심는다.
/// 주입 <c>nowLocal</c> 은 설정 시각 <c>C</c> 계산 · 근태에만 쓰인다.</para>
/// <para>🔴 이 PC(개발 PC)는 3306 = 실물 DB 라 돌리지 않는다. 시험 DB 환경 변수가 없으면 <c>root</c>·빈 비밀번호로 붙으려다 거절되어 <c>[SKIP]</c> 이다.
/// CI <c>db-gate</c> 잡(<c>HITPAN_REQUIRE_DB</c>)이 유일한 계측 경로 — 그 잡에서 SKIP 은 FAIL 이다.</para>
/// <para>음성 대조군: 같은 시험 안의 <b>짝(양성 대조)</b>으로 「준비가 실제로 그 길을 탄다」 를 보이고, 제품 문장 하나를 빼는 변이는
/// 개발명세서 §3 의 변이 표대로 CI 변이 런(<c>scripts/gates/cooltime-mutation-run.sh</c>)이 FAIL 을 확인한다.</para>
/// </remarks>
[Collection("CooltimeDbGate")]
public sealed partial class CooltimeDbGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_ctm_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;
    private readonly ITestOutputHelper _out;
    private readonly string _tenantA = Guid.NewGuid().ToString();
    private readonly string _tenantB = Guid.NewGuid().ToString();

    private const string PcUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
    private const string PhoneUa = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

    public CooltimeDbGateTests(ITestOutputHelper output)
    {
        _out = output;
        // JWT_SECRET — 파일이 없는 환경의 폴백. 값을 레포에 남기지 않는다(매 실행 무작위 · SessionRecordConcurrentPcGateTests 와 같은 이유).
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JWT_SECRET")))
            Environment.SetEnvironmentVariable("JWT_SECRET", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    // ══════════════════════════════════════════════════════════════
    // 준비물 — ApprovalRetiredAccessGateDbTests 와 같은 방식(출하 DDL 위 격리 DB · 연결 풀 끔)
    // ══════════════════════════════════════════════════════════════

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
            Console.Error.WriteLine($"[CooltimeDbGate] 임시 DB 정리 실패 — 손으로 지워라: {_dbName} ({ex.Message})");
        }
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(DbConnString());
        await c.OpenAsync();
        return c;
    }

    // ══════════════════════════════════════════════════════════════
    // 통로 — 판정하지 않는다
    // ══════════════════════════════════════════════════════════════

    /// <summary>푸시 송신을 센다(G-C2 「송신 1회」 · G-C17 「푸시 0」). 실제 송신은 하지 않는다.</summary>
    private sealed class CountingPusher : ICooltimePusher
    {
        private readonly object _gate = new();
        public List<(string Tenant, string Sid, int Seconds)> Prompts { get; } = new();
        public List<(string Tenant, string Sid, CooltimeEndCause Cause)> Ended { get; } = new();

        public Task PromptAsync(string tenantId, string sessionId, int secondsLeft, CancellationToken ct = default)
        {
            lock (_gate) Prompts.Add((tenantId, sessionId, secondsLeft));
            return Task.CompletedTask;
        }

        public Task EndedAsync(string tenantId, string sessionId, CooltimeEndCause cause, CancellationToken ct = default)
        {
            lock (_gate) Ended.Add((tenantId, sessionId, cause));
            return Task.CompletedTask;
        }

        /// <summary>🆕 11/27 고침 차례(§273 V1) — 「계속됨」 송신을 센다(G-C14 ①).</summary>
        public List<(string Tenant, string Sid)> Continued { get; } = new();

        public Task ContinuedAsync(string tenantId, string sessionId, CancellationToken ct = default)
        {
            lock (_gate) Continued.Add((tenantId, sessionId));
            return Task.CompletedTask;
        }
    }

    private static CooltimeEngine Engine(MySqlConnection db, CountingPusher pusher) =>
        new(db, pusher, NullLogger<CooltimeEngine>.Instance);

    // ══════════════════════════════════════════════════════════════
    // 상태 만들기 · 읽기 (판정 0)
    // ══════════════════════════════════════════════════════════════

    private static async Task<DateTime> DbNowLocalAsync(MySqlConnection db) =>
        CooltimeEngine.UtcToServerLocal(await db.ExecuteScalarAsync<DateTime>("SELECT UTC_TIMESTAMP(6)"));

    /// <summary>
    /// 고정 기준일 <c>D</c> = DB 시계 기준 서버 로컬 오늘의 <b>이틀 전</b>. 근태 시각(09:00 · 18:00 · 22:00)을 그 날에 놓는다.
    /// 이틀 전이라 ②(놓침)의 「지금 ≥ C + 5분」 이 DB 시계로 늘 성립한다.
    /// </summary>
    private static async Task<DateTime> DayDAsync(MySqlConnection db) => (await DbNowLocalAsync(db)).Date.AddDays(-2);

    private static Task SettingsAsync(MySqlConnection db, string tenantId, bool enabled, TimeSpan? at, TimeSpan? leave,
        DateTime? changedAtUtc = null) =>
        db.ExecuteAsync(@"
            INSERT INTO tenant_settings (tenant_id, cooltime_enabled, cooltime_at, regular_leave_time, cooltime_changed_at)
            VALUES (@T, @E, @At, @Leave, @Changed)
            ON DUPLICATE KEY UPDATE cooltime_enabled = @E, cooltime_at = @At, regular_leave_time = @Leave,
                                    cooltime_changed_at = @Changed",
            new { T = tenantId, E = enabled ? 1 : 0, At = at, Leave = leave, Changed = changedAtUtc });

    /// <summary>설정 시각 = DB 시계 기준 서버 로컬 「지금 − n초」(초 단위 내림) · 켬 · 정시 18:00. 돌려주는 값 = 그 순간의 <c>nowLocal</c>(한 바퀴에 넣는다).</summary>
    private static async Task<DateTime> CooltimeJustPassedAsync(MySqlConnection db, string tenantId, int secondsAgo = 15, bool enabled = true)
    {
        var now = await DbNowLocalAsync(db);
        var c = now.AddSeconds(-secondsAgo);
        await SettingsAsync(db, tenantId, enabled, new TimeSpan(c.Hour, c.Minute, c.Second), new TimeSpan(18, 0, 0));
        return now;
    }

    private static async Task<string> InsertUserAsync(MySqlConnection db, string tenantId, string? userId = null)
    {
        var id = userId ?? Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent,
                               is_active, is_deleted, created_at, updated_at)
            VALUES (@U, @T, @E, 'x', '게이트직원', 'User', 'tenant_user', 0, 1, 0, NOW(6), NOW(6))",
            new { U = id, T = tenantId, E = "ctm_" + id[..8] + "@gate.local" });
        return id;
    }

    private static async Task<string> InsertEmployeeAsync(MySqlConnection db, string tenantId, string userId)
    {
        var id = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO employees (employee_id, tenant_id, emp_no, emp_name, emp_type, join_date, is_active,
                                   created_at, updated_at, user_id)
            VALUES (@E, @T, @No, '게이트사원', '정규직', NOW(6), 1, NOW(6), NOW(6), @U)",
            new { E = id, T = tenantId, No = "C" + id[..8], U = userId });
        return id;
    }

    /// <summary>세션 행 한 줄(로그인 흉내). 시각은 「몇 초 전」 또는 고정 UTC 로 심는다 — 엔진이 쓰는 시계(<c>UTC_TIMESTAMP(6)</c>) 기준.</summary>
    private static async Task<string> InsertSessionAsync(MySqlConnection db, string tenantId, string userId, string kind,
        int loginAgoSec = 600, int? askedAgoSec = null, DateTime? loginUtc = null, DateTime? askedUtc = null)
    {
        var sid = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO user_sessions (session_id, user_id, tenant_id, expires_at, device_kind, login_at, user_agent, cooltime_asked_at)
            VALUES (@S, @U, @T, UTC_TIMESTAMP(6) + INTERVAL 8 HOUR, @K,
                    COALESCE(@LoginUtc, UTC_TIMESTAMP(6) - INTERVAL @LoginAgo SECOND), @Ua,
                    CASE WHEN @AskedUtc IS NOT NULL THEN @AskedUtc
                         WHEN @AskedAgo IS NULL THEN NULL
                         ELSE UTC_TIMESTAMP(6) - INTERVAL @AskedAgo SECOND END)",
            new
            {
                S = sid, U = userId, T = tenantId, K = kind, LoginUtc = loginUtc, LoginAgo = loginAgoSec,
                Ua = kind == "pc" ? PcUa : PhoneUa, AskedUtc = askedUtc, AskedAgo = askedAgoSec
            });
        return sid;
    }

    private static async Task<string> InsertRefreshAsync(MySqlConnection db, string userId, string sid)
    {
        var id = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked, session_id)
            VALUES (@Id, @U, @H, UTC_TIMESTAMP(6) + INTERVAL 7 DAY, 0, @S)",
            new { Id = id, U = userId, H = Guid.NewGuid().ToString("N"), S = sid });
        return id;
    }

    private static async Task<string> InsertAttendanceAsync(MySqlConnection db, string tenantId, string employeeId,
        DateTime checkIn, DateTime? checkOut = null, decimal? hours = null, string? source = null)
    {
        var id = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO attendance (attendance_id, tenant_id, employee_id, work_date, check_in, check_out, work_hours, check_out_source)
            VALUES (@Id, @T, @E, @D, @In, @Out, @H, @Src)",
            new { Id = id, T = tenantId, E = employeeId, D = checkIn.Date, In = checkIn, Out = checkOut, H = hours, Src = source });
        return id;
    }

    private sealed class AttRow
    {
        public DateTime? CheckOut { get; set; }
        public decimal? WorkHours { get; set; }
        public string? Source { get; set; }
    }

    private static Task<AttRow> AttAsync(MySqlConnection db, string attendanceId) =>
        db.QuerySingleAsync<AttRow>(
            "SELECT check_out AS CheckOut, work_hours AS WorkHours, check_out_source AS Source FROM attendance WHERE attendance_id = @Id",
            new { Id = attendanceId });

    private static Task<int> SessionExistsAsync(MySqlConnection db, string sid) =>
        db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM user_sessions WHERE session_id = @S", new { S = sid });

    private static Task<DateTime?> AskedAtAsync(MySqlConnection db, string sid) =>
        db.ExecuteScalarAsync<DateTime?>("SELECT cooltime_asked_at FROM user_sessions WHERE session_id = @S", new { S = sid });

    private static Task<int> RevokedAsync(MySqlConnection db, string tokenId) =>
        db.ExecuteScalarAsync<int>("SELECT is_revoked FROM refresh_tokens WHERE token_id = @Id", new { Id = tokenId });

    private static Task<int> LogoutTrailCountAsync(MySqlConnection db, string sid) =>
        db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM audit_trail WHERE entity_type = 'user_session' AND action_type = 'logout' AND entity_id = @S",
            new { S = sid });

    private static DateTime Utc(DateTime local) => CooltimeEngine.ServerLocalToUtc(local);

    // ══════════════════════════════════════════════════════════════
    // G-C1 — 행 없음 / 스위치 0 → 물음 0 · 세션 그대로
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C1 설정 행 없음 · 스위치 0 → 설정 시각 지나도 물음 0 · 세션 그대로 · 짝(켬) = 물음 1")]
    public async Task G_C1_꺼짐이면_묻지_않는다()
    {
        if (!Ready("G-C1")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();

        // ① 설정 행 없음(회사 A)
        var uA = await InsertUserAsync(db, _tenantA);
        var sA = await InsertSessionAsync(db, _tenantA, uA, "pc");
        // ② 행은 있고 스위치 0 · 시각은 15초 전(회사 B)
        var uB = await InsertUserAsync(db, _tenantB);
        var sB = await InsertSessionAsync(db, _tenantB, uB, "pc");
        var now = await CooltimeJustPassedAsync(db, _tenantB, enabled: false);

        var r = await Engine(db, pusher).RunOnceAsync(now);
        Assert.Equal(0, r.EnabledTenants);
        Assert.Equal(0, r.Asked);
        Assert.Empty(pusher.Prompts);
        Assert.Null(await AskedAtAsync(db, sA));
        Assert.Null(await AskedAtAsync(db, sB));
        Assert.Equal(1, await SessionExistsAsync(db, sA));
        Assert.Equal(1, await SessionExistsAsync(db, sB));

        // 🔴 짝(양성 대조) — 같은 준비에서 스위치만 1 ⇒ 물음 1. 이게 안 나오면 위 「0」 은 준비가 틀려 나온 0 이다.
        now = await CooltimeJustPassedAsync(db, _tenantB, enabled: true);
        var r2 = await Engine(db, pusher).RunOnceAsync(now);
        Assert.Equal(1, r2.Asked);
        Assert.NotNull(await AskedAtAsync(db, sB));
        Assert.Null(await AskedAtAsync(db, sA));                 // 행 없는 회사는 그대로 끔
    }

    // ══════════════════════════════════════════════════════════════
    // G-C2 · G-C3 — 묻기 → 무응답 끝내기 · 휴대폰 무접촉
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C2 C 이전 PC 로그인 → C+15초 한 바퀴 = asked_at 채워짐 · 그룹 송신 1회(다음 바퀴 재송신 0)")]
    public async Task G_C2_설정시각_지나면_묻는다()
    {
        if (!Ready("G-C2")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var u = await InsertUserAsync(db, _tenantA);
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", loginAgoSec: 600);
        var now = await CooltimeJustPassedAsync(db, _tenantA, secondsAgo: 15);

        var r = await Engine(db, pusher).RunOnceAsync(now);
        Assert.Equal(1, r.Asked);
        Assert.Equal(0, r.EndedMissed);
        var asked = await AskedAtAsync(db, sid);
        Assert.NotNull(asked);
        Assert.InRange(await db.ExecuteScalarAsync<long>(
            "SELECT TIMESTAMPDIFF(SECOND, cooltime_asked_at, UTC_TIMESTAMP(6)) FROM user_sessions WHERE session_id = @S", new { S = sid }), 0, 10);
        Assert.Equal(new[] { (_tenantA, sid, CooltimeWire.PromptSeconds) }, pusher.Prompts.ToArray());

        var r2 = await Engine(db, pusher).RunOnceAsync(await DbNowLocalAsync(db));
        Assert.Equal(0, r2.Asked);
        Assert.Single(pusher.Prompts);                           // 두 번 묻지 않는다
        Assert.Equal(asked, await AskedAtAsync(db, sid));
    }

    [Fact(DisplayName = "G-C3 물은 뒤 무응답(+70초) 한 바퀴 = 그 세션 행 없음 · 그 sid refresh 폐기 · 같은 계정 휴대폰 세션·refresh 그대로 · 접속기록 사유 1")]
    public async Task G_C3_무응답이면_끝내고_휴대폰은_그대로()
    {
        if (!Ready("G-C3")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var u = await InsertUserAsync(db, _tenantA);
        var pc = await InsertSessionAsync(db, _tenantA, u, "pc");
        var phone = await InsertSessionAsync(db, _tenantA, u, "mobile");
        var pcRt = await InsertRefreshAsync(db, u, pc);
        var phoneRt = await InsertRefreshAsync(db, u, phone);
        var now = await CooltimeJustPassedAsync(db, _tenantA);

        Assert.Equal(1, (await Engine(db, pusher).RunOnceAsync(now)).Asked);   // PC 만 물었다
        Assert.Null(await AskedAtAsync(db, phone));

        // 60초 + 여유를 넘긴 상태 = 물은 순간을 70초 과거로 민다(DB 시계는 주입 못 함 · 설계 §7 머리)
        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_asked_at = cooltime_asked_at - INTERVAL 70 SECOND WHERE session_id = @S", new { S = pc });
        var r = await Engine(db, pusher).RunOnceAsync(await DbNowLocalAsync(db));

        Assert.Equal(1, r.EndedNoAnswer);
        Assert.Equal(0, await SessionExistsAsync(db, pc));
        Assert.Equal(1, await RevokedAsync(db, pcRt));
        Assert.Equal(1, await SessionExistsAsync(db, phone));
        Assert.Equal(0, await RevokedAsync(db, phoneRt));
        Assert.Contains((_tenantA, pc, CooltimeEndCause.NoAnswer), pusher.Ended);
        Assert.DoesNotContain(pusher.Ended, e => e.Sid == phone);
        Assert.Equal(CooltimeWire.AuditReason(CooltimeEndCause.NoAnswer), await db.ExecuteScalarAsync<string>(
            "SELECT reason FROM audit_trail WHERE entity_type = 'user_session' AND action_type = 'logout' AND entity_id = @S", new { S = pc }));
    }

    // ══════════════════════════════════════════════════════════════
    // G-C4 — 작2 두 번째 PC: 끝내기 전 409 → 끝낸 뒤 200
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C4 두 번째 PC 로그인(실물 AuthService) — 쿨타임 끝내기 전 409 → 끝낸 뒤 200")]
    public async Task G_C4_끝낸_뒤_두번째PC_풀림()
    {
        if (!Ready("G-C4")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var (user, password) = NewLoginUser(_tenantA);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));   // 한 PC 규칙 = 출하 기본 켬

        var svc = new AuthService(new GateUnitOfWork(db), new GateUserLookup(user));
        var pc1 = await svc.LoginAsync(LoginOf(user, password));
        var sid1 = SidOf(pc1);
        Assert.NotNull(sid1);
        await Assert.ThrowsAsync<ConcurrentPcLoginException>(() => svc.LoginAsync(LoginOf(user, password)));   // 409

        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_asked_at = UTC_TIMESTAMP(6) - INTERVAL 70 SECOND WHERE session_id = @S", new { S = sid1 });
        var r = await Engine(db, pusher).RunOnceAsync(await DbNowLocalAsync(db));
        Assert.Equal(1, r.EndedNoAnswer);

        var pc2 = await svc.LoginAsync(LoginOf(user, password));                                    // 200
        var sid2 = SidOf(pc2);
        Assert.NotNull(sid2);
        Assert.NotEqual(sid1, sid2);
        Assert.Equal("pc", await db.ExecuteScalarAsync<string>("SELECT device_kind FROM user_sessions WHERE session_id = @S", new { S = sid2 }));
    }

    // ══════════════════════════════════════════════════════════════
    // G-C5 · G-C6 — [계속] 1시간 · 65초 경계 경주
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C5 [계속] 뒤 +59분 = 안 물음 · +61분 = 다시 물음 (snooze = 지금 + 60분)")]
    public async Task G_C5_계속하면_한시간_뒤_다시_묻는다()
    {
        if (!Ready("G-C5")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var u = await InsertUserAsync(db, _tenantA);
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", askedAgoSec: 10);
        await CooltimeJustPassedAsync(db, _tenantA);
        var engine = Engine(db, pusher);

        Assert.True(await engine.ContinueAsync(_tenantA, sid, u));
        Assert.Null(await AskedAtAsync(db, sid));
        Assert.InRange(await db.ExecuteScalarAsync<long>(
            "SELECT TIMESTAMPDIFF(SECOND, UTC_TIMESTAMP(6), cooltime_snooze_until) FROM user_sessions WHERE session_id = @S", new { S = sid }), 3590, 3600);

        // +59분 = 미룸 끝을 59분 앞당긴다(시계가 59분 간 것과 같다)
        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_snooze_until = cooltime_snooze_until - INTERVAL 59 MINUTE WHERE session_id = @S", new { S = sid });
        var r59 = await engine.RunOnceAsync(await DbNowLocalAsync(db));
        Assert.Equal(0, r59.Asked);
        Assert.Null(await AskedAtAsync(db, sid));

        // +61분
        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_snooze_until = cooltime_snooze_until - INTERVAL 2 MINUTE WHERE session_id = @S", new { S = sid });
        var r61 = await engine.RunOnceAsync(await DbNowLocalAsync(db));
        Assert.Equal(1, r61.Asked);
        Assert.NotNull(await AskedAtAsync(db, sid));
        Assert.Single(pusher.Prompts);
        Assert.Equal(1, await SessionExistsAsync(db, sid));
    }

    [Fact(DisplayName = "G-C6 [계속] ↔ ① 경주 — 65초 경계 앞: [계속]만 성공(세션 유지) · 뒤: 끝내기만 성공(삭제+409) · 순서 바꿔도 같음 · 동시 실행 둘 다 성공 0")]
    public async Task G_C6_계속과_끝내기는_한쪽만_이긴다()
    {
        if (!Ready("G-C6")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var u = await InsertUserAsync(db, _tenantA);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));
        var engine = Engine(db, pusher);
        var now = await DbNowLocalAsync(db);

        foreach (var (askedAgo, continueFirst) in new[] { (55, true), (55, false), (75, true), (75, false) })
        {
            var sid = await InsertSessionAsync(db, _tenantA, u, "pc", askedAgoSec: askedAgo);
            bool cont;
            CooltimeEndOutcome end;
            if (continueFirst)
            {
                cont = await engine.ContinueAsync(_tenantA, sid, u);
                end = (await engine.CooltimeLogoutAsync(_tenantA, sid, now, CooltimeEndCause.NoAnswer)).Outcome;
            }
            else
            {
                end = (await engine.CooltimeLogoutAsync(_tenantA, sid, now, CooltimeEndCause.NoAnswer)).Outcome;
                cont = await engine.ContinueAsync(_tenantA, sid, u);
            }
            var label = $"물은 지 {askedAgo}초 · {(continueFirst ? "[계속] 먼저" : "끝내기 먼저")}";
            if (askedAgo < CooltimeWire.ServerGraceSeconds)
            {
                Assert.True(cont, $"{label}: [계속] 이 져서는 안 된다");
                Assert.Equal(CooltimeEndOutcome.NotEnded, end);
                Assert.Equal(1, await SessionExistsAsync(db, sid));
            }
            else
            {
                Assert.False(cont, $"{label}: 65초가 지났는데 [계속] 이 이겼다(409 여야 함 · 대조군 ⓒ)");
                Assert.Equal(CooltimeEndOutcome.Ended, end);
                Assert.Equal(0, await SessionExistsAsync(db, sid));
            }
        }

        // 동시 — 연결 둘(한 연결 동시 사용 아님 · #16) · 경계(65초) 근처 6회 · 둘 다 성공 0
        for (var i = 0; i < 6; i++)
        {
            var sid = await InsertSessionAsync(db, _tenantA, u, "pc", askedAgoSec: CooltimeWire.ServerGraceSeconds);
            await using var c1 = await OpenAsync();
            await using var c2 = await OpenAsync();
            var e1 = Engine(c1, pusher);
            var e2 = Engine(c2, pusher);
            var tCont = e1.ContinueAsync(_tenantA, sid, u);
            var tEnd = e2.CooltimeLogoutAsync(_tenantA, sid, now, CooltimeEndCause.NoAnswer);
            await Task.WhenAll(tCont, tEnd);
            var cont = await tCont;
            var ended = (await tEnd).Outcome == CooltimeEndOutcome.Ended;
            Assert.False(cont && ended, $"동시 {i}: [계속] 과 끝내기가 둘 다 성공했다");
            Assert.Equal(ended ? 0 : 1, await SessionExistsAsync(db, sid));
            _out.WriteLine($"[G-C6 동시 {i}] 계속={cont} 끝냄={ended}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-C7 — 휴대폰 세션만 있는 계정
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C7 휴대폰 세션만 — 물음 0 · 삭제 0(서버 끝내기·API 끝내기 직접 불러도) · 짝(PC) = 삭제 1")]
    public async Task G_C7_휴대폰은_묻지도_끝내지도_않는다()
    {
        if (!Ready("G-C7")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var u = await InsertUserAsync(db, _tenantA);
        var phone = await InsertSessionAsync(db, _tenantA, u, "mobile");
        var phoneRt = await InsertRefreshAsync(db, u, phone);
        var now = await CooltimeJustPassedAsync(db, _tenantA);
        var engine = Engine(db, pusher);

        Assert.Equal(0, (await engine.RunOnceAsync(now)).Asked);
        Assert.Null(await AskedAtAsync(db, phone));

        // 물음 칸이 (어떤 길로든) 채워진 휴대폰 행에 끝내기를 직접 불러도 지우지 않는다 — 삭제 문장의 device_kind='pc' 가 심판(대조군 ⓐ)
        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_asked_at = UTC_TIMESTAMP(6) - INTERVAL 70 SECOND WHERE session_id = @S", new { S = phone });
        Assert.Equal(CooltimeEndOutcome.NotEnded, (await engine.CooltimeLogoutAsync(_tenantA, phone, now, CooltimeEndCause.NoAnswer)).Outcome);
        Assert.Equal(CooltimeEndOutcome.NotEnded, (await engine.CooltimeLogoutAsync(_tenantA, phone, now, CooltimeEndCause.UserChoice, jwtUserId: u)).Outcome);
        Assert.Equal(1, await SessionExistsAsync(db, phone));
        Assert.Equal(0, await RevokedAsync(db, phoneRt));
        Assert.Empty(pusher.Ended);

        // 짝(양성 대조) — 같은 모양의 PC 행은 끝난다
        var pc = await InsertSessionAsync(db, _tenantA, u, "pc", askedAgoSec: 70);
        Assert.Equal(CooltimeEndOutcome.Ended, (await engine.CooltimeLogoutAsync(_tenantA, pc, now, CooltimeEndCause.NoAnswer)).Outcome);
        Assert.Equal(0, await SessionExistsAsync(db, pc));
        Assert.Equal(1, await SessionExistsAsync(db, phone));
    }

    // ══════════════════════════════════════════════════════════════
    // G-C8 · G-C9 · G-C10 — 퇴근 기록
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C8 퇴근 기록 — 출근 09:00 · 정시 18:00 · 쿨타임 22:00 무응답 → check_out 18:00 · work_hours 9.0 · source cooltime")]
    public async Task G_C8_정시퇴근으로_적는다()
    {
        if (!Ready("G-C8")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var d = await DayDAsync(db);
        var u = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, u);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));
        var att = await InsertAttendanceAsync(db, _tenantA, emp, d.AddHours(9));
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", loginUtc: Utc(d.AddHours(8.9)), askedUtc: Utc(d.AddHours(22)));

        var r = await Engine(db, pusher).RunOnceAsync(await DbNowLocalAsync(db));
        Assert.Equal(1, r.EndedNoAnswer);
        Assert.Equal(0, await SessionExistsAsync(db, sid));

        var a = await AttAsync(db, att);
        Assert.Equal(d.AddHours(18), a.CheckOut);
        Assert.Equal(9.0m, a.WorkHours);
        Assert.Equal("cooltime", a.Source);
        Assert.Equal("자동", CooltimeScreenRules.SourceLabel(a.Source));   // DB 값 → 화면 표시
    }

    [Fact(DisplayName = "G-C9 자정 넘김 — 쿨타임 02:00 · 전날 09:00 출근 → 전날 행에 18:00 · 오늘 행 생성 0")]
    public async Task G_C9_자정넘김은_전날_행에_붙는다()
    {
        if (!Ready("G-C9")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var d = await DayDAsync(db);
        var u = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, u);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(2, 0, 0), new TimeSpan(18, 0, 0));
        var att = await InsertAttendanceAsync(db, _tenantA, emp, d.AddHours(9));
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", loginUtc: Utc(d.AddHours(8.9)), askedUtc: Utc(d.AddDays(1).AddHours(2)));

        var res = await Engine(db, pusher).CooltimeLogoutAsync(_tenantA, sid, d.AddDays(1).AddHours(2.1), CooltimeEndCause.NoAnswer);
        Assert.Equal(CooltimeEndOutcome.Ended, res.Outcome);
        Assert.Equal(CooltimeAttendanceOutcome.WroteRegularLeave, res.Attendance);
        Assert.Equal(att, res.AttendanceId);
        var a = await AttAsync(db, att);
        Assert.Equal(d.AddHours(18), a.CheckOut);
        Assert.Equal("cooltime", a.Source);
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM attendance WHERE employee_id = @E", new { E = emp }));
    }

    [Fact(DisplayName = "G-C10 이미 퇴근 있는 행(C-3) — 변경 0 · 세션은 끝남")]
    public async Task G_C10_이미_퇴근이면_건드리지_않는다()
    {
        if (!Ready("G-C10")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var d = await DayDAsync(db);
        var u = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, u);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));
        var att = await InsertAttendanceAsync(db, _tenantA, emp, d.AddHours(9), d.AddHours(17.5), 8.5m, null);
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", loginUtc: Utc(d.AddHours(8.9)), askedUtc: Utc(d.AddHours(22)));

        var res = await Engine(db, pusher).CooltimeLogoutAsync(_tenantA, sid, d.AddHours(22.1), CooltimeEndCause.NoAnswer);
        Assert.Equal(CooltimeEndOutcome.Ended, res.Outcome);
        Assert.Equal(CooltimeAttendanceOutcome.NoOpenRow, res.Attendance);
        var a = await AttAsync(db, att);
        Assert.Equal(d.AddHours(17.5), a.CheckOut);
        Assert.Equal(8.5m, a.WorkHours);
        Assert.Null(a.Source);
    }

    // ══════════════════════════════════════════════════════════════
    // G-C11 · G-C21 — 놓침(②) · 기준 순간 A = C (E-4)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C11 서버 꺼졌다 다음 날 08:00 켜짐(②) — 묻지 않고 끝냄 · 전날 행 18:00")]
    public async Task G_C11_놓치면_묻지_않고_끝낸다()
    {
        if (!Ready("G-C11")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var d = await DayDAsync(db);
        var u = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, u);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));
        var att = await InsertAttendanceAsync(db, _tenantA, emp, d.AddHours(9));
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", loginUtc: Utc(d.AddHours(9)));

        var r = await Engine(db, pusher).RunOnceAsync(d.AddDays(1).AddHours(8));
        Assert.Equal(1, r.EndedMissed);
        Assert.Equal(0, r.Asked);
        Assert.Empty(pusher.Prompts);
        Assert.Contains((_tenantA, sid, CooltimeEndCause.Missed), pusher.Ended);
        Assert.Equal(0, await SessionExistsAsync(db, sid));
        var a = await AttAsync(db, att);
        Assert.Equal(d.AddHours(18), a.CheckOut);
        Assert.Equal("cooltime", a.Source);
    }

    [Fact(DisplayName = "G-C21 놓침(②) 기준 순간 = 설정 시각 C(E-4) — 어제 09:00 행에 18:00 · 오늘 07:50 출근 행(휴대폰) 변경 0")]
    public async Task G_C21_놓침은_설정시각_기준으로_붙인다()
    {
        if (!Ready("G-C21")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var d = await DayDAsync(db);
        var u = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, u);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));
        var yesterday = await InsertAttendanceAsync(db, _tenantA, emp, d.AddHours(9));
        var today = await InsertAttendanceAsync(db, _tenantA, emp, d.AddDays(1).AddHours(7) + TimeSpan.FromMinutes(50));
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", loginUtc: Utc(d.AddHours(9)));

        var r = await Engine(db, pusher).RunOnceAsync(d.AddDays(1).AddHours(8));
        Assert.Equal(1, r.EndedMissed);
        Assert.Equal(0, await SessionExistsAsync(db, sid));
        var y = await AttAsync(db, yesterday);
        Assert.Equal(d.AddHours(18), y.CheckOut);
        Assert.Equal("cooltime", y.Source);
        var t = await AttAsync(db, today);
        Assert.Null(t.CheckOut);
        Assert.Null(t.Source);                                   // 대조군 ⓘ(기준 = 끝낸 순간)이면 오늘 행이 cooltime_blank 가 된다
    }

    // ══════════════════════════════════════════════════════════════
    // G-C22 — 규칙 ④(E-6 · 대표 결재 §287 (가) W = 4) · 🆕 11/28
    //   설계 §E E-6 예시 표 앞 4줄(22:00 회사 · 09:00 · 23:30 · 00:40 · 03:00 로그인)을 「첫 물음」 시각 직전 · 직후 바퀴로 잰다.
    //   DB 시계는 주입할 수 없다(§7 머리) ⇒ 22:00 은 「C 와 로그인 사이 간격」 으로 옮긴다:
    //   로그인 = UTC_TIMESTAMP(6) - n 으로 심고, 설정 시각(시:분:초) = nowLocal − (n + 로그인−C 간격) 으로 놓는다.
    //   회사(테넌트)마다 C 가 하나이므로 경우마다 회사를 따로 둔다 — 한 바퀴(RunOnceAsync 1회)가 전부를 판정한다.
    // ══════════════════════════════════════════════════════════════

    private sealed record RuleFourCase(string Name, TimeSpan LoginAgo, TimeSpan CAgo, bool ExpectAsk,
        TimeSpan? SnoozeFromNow = null);

    [Fact(DisplayName = "G-C22 규칙 ④(E-6 · W=4) — 22:00 회사 · 09:00 · 03:00 로그인 = ④ 아님(로그인+1h 물음 0 · 다음 C 에 ③) · 23:30 · 00:40 = ④(로그인+1h 직전 0 · 직후 1) · 폭 경계 3h59m/4h01m · 미룸 지남 = 물음 · 미룸 남음 = 0")]
    public async Task G_C22_규칙4는_설정시각_뒤_W시간_안_로그인만_묻는다()
    {
        if (!Ready("G-C22")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var now = await DbNowLocalAsync(db);

        static TimeSpan H(double h) => TimeSpan.FromHours(h);
        static TimeSpan M(double m) => TimeSpan.FromMinutes(m);
        var before = M(59);   // 「첫 물음」(로그인 + 1시간) 1분 전
        var after = M(61);    // 「첫 물음」 1분 뒤

        // 이름 · 로그인이 몇 전 · C 가 몇 전 · 물음 기대 (C 전 = 로그인 전 + (로그인 − C))
        var cases = new List<RuleFourCase>
        {
            // ── E-6 예시 1줄 — 22:00 회사 · 오늘 09:00 로그인 · 그때 C = 어제 22:00(+11h) · ④ 아님 ──
            new("09:00 ④자리 직전", before, before + H(11), false),
            new("09:00 ④자리 직후", after, after + H(11), false),             // 대조군 ⓙ(폭 조건 제거)이면 여기서 물음 → FAIL
            //    첫 물음 = 오늘 22:00(③) — 다음 C 의 1분 전(아직 옛 C) · 15초 뒤(새 C)
            new("09:00 ③자리 직전", H(13) - M(1), H(24) - M(1), false),       // C = 지금 + 1분 − 24h(어제의 그 시각)
            new("09:00 ③자리 직후", H(13) + TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), true),
            // ── E-6 예시 2줄 — 23:30 로그인(C + 1.5h) · ④ · 첫 물음 00:30 ──
            new("23:30 ④ 직전", before, before + H(1.5), false),
            new("23:30 ④ 직후", after, after + H(1.5), true),
            // ── E-6 예시 3줄 — 자정 넘어 00:40(C + 2h40m) · ④ · 첫 물음 01:40 ──
            new("00:40 ④ 직전", before, before + H(2) + M(40), false),
            new("00:40 ④ 직후", after, after + H(2) + M(40), true),
            // ── E-6 예시 4줄 — 03:00(C + 5h) · ④ 아님 · 첫 물음 그날 22:00(③) ──
            new("03:00 ④자리 직전", before, before + H(5), false),
            new("03:00 ④자리 직후", after, after + H(5), false),             // 대조군 ⓙ 이면 여기서도 물음
            new("03:00 ③자리 직전", H(19) - M(1), H(24) - M(1), false),
            new("03:00 ③자리 직후", H(19) + TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), true),
            // ── 폭 경계 W = 4 — C + 3h59m(안) · C + 4h01m(밖) · 둘 다 로그인 + 1h 지남 ──
            new("폭 안 3h59m", after, after + H(4) - M(1), true),
            new("폭 밖 4h01m", after, after + H(4) + M(1), false),
            // ── 미룸 있는 ④([계속] 뒤 1시간마다) — 로그인 = C + 1.5h · 로그인 3시간 전 ──
            new("④ 미룸 지남", H(3), H(3) + H(1.5), true, SnoozeFromNow: -M(1)),
            new("④ 미룸 남음", H(3), H(3) + H(1.5), false, SnoozeFromNow: M(30)),   // 로그인+1h 는 지났어도 미룸이 먼저
        };

        var sids = new Dictionary<string, string>();
        foreach (var c in cases)
        {
            var tenant = Guid.NewGuid().ToString();
            var cLocal = now - c.CAgo;
            await SettingsAsync(db, tenant, true, new TimeSpan(cLocal.Hour, cLocal.Minute, cLocal.Second), new TimeSpan(18, 0, 0));
            var u = await InsertUserAsync(db, tenant);
            var sid = await InsertSessionAsync(db, tenant, u, "pc", loginAgoSec: (int)c.LoginAgo.TotalSeconds);
            if (c.SnoozeFromNow is TimeSpan s)
                await db.ExecuteAsync(
                    "UPDATE user_sessions SET cooltime_snooze_until = UTC_TIMESTAMP(6) + INTERVAL @Sec SECOND WHERE session_id = @S",
                    new { Sec = (int)s.TotalSeconds, S = sid });
            sids[c.Name] = sid;

            // 준비 확인(판정 아님) — 엔진이 이 회사에서 쓸 C 가 의도한 간격인지(초 내림 1초 이내).
            var engineC = CooltimeEngine.MostRecentCooltimeLocal(now, new TimeSpan(cLocal.Hour, cLocal.Minute, cLocal.Second));
            Assert.True(Math.Abs((engineC - cLocal).TotalSeconds) < 1.0, $"[{c.Name}] 준비 C 어긋남: {engineC:O} vs {cLocal:O}");
        }

        var r = await Engine(db, pusher).RunOnceAsync(now);

        Assert.Equal(0, r.EndedMissed);      // ② 는 login_at < C 만 — ④ 로그인을 묻지 않고 끝내지 않는다
        Assert.Equal(0, r.EndedNoAnswer);
        var fails = new List<string>();
        foreach (var c in cases)
        {
            var asked = await AskedAtAsync(db, sids[c.Name]) is not null;
            var pushed = pusher.Prompts.Any(p => p.Sid == sids[c.Name]);
            _out.WriteLine($"[G-C22] {c.Name}: 기대 {(c.ExpectAsk ? "물음" : "0")} · 실제 asked_at={(asked ? "있음" : "없음")} · 푸시={(pushed ? 1 : 0)}");
            if (asked != c.ExpectAsk || pushed != c.ExpectAsk)
                fails.Add($"{c.Name}: 기대 {(c.ExpectAsk ? "물음 1" : "물음 0")} · asked_at {(asked ? "있음" : "없음")} · 푸시 {(pushed ? 1 : 0)}");
            Assert.Equal(1, await SessionExistsAsync(db, sids[c.Name]));   // 묻기만 — 끝내지 않는다
        }
        Assert.True(fails.Count == 0, "[G-C22] 규칙 ④ 판정 어긋남:\n  " + string.Join("\n  ", fails));
        Assert.Equal(cases.Count(c => c.ExpectAsk), r.Asked);
    }

    // ══════════════════════════════════════════════════════════════
    // G-C23 — 사원 연결 없는 계정(대표 계정 등 · 리뷰 P3-1) · 🆕 11/28
    //   끝내기는 그대로(세션 삭제 · refresh 폐기 · 접속기록) · 퇴근 기록만 건너뜀(NoEmployee) · 남의 근태 변경 0 · 예외 0.
    //   짝(양성 대조) = 같은 회사의 사원 연결된 계정 → 퇴근 기록 1(정시 18:00).
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C23 사원 연결 없는 계정 PC 끝내기 — 세션 끝남 · refresh 폐기 · 결과 NoEmployee · 근태 변경 0 · 짝(사원 있는 계정) = 퇴근 18:00 기록 1")]
    public async Task G_C23_사원없는_계정은_끝내고_근태는_건너뛴다()
    {
        if (!Ready("G-C23")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var d = await DayDAsync(db);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));

        // 사원 없는 계정(employees 에 user_id 줄 0) · 사원 있는 계정(오늘 09:00 출근 · 퇴근 빈칸)
        var noEmpUser = await InsertUserAsync(db, _tenantA);
        var empUser = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, empUser);
        var att = await InsertAttendanceAsync(db, _tenantA, emp, d.AddHours(9));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM employees WHERE tenant_id = @T AND user_id = @U", new { T = _tenantA, U = noEmpUser }));   // 준비 확인

        var sidNoEmp = await InsertSessionAsync(db, _tenantA, noEmpUser, "pc", loginUtc: Utc(d.AddHours(8.9)), askedUtc: Utc(d.AddHours(22)));
        var rtNoEmp = await InsertRefreshAsync(db, noEmpUser, sidNoEmp);
        var sidEmp = await InsertSessionAsync(db, _tenantA, empUser, "pc", loginUtc: Utc(d.AddHours(8.9)), askedUtc: Utc(d.AddHours(22)));
        var attRowsBefore = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM attendance WHERE tenant_id = @T", new { T = _tenantA });

        // ① 사원 없는 계정 — 서버 작업 ① 무응답 끝내기(제품 공개 메서드)
        var res = await Engine(db, pusher).CooltimeLogoutAsync(_tenantA, sidNoEmp, d.AddHours(22.1), CooltimeEndCause.NoAnswer);
        Assert.Equal(CooltimeEndOutcome.Ended, res.Outcome);
        Assert.Equal(CooltimeAttendanceOutcome.NoEmployee, res.Attendance);   // 대조군 ⓧ(반환 제거)이면 NoOpenRow
        Assert.Null(res.EmployeeId);
        Assert.Null(res.AttendanceId);
        Assert.Equal(0, await SessionExistsAsync(db, sidNoEmp));
        Assert.Equal(1, await RevokedAsync(db, rtNoEmp));
        Assert.Equal(1, await LogoutTrailCountAsync(db, sidNoEmp));
        Assert.Contains((_tenantA, sidNoEmp, CooltimeEndCause.NoAnswer), pusher.Ended);
        // 근태 변경 0 — 같은 회사 다른 사람의 빈칸 행도 그대로 · 행 수 그대로
        var untouched = await AttAsync(db, att);
        Assert.Null(untouched.CheckOut);
        Assert.Null(untouched.WorkHours);
        Assert.Null(untouched.Source);
        Assert.Equal(attRowsBefore, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM attendance WHERE tenant_id = @T", new { T = _tenantA }));

        // ② 짝 — 사원 있는 계정은 같은 길에서 퇴근 기록 1(준비가 실제로 퇴근 기록 길을 탄다)
        var pair = await Engine(db, pusher).CooltimeLogoutAsync(_tenantA, sidEmp, d.AddHours(22.1), CooltimeEndCause.NoAnswer);
        Assert.Equal(CooltimeEndOutcome.Ended, pair.Outcome);
        Assert.Equal(CooltimeAttendanceOutcome.WroteRegularLeave, pair.Attendance);
        Assert.Equal(att, pair.AttendanceId);
        var written = await AttAsync(db, att);
        Assert.Equal(d.AddHours(18), written.CheckOut);
        Assert.Equal("cooltime", written.Source);
    }

    // ══════════════════════════════════════════════════════════════
    // G-C12 — 퇴근 고치기 · 다른 회사 404
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C12 퇴근 고치기 — check_out 바뀜 · source edited · audit_trail 1줄(전/후/사유/고친 사람) · 다른 회사 id = 404(NotFound) 행 변경 0 · 사람 기록 = 409")]
    public async Task G_C12_퇴근_고치기()
    {
        if (!Ready("G-C12")) return;
        await using var db = await OpenAsync();
        var d = await DayDAsync(db);
        var uA = await InsertUserAsync(db, _tenantA);
        var empA = await InsertEmployeeAsync(db, _tenantA, uA);
        var uB = await InsertUserAsync(db, _tenantB);
        var empB = await InsertEmployeeAsync(db, _tenantB, uB);
        var attA = await InsertAttendanceAsync(db, _tenantA, empA, d.AddHours(9), d.AddHours(18), 9.0m, "cooltime");
        var attB = await InsertAttendanceAsync(db, _tenantB, empB, d.AddHours(9), d.AddHours(18), 9.0m, "cooltime");
        var human = await InsertAttendanceAsync(db, _tenantA, empA, d.AddDays(-1).AddHours(9), d.AddDays(-1).AddHours(18), 9.0m, null);
        var editor = Guid.NewGuid().ToString();
        var hr = new HrService(db);

        var ok = await hr.EditCheckOutAsync(_tenantA, attA, editor,
            new EditCheckOutRequest { CheckOut = d.AddHours(18).AddMinutes(40), Reason = "외근 뒤 복귀" });
        Assert.Equal(EditCheckOutOutcome.Saved, ok.Outcome);
        var a = await AttAsync(db, attA);
        Assert.Equal(d.AddHours(18).AddMinutes(40), a.CheckOut);
        Assert.Equal("edited", a.Source);
        Assert.Equal(9.7m, a.WorkHours);
        var trail = (await db.QueryAsync<(string UserId, string Reason, string Before, string After)>(
            @"SELECT user_id, reason, before_value, after_value FROM audit_trail
               WHERE tenant_id = @T AND entity_type = 'attendance' AND action_type = 'update' AND entity_id = @Id",
            new { T = _tenantA, Id = attA })).ToList();
        Assert.Single(trail);
        Assert.Equal(editor, trail[0].UserId);
        Assert.Equal("외근 뒤 복귀", trail[0].Reason);
        Assert.Contains("cooltime", trail[0].Before);
        Assert.Contains("18:00", trail[0].Before);
        Assert.Contains("edited", trail[0].After);
        Assert.Contains("18:40", trail[0].After);

        // 다른 회사 행 — 회사 A 가 B 의 id 로 부른다 ⇒ 404 · 행 변경 0 · 이력 0
        var cross = await hr.EditCheckOutAsync(_tenantA, attB, editor,
            new EditCheckOutRequest { CheckOut = d.AddHours(20), Reason = "남의 회사" });
        Assert.Equal(EditCheckOutOutcome.NotFound, cross.Outcome);
        var b = await AttAsync(db, attB);
        Assert.Equal(d.AddHours(18), b.CheckOut);
        Assert.Equal("cooltime", b.Source);
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM audit_trail WHERE entity_type = 'attendance' AND entity_id = @Id", new { Id = attB }));

        // 사람이 적은 퇴근(source NULL)은 고치기 대상 아님(C-5) ⇒ 409 · 변경 0
        var notEditable = await hr.EditCheckOutAsync(_tenantA, human, editor,
            new EditCheckOutRequest { CheckOut = d.AddDays(-1).AddHours(19), Reason = "사람 기록" });
        Assert.Equal(EditCheckOutOutcome.NotEditable, notEditable.Outcome);
        Assert.Equal(d.AddDays(-1).AddHours(18), (await AttAsync(db, human)).CheckOut);

        // 🆕 11/27 고침 차례(§273 V3) — 「고침」 행을 다시 고친다 ⇒ Saved · 값 바뀜 · 이력 2줄(두 번째 전 = edited/18:40)
        //   ⚠️ UPDATE WHERE 의 'edited' 를 빼면 여기서 NotEditable 로 FAIL(대조군 ⓝ).
        var again = await hr.EditCheckOutAsync(_tenantA, attA, editor,
            new EditCheckOutRequest { CheckOut = d.AddHours(18).AddMinutes(20), Reason = "40분이 아니라 20분" });
        Assert.Equal(EditCheckOutOutcome.Saved, again.Outcome);
        var a2 = await AttAsync(db, attA);
        Assert.Equal(d.AddHours(18).AddMinutes(20), a2.CheckOut);
        Assert.Equal("edited", a2.Source);
        Assert.Equal(9.3m, a2.WorkHours);
        var trail2 = (await db.QueryAsync<(string Reason, string Before, string After)>(
            @"SELECT reason, before_value, after_value FROM audit_trail
               WHERE tenant_id = @T AND entity_type = 'attendance' AND action_type = 'update' AND entity_id = @Id",
            new { T = _tenantA, Id = attA })).ToList();
        Assert.Equal(2, trail2.Count);
        var second = trail2.Single(t => t.Reason == "40분이 아니라 20분");
        Assert.Contains("edited", second.Before);
        Assert.Contains("18:40", second.Before);
        Assert.Contains("18:20", second.After);

        // 🆕 11/27 고침 차례(§273 ② · V8) — 아직 오지 않은 시각은 막는다(출근 1시간 전 · 24시간 안이지만 미래) ⇒ Invalid · 변경 0 · 이력 0
        //   짝: 같은 행에 지금보다 10분 앞 시각 = Saved(막는 것이 미래 조건뿐임을 보인다). ⚠️ 미래 줄을 빼면 FAIL(대조군 ⓞ).
        var nowLocal = DateTime.Now;
        var fresh = await InsertAttendanceAsync(db, _tenantA, empA, nowLocal.AddHours(-1), null, null, "cooltime_blank");
        var future = await hr.EditCheckOutAsync(_tenantA, fresh, editor,
            new EditCheckOutRequest { CheckOut = nowLocal.AddHours(2), Reason = "미래 시각" });
        Assert.Equal(EditCheckOutOutcome.Invalid, future.Outcome);
        Assert.Null((await AttAsync(db, fresh)).CheckOut);
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM audit_trail WHERE entity_type = 'attendance' AND entity_id = @Id", new { Id = fresh }));
        var past = await hr.EditCheckOutAsync(_tenantA, fresh, editor,
            new EditCheckOutRequest { CheckOut = nowLocal.AddMinutes(-10), Reason = "지금 직전 퇴근" });
        Assert.Equal(EditCheckOutOutcome.Saved, past.Outcome);
    }

    // ══════════════════════════════════════════════════════════════
    // G-C13 · G-C15 · G-C16 — 손 퇴근 회귀 · 빈칸 갈래 · 같은 날 손 로그아웃
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C13 손 로그아웃(기존 CheckOutAsync) 회귀 — 그 순간이 퇴근 · source NULL")]
    public async Task G_C13_손_로그아웃은_그_순간()
    {
        if (!Ready("G-C13")) return;
        await using var db = await OpenAsync();
        var u = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, u);
        var att = await InsertAttendanceAsync(db, _tenantA, emp, DateTime.Today);

        var before = DateTime.Now.AddSeconds(-2);
        await new HrService(db).CheckOutAsync(_tenantA, emp);
        var after = DateTime.Now.AddSeconds(2);

        var a = await AttAsync(db, att);
        Assert.NotNull(a.CheckOut);
        Assert.InRange(a.CheckOut!.Value, before, after);
        Assert.Null(a.Source);
    }

    [Fact(DisplayName = "G-C15 C-2 빈칸 갈래 — 19:00 출근 · 정시 18:00 · 23:00 쿨타임 → check_out NULL · work_hours 변경 0 · source cooltime_blank · 화면 「자동 로그아웃 · 퇴근 빈칸」")]
    public async Task G_C15_정시가_아직이면_빈칸()
    {
        if (!Ready("G-C15")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var d = await DayDAsync(db);
        var u = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, u);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(23, 0, 0), new TimeSpan(18, 0, 0));
        var att = await InsertAttendanceAsync(db, _tenantA, emp, d.AddHours(19));
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", loginUtc: Utc(d.AddHours(18.9)), askedUtc: Utc(d.AddHours(23)));

        var res = await Engine(db, pusher).CooltimeLogoutAsync(_tenantA, sid, d.AddHours(23.1), CooltimeEndCause.NoAnswer);
        Assert.Equal(CooltimeEndOutcome.Ended, res.Outcome);
        Assert.Equal(CooltimeAttendanceOutcome.LeftBlank, res.Attendance);
        var a = await AttAsync(db, att);
        Assert.Null(a.CheckOut);
        Assert.Null(a.WorkHours);
        Assert.Equal("cooltime_blank", a.Source);
        Assert.Equal("자동 로그아웃 · 퇴근 빈칸", CooltimeScreenRules.SourceLabel(a.Source));
    }

    [Fact(DisplayName = "G-C16 D-2 안 A — 빈칸 행(오늘) 뒤 같은 날 다시 로그인 → 손 로그아웃 = check_out 그 순간 · source NULL(표시와 값 엇갈림 0)")]
    public async Task G_C16_빈칸행을_손퇴근이_채우면_표시를_지운다()
    {
        if (!Ready("G-C16")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var u = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, u);
        // 정시 23:59:59 · 출근 오늘 00:00 ⇒ 정시는 아직 안 왔다 ⇒ 빈칸 갈래(엔진이 만든다)
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(23, 59, 59));
        var att = await InsertAttendanceAsync(db, _tenantA, emp, DateTime.Today);
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", askedAgoSec: 0);

        var res = await Engine(db, pusher).CooltimeLogoutAsync(_tenantA, sid, DateTime.Now, CooltimeEndCause.UserChoice, jwtUserId: u);
        Assert.Equal(CooltimeEndOutcome.Ended, res.Outcome);
        Assert.Equal(CooltimeAttendanceOutcome.LeftBlank, res.Attendance);
        Assert.Equal("cooltime_blank", (await AttAsync(db, att)).Source);

        // 같은 날 다시 로그인해 일하다 손 로그아웃(AuthController.Logout 이 부르는 그 함수)
        var before = DateTime.Now.AddSeconds(-2);
        await new HrService(db).CheckOutAsync(_tenantA, emp);
        var after = DateTime.Now.AddSeconds(2);
        var a = await AttAsync(db, att);
        Assert.NotNull(a.CheckOut);
        Assert.InRange(a.CheckOut!.Value, before, after);
        Assert.Null(a.Source);                                   // 대조군 ⓓ(CASE 제거)면 cooltime_blank 가 남는다
        Assert.Null(CooltimeScreenRules.SourceLabel(a.Source));
    }

    // ══════════════════════════════════════════════════════════════
    // G-C17 — 다른 자리가 먼저 지운 세션 (D-6 (가))
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C17 계정 정지(CutAccessAsync)가 먼저 세션을 지운 뒤 쿨타임 끝내기 — 0행 · 퇴근 변경 0 · 예외 0 · 접속기록·푸시 0 · 짝(정지 안 함) = 끝냄+18:00")]
    public async Task G_C17_남이_먼저_지우면_아무것도_안_적는다()
    {
        if (!Ready("G-C17")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var d = await DayDAsync(db);
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));

        var u = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, u);
        var att = await InsertAttendanceAsync(db, _tenantA, emp, d.AddHours(9));
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", loginUtc: Utc(d.AddHours(8.9)), askedUtc: Utc(d.AddHours(22)));

        await new UserService(db, new NoOpAudit()).SuspendAsync(u, _tenantA);
        Assert.Equal(0, await SessionExistsAsync(db, sid));

        var engine = Engine(db, pusher);
        var res = await engine.CooltimeLogoutAsync(_tenantA, sid, d.AddHours(22.1), CooltimeEndCause.NoAnswer);
        Assert.Equal(CooltimeEndOutcome.NotEnded, res.Outcome);
        Assert.Equal(CooltimeAttendanceOutcome.NotAttempted, res.Attendance);
        Assert.Equal(0, (await engine.RunOnceAsync(await DbNowLocalAsync(db))).EndedNoAnswer);
        var a = await AttAsync(db, att);
        Assert.Null(a.CheckOut);
        Assert.Null(a.Source);
        Assert.Equal(0, await LogoutTrailCountAsync(db, sid));
        Assert.Empty(pusher.Ended);

        // 짝(양성 대조) — 정지 없이 같은 모양이면 끝내고 18:00 을 적는다
        var u2 = await InsertUserAsync(db, _tenantA);
        var emp2 = await InsertEmployeeAsync(db, _tenantA, u2);
        var att2 = await InsertAttendanceAsync(db, _tenantA, emp2, d.AddHours(9));
        var sid2 = await InsertSessionAsync(db, _tenantA, u2, "pc", loginUtc: Utc(d.AddHours(8.9)), askedUtc: Utc(d.AddHours(22)));
        Assert.Equal(CooltimeEndOutcome.Ended, (await engine.CooltimeLogoutAsync(_tenantA, sid2, d.AddHours(22.1), CooltimeEndCause.NoAnswer)).Outcome);
        Assert.Equal(d.AddHours(18), (await AttAsync(db, att2)).CheckOut);
        Assert.Equal(1, await LogoutTrailCountAsync(db, sid2));
    }

    // ══════════════════════════════════════════════════════════════
    // G-C19 · G-C20 — 저장 뒤 첫 C 부터(E-2) · 켬/끔 때 물음 칸 비우기(E-3)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C19 낮에 「1시간 전 시각」 으로 켬(실물 SettingsService) → 첫 바퀴 물음 0 · 끝냄 0 · 근태 변경 0 / 다음 날 그 시각 = 물음 2 · 짝(기록 없는 옛 행) = 첫 바퀴 끝냄 2")]
    public async Task G_C19_저장_뒤_첫_설정시각부터()
    {
        if (!Ready("G-C19")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var now = await DbNowLocalAsync(db);
        var atText = now.AddHours(-1).ToString("HH:mm");

        var u1 = await InsertUserAsync(db, _tenantA);
        var u2 = await InsertUserAsync(db, _tenantA);
        var emp1 = await InsertEmployeeAsync(db, _tenantA, u1);
        var att = await InsertAttendanceAsync(db, _tenantA, emp1, now.AddHours(-3));
        var s1 = await InsertSessionAsync(db, _tenantA, u1, "pc", loginAgoSec: 7200);
        var s2 = await InsertSessionAsync(db, _tenantA, u2, "pc", loginAgoSec: 7200);

        await new SettingsService(db).SaveAsync(
            new UpdateTenantSettingsDto { CooltimeEnabled = true, CooltimeAt = atText, RegularLeaveTime = "18:00" }, _tenantA);
        Assert.NotNull(await db.ExecuteScalarAsync<DateTime?>(
            "SELECT cooltime_changed_at FROM tenant_settings WHERE tenant_id = @T", new { T = _tenantA }));

        var engine = Engine(db, pusher);
        var r1 = await engine.RunOnceAsync(await DbNowLocalAsync(db));
        Assert.Equal(0, r1.Asked);
        Assert.Equal(0, r1.EndedMissed);
        Assert.Equal(0, r1.EndedNoAnswer);
        Assert.Equal(1, await SessionExistsAsync(db, s1));
        Assert.Equal(1, await SessionExistsAsync(db, s2));
        var a = await AttAsync(db, att);
        Assert.Null(a.CheckOut);
        Assert.Null(a.Source);

        // 다음 날 그 시각 + 15초 — 주입 nowLocal 로 C 를 다음 날로 정한다(설계 §7 머리 · nowLocal 은 C 계산에만 쓰인다)
        var cToday = CooltimeEngine.MostRecentCooltimeLocal(await DbNowLocalAsync(db), TimeSpan.Parse(atText));
        var r2 = await engine.RunOnceAsync(cToday.AddDays(1).AddSeconds(15));
        Assert.Equal(2, r2.Asked);
        Assert.Equal(0, r2.EndedMissed);

        // 🔴 짝(양성 대조 · 대조군 ⓕ 의 모양) — 저장 기록(cooltime_changed_at)이 없는 회사는 같은 첫 바퀴에서 끝낸다
        var uB = await InsertUserAsync(db, _tenantB);
        var uB2 = await InsertUserAsync(db, _tenantB);
        var sB1 = await InsertSessionAsync(db, _tenantB, uB, "pc", loginAgoSec: 7200);
        var sB2 = await InsertSessionAsync(db, _tenantB, uB2, "pc", loginAgoSec: 7200);
        await SettingsAsync(db, _tenantB, true, TimeSpan.Parse(atText), new TimeSpan(18, 0, 0), changedAtUtc: null);
        var r3 = await engine.RunOnceAsync(await DbNowLocalAsync(db));
        Assert.Equal(2, r3.EndedMissed);
        Assert.Equal(0, await SessionExistsAsync(db, sB1));
        Assert.Equal(0, await SessionExistsAsync(db, sB2));
    }

    [Fact(DisplayName = "G-C20 ⓐ 물음 → 저장으로 끔 → API 로그아웃 = 물음·미룸 NULL · ended:false · 퇴근 0 / ⓑ 물음(70초 전) → DB 직접 끔 → 저장으로 켬 → 한 바퀴 = 끝냄 0 · 퇴근 0")]
    public async Task G_C20_켬끔때_물음칸을_비운다()
    {
        if (!Ready("G-C20")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        var now = await DbNowLocalAsync(db);
        var settings = new SettingsService(db);
        var engine = Engine(db, pusher);
        UpdateTenantSettingsDto Dto(bool on) => new() { CooltimeEnabled = on, CooltimeAt = "22:00", RegularLeaveTime = "18:00" };

        // ⓐ
        await settings.SaveAsync(Dto(true), _tenantA);
        var u = await InsertUserAsync(db, _tenantA);
        var emp = await InsertEmployeeAsync(db, _tenantA, u);
        var att = await InsertAttendanceAsync(db, _tenantA, emp, now.AddHours(-2));
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", askedAgoSec: 10);
        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_snooze_until = UTC_TIMESTAMP(6) + INTERVAL 1 HOUR WHERE session_id = @S", new { S = sid });

        await settings.SaveAsync(Dto(false), _tenantA);
        Assert.Null(await AskedAtAsync(db, sid));
        Assert.Null(await db.ExecuteScalarAsync<DateTime?>("SELECT cooltime_snooze_until FROM user_sessions WHERE session_id = @S", new { S = sid }));
        var res = await engine.CooltimeLogoutAsync(_tenantA, sid, now, CooltimeEndCause.UserChoice, jwtUserId: u);
        Assert.Equal(CooltimeEndOutcome.NotEnded, res.Outcome);                  // 화면 = ended:false
        Assert.Equal(1, await SessionExistsAsync(db, sid));
        var a = await AttAsync(db, att);
        Assert.Null(a.CheckOut);
        Assert.Null(a.Source);

        // ⓑ
        var uB = await InsertUserAsync(db, _tenantB);
        var empB = await InsertEmployeeAsync(db, _tenantB, uB);
        await settings.SaveAsync(Dto(true), _tenantB);
        var attB = await InsertAttendanceAsync(db, _tenantB, empB, now.AddHours(-2));
        var sidB = await InsertSessionAsync(db, _tenantB, uB, "pc", askedAgoSec: 70);
        await db.ExecuteAsync("UPDATE tenant_settings SET cooltime_enabled = 0 WHERE tenant_id = @T", new { T = _tenantB });   // 비상 끔(저장 함수 안 지남)
        await settings.SaveAsync(Dto(true), _tenantB);
        Assert.Null(await AskedAtAsync(db, sidB));

        var r = await engine.RunOnceAsync(await DbNowLocalAsync(db));
        Assert.Equal(0, r.EndedNoAnswer);
        Assert.Equal(0, r.EndedMissed);
        Assert.Equal(1, await SessionExistsAsync(db, sidB));
        var b = await AttAsync(db, attB);
        Assert.Null(b.CheckOut);
        Assert.Null(b.Source);
    }

    // ══════════════════════════════════════════════════════════════
    // 확인 거리(판정 아님) — 설계 §E E-5 ⚠️ · 갈래 ① 명세서 §1-1 ⚠️
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔎 <b>계측 · 판정 아님</b> — 「팝업 60초 안에 관리자가 끔 → 화면은 로그인 화면 · 서버 세션은 남음 → 같은 PC 다시 로그인」 이
    /// 작2 「두 번째 PC 불가」 에 걸리는지. 결과(409 / 200)는 출력 줄 <c>[E-5 확인]</c> 에 남긴다(CI trx).
    /// 준비가 맞게 섰는지(세션 남음 · 물음 칸 비워짐)만 단언한다 — 기대값은 PM 결정 전이라 정하지 않는다(#33).
    /// </summary>
    [Fact(DisplayName = "확인 E-5 — 팝업 중 끔 → 같은 PC 다시 로그인 결과 기록(409/200) · 판정 아님")]
    public async Task 확인_E5_끈_뒤_같은PC_다시_로그인()
    {
        if (!Ready("확인 E-5")) return;
        await using var db = await OpenAsync();
        var (user, password) = NewLoginUser(_tenantA);
        var settings = new SettingsService(db);
        await settings.SaveAsync(new UpdateTenantSettingsDto { CooltimeEnabled = true, CooltimeAt = "22:00", RegularLeaveTime = "18:00" }, _tenantA);

        var svc = new AuthService(new GateUnitOfWork(db), new GateUserLookup(user));
        var sid1 = SidOf(await svc.LoginAsync(LoginOf(user, password)));
        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_asked_at = UTC_TIMESTAMP(6) - INTERVAL 10 SECOND WHERE session_id = @S", new { S = sid1 });
        await settings.SaveAsync(new UpdateTenantSettingsDto { CooltimeEnabled = false, CooltimeAt = "22:00", RegularLeaveTime = "18:00" }, _tenantA);

        Assert.Equal(1, await SessionExistsAsync(db, sid1!));
        Assert.Null(await AskedAtAsync(db, sid1!));

        string outcome;
        try
        {
            var again = await svc.LoginAsync(LoginOf(user, password));
            outcome = $"200 (새 sid {(SidOf(again) is null ? "없음" : "있음")})";
        }
        catch (ConcurrentPcLoginException)
        {
            outcome = "409 (두 번째 PC 불가에 걸림 — 세션 만료까지 같은 계정 PC 로그인 막힘)";
        }
        _out.WriteLine($"[E-5 확인] 팝업 중 끔 → 같은 PC 다시 로그인 = {outcome}");
        Console.Out.WriteLine($"[E-5 확인] 팝업 중 끔 → 같은 PC 다시 로그인 = {outcome}");
    }

    // ══════════════════════════════════════════════════════════════
    // 로그인 실물(AuthService)용 통로 — SessionRecordConcurrentPcGateTests 와 같은 꼴(판정 0)
    // ══════════════════════════════════════════════════════════════

    private static (User User, string Password) NewLoginUser(string tenantId)
    {
        var password = Guid.NewGuid().ToString("N") + "Aa1!";   // 매 실행 무작위(레포에 값 0)
        var id = Guid.NewGuid().ToString();
        return (new User
        {
            Id = id,
            UserId = id,
            TenantId = tenantId,
            Email = "ctm_" + id[..8] + "@gate.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            UserName = "게이트",
            AccountType = "tenant_user",
            IsActive = true,
            LastLoginAt = DateTime.UtcNow.AddDays(-1)
        }, password);
    }

    private static LoginRequest LoginOf(User user, string password) => new()
    {
        Email = user.Email,
        Password = password,
        DeviceType = "pc",
        UserAgent = PcUa,
        ForceSignOutOtherPc = false,
        DeviceFingerprint = null
    };

    private static string? SidOf(LoginResponse response) =>
        new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken)
            .Claims.FirstOrDefault(c => c.Type == "sid")?.Value;

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class GateUnitOfWork : IUnitOfWork
    {
        private readonly DbConnection _conn;
        public GateUnitOfWork(DbConnection conn) => _conn = conn;
        public DbConnection GetDbConnection() => _conn;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public IRepository<T> Repository<T>() where T : BaseEntity =>
            throw new NotSupportedException("게이트는 EF 리포지토리를 태우지 않는다(사원 백필은 운영 try/catch 가 받는다).");
        public Task<ISharedTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("게이트는 EF 공유 트랜잭션을 태우지 않는다.");
        public void Dispose() { }
    }

    private sealed class GateUserLookup : IAuthUserLookup
    {
        private readonly User _user;
        public GateUserLookup(User user) => _user = user;
        public Task<User?> FindUserByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<User?>(_user);
        public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult<User?>(string.Equals(userId, _user.Id, StringComparison.Ordinal) ? _user : null);
        public Task<Employee?> FindActiveEmployeeByUserAsync(string userId, string tenantId, CancellationToken ct = default) =>
            Task.FromResult<Employee?>(null);
        public Task<List<Employee>> FindEmployeesByTenantAsync(string tenantId, CancellationToken ct = default) =>
            Task.FromResult(new List<Employee>());
    }
}
