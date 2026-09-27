using Dapper;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-1 ~ G-8</b> — 세션이 <b>실제로 기록되고</b>, 같은 계정의 PC 동시 로그인이 <b>실제로 막히고</b>,
/// 밀어내기가 <b>실제로 반영되는가</b> (20260927작1 절F).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇이 났나</b> — 동시접속 제한이 <b>한 번도 걸린 적이 없었다.</b>
/// 세는 곳(<c>SessionLimitMiddleware:114</c>)과 지우는 곳(<c>AuthController</c> 로그아웃)은 있는데
/// <b>넣는 곳이 레포 전체에 0건</b>이라 <c>user_sessions</c> 가 항상 비어 있었다 ⇒
/// 판정식 <c>activeCount &gt; limit</c> 의 왼쪽이 영원히 0.
/// <c>DB-28</c> 은 행이 있다고 전제하고 <c>expires_at</c> 컬럼까지 늘렸다 — 최소 2명이 속았다.
/// (선행검증서 <c>docs/검증/선행/20260927_선행검증서_계정과금_PC동시로그인차단_전제실측.md</c>)
/// </para>
///
/// <para>
/// 🟢 <b>초록불이 어디서 오나</b> — 격리 DB 에 <b>출하 DDL</b>(헌법 #36)을 넣고,
/// <b>코드가 실제로 쓰는 SQL 문장 그대로</b>를 돌린 뒤 <b>표를 읽는다.</b>
/// 주석·문자열을 세지 않는다. 판정 규칙은 <c>AuthService</c> 의 <b>실제 메서드</b>를 부른다.
/// </para>
///
/// <para>
/// 🔴 <b>G-1N 음성 대조군이 이 파일의 핵심이다.</b> 봉합(DB-127 컬럼)을 <b>빼면 반드시 실패해야</b> 한다.
/// 대조군 없이 초록불만 보면, 시험이 무엇을 재는지 아무도 모른 채 통과한다 —
/// 이 트랙을 만든 사고가 정확히 그것이다.
/// </para>
///
/// <para>
/// ⚠️ <b>운영 무접촉</b>(헌법 #39) — 임시 DB(<c>hitpan_session_gate_*</c>)만 만들고 반드시 지운다.
/// ⚠️ MariaDB 가 없으면 건너뛴다(<c>DbGateEnvironment.SkipOrFail</c>) — <b>그 환경에서 이 게이트는
/// 아무것도 검사하지 않는다.</b> 초록불이 곧 안전이 아니다. CI 는 <c>HITPAN_REQUIRE_DB</c> 로 SKIP 을 막는다.
/// </para>
/// </remarks>
public sealed class SessionRecordConcurrentPcGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_session_gate_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string UserId = "22222222-2222-2222-2222-222222222222";

    // ══════════════════════════════════════════════════════════════
    // 🔴 코드가 실제로 쓰는 문장 — 여기 복붙이 아니라 **같은 문장**이어야 한다.
    //   AuthService.InsertSessionAsync / EnforceSinglePcLoginAsync 와 대조하며 읽을 것.
    //   컬럼 이름이 어긋나면 이 게이트가 먼저 빨간불이 된다(#13 DESCRIBE 사고 계통).
    // ══════════════════════════════════════════════════════════════

    private const string InsertSessionSql =
        @"INSERT INTO user_sessions (session_id, user_id, tenant_id, login_at, last_active_at, expires_at, device_kind)
          VALUES (@SessionId, @UserId, @TenantId, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @ExpiresAt, @DeviceKind)";

    /// <summary>축 B 판정 — 같은 계정의 <b>살아 있는 PC</b> 세션을 찾는다.</summary>
    private const string FindOtherPcSql =
        @"SELECT last_active_at FROM user_sessions
           WHERE user_id = @UserId AND device_kind = 'pc' AND expires_at > UTC_TIMESTAMP(6)
           ORDER BY last_active_at DESC LIMIT 1";

    /// <summary>SessionValidityMiddleware 의 생존 확인.</summary>
    private const string SessionAliveSql =
        "SELECT 1 FROM user_sessions WHERE session_id = @Sid AND expires_at > UTC_TIMESTAMP(6)";

    // ══════════════════════════════════════════════════════════════
    // 준비물 — 선례(MainPcRestartMarkGateTests)와 같은 방식
    // ══════════════════════════════════════════════════════════════

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
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 실패로 드러난다 (작14 W1)
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

        Assert.True(proc.ExitCode == 0, $"출하 DDL 적용 실패:\n{err}");
    }

    private static object SessionRow(string sid, string kind, int expiresInHours = 8) => new
    {
        SessionId = sid,
        UserId,
        TenantId,
        ExpiresAt = DateTime.UtcNow.AddHours(expiresInHours),
        DeviceKind = kind
    };

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
            Console.Error.WriteLine($"[정리 실패] 임시 DB {_dbName} 를 못 지웠다: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-1 — 세션이 실제로 기록된다 (+ G-1N 음성 대조군)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-1</b> — 코드가 쓰는 INSERT 문장 그대로가 출하 DDL 스키마에서 <b>행을 만든다.</b>
    /// </summary>
    /// <remarks>
    /// 이 트랙 전체가 <i>"넣는 코드가 없어 표가 늘 비어 있었다"</i> 에서 시작했다.
    /// 그래서 <b>가장 먼저 재는 것이 「행이 생기는가」</b> 다.
    /// </remarks>
    [Fact]
    public void G1_세션_INSERT_가_실제로_행을_만든다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-1 세션 INSERT")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();

        var before = db.ExecuteScalar<int>("SELECT COUNT(*) FROM user_sessions");
        Assert.Equal(0, before);   // 신규 설치는 비어 있다 — 출발점을 확인하고 시작한다

        db.Execute(InsertSessionSql, SessionRow("sid-g1", "pc"));

        var after = db.ExecuteScalar<int>("SELECT COUNT(*) FROM user_sessions");
        Assert.Equal(1, after);

        var kind = db.ExecuteScalar<string>(
            "SELECT device_kind FROM user_sessions WHERE session_id = 'sid-g1'");
        Assert.Equal("pc", kind);
    }

    /// <summary>
    /// 🔴🔴 <b>G-1N 음성 대조군</b> — 봉합(<c>device_kind</c>)을 <b>빼면 반드시 실패한다.</b>
    /// </summary>
    /// <remarks>
    /// 이것이 없으면 G-1 이 무엇을 재는지 증명되지 않는다.
    /// <b>통과하는 시험은 실패할 줄도 알아야 한다.</b>
    /// DB-127 이전 모양(컬럼 없음)을 격리 DB 안에서만 재현한다(헌법 #39).
    /// </remarks>
    [Fact]
    public void G1N_봉합을_빼면_세션_INSERT_가_실패한다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-1N 음성 대조군")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();

        // DB-127 이전 모양으로 되돌린다 — 옛 고객 DB 와 같은 상태
        db.Execute("ALTER TABLE user_sessions DROP COLUMN device_kind");

        var ex = Assert.ThrowsAny<MySqlException>(
            () => db.Execute(InsertSessionSql, SessionRow("sid-g1n", "pc")));

        Assert.Contains("device_kind", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════
    // G-2 / G-2c — 같은 계정 PC 동시 로그인 판정
    // ══════════════════════════════════════════════════════════════

    /// <summary>🔴 <b>G-2</b> — 같은 계정의 살아 있는 PC 세션이 있으면 <b>찾아낸다</b>(= 거절 근거).</summary>
    [Fact]
    public void G2_같은계정_PC세션이_있으면_찾는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-2 동시 PC 판정")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();
        db.Execute(InsertSessionSql, SessionRow("sid-pc1", "pc"));

        var found = db.QueryFirstOrDefault<DateTime?>(FindOtherPcSql, new { UserId });
        Assert.NotNull(found);

        // 음성 대조군 — **다른 계정**은 걸리지 않는다
        var other = db.QueryFirstOrDefault<DateTime?>(
            FindOtherPcSql, new { UserId = "99999999-9999-9999-9999-999999999999" });
        Assert.Null(other);
    }

    /// <summary>
    /// 🔴 <b>G-2c</b> — <b>죽은 세션은 막지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 컴퓨터가 꺼져 로그아웃 <c>DELETE</c> 가 안 돈 경우, 남은 행이
    /// <b>본인 계정을 스스로 잠그는</b> 일이 생긴다. 만료된 행은 판정에서 빠져야 한다.
    /// </remarks>
    [Fact]
    public void G2c_만료된_세션은_로그인을_막지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-2c 죽은 세션")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();
        db.Execute(InsertSessionSql, SessionRow("sid-dead", "pc", expiresInHours: -1));   // 이미 만료

        var found = db.QueryFirstOrDefault<DateTime?>(FindOtherPcSql, new { UserId });
        Assert.Null(found);
    }

    // ══════════════════════════════════════════════════════════════
    // G-3 — 모바일은 섞이지 않는다
    // ══════════════════════════════════════════════════════════════

    /// <summary>🔴 <b>G-3</b> — 모바일 세션은 PC 판정에 <b>섞이지 않는다</b>(모바일 FREE · 9/25 결재).</summary>
    [Fact]
    public void G3_모바일_세션은_PC판정에_안_섞인다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-3 모바일 분리")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();
        db.Execute(InsertSessionSql, SessionRow("sid-m1", "mobile"));
        db.Execute(InsertSessionSql, SessionRow("sid-m2", "mobile"));

        var found = db.QueryFirstOrDefault<DateTime?>(FindOtherPcSql, new { UserId });
        Assert.Null(found);   // 모바일이 몇 대든 PC 판정은 그대로다
    }

    /// <summary>
    /// 🔴 <b>G-3b</b> — 기기 종류 판정 <b>규칙</b>이 히트판의 결정과 같은가:
    /// <b>"모르는 것은 싼 칸으로"</b>.
    /// </summary>
    /// <remarks>
    /// 글자가 아니라 <c>AuthService</c> 의 <b>실제 메서드</b>를 부른다.
    /// 규칙을 다른 파일로 옮기거나 문자열만 바꿔도 통과하던 실패를 되풀이하지 않는다.
    /// <para>
    /// 근거: 20260815 아키텍처명세서 §3 #4 — 모르는 값을 <c>pc</c> 로 보내
    /// <b>고객이 쓰지도 않은 비싼 자리에 돈을 냈다.</b>
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("pc", "pc")]
    [InlineData("PC", "pc")]        // 대소문자는 같은 값이다
    [InlineData("mobile", "mobile")]
    [InlineData("tablet", "mobile")] // 태블릿은 휴대기기 (사장님 판정)
    [InlineData("", "mobile")]       // 안 보냈으면 싼 칸
    [InlineData(null, "mobile")]     // 모르는 값도 싼 칸
    [InlineData("무엇인가", "mobile")]
    public void G3b_기기종류_판정은_모르면_싼칸이다(string? input, string expected)
    {
        Assert.Equal(expected, AuthService.NormalizeDeviceKindForTests(input));
    }

    // ══════════════════════════════════════════════════════════════
    // G-5 — 킬스위치 기본값
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-5</b> — 축 A 는 <b>꺼진 채</b>, 축 B 는 <b>켜진 채</b> 출하된다.
    /// </summary>
    /// <remarks>
    /// 축 A(테넌트 총량 제한)는 넣는 코드가 없어 <b>한 번도 돈 적이 없다.</b>
    /// 이번 작업으로 행이 생기는 순간 소리 없이 켜지면, 한도 초과 고객은 그날부터 429 다.
    /// <b>기본값이 0 이라는 사실 자체가 안전장치</b>이므로 게이트로 고정한다.
    /// </remarks>
    [Fact]
    public void G5_킬스위치_기본값은_축A_끔_축B_켬()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-5 킬스위치 기본값")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();

        var aDefault = db.ExecuteScalar<string>(
            @"SELECT COLUMN_DEFAULT FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tenant_settings'
                 AND COLUMN_NAME = 'enforce_tenant_session_limit'");
        var bDefault = db.ExecuteScalar<string>(
            @"SELECT COLUMN_DEFAULT FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tenant_settings'
                 AND COLUMN_NAME = 'enforce_single_pc_login'");

        Assert.Equal("0", aDefault);   // 축 A — 꺼둔 채 출하한다
        Assert.Equal("1", bDefault);   // 축 B — 켜서 출하한다
    }

    // ══════════════════════════════════════════════════════════════
    // G-8 — 밀어내기가 실제로 반영된다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-8</b> — 밀어내기 뒤 그 세션은 <b>더 이상 살아 있지 않다.</b>
    /// </summary>
    /// <remarks>
    /// 세션 행만 지우고 끝내면 JWT 가 상태 없이 최대 8시간 살아남아
    /// <b>끊었다고 믿는데 안 끊긴 상태</b>가 된다. 그래서 요청 경로에서 이 문장을 본다
    /// (<c>SessionValidityMiddleware</c>). 그 문장이 실제로 「없음」을 답하는지 잰다.
    /// </remarks>
    [Fact]
    public void G8_밀어내기_뒤_세션은_죽는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-8 밀어내기 반영")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();
        db.Execute(InsertSessionSql, SessionRow("sid-victim", "pc"));

        // 밀어내기 전 — 살아 있다 (양성 대조군)
        Assert.NotNull(db.ExecuteScalar<int?>(SessionAliveSql, new { Sid = "sid-victim" }));

        // 사용자가 [그 컴퓨터 접속을 끊기] 를 눌렀을 때 도는 문장 그대로
        db.Execute("DELETE FROM user_sessions WHERE user_id = @UserId AND device_kind = 'pc'",
            new { UserId });

        // 밀어내기 후 — 죽었다
        Assert.Null(db.ExecuteScalar<int?>(SessionAliveSql, new { Sid = "sid-victim" }));
    }
}
