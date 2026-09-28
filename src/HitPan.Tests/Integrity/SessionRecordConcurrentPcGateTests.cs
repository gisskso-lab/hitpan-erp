using System.Data;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Dapper;
using HitPan.API.Controllers;
using HitPan.API.Middleware;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Auth;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Domain.Common;
using HitPan.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-B1 ~ G-B15</b> — 축 B 봉합이 <b>동작으로</b> 성립하는가
/// (20260927작2 절I · <b>3차</b>에서 G-B13 추가 · <b>4차</b>에서 G-B14·G-B15 추가 + <b>G-B4 기대값 뒤집힘</b>
/// · <b>5차</b>에서 <b>G-B13b 를 재설계</b>(봉합을 한 줄도 안 재고 있었다 — [4] R-1)하고 <b>G-B13c 추가</b>
/// · <b>6차</b>에서 <b>G-B13d 추가</b> — 결재 조건 「이어받은 세션은 건드리지 않는다」가 5차까지
/// <b>무계측</b>이었다([4] V-2: 기존 세 게이트는 전부 <c>sid</c> 없는 토큰이라 보호대
/// <c>isNewSession &amp;&amp;</c> 를 지워도 FAIL 이 0건이었다)).
/// 🔴 5차 — 이 파일의 음성 대조군 서술에서 <b>줄번호 참조를 걷어냈다</b>([4] R-7).
/// 참조는 <b>함수·식 이름</b>으로 적는다 — 주석 한 줄만 얹혀도 줄번호는 커밋 순간 어긋난다.
/// </summary>
/// <remarks>
/// <para>
/// ⬛ <b>4차(2026-09-27 · 통제 분리) — G-B4 를 읽을 때 주의한다.</b>
/// 3차까지 G-B4 는 *"축 B 를 끄면 세션 행이 없어도 통과"* 를 고정했는데, 그 기대값이 <b>틀렸다</b>
/// (V-B3: 축 B 기본 OFF 출하 ⇒ 로그아웃이 8시간 안 먹었다). 4차는 생존 확인을
/// <c>enforce_session_validity</c>(DB-129 · 기본 <b>1</b>) 라는 별 스위치로 떼어냈고,
/// G-B4 는 *"축 B 를 꺼도 죽은 세션은 401"* 로 뒤집혔다.
/// 사유 → <c>docs/운영기록/20260927_PM전결_VB3_통제분리_결재.md</c> §2.
/// </para>
/// <para>
/// 🔴 <b>이 파일이 왜 통째로 다시 쓰였나 — [4] 반려 F-2.</b>
/// 종전 이 파일은 운영 SQL 을 <b>문자열 상수로 복사</b>해 두고(<c>InsertSessionSql</c> 등)
/// <b>자기가 쓴 SQL 을 자기가 돌려</b> 표를 읽었다. 그래서
/// <c>AuthService</c> 의 <c>InsertSessionAsync</c> <b>호출 한 줄을 지워도 게이트 8건이 전부 초록</b>이었다.
/// 무엇을 재는지 증명하지 못하는 시험은 통과해도 통과가 아니다(작지 §3 금지 #8).
/// </para>
/// <para>
/// 🟢 <b>지금 무엇이 달라졌나</b> — 복사본을 전부 지우고 <b>운영 코드를 태운다.</b>
/// 격리 DB 에 출하 DDL(#36)을 넣고, 그 위에서
/// <c>AuthService</c>·<c>AuthController</c>·<c>SessionValidityMiddleware</c> <b>실물</b>을 만들어
/// <c>LoginAsync</c>/<c>RefreshAsync</c>/<c>Login</c>/<c>Logout</c>/<c>InvokeAsync</c> 를 <b>부른 뒤</b> 표를 읽는다.
/// ⇒ 봉합 줄을 지우면 이 게이트가 <b>실제로</b> 빨간불이 된다.
/// </para>
/// <para>
/// 🔴 <b>1순위(실 DB 로 <c>AuthService</c> 직접 생성)를 골랐다.</b> 작지 §4 계측 규칙이 *"1순위 가능성
/// 미측정"* 이라고 남겼으므로 실측했다 — 가능하다. 근거는 셋이다:
/// ① <c>AuthService</c> 의 의존은 <c>IUnitOfWork</c>·<c>IAuthUserLookup</c> <b>둘뿐</b>이고
///    <c>GetDbConnection()</c> 이 <c>DbConnection</c> 을 돌려주므로 격리 DB 연결을 그대로 끼울 수 있다.
/// ② <c>JWT_SECRET</c> 은 <c>TenantConfigReader.GetRequired</c> 가 <b>환경변수로 폴백</b>한다.
/// ③ <b>2순위는 존재하지 않는다</b> — 작지가 선례로 적은 <c>MultiTenantIsolationTests</c> 는
///    <c>WebApplicationFactory</c> 를 쓰지 않는다(Moq 뿐이고, 그 낱말은 파일 머리 <b>주석</b>에만 있다).
///    <c>HitPan.Tests.csproj</c> 에 <c>Microsoft.AspNetCore.Mvc.Testing</c> 참조가 <b>없다</b>.
///    ⇒ 종단(G-B3·G-B4·G-B7·G-B10)은 이 레포의 실제 선례
///    (<c>MainPcProofRoundTripGateTests</c>·<c>LegacyUnpostedViewTenantGateTests</c>)대로
///    <c>DefaultHttpContext</c> 로 <b>컨트롤러·미들웨어 실물을 직접 돌린다.</b>
/// </para>
/// <para>
/// ⚠️ <b>운영 무접촉</b>(#39) — 임시 DB(<c>hitpan_session_gate_*</c>)만 만들고 반드시 지운다.
/// ⚠️ <b>개발 PC 에서는 이 게이트가 아무것도 검사하지 않는다.</b> 2026-09-27 실측:
/// <c>hitpan</c> 계정은 <c>CREATE DATABASE</c> 거부(ERROR 1044), <c>root</c> 는 빈 비밀번호 거부(ERROR 1045).
/// ⇒ 로컬은 <c>[SKIP]</c> 이고, <b>CI <c>db-gate</c> 잡이 유일한 계측 경로</b>다.
/// <c>build</c> 잡 초록은 증거가 아니다.
/// </para>
/// </remarks>
public sealed class SessionRecordConcurrentPcGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_session_gate_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string UserId = "22222222-2222-2222-2222-222222222222";

    /// <summary>서버가 <b>헤더에서 직접 읽는</b> 컴퓨터 User-Agent.</summary>
    private const string WindowsUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/140.0.0.0 Safari/537.36";

    /// <summary>휴대기기 User-Agent — 아이폰은 <c>like Mac OS X</c> 를 달고 온다.</summary>
    private const string IPhoneUa =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) "
        + "Version/17.0 Mobile/15E148 Safari/604.1";

    /// <summary>
    /// 시험용 계정의 비밀번호 — <b>매 실행 무작위</b>다.
    /// </summary>
    /// <remarks>
    /// 🔴 고정 문자열로 적으면 비밀검사(TruffleHog)가 접속정보 모양으로 읽는다
    /// (2026-09-27 `bef7d5e0` 에서 같은 사고를 이미 한 번 치웠다). 값을 레포에 남기지 않는다.
    /// </remarks>
    private readonly string _password = Guid.NewGuid().ToString("N") + "Aa1!";

    private readonly User _user;

    public SessionRecordConcurrentPcGateTests()
    {
        _user = new User
        {
            Id = UserId,
            UserId = UserId,
            TenantId = TenantId,
            Email = "gate@hitpan.kr",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(_password),
            UserName = "게이트",
            AccountType = "tenant_user",
            IsActive = true,
            // 🔴 null 이면 RedirectToWelcome 경로가 섞인다 — 재는 것과 무관한 분기를 끌어들이지 않는다.
            LastLoginAt = DateTime.UtcNow.AddDays(-1)
        };

        // JWT_SECRET 은 db.conf / hitpan-keys.conf 가 없는 환경에서 환경변수로 폴백한다.
        //   ⚠️ 값을 파일에 적지 않는다 — 매 실행 무작위로 만든다(위 _password 와 같은 이유).
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JWT_SECRET")))
        {
            Environment.SetEnvironmentVariable(
                "JWT_SECRET", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 🔴 운영 SQL 복사본이 **여기 있었다.** 지웠다 (절I · 작지 §3 금지 #8).
    //   `InsertSessionSql` · `FindOtherPcSql` · `SessionAliveSql` 세 상수가
    //   운영 문장을 베껴 두고 시험이 그것을 직접 돌렸다 ⇒ 운영 호출을 지워도 초록이었다.
    //   이제 이 파일에는 **판정하는 SQL 이 없다.** 남은 SQL 은 두 종류뿐이다:
    //     · 표를 **읽는** 확인 조회(COUNT·device_kind·information_schema)
    //     · 상태를 **만드는** 준비(tenant_settings 한 줄, 세션 만료시키기)
    //   판정은 전부 운영 코드가 한다.
    // ══════════════════════════════════════════════════════════════

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

        RunSqlFile(ddlPath);
    }

    /// <summary>SQL 파일 하나를 임시 DB 에 그대로 흘린다 (출하 DDL · 마이그 공용).</summary>
    private void RunSqlFile(string path)
    {
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
        proc.StandardInput.Write(File.ReadAllText(path));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0, $"SQL 적용 실패 ({Path.GetFileName(path)}):\n{err}");
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
            Console.Error.WriteLine($"[정리 실패] 임시 DB {_dbName} 를 못 지웠다: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 🔴 운영 코드를 태우는 배선 — 여기가 이 파일의 본체다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>AuthService</c> <b>실물</b>을 격리 DB 에 물려 만든다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>가짜는 두 개뿐이고, 둘 다 판정에 끼어들지 않는다.</b>
    /// <c>IUnitOfWork</c> 는 <b>연결을 넘겨주는 통로</b>이고, <c>IAuthUserLookup</c> 은
    /// <b>계정 한 줄을 돌려주는 조회</b>다. 축 B 판정·세션 기록·토큰 발급은 전부 운영 코드가 한다.
    /// <para>
    /// ⚠️ <c>users</c> 표를 시드하지 않는 이유 — 로그인 경로는 계정을 <c>IAuthUserLookup</c> 으로만 읽는다.
    /// 표를 채우면 EF 매핑까지 끌어들여 <b>재는 것과 무관한 실패</b>가 섞인다.
    /// </para>
    /// </remarks>
    private (AuthService Svc, MySqlConnection Db) NewAuthService()
    {
        var db = new MySqlConnection(DbConnString());
        db.Open();
        return (new AuthService(new GateUnitOfWork(db), new GateUserLookup(_user)), db);
    }

    /// <summary>
    /// G-B29 전용 — <b>자기 연결 · 자기 계정 객체</b>를 가진 <c>AuthService</c> 실물(운영의 요청 하나 = DI 범위 하나 모양).
    /// </summary>
    /// <remarks>
    /// 동시에 도는 두 로그인이 <c>User</c> 객체(실패 횟수·마지막 로그인 칸을 고친다)를 나눠 쓰지 않게 한다 —
    /// 같은 계정(같은 <c>Id</c>·비밀번호 해시)이지만 객체는 따로다. 판정·잠금은 운영 코드와 DB 가 한다.
    /// </remarks>
    private (AuthService Svc, MySqlConnection Db) NewAuthServiceWithOwnUser()
    {
        var user = new User
        {
            Id = _user.Id,
            UserId = _user.UserId,
            TenantId = _user.TenantId,
            Email = _user.Email,
            PasswordHash = _user.PasswordHash,
            UserName = _user.UserName,
            AccountType = _user.AccountType,
            IsActive = true,
            LastLoginAt = _user.LastLoginAt
        };
        var db = new MySqlConnection(DbConnString());
        db.Open();
        return (new AuthService(new GateUnitOfWork(db), new GateUserLookup(user)), db);
    }

    /// <summary>작업 하나의 결과를 <b>예외째</b> 받는다(성공 = <c>null</c>) — 동시 실행 결과를 나란히 세기 위한 것(G-B29).</summary>
    private static async Task<Exception?> CaptureAsync(Func<Task> work)
    {
        try
        {
            await work();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// 준비 (G-B31 대조) — 그 세션 번호로 <b>쓸 수 있는 refresh 한 줄</b>을 붙인다(상태 만들기 · 판정 아님).
    /// </summary>
    /// <remarks>컬럼은 출하 DDL 실측(<c>refresh_tokens</c> — token_id·user_id·token_hash·expires_at 만 NOT NULL · #13).
    /// <c>expires_at</c> 은 앱이 적는 것과 같은 UTC 다.</remarks>
    private static void AttachUsableRefresh(IDbConnection db, string sid) =>
        db.Execute(
            @"INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked, session_id)
              VALUES (UUID(), @UserId, @Hash, UTC_TIMESTAMP(6) + INTERVAL 1 DAY, 0, @Sid)",
            new { UserId, Hash = "gate-b31-" + Guid.NewGuid().ToString("N"), Sid = sid });

    /// <summary>
    /// 운영 코드의 <c>Trace</c> 경고를 모은다(G-B31b) — 읽기만 한다. 판정하지 않는다.
    /// </summary>
    /// <remarks>⚠️ <c>Trace.Listeners</c> 는 전역이다 — 다른 시험이 섞어 쓴 줄이 들어올 수 있어, 찾는 쪽이 문장으로 거른다.</remarks>
    private sealed class CollectingTraceListener : System.Diagnostics.TraceListener
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Messages => _lines.ToArray();

        public override void Write(string? message)
        {
            if (message is not null) _lines.Enqueue(message);
        }

        public override void WriteLine(string? message)
        {
            if (message is not null) _lines.Enqueue(message);
        }
    }

    private LoginRequest NewLoginRequest(
        string? claimedDeviceType, string? userAgent, bool force = false, string? fingerprint = null) => new()
        {
            Email = _user.Email,
            Password = _password,
            DeviceType = claimedDeviceType,
            UserAgent = userAgent,
            ForceSignOutOtherPc = force,
            DeviceFingerprint = fingerprint
        };

    /// <summary>축 B 킬스위치를 <b>고객사 설정 한 줄</b>로 만든다(준비 — 판정은 운영 코드가 읽는다).</summary>
    /// <remarks>
    /// ⬛ 4차: 이 헬퍼가 세우는 것은 <b>축 B(<c>enforce_single_pc_login</c>)뿐</b>이다.
    /// 3차까지는 이 한 줄이 생존 확인까지 함께 켜고 껐다 — 그것이 V-B3 가 지적한 고장이다.
    /// 생존 확인을 세울 때는 <c>SetValiditySwitch</c> 를 쓴다
    /// (전결 → <c>docs/운영기록/20260927_PM전결_VB3_통제분리_결재.md</c> §2).
    /// <para>
    /// ⬛ <b>20260928작2 절F — 세우는 칸이 바뀌었다.</b> 1.3.46 까지는 옛 칸 <c>enforce_single_pc_login</c> 이었고,
    /// 지금 운영 코드는 새 칸 <c>enforce_one_pc_login</c>(DB-131 · 기본 <b>1</b> · 행 없음도 켬)만 읽는다.
    /// ⇒ 이 헬퍼도 <b>새 칸</b>을 세운다. 옛 칸을 세워야 하는 시험(G-B8 ②)은 <c>SetLegacyKillSwitch</c> 를 쓴다.
    /// 🔴 출하 기본이 <b>켬</b>으로 뒤집혔다 — PC 를 두 번 로그인하는 준비는 이 헬퍼로 <c>0</c> 을 <b>명시</b>한다(기대값 아님).
    /// </para>
    /// </remarks>
    private static void SetKillSwitch(IDbConnection db, int on) =>
        db.Execute(
            @"INSERT INTO tenant_settings (tenant_id, enforce_one_pc_login)
              VALUES (@TenantId, @On)
              ON DUPLICATE KEY UPDATE enforce_one_pc_login = @On",
            new { TenantId, On = on });

    /// <summary>
    /// 옛 칸(<c>enforce_single_pc_login</c> · DB-127/128)을 세운다 — <b>G-B8 ② 전용</b>(20260928작2 절F).
    /// </summary>
    /// <remarks>운영 코드는 이 칸을 더 읽지 않는다(#37 — 되돌림 안전장치로만 남아 있다). 마이그 DB-128 이 재는 대상이라 남긴다.</remarks>
    private static void SetLegacyKillSwitch(IDbConnection db, int on) =>
        db.Execute(
            @"INSERT INTO tenant_settings (tenant_id, enforce_single_pc_login)
              VALUES (@TenantId, @On)
              ON DUPLICATE KEY UPDATE enforce_single_pc_login = @On",
            new { TenantId, On = on });

    /// <summary>
    /// 세션 생존 확인 스위치(DB-129 · 4차 신설)를 <b>고객사 설정 한 줄</b>로 만든다(준비).
    /// </summary>
    /// <remarks>
    /// ⚠️ 행이 없으면 만든다 — 이때 축 B 는 출하 기본값(0)으로 들어온다.
    /// 축 B 를 함께 세우려면 <c>SetKillSwitch</c> 를 <b>따로</b> 부른다. 두 스위치는 별개다.
    /// </remarks>
    private static void SetValiditySwitch(IDbConnection db, int on) =>
        db.Execute(
            @"INSERT INTO tenant_settings (tenant_id, enforce_session_validity)
              VALUES (@TenantId, @On)
              ON DUPLICATE KEY UPDATE enforce_session_validity = @On",
            new { TenantId, On = on });

    private static string? SessionKind(IDbConnection db, string sid) =>
        db.ExecuteScalar<string>(
            "SELECT device_kind FROM user_sessions WHERE session_id = @Sid", new { Sid = sid });

    private static int SessionCount(IDbConnection db) =>
        db.ExecuteScalar<int>("SELECT COUNT(*) FROM user_sessions");

    /// <summary>이 사용자의 refresh 토큰 해시 목록(정렬) — 회전이 일어났는지 판정 없이 <b>읽기만</b> 한다(G-B16).</summary>
    private static List<string> RefreshTokenHashes(IDbConnection db) =>
        db.Query<string>(
            "SELECT token_hash FROM refresh_tokens WHERE user_id = @UserId ORDER BY token_hash",
            new { UserId }).ToList();

    private static int AlertCount(IDbConnection db, string type) =>
        db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM security_alerts WHERE alert_type = @Type", new { Type = type });

    /// <summary>발급된 access 토큰에서 <c>sid</c> 클레임을 꺼낸다(없으면 <c>null</c>).</summary>
    private static string? SidOf(LoginResponse response) =>
        new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken)
            .Claims.FirstOrDefault(c => c.Type == "sid")?.Value;

    /// <summary>
    /// <c>user_sessions</c> 를 <b>잠깐 치운다</b> — 운영 INSERT 가 <b>실제로 실패</b>하게 만드는 상태.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>DROP</c> 이 아니라 <c>RENAME</c> 이다. 같은 시험 안에서 <b>원래 스키마 그대로</b>
    /// 되돌려야 하는 게이트(G-B12)가 있고, 표를 다시 만들면 출하 DDL 과 어긋날 수 있다(#36·#13).
    /// </remarks>
    private static void HideSessionTable(IDbConnection db) =>
        db.Execute("RENAME TABLE user_sessions TO user_sessions_hidden_by_gate");

    private static void RestoreSessionTable(IDbConnection db) =>
        db.Execute("RENAME TABLE user_sessions_hidden_by_gate TO user_sessions");

    // ══════════════════════════════════════════════════════════════
    // G-B1 — 신고값을 그대로 믿지 않는다 (판정을 서버로 되돌렸다)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-B1</b> — 운영 <c>LoginAsync</c> 를 태운다: 신고값 <c>"mobile"</c> + 헤더 UA = Windows
    /// ⇒ <c>user_sessions.device_kind = 'pc'</c>.
    /// </summary>
    /// <remarks>
    /// [4] 반려 F-4 — 판정 근거가 <b>클라이언트 자진신고 한 칸</b>이라 화면에서 <c>"mobile"</c> 이라고
    /// 보내면 축 B 차단을 그대로 빠져나갔다.
    /// <para>
    /// 🔴 음성 대조군 — <c>LoginAsync</c> 의
    /// <c>DeviceTypeResolver.ResolveDeviceType(request.DeviceType, request.UserAgent)</c> 한 줄을
    /// 신고값 단독 판정(옛 <c>NormalizeDeviceKind</c> 식)으로
    /// 되돌리면 <c>'mobile'</c> 이 적혀 FAIL 한다. 그 <b>판정축이 실제로 갈라지는지</b>는
    /// DB 없이도 재진다 — <c>G_B1c</c> 참조.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task G_B1_신고값이_mobile_이어도_서버가_읽은_UA_가_컴퓨터면_pc_로_적힌다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B1 서버 판정 복귀")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            var response = await svc.LoginAsync(NewLoginRequest("mobile", WindowsUa));

            var sid = SidOf(response);
            Assert.NotNull(sid);
            Assert.Equal("pc", SessionKind(db, sid!));
        }
    }

    /// <summary>
    /// 🔴 <b>G-B1c</b> — 음성 대조군의 <b>판정축</b>을 DB 없이 잰다:
    /// 같은 입력에서 <b>옛 규칙과 새 규칙의 답이 다르다.</b>
    /// </summary>
    /// <remarks>
    /// [왜 따로 두나] G-B1 의 완전한 FAIL 확인은 운영 코드를 되돌려 CI <c>db-gate</c> 에서 돌려야 한다.
    /// 그런데 그 전에 <b>고른 입력이 애초에 옛·새를 가르는 입력인지</b>부터 증명해야 한다 —
    /// 안 갈라지는 입력으로 시험을 세우면 봉합을 빼도 초록이다(F-2 의 본질).
    /// <para>
    /// ⚠️ 이 시험은 <b>G-B1 을 대신하지 않는다.</b> 판정 규칙만 재고, <c>user_sessions</c> 에 실제로
    /// 무엇이 적히는지는 재지 않는다.
    /// </para>
    /// </remarks>
    [Fact]
    public void G_B1c_옛_신고값단독_판정과_새_교차검증_판정은_같은_입력에서_갈린다()
    {
        // 옛 규칙 = 신고값만 본다(제거된 AuthService.NormalizeDeviceKind 와 같은 식)
        var oldRule = DeviceTypeResolver.ToSessionDeviceKind(
            DeviceTypeResolver.NormalizeDeviceType("mobile"));

        // 새 규칙 = 서버가 읽은 User-Agent 로 교차검증한다(8/18 V-05)
        var newRule = DeviceTypeResolver.ToSessionDeviceKind(
            DeviceTypeResolver.ResolveDeviceType("mobile", WindowsUa));

        Assert.Equal("mobile", oldRule);
        Assert.Equal("pc", newRule);
        Assert.NotEqual(oldRule, newRule);
    }

    // ══════════════════════════════════════════════════════════════
    // G-B2 — 세션 행이 실제로 생긴다 (F-2 가 못 잡던 그 자리)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>G-B2</b> — 운영 <c>LoginAsync</c> <b>1회</b> ⇒ <c>user_sessions</c> <b>1건</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>이 트랙이 존재하는 이유 그 자체다.</b> 세는 곳·지우는 곳은 있는데 <b>넣는 코드가 0건</b>이라
    /// 표가 늘 비어 있었고, 동시접속 제한이 한 번도 걸린 적이 없었다.
    /// <para>
    /// 🔴 음성 대조군 — <c>LoginAsync</c> 의 <c>InsertSessionAsync(...)</c> <b>호출 줄을 주석</b>하면
    /// 0건이 되어 FAIL 한다. <b>종전 게이트는 이 자리를 못 잡았다</b>(자기가 쓴 INSERT 를 자기가 돌렸으므로).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task G_B2_운영_로그인_한번이_세션_행_한건을_만든다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B2 세션 기록 실물")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            Assert.Equal(0, SessionCount(db));   // 신규 설치는 비어 있다 — 출발점을 확인하고 시작한다

            await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));

            Assert.Equal(1, SessionCount(db));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B3 — 종단: 서버가 읽은 헤더가 본문 신고값을 이긴다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-B3</b> — <c>AuthController.Login</c> <b>실물</b>을 태운다:
    /// 본문 <c>userAgent = "iPhone…"</c> + 헤더 UA = Windows ⇒ <c>'pc'</c>.
    /// </summary>
    /// <remarks>
    /// <c>LoginRequest</c> 가 <c>[FromBody]</c> 라 클라이언트가 <c>userAgent</c> 를 본문에 실을 수 있다.
    /// 컨트롤러가 <b>덮어쓰지 않으면</b> 자진신고 칸이 하나 더 생겨 8/18 V-05 가 무력해진다.
    /// <para>🔴 음성 대조군 — <c>AuthController.Login</c> 의
    /// <c>request.UserAgent = Request.Headers["User-Agent"]…</c> <b>덮어쓰기 줄을 제거</b>하면
    /// 본문값이 이겨 <c>'mobile'</c> 이 적혀 FAIL 한다.</para>
    /// <para>
    /// ⚠️ <c>WebApplicationFactory</c> 가 아니다 — 이 레포엔 그 참조가 없다(클래스 주석 ③).
    /// <c>DefaultHttpContext</c> 로 컨트롤러 실물을 돌리는 선례
    /// (<c>LegacyUnpostedViewTenantGateTests:206</c>)를 따른다.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task G_B3_종단_본문에_아이폰이라_적어도_헤더가_컴퓨터면_pc_로_적힌다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B3 종단 헤더 우선")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            var ctl = NewAuthController(svc, db, WindowsUa);

            // 🔴 본문에는 휴대기기라고 적어 보낸다 — 공격자가 실제로 하는 그것이다.
            var body = NewLoginRequest("mobile", IPhoneUa);
            var result = await ctl.Login(body, CancellationToken.None);

            var response = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result).Value;
            var login = Assert.IsType<LoginResponse>(response);

            var sid = SidOf(login);
            Assert.NotNull(sid);
            Assert.Equal("pc", SessionKind(db, sid!));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B4 / G-B10 / G-B14 / G-B15 — 미들웨어가 어느 스위치를 읽는가
    //
    // ⬛ 2026-09-27 봉합 4차에서 **G-B4 의 기대값이 뒤집혔다.**
    //   옛 줄(3차까지): "축 B 를 끄면 세션 행이 없어도 통과" ← 이제 **틀리다.**
    //   지금(4차):      "축 B 를 꺼도 죽은 세션은 401" — 생존 확인은 축 B 와 무관해졌다.
    //   사유 → docs/운영기록/20260927_PM전결_VB3_통제분리_결재.md §2 (안 다 — 통제를 두 스위치로 분리)
    //   🔴 옛 줄을 지우지 않고 ⬛ 로 남긴다. 기대값이 **뒤집혔다는 사실 자체**가 다음 사람에게
    //     가장 중요한 정보다 — 지우면 *"원래 그랬던 것"* 으로 읽힌다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>G-B4 (4차 개정)</b> — <c>sid</c> 있는 토큰 + 세션 <b>죽음</b> +
    /// <b>축 B 스위치 <c>0</c></b>(+ 생존 확인은 기본 <c>1</c>) ⇒ 보호 API <b>401</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⬛ <b>3차까지의 이 게이트</b>: <c>G_B4_킬스위치가_꺼져_있으면_세션_행이_없어도_통과한다</c> —
    /// *"세션 행 없음 + 축 B <c>0</c> ⇒ <b>200</b>"* 을 고정했다.
    /// 🔴 그 기대값이 <b>바로 V-B3 가 지적한 고장</b>이었다: 축 B 는 기본 OFF 로 출하하므로
    /// (DB-128) 그 상태에서 <b>로그아웃·밀어내기가 액세스 토큰 수명(8시간) 내내 먹지 않았다.</b>
    /// 4차는 생존 확인을 별 스위치로 떼어냈고, 그래서 <b>축 B 를 꺼도 죽은 세션은 401</b> 이다.
    /// </para>
    /// <para>
    /// 🔴 <b>음성 대조군</b> — 미들웨어의 생존 확인 스위치 판독을 옛 컬럼
    /// (<c>enforce_single_pc_login</c>)으로 되돌리면, 이 시험이 축 B 를 <c>0</c> 으로 두었으므로
    /// 곧바로 통과해 <b>200</b> 이 되어 FAIL 한다. ⇒ *"어느 컬럼을 읽는가"* 를 실제로 가르는 시험이다.
    /// 🟢 <b>실측(2026-09-27 · 로컬)</b>: 미들웨어가 읽는 컬럼을 <c>enforce_single_pc_login</c> 으로
    /// 되돌려 <b>FAIL(기대 401 · 실제 200)</b> 을 확인한 뒤 되돌렸다.
    /// ⚠️ <b>단, 이 게이트 자체로 잰 것이 아니다</b> — 개발 PC 는 <c>CREATE DATABASE</c> 거부라
    /// 이 게이트가 <c>[SKIP]</c> 이다. 같은 미들웨어 실물을 권한 있는 시험 DB 에 물린
    /// <b>임시 탐침</b>으로 쟀고 그 파일은 지웠다(명세서 §4-3). <b>게이트 형태의 계측은 CI <c>db-gate</c> 뿐이다.</b>
    /// </para>
    /// <para>🔴 판정 자리도 같이 잰다 — 스위치는 <c>sid</c> 판독 <b>뒤</b>, 생존 조회 <b>앞</b>이어야 한다.
    /// 앞에 두면 <c>sid</c> 없는 옛 토큰까지 DB 를 한 번 물고(금지 #1b 경로), 뒤에 두면 이미 401 이 나간 뒤다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B4_축B_를_꺼도_죽은_세션은_401_이다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B4 축B 끔 → 그래도 401")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();
        // ⬛ [낡은 주석] 「출하 기본 상태(DB-128) 그대로」 — 20260928작2 부터 출하 기본은 **켬**(DB-131)이다.
        //   이 시험은 여전히 축 B 를 **명시적으로 꺼** 두고 「꺼도 죽은 세션은 401」 을 잰다(기대값 불변).
        SetKillSwitch(db, 0);   // 🔴 축 B 는 꺼 둔다

        // 🔴 양성 조건을 명시한다 — 생존 확인은 출하 기본값(1)으로 켜져 있어야 한다(DB-129).
        //   여기서 값을 직접 세우지 않는 이유: **출하 DDL 이 넣어 준 기본값이 실제로 1 인지**를
        //   이 게이트가 함께 재게 한다(세우면 DDL 이 틀려도 초록이 된다).
        Assert.Equal(1, CurrentValiditySwitch(db));

        var (ctx, nextCalled) = await RunSessionValidityAsync(db, sid: "sid-not-in-table");

        Assert.False(nextCalled,
            "축 B 를 껐다는 이유로 죽은 세션이 통과했다 — 로그아웃·밀어내기가 안 먹는다(V-B3).");
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        Assert.Equal(0, SessionCount(db));   // 행이 없는 상태였음을 확인한다(양성 조건 확인)
    }

    /// <summary>
    /// 🔴 <b>G-B10</b> — <c>sid</c> 있음 + 세션 <b>죽음</b> + 생존 확인 스위치 <b>ON</b> ⇒ <b>401</b>.
    /// </summary>
    /// <remarks>
    /// 작1 에서 <b>무시험</b>이던 분기다 — F-1 잠금이 실제로 나오는 자리이므로,
    /// *"켜면 끊는다"* 가 성립하는지를 반대편에서 고정한다.
    /// <para>⬛ 4차: 세우는 스위치가 축 B → <c>enforce_session_validity</c> 로 바뀌었다(전결 §2).
    /// 축 B 는 <b>일부러 켜 둔다</b> — 두 스위치가 다 켜진 경우에도 401 이 나오는지를 이쪽이 맡는다.</para>
    /// <para>🔴 음성 대조군 — 세션 생존 확인(<c>session-alive</c> 조회와 <c>if (!alive)</c>)을 제거하면
    /// 200 이 되어 FAIL 한다.
    /// 🟢 <b>실측(2026-09-27 · 로컬)</b>: <c>if (!alive)</c> 블록을 죽여
    /// <b>FAIL(기대 401 · 실제 200)</b> 을 확인한 뒤 되돌렸다.
    /// ⚠️ 이 게이트가 아니라 <b>임시 탐침</b>으로 쟀다(G-B4 주석과 같은 사정 · 명세서 §4-3).</para>
    /// </remarks>
    [Fact]
    public async Task G_B10_생존확인이_켜져_있고_세션이_죽었으면_401_이다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B10 생존확인 켬 → 401")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();
        SetKillSwitch(db, 1);         // 축 B 도 켠 상태 — 둘 다 켜져도 결과는 같아야 한다
        SetValiditySwitch(db, 1);

        var (ctx, nextCalled) = await RunSessionValidityAsync(db, sid: "sid-already-dead");

        Assert.False(nextCalled, "죽은 세션인데 요청이 통과했다 — 밀어내기가 즉시 반영되지 않는다.");
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    /// <summary>
    /// 🔴🔴 <b>G-B14 (4차 신설)</b> — <c>enforce_session_validity = 0</c> 이면
    /// 죽은 세션이어도 <b>통과(200)</b>. <b>비상 스위치가 실제로 듣는가.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>왜 이 게이트가 있어야 하나 — 비상 탈출구가 없으면 F-1 이 돌아온다.</b>
    /// 4차는 생존 확인을 <b>기본 켬</b>으로 출하한다. 그 상태에서 세션 행이 어떤 이유로든
    /// 사라지면(초기화·수동 수술) 그 사람은 401 을 계속 맞는다. 재로그인이 정상 복구 경로지만,
    /// 그것도 안 되는 상황에서 <b>DB 한 줄로 끌 수 있어야</b> 한다(전결 §3).
    /// 전결문이 *"안 나(스위치를 없애고 항상 검사)를 고르지 않은"* 이유가 바로 이 줄이다.
    /// </para>
    /// <para>
    /// 🔴 <b>음성 대조군</b> — 미들웨어의 스위치 판독 한 줄
    /// (<c>if (!await IsSessionValidityEnforcedAsync(...)) { await _next(context); return; }</c>)을
    /// 제거하면 스위치가 <c>0</c> 이어도 생존 조회로 내려가 <b>401</b> 이 되어 FAIL 한다.
    /// 🟢 <b>실측(2026-09-27 · 로컬)</b>: 그 줄을 죽여 <b>FAIL(기대 200 · 실제 401)</b> 을 확인한 뒤 되돌렸다.
    /// ⚠️ 이 게이트가 아니라 <b>임시 탐침</b>으로 쟀다(G-B4 주석과 같은 사정 · 명세서 §4-3).
    /// 🟢 같은 탐침으로 <b>판정축 분리</b>도 쟀다: 축B=0·생존=1 ⇒ <b>401</b> / 축B=1·생존=0 ⇒ <b>200</b>
    /// ⇒ 미들웨어가 실제로 <b>새 컬럼</b>을 읽는다. 컬럼이 아예 없는 옛 DB ⇒ <b>200</b>(fail-open 성립).
    /// </para>
    /// <para>
    /// ⚠️ <b>이 스위치에는 아직 쓰기 경로(화면)가 0건이다</b>(전결 §5). 지금은 DB 한 줄로만 끈다.
    /// 그래서 이 게이트도 화면이 아니라 <b>DB 값</b>을 세워 잰다 — 그것이 지금의 유일한 조작 경로다.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task G_B14_생존확인을_끄면_죽은_세션이어도_통과한다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B14 비상 스위치 끔 → 통과")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();
        SetKillSwitch(db, 1);          // 🔴 축 B 는 켜 둔다 — 끄는 것은 이 새 스위치 하나여야 한다
        SetValiditySwitch(db, 0);      // 비상 끔

        var (ctx, nextCalled) = await RunSessionValidityAsync(db, sid: "sid-not-in-table");

        Assert.True(nextCalled,
            "비상 스위치를 0 으로 내렸는데 요청이 끊겼다 — 고객이 끌 수 없는 전면 잠금(F-1)이 남아 있다.");
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    /// <summary>
    /// 🔴 <b>G-B15 ① (4차 신설)</b> — 빈 DB + <b>출하 DDL</b>(#36) ⇒
    /// <c>enforce_session_validity</c> 기본값 <b>1</b>.
    /// </summary>
    /// <remarks>
    /// 생존 확인은 <b>정합성</b>이므로 켜서 출하한다(전결 §2). 기본값이 0 이면
    /// 신규설치 고객이 <b>3차와 똑같은 상태</b>(로그아웃이 8시간 안 먹는다)로 나간다.
    /// <para>🔴 음성 대조군 — 출하 DDL 의 그 컬럼을 <c>DEFAULT 0</c> 으로 바꾸면 FAIL 한다.
    /// 🟢 <b>실측(2026-09-27 · hitpan_trgtest)</b>: 출하 DDL 의 <c>tenant_settings</c> 정의를
    /// 그대로 떠서 돌려 <c>COLUMN_DEFAULT = 1</c> · 새 행 값 <c>1</c> 을 확인했다(#13).</para>
    /// <para>⚠️ 축 B 는 이 시험에서도 <b>0</b> 이어야 한다 — 두 스위치의 방향이 반대라는 사실을 함께 고정한다.</para>
    /// </remarks>
    [Fact]
    public void G_B15_1_출하DDL_신규설치에서_생존확인_기본값은_1_이다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B15① 출하 DDL 기본값 1")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();

        Assert.Equal("1", ColumnDefault(db, "enforce_session_validity"));
        Assert.Equal("0", ColumnDefault(db, "enforce_single_pc_login"));   // 🔴 방향이 반대다

        // 실제로 들어오는 행의 값도 1 인가 — 기본값만 맞고 값이 0 이면 통제는 안 돈다.
        db.Execute("INSERT INTO tenant_settings (tenant_id) VALUES (@TenantId)", new { TenantId });
        Assert.Equal(1, CurrentValiditySwitch(db));
    }

    /// <summary>
    /// 🔴 <b>G-B15 ② (4차 신설)</b> — <b>컬럼이 없던 기존 DB</b> + <c>DB-129</c> ⇒
    /// 기본값 <b>1</b> <i>그리고</i> <b>이미 있던 행의 값도 1</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>이것이 DB-128 이 물려준 급소다.</b> <c>ADD COLUMN IF NOT EXISTS</c> 만 써 두면
    /// 나중에 기본값을 고쳐도 <b>이미 적용한 DB 가 한 줄도 안 바뀐다.</b>
    /// 여기서는 컬럼이 신설이므로 MariaDB 가 기존 행에 <c>DEFAULT</c> 값을 채워 넣는데,
    /// <b>그 사실을 믿지 않고 잰다</b> — 채워지지 않으면 기존 고객의 통제가 꺼진 채 남는다.
    /// 🟢 <b>실측(2026-09-27 · hitpan_trgtest · MariaDB 11.4.10)</b>: 행 2건이 있는 표에
    /// DB-129 를 돌려 두 행 모두 <c>1</c>, 기본값 <c>1</c>. 재적용도 <c>EXIT=0</c>.
    /// </para>
    /// <para>
    /// 🔴 <b>음성 대조군</b> — DB-129 의 ② <c>MODIFY</c> 문장을 지우고, 컬럼이 <c>DEFAULT 0</c> 으로
    /// 이미 있는 DB 에서 돌리면 <c>ADD … IF NOT EXISTS</c> 가 건너뛰어 기본값이 <b>0</b> 으로 남아 FAIL 한다.
    /// 🟢 <b>실측(2026-09-27 · hitpan_trgtest)</b>: 그 모양을 만들어 확인했다 — 아래 시험이 그 상태를 그대로 만든다.
    /// </para>
    /// <para>
    /// 🔴 <b>값은 일부러 뒤집지 않는다</b>(DB-129 머리말 ③). 이 컬럼은 <b>0 이 정당한 고객 상태</b>(비상 끔)라서,
    /// 전면 <c>UPDATE</c> 를 넣으면 비상으로 끈 고객이 다음 업데이트에서 소리 없이 다시 켜진다.
    /// ⇒ 이 시험은 <b>컬럼이 없던</b> 기존 DB 만 잰다. 그것이 마이그가 책임지는 범위다.
    /// </para>
    /// </remarks>
    [Fact]
    public void G_B15_2_기존DB_에_DB129_을_적용하면_기본값과_기존행_모두_1_이_된다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B15② DB-129 기존 DB")) return;

        var migration = Path.Combine(
            RepoRoot(), "src", "HitPan.API", "Migrations", "SQL",
            "DB-129_tenant_settings_session_validity.sql");
        Assert.True(File.Exists(migration),
            $"DB-129 이 없다: {migration}\n"
          + "  이 파일이 없으면 기존 고객 DB 에 생존 확인 스위치 컬럼이 안 생긴다 —\n"
          + "  미들웨어 조회가 실패해 fail-open 으로 떨어지고, 로그아웃이 8시간 안 먹는 3차 상태가 남는다.");

        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();

        // 🔴 DB-128 직후(= 이 마이그 직전)의 모양을 **명시적으로** 만든다 — 컬럼이 아예 없고, 행이 2건 있다.
        //   ①의 결과에 기대지 않는다: 출하 DDL 이 고쳐지는 순간 ②가 무엇을 쟀는지 모르게 된다.
        db.Execute("ALTER TABLE tenant_settings DROP COLUMN enforce_session_validity");
        db.Execute("INSERT INTO tenant_settings (tenant_id) VALUES (@TenantId), ('99999999-9999-9999-9999-999999999999')",
            new { TenantId });
        Assert.Null(ColumnDefault(db, "enforce_session_validity"));   // 컬럼이 없음을 확인한다

        RunSqlFile(migration);

        Assert.Equal("1", ColumnDefault(db, "enforce_session_validity"));
        Assert.Equal(1, CurrentValiditySwitch(db));                    // 🔴 기존 행에도 값이 들어왔는가
        Assert.Equal(2, db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM tenant_settings WHERE enforce_session_validity = 1"));

        // 🔴 ② MODIFY 를 재는 자리 — 컬럼이 DEFAULT 0 으로 이미 있는 DB 에서도 기본값이 1 로 고쳐지는가.
        //   (ADD … IF NOT EXISTS 는 여기서 통째로 건너뛰어진다 — DB-128 이 물려준 급소)
        db.Execute(
            "ALTER TABLE tenant_settings MODIFY enforce_session_validity tinyint(1) NOT NULL DEFAULT 0");
        Assert.Equal("0", ColumnDefault(db, "enforce_session_validity"));

        RunSqlFile(migration);

        Assert.Equal("1", ColumnDefault(db, "enforce_session_validity"));   // MODIFY 가 없으면 0 이 남는다
    }

    // ══════════════════════════════════════════════════════════════
    // G-B5 — 기록 실패 시 sid 를 싣지 않는다 (잠금이 원리적으로 안 생긴다)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>G-B5</b> — 세션 INSERT 가 <b>실패하는 상태</b>에서 로그인 ⇒
    /// <b>200</b> + 토큰에 <c>sid</c> <b>없음</b> + <c>security_alerts</c> <b>1건</b>.
    /// </summary>
    /// <remarks>
    /// 행이 없는데 번호를 실으면 미들웨어가 못 찾아 <b>모든 요청이 401</b> 이 된다([4] 반려 F-1).
    /// <para>🔴 음성 대조군 — <c>LoginAsync</c> 의
    /// <c>CreateLoginResponse(..., sessionRecorded ? sessionId : null)</c> 을
    /// 항상 <c>sessionId</c> 로 되돌리면 <c>sid</c> 가 실려 FAIL 한다.</para>
    /// <para>
    /// ⚠️ 표를 치워 실패를 만든다 — <b>옛 고객 DB(마이그 미적용)와 같은 모양</b>이고,
    /// 격리 DB 안에서만 한다(#39).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task G_B5_세션_기록이_실패하면_토큰에_sid_가_안_실리고_흔적이_남는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B5 기록 실패 → sid 미탑재")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            HideSessionTable(db);   // 운영 INSERT 가 실제로 실패한다

            var response = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));

            Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));   // 로그인은 막지 않는다
            Assert.Null(SidOf(response));                                    // 🔴 번호를 싣지 않는다
            Assert.Equal(1, AlertCount(db, "session_insert_failed"));        // 조용히 넘기지 않는다(#15)
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B6 — 갱신이 만료된 PC 세션을 되살려도 되는지 판정한다 (F-6)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-B6 (양)</b> — 만료된 내 PC 세션 + 다른 PC <b>없음</b> ⇒ <c>RefreshAsync</c> <b>되살린다</b>.
    /// </summary>
    /// <remarks>🔴 <b>밤샘 근무는 정상 업무다</b>(#20). 8시간이 지났을 뿐이면 끊지 않는다.</remarks>
    [Fact]
    public async Task G_B6_양_만료된_내PC_뿐이면_갱신이_되살린다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B6 양 밤샘 되살림")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);   // 켜 둔 상태에서도 밤샘은 통과해야 한다

            var first = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid = SidOf(first);
            Assert.NotNull(sid);
            ExpireSession(db, sid!);

            var refreshed = await svc.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = first.RefreshToken, UserAgent = WindowsUa });

            Assert.Equal(sid, SidOf(refreshed));   // 같은 세션을 이어받는다(행이 쌓이지 않는다)
            Assert.Equal(1, SessionCount(db));
            Assert.True(IsAlive(db, sid!), "다른 PC 가 없는데 밤샘 갱신이 되살려지지 않았다 (#20).");
        }
    }

    /// <summary>
    /// 🔴 <b>G-B6 (음)</b> — 만료된 내 PC + <b>살아 있는 다른 PC</b> ⇒ 갱신 <b>거절</b>(401).
    /// </summary>
    /// <remarks>
    /// [4] 반려 F-6 — 갱신이 <c>sid</c> 만 보고 <c>expires_at</c> 를 무조건 밀어
    /// <b>만료된 PC1 이 부활해 PC2 와 영구 공존</b>했다. 「같은 계정 PC 1대」가 갱신 한 번으로 무력해진다.
    /// <para>🔴 음성 대조군 — <c>GuardExpiredPcSessionRevivalAsync</c> 의 <i>"다른 PC 생존"</i> 조회를
    /// 제거하면 둘 다 되살아나 <b>예외가 안 나와</b> FAIL 한다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B6_음_만료된_내PC_인데_다른PC_가_살아있으면_갱신을_거절한다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B6 음 다른 PC 생존")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            // PC1 — 운영 로그인으로 만든다
            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid1 = SidOf(pc1);
            Assert.NotNull(sid1);

            // ⬛ [낡은 준비 · 1.3.46 까지] PC2 를 `force: true` 로 들여 PC1 을 밀어낸 뒤 `ReviveSessionRowAsExpired` 로
            //   PC1 행을 만료 상태로 되살렸다. 🔴 [선행 1-5] 그때도 PC2 로그인의 **계정 전삭**이 PC1 refresh 를 지워
            //   가드에 **닿지 못했다**(「로그아웃된 토큰」에서 먼저 막혔다 — 이 시험은 가드를 한 번도 안 쟀다).
            // 🔴 [지금 · 20260928작2 절F 보정] force 로는 들어갈 수 없다(K-4 다) ⇒ 준비 순서를 바꾼다:
            //   PC1 을 **만료**로 → PC2 **비강제** 로그인(만료라 409 없음) → PC1 refresh 를 되돌려 **가드만** 재게 격리.
            ExpireSession(db, sid1!);

            var pc2 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid2 = SidOf(pc2);
            Assert.NotNull(sid2);

            // PC2 로그인의 **죽은 PC 정리**(설계 §3-1)가 PC1 refresh 를 폐기했다 — 그대로면 폐기 검사에서 먼저 막혀
            //   가드에 또 못 닿는다. ⇒ PC1 refresh 만(그 session_id 한정) 되살려 가드를 격리한다(준비).
            Assert.Equal(1, db.Execute(
                "UPDATE refresh_tokens SET is_revoked = 0 WHERE user_id = @UserId AND session_id = @Sid AND is_revoked = 1",
                new { UserId, Sid = sid1 }));   // 양성 조건 — 정리가 실제로 1건 폐기했었다(G-B20 과 교차 확인)

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.RefreshAsync(
                    new RefreshTokenRequest { RefreshToken = pc1.RefreshToken, UserAgent = WindowsUa }));

            // 고객 언어로만 말한다 — 개발용어 금지
            Assert.DoesNotContain("session", ex.Message, StringComparison.OrdinalIgnoreCase);
            // 🔴 거절의 **출처**를 고정한다 — 가드 ⑦ 의 문장이어야 한다. 「로그아웃된 토큰」이면 격리 실패다.
            Assert.DoesNotContain("로그아웃된", ex.Message, StringComparison.Ordinal);
            Assert.Contains("다른 컴퓨터에서 사용 중", ex.Message, StringComparison.Ordinal);
            Assert.False(IsAlive(db, sid1!), "거절했는데도 PC1 세션이 되살아났다 — PC 2대가 공존한다(F-6).");
            Assert.True(IsAlive(db, sid2!), "PC2 가 살아 있어야 가드 ⑦ 을 잰 것이다(양성 조건).");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B16 — 행 없는 sid 의 갱신은 「갇힘」이 아니라 재로그인이다 (20260928작1 절B · R-1)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-B16 ① (20260928작1 신설)</b> — 생존 확인 <b>켬</b> + 세션 행이 <b>지워진</b> <c>sid</c> 로 갱신
    /// ⇒ <b>401</b> · refresh 토큰 <b>회전 안 됨</b> · 행 <b>되살아나지 않음</b>.
    /// </summary>
    /// <remarks>
    /// [선행검증 R-1] 종전 갱신은 행이 없으면 통과시켜(「② 행 없음 — 현행 유지」) <b>같은 죽은 <c>sid</c></b> 로 200 을 줬다.
    /// 미들웨어는 그 <c>sid</c> 를 401 로 끊으므로 화면은 401 → 갱신 성공 → 재시도 401 을 되풀이하고,
    /// 「갱신 성공」 갈래라 토큰을 안 지워 <b>로그인 화면으로도 못 간다</b>(갇힘).
    /// ⇒ 갱신이 미들웨어와 <b>같은 답</b>(죽은 세션 = 401)을 내야 화면이 토큰을 지우고 재로그인으로 간다.
    /// <para>🔴 음성 대조군 — <c>GuardExpiredPcSessionRevivalAsync</c> 의 「② 행 없음」 갈래를
    /// 종전 <c>return</c> 으로 되돌리면 <b>예외가 안 나와</b> FAIL 한다.</para>
    /// <para>⚠️ 양성 조건 — 토큰에 <c>sid</c> 가 실렸고(이어받은 경로를 탄다) 행 삭제가 1건이어야
    /// 이 시험이 「행 없는 sid」를 잰 것이다. 둘 중 하나라도 아니면 아무것도 안 잰 초록이다.</para>
    /// <para>⚠️ 행은 운영 로그인이 만들고, 지우는 것만 시험이 한다(준비) — 판정은 운영 코드가 한다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B16_1_생존확인_켬_행없는_sid_의_갱신은_401_이고_토큰을_회전하지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B16 ① 행 없는 sid 갱신 → 401")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetValiditySwitch(db, 1);

            var first = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid = SidOf(first);
            Assert.NotNull(sid);   // 양성 조건 ⓐ — sid 가 실려야 이어받은 경로(Guard)를 탄다

            var removed = db.Execute("DELETE FROM user_sessions WHERE session_id = @Sid", new { Sid = sid });
            Assert.Equal(1, removed);   // 양성 조건 ⓑ — 「행 없는 sid」 상태를 실제로 만들었다

            var hashesBefore = RefreshTokenHashes(db);

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.RefreshAsync(
                    new RefreshTokenRequest { RefreshToken = first.RefreshToken, UserAgent = WindowsUa }));

            // 고객 언어로만 말한다 — 개발용어 금지
            Assert.DoesNotContain("session", ex.Message, StringComparison.OrdinalIgnoreCase);
            // 🔴 [4] F-1 — 거절의 **출처**를 고정한다. 앞단의 다른 401(「로그아웃된 토큰」 등)로 통과하면
            //   「② 행 없음」 갈래를 잰 것이 아니다. 문장은 Guard 「② 행 없음」 갈래만 쓴다.
            Assert.Contains("접속이 종료되었습니다", ex.Message, StringComparison.Ordinal);
            Assert.Equal(hashesBefore, RefreshTokenHashes(db));   // 거절은 회전 **앞** — 새 토큰을 굽지 않았다
            Assert.Equal(0, SessionCount(db));                    // 되살리지도 새로 만들지도 않았다(작1 절C)
        }
    }

    /// <summary>
    /// 🔴 <b>G-B16 ② (20260928작1 신설)</b> — 생존 확인 <b>끔</b>(비상 스위치 0) + 같은 상태 ⇒ <b>종전 그대로 통과</b>.
    /// </summary>
    /// <remarks>
    /// 비상 스위치는 「종전 동작 완전 복귀」여야 의미가 있다 — 끈 고객사에서는 미들웨어도 통과시키므로
    /// 갱신이 끼어들어 끊을 이유가 없다(설계 §2 B 스위치 연동).
    /// <para>🔴 음성 대조군 — 거절을 스위치와 무관하게 걸면(스위치 조회를 빼면) 여기서 401 이 나와 FAIL 한다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B16_2_생존확인_끔이면_행없는_sid_의_갱신은_종전대로_통과한다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B16 ② 스위치 끔 → 종전 통과")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetValiditySwitch(db, 0);

            var first = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid = SidOf(first);
            Assert.NotNull(sid);
            Assert.Equal(1, db.Execute("DELETE FROM user_sessions WHERE session_id = @Sid", new { Sid = sid }));

            var refreshed = await svc.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = first.RefreshToken, UserAgent = WindowsUa });

            Assert.Equal(sid, SidOf(refreshed));   // 종전 그대로 — 같은 sid 를 이어 싣는다
            Assert.Equal(0, SessionCount(db));     // 되살리지 않는다(UPDATE 0행 · 작1 절C 유지)
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B7 — 로그아웃은 내 세션만 지운다 (F-5)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-B7</b> — PC + 모바일 2건에서 <b>모바일 <c>sid</c> 로 로그아웃</b> ⇒ PC 행 <b>생존</b>.
    /// </summary>
    /// <remarks>
    /// [4] 반려 F-5 — 로그아웃이 <c>WHERE user_id</c> 로 <b>전삭</b>해서, 휴대폰에서 로그아웃하면
    /// <b>일하던 컴퓨터가 401</b> 로 끊겼다. 모바일은 FREE 이고 PC 와 동시 접속이 허용된다(9/25 결재).
    /// <para>🔴 음성 대조군 — <c>AuthController</c> 로그아웃의
    /// <c>DELETE FROM user_sessions WHERE session_id = @Sid</c> 를
    /// <c>WHERE user_id</c> 로 되돌리면 PC 행이 사라져 FAIL 한다.</para>
    /// <para>⚠️ 두 세션 모두 <b>운영 로그인</b>으로 만든다 — 시험이 행을 심으면 무엇을 쟀는지 증명되지 않는다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B7_모바일_로그아웃은_컴퓨터_세션을_끊지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B7 sid 단위 로그아웃")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            // ⬛ [낡은 주석] 「킬스위치는 꺼 둔다」 — 행 없음 = 끔이던 1.3.46 까지의 사정이다.
            //   🔴 20260928작2 부터 행 없음 = **켬**이다. PC 1 + 모바일 1 이라 켜져 있어도 막히지 않는다(모바일 FREE).
            var pc = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var mobile = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));

            var pcSid = SidOf(pc);
            var mobileSid = SidOf(mobile);
            Assert.NotNull(pcSid);
            Assert.NotNull(mobileSid);
            Assert.NotEqual(pcSid, mobileSid);
            Assert.Equal("pc", SessionKind(db, pcSid!));
            Assert.Equal("mobile", SessionKind(db, mobileSid!));

            var ctl = NewAuthController(svc, db, IPhoneUa, sid: mobileSid);
            await ctl.Logout(CancellationToken.None);

            Assert.Null(SessionKind(db, mobileSid!));                 // 내 세션은 지워졌다
            Assert.Equal("pc", SessionKind(db, pcSid!));              // 🔴 컴퓨터는 살아 있다

            // 🔴 20260928작2 절F 보정 — 행만 보던 **헛초록**을 없앤다. 컴퓨터가 실제로 **계속 쓸 수 있나**
            //   (갱신 2회 성공)까지 본다. 1.3.46 은 로그아웃이 계정 refresh 를 전부 폐기해 행은 살아도 갱신이 401 이었다.
            var pcAfter = await RefreshTwiceAsync(svc, pc, WindowsUa);
            Assert.Equal(pcSid, SidOf(pcAfter));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B8 — 축 B 는 신규설치·기존DB 양쪽에서 꺼진 채 출하된다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-B8 ①</b> — 빈 DB + <b>출하 DDL</b>(#36) ⇒ <c>enforce_single_pc_login</c> 기본값 <b>0</b>.
    /// </summary>
    /// <remarks>
    /// 축 B 는 이번 차수에 <b>기본 OFF 로 출하</b>한다(사장님 전결 §1 · 작지 §3 금지 #3c).
    /// 기본값이 1 이면 <b>고객이 끄지도 못하는 상태로 켜진다</b>(설정 화면에 쓸 행 자체가 없는 DB 가 있다).
    /// <para>🔴 <b>절G(갈래 C1) 산출물을 재는 게이트다.</b> 2026-09-27 이 시험을 쓴 시점의 실측:
    /// <c>installer/hitpan_db_clean.sql:4145</c> 가 아직 <c>DEFAULT 1</c> 이다 ⇒ <b>이 게이트는 지금 빨간불이 맞다.</b>
    /// 초록으로 만들려고 기대값을 1 로 적으면 그것이 거짓봉합이다.</para>
    /// </remarks>
    [Fact]
    public void G_B8_1_출하DDL_신규설치에서_축B_기본값은_0_이다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B8① 출하 DDL 기본값")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();

        Assert.Equal("0", ColumnDefault(db, "enforce_single_pc_login"));
        Assert.Equal("0", ColumnDefault(db, "enforce_tenant_session_limit"));   // 축 A 도 꺼둔 채다
    }

    /// <summary>
    /// 🔴 <b>G-B8 ②</b> — <b>이미 켜져 있던 기존 DB</b> + <c>DB-128</c> ⇒ 기본값과 <b>값</b> 모두 <b>0</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 음성 대조군 — <c>DB-128</c> 의 <c>UPDATE</c> 를 지우면 <b>이미 저장된 값 1 이 그대로 남아</b> FAIL 한다.
    /// <c>MODIFY … DEFAULT 0</c> 은 <b>앞으로 들어올 행</b>만 바꾼다 — 그것만으로는 켜진 고객사가 안 꺼진다.
    /// <para>
    /// 🔴 <b>C1 산출물 의존</b> — <c>DB-128</c> 파일은 갈래 C1(절G)이 만든다.
    /// 없으면 <b>건너뛰지 않고 실패</b>시킨다. 파일이 없다는 사실을 초록으로 덮으면
    /// *"마이그가 빠져도 잠기지 않게"* 라는 이번 봉합의 닫는 조건이 검증되지 않는다.
    /// </para>
    /// <para>
    /// ⚠️ 출발 상태는 시험이 <b>명시적으로</b> 만든다(기본값 1 + 값 1). ①의 결과에 기대면
    /// C1 이 ①을 고치는 순간 ②가 무엇을 쟀는지 모르게 된다(대조 두 쪽을 같은 것으로 읽지 않는다).
    /// </para>
    /// </remarks>
    [Fact]
    public void G_B8_2_기존DB_에_DB128_을_적용하면_켜져있던_값도_0_이_된다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B8② DB-128 기존 DB")) return;

        var migration = Path.Combine(
            RepoRoot(), "src", "HitPan.API", "Migrations", "SQL",
            "DB-128_tenant_settings_single_pc_default_off.sql");
        Assert.True(File.Exists(migration),
            $"DB-128 이 없다: {migration}\n"
          + "  이 파일은 절G(갈래 C1) 산출물이다. 없으면 기존 고객 DB 의 켜진 값이 안 꺼진다 —\n"
          + "  '마이그가 빠져도 잠기지 않게' 라는 이번 봉합의 닫는 조건이 성립하지 않는다.");

        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();

        // DB-127 직후의 모양을 만든다 — 기본값 1 이고, 이미 켜진 고객사 한 곳이 있다.
        db.Execute(
            "ALTER TABLE tenant_settings MODIFY enforce_single_pc_login tinyint(1) NOT NULL DEFAULT 1");
        // ⬛ [낡은 줄] `SetKillSwitch(db, 1);` — 20260928작2 절F 에서 SetKillSwitch 가 새 칸을 세우게 바뀌었다.
        //   이 시험이 재는 것은 **옛 칸**(DB-128)이므로 옛 칸 전용 헬퍼로 세운다.
        SetLegacyKillSwitch(db, 1);
        Assert.Equal("1", ColumnDefault(db, "enforce_single_pc_login"));
        Assert.Equal(1, CurrentKillSwitch(db));

        RunSqlFile(migration);

        Assert.Equal("0", ColumnDefault(db, "enforce_single_pc_login"));
        Assert.Equal(0, CurrentKillSwitch(db));   // 🔴 UPDATE 가 없으면 여기서 1 이 남는다
    }

    // ══════════════════════════════════════════════════════════════
    // G-B9 — 밀어낸 사실을 남긴다 (P1-2 비대칭)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-B9</b> — <c>ForceSignOutOtherPc = true</c> 로그인 ⇒ 다른 PC 행 삭제 +
    /// <c>security_alerts</c>(<c>pc_session_forced_out</c>) <b>1건</b>.
    /// </summary>
    /// <remarks>
    /// [무엇이 비대칭이었나] 세션 기록 <b>실패</b>(기술적 사고)는 흔적이 남는데,
    /// <b>남의 PC 접속을 끊은 일</b>(보안 사건)은 아무 흔적도 없었다 —
    /// 계정이 털려 남이 내 PC 를 밀어내도 <b>물어볼 자료가 없다.</b>
    /// <para>⬛ 음성 대조군(1.3.46 까지) — <c>EnforceSinglePcLoginAsync</c> 의 감사 INSERT 를 지우면 0건이 되어 FAIL 한다.</para>
    /// <para>
    /// ⬛ <b>위 서술과 옛 이름 <c>G_B9_밀어내기는_보안_흔적을_남긴다</c> 는 1.3.46 까지다.</b>
    /// 🔴 <b>20260928작2 절F — 기대값 반전</b>(K-4 다 · 사장님 원문 <i>"「다른 PC에서 사용 중입니다」 안내가 뜨고, 로그인 불가."</i>):
    /// <c>force: true</c> 여도 두 번째 PC 는 <b>409</b>(<c>ConcurrentPcLoginException</c>) · PC1 행 <b>생존</b> ·
    /// <c>pc_session_forced_out</c> <b>0건</b>. 밀어내기 자체가 없어졌으므로 흔적도 새로 생기지 않는다.
    /// 기대값이 <b>뒤집혔다는 사실</b>이 다음 사람에게 가장 중요한 정보라 옛 서술을 지우지 않았다.
    /// </para>
    /// <para>🔴 음성 대조군(지금) — force 갈래(<c>if (!force) throw</c> + <c>DELETE</c> + 감사 INSERT)를 되살리면
    /// PC2 가 들어오고 PC1 행이 사라지고 흔적이 1건이 되어 FAIL 한다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B9_반전_force_여도_두번째PC_는_409_이고_밀어낸_흔적은_0건이다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B9 반전 force → 409")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid1 = SidOf(pc1);
            Assert.NotNull(sid1);
            Assert.Equal(0, AlertCount(db, "pc_session_forced_out"));   // 출발점

            // ⬛ [낡은 기대] PC2 force 성공 · PC1 밀려남 · 흔적 1건
            await Assert.ThrowsAsync<ConcurrentPcLoginException>(
                () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa, force: true)));

            Assert.Equal("pc", SessionKind(db, sid1!));                 // 🔴 PC1 은 밀려나지 않았다
            Assert.Equal(1, SessionCount(db));                          // PC2 행은 생기지 않았다
            Assert.Equal(0, AlertCount(db, "pc_session_forced_out"));   // 🔴 밀어낸 일이 없으니 흔적도 없다
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B11 — 지문 없는 로그인에도 판정이 성립한다 (설계 §3)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-B11</b> — <c>DeviceFingerprint</c> <b>없이</b> 로그인(⇒ <c>tenant_devices</c> 0건) +
    /// 헤더 UA = Windows ⇒ <c>'pc'</c> 로 판정되고 로그인 <b>200</b>.
    /// </summary>
    /// <remarks>
    /// 작지 §3 금지 #3b — <b>축 B 판정은 <c>tenant_devices</c> 를 읽지 않는다.</b>
    /// 지문 없는 로그인에는 그 행이 없고, 판정 시점이 기기 등록보다 <b>앞</b>이다(설계 §3).
    /// <para>🔴 음성 대조군 — 판정이 <c>tenant_devices</c> 행을 전제하면 판정불가/예외가 되어 FAIL 한다.
    /// 이 시험은 그 전제가 <b>실제로 없다</b>는 것을 표 0건으로 함께 고정한다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B11_지문이_없어도_기기줄_없이_pc_로_판정되고_로그인은_된다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B11 지문 없는 로그인")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            // 🔴 지문을 주지 않는다 — 실재하는 경로다(기기 등록 전/지문 미지원 브라우저).
            var response = await svc.LoginAsync(NewLoginRequest(null, WindowsUa, fingerprint: null));

            var sid = SidOf(response);
            Assert.NotNull(sid);
            Assert.Equal("pc", SessionKind(db, sid!));
            Assert.Equal(0, db.ExecuteScalar<int>("SELECT COUNT(*) FROM tenant_devices"));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B12 — 갱신이 만드는 새 세션의 기기 종류 (PM 결재 조건 C-5)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-B12</b> (PM 결재 조건 C-5 신설) — 옛 토큰(<c>sid</c> 없음)으로 <b>PC UA 로 갱신</b>
    /// ⇒ 새 세션 <c>device_kind = 'pc'</c>.
    /// </summary>
    /// <remarks>
    /// 🟢 PM 실측: 갱신 경로가 <c>NormalizeDeviceKind(null)</c> 로 <b>항상 <c>'mobile'</c></b> 을 적었다.
    /// 그 행은 축 B 의 <c>device_kind='pc'</c> 조회에 안 잡히므로
    /// <b>옛 토큰으로 <c>/refresh</c> 한 번 = 축 B 영구 회피</b>였다.
    /// <para>🔴 음성 대조군 — 갱신 판정을 <c>null</c> 폴백(옛 식)으로 되돌리면 <c>'mobile'</c> 이 적혀 FAIL 한다.
    /// 그 <b>판정축</b>은 DB 없이도 재진다 — <c>G_B12c</c> 참조.</para>
    /// <para>
    /// ⚠️ <c>sid</c> 없는 토큰을 <b>운영 경로로</b> 만든다 — 세션 표를 잠깐 치우면
    /// 절B 규칙(기록 실패 시 <c>sid</c> 미탑재)에 따라 운영 코드가 스스로 옛 토큰 모양을 만든다.
    /// 시험이 토큰을 손으로 굽지 않는다.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task G_B12_옛토큰_갱신이_만드는_새_세션은_UA_판정으로_pc_가_된다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B12 갱신 UA 단독 판정")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            HideSessionTable(db);
            var old = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            Assert.Null(SidOf(old));   // 옛 토큰과 같은 모양 — sid 가 없다
            RestoreSessionTable(db);

            var refreshed = await svc.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = old.RefreshToken, UserAgent = WindowsUa });

            var sid = SidOf(refreshed);
            Assert.NotNull(sid);
            Assert.Equal(1, SessionCount(db));
            Assert.Equal("pc", SessionKind(db, sid!));   // 🔴 옛 식이면 여기서 'mobile' 이다
        }
    }

    /// <summary>
    /// 🔴 <b>G-B12c</b> — 음성 대조군의 <b>판정축</b>을 DB 없이 잰다:
    /// 갱신 경로에서 옛 식(<c>null</c> 폴백)과 새 식(UA 단독 판정)의 답이 <b>다르다</b>.
    /// </summary>
    [Fact]
    public void G_B12c_갱신경로_옛_null폴백과_새_UA단독판정은_갈린다()
    {
        // 옛 식 — 갱신에는 신고값이 없으니 무조건 싼 칸이었다
        var oldRule = DeviceTypeResolver.ToSessionDeviceKind(
            DeviceTypeResolver.NormalizeDeviceType(null));

        // 새 식 — 서버가 읽은 User-Agent 단독 판정 (PM 결재 C-5)
        var newRule = DeviceTypeResolver.ToSessionDeviceKind(
            DeviceTypeResolver.ResolveDeviceType(null, WindowsUa));

        Assert.Equal("mobile", oldRule);
        Assert.Equal("pc", newRule);

        // ⚠️ UA 가 없으면 종전과 같이 싼 칸으로 떨어진다 — 엄격해진 것은 UA 가 있을 때다.
        Assert.Equal("mobile", DeviceTypeResolver.ToSessionDeviceKind(
            DeviceTypeResolver.ResolveDeviceType(null, null)));
    }

    // ══════════════════════════════════════════════════════════════
    // G-B13 — 회전이 실패하면 갱신이 만든 세션 행이 남지 않는다 (3차 보상 삭제)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>G-B13</b> — 갱신에서 <b>refresh_tokens 회전이 401 로 실패</b>하면
    /// 그 갱신이 방금 만든 <c>user_sessions</c> 행이 <b>남지 않는다</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [무엇을 막는 게이트인가] 2차 봉합(D-2)이 세션 INSERT 를 회전 <b>앞</b>으로 올렸다.
    /// 그래서 회전이 <i>"이미 사용된 토큰"</i>(단일사용 거부)으로 <b>401</b> 을 던지면
    /// <b>아무도 쓰지 않는 <c>pc</c> 세션 행이 만료까지 남는다.</b>
    /// 축 B 를 켠 고객은 <b>자기 잔여 행 때문에 자기 재로그인이 409</b> 가 된다.
    /// </para>
    /// <para>
    /// 🔴 <b>경주를 결정적으로 만든다</b> — <c>deleted == 0</c> 은 원래 *"동시 갱신 2발"* 이라야 나온다.
    /// 시험이 두 번 동시에 부르면 어느 쪽이 이길지 모른다(간헐적 게이트는 게이트가 아니다).
    /// ⇒ <c>user_sessions</c> 에 <b>AFTER INSERT 트리거</b>를 달아, 운영 코드가 세션 행을 넣는 <b>그 순간</b>
    /// 남이 이 토큰을 먼저 써 버린 상태(= refresh_tokens 행이 사라진 상태)가 되게 한다.
    /// ⚠️ 트리거는 <b>상태 만들기</b>다 — 판정하는 SQL 이 아니다(이 파일의 규칙 · 작지 §3 금지 #8).
    /// 🔴 이 트리거가 도는 것 자체가 *"INSERT 가 회전보다 먼저다"* 를 증명한다 — 순서가 되돌아가면
    /// 트리거가 안 돌아 401 이 아니라 성공이 되고, 이 시험이 <b>먼저 깨진다.</b>
    /// </para>
    /// <para>
    /// 🔴 <b>음성 대조군</b> — <c>AuthService</c> 회전 <c>catch</c> 두 곳의
    /// <c>CompensateNewSessionRowAsync</c> 호출을 지우면 행이 <b>1건 남아 FAIL</b> 한다.
    /// ⬛ [낡은 줄 · 3차] *"(실제로 지워 FAIL 을 확인한 기록은 개발명세서 §4 에 있다.)"*
    /// — **거짓이었다**([4] R-8). 같은 차수 개발명세서 §1 은 그 항목을 <c>⚠️미계측 · "못 했다"</c> 로
    /// 적고 있었다. 주석이 문서와 정면 충돌한 것이다(#42).
    /// 🔴 [정확한 서술 · 5차] 이 게이트는 <b>기본 설정에서는 CI <c>db-gate</c> 잡에서만 계측된다.</b>
    /// 개발 PC 는 시험이 쓰는 임시 DB 이름에 <c>CREATE DATABASE</c> 가 거부돼
    /// (<c>hitpan</c> 1044 · <c>root</c> 1045) <c>[SKIP]</c> 이다 —
    /// <b>로컬 초록불을 이 게이트의 통과로 읽지 마라.</b>
    /// 음성 대조군의 FAIL 확인 기록은 <b>그것을 실제로 잰 차수의 개발명세서</b>에만 있다
    /// (3차 명세서 §4 에는 없다 · 5차 명세서 §4 에 로그가 있다).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task G_B13_회전이_401_로_실패하면_갱신이_만든_세션_행이_남지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B13 회전 실패 보상 삭제")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            // ① 옛 토큰 모양(`sid` 없음)을 **운영 경로로** 만든다 — G-B12 와 같은 방식.
            HideSessionTable(db);
            var old = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            Assert.Null(SidOf(old));
            RestoreSessionTable(db);
            Assert.Equal(0, SessionCount(db));   // 출발선: 세션 행이 없다

            // ② 세션 INSERT 가 끝나는 순간 회전이 실패하게 만든다(경주 재현).
            StealRefreshTokenWhenSessionInserted(db);
            try
            {
                var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                    () => svc.RefreshAsync(
                        new RefreshTokenRequest { RefreshToken = old.RefreshToken, UserAgent = WindowsUa }));

                // 우리가 노린 그 401 인가 — 다른 이유로 튕긴 것을 통과로 읽지 않는다.
                Assert.Contains("이미 사용된", ex.Message);
            }
            finally
            {
                DropSessionInsertTrigger(db);   // 확인 조회가 트리거에 걸리지 않게 먼저 뗀다
            }

            // ③ 🔴 본체 — 주인 없는 세션 행이 남지 않았다.
            Assert.Equal(0, SessionCount(db));
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B13b</b> — 회전이 <b>트랜잭션 안에서</b> 401(단일사용 거부)로 실패했을 때,
    /// 보상 삭제가 <b>함께 살아 있는 세션 행은 건드리지 않는다</b>(과잉 봉합 방지).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⬛ <b>[낡은 시험 · 3차 — 원문 그대로]</b> *"정상 로그인 → <c>StealRefreshToken</c>(refresh_tokens 전삭)
    /// → 갱신 401 → <c>Assert.Contains("로그아웃된", ex.Message)</c> → 이어받은 세션이 살아 있다"*.
    /// 🔴 <b>그 시험은 봉합을 한 줄도 재지 않았다</b>([4] R-1). 그 401 은 <c>RefreshAsync</c> 의
    /// <b>회전 전</b> 확인(<i>"로그아웃된 토큰입니다"</i>)에서 나오고, 그 자리는
    /// <c>conn.BeginTransaction()</c> <b>앞</b>이다 ⇒ <c>CompensateNewSessionRowAsync</c> 는 <b>0회</b> 불렸다.
    /// PM 음성대조군 ②에서 보상을 무력화했는데도 이 시험이 <b>PASS</b> 한 것이 그 증거다.
    /// <b>봉합을 지워도 통과하는 시험은 봉합을 재지 않는다.</b>
    /// </para>
    /// <para>
    /// 🔴 <b>"이어받은 세션 그 자체" 로는 음성 대조군이 성립하지 않는다 — 구조상이다.</b>
    /// 보상 인자는 <c>isNewSession &amp;&amp; sessionRecorded ? sessionId : null</c> 이다.
    /// 이어받은 갱신(<c>isNewSession == false</c>)은 인자가 <b>항상 <c>null</c></b> 이고
    /// <c>CompensateNewSessionRowAsync</c> 첫 줄이 즉시 반환한다 ⇒ helper 안의
    /// <c>WHERE session_id</c> 를 <c>WHERE user_id</c> 로 바꿔도 <b>그 시나리오는 결과가 안 바뀐다.</b>
    /// ⇒ 과잉 봉합이 실제로 드러나는 모양은 <b>보상이 실제로 도는 갱신</b>(새 세션을 만든 갱신)에
    /// <b>같은 계정의 다른 살아 있는 세션</b>이 공존할 때다. 이 시험이 재는 것은 그것이다.
    /// </para>
    /// <para>
    /// 🔴 <b>음성 대조군</b> — <c>CompensateNewSessionRowAsync</c> 의
    /// <c>WHERE session_id = @SessionId</c> 를 <c>WHERE user_id = @UserId</c>(전삭)로 바꾸면
    /// <b>일하고 있는 PC 의 행까지 사라져 FAIL</b> 한다(#20 흐름은 안 끊긴다).
    /// 🟢 <b>5차에 실제로 넣어 FAIL 을 확인했다</b>(1 FAIL / 2 PASS · 로그는 5차 개발명세서 §4).
    /// ⚠️ 평소 개발 PC 에서는 <c>[SKIP]</c> 이다 — 기본 계측 경로는 CI <c>db-gate</c> 잡이다.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task G_B13b_회전_401_보상이_함께_살아있는_세션은_지우지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B13b 보상 과잉 방지")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            // ① 일하고 있는 PC — 정상 로그인. 이 행은 **끝까지 살아 있어야** 한다.
            var working = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var workingSid = SidOf(working);
            Assert.NotNull(workingSid);
            Assert.Equal(1, SessionCount(db));

            // ② 옛 토큰 모양(`sid` 없음)을 **운영 경로로** 하나 더 만든다 — 이 토큰의 갱신이 새 세션을 만든다.
            //    ⬛ [낡은 주석 · 1.3.46 까지] ⚠️ 축 B 기본값은 꺼짐이라(tenant_settings 행 없음 → `?? 0`) 두 번째 `pc` 로그인이
            //      일하는 PC 를 밀어내지 않는다. 그것이 출하 기본 상태다(§2-3).
            //    🔴 [20260928작2 절F 보정] 출하 기본이 **켬**(행 없음도 켬 · K-2)으로 뒤집혔다 ⇒ 그대로 두면 아래
            //      갱신이 「다른 PC 생존」으로 401 이 되어 재려는 「이미 사용된」 401 에 닿지 못한다.
            //      ⇒ 축 B 를 **명시적으로 끈다**(준비 — 기대값은 바뀌지 않았다). 이 게이트가 재는 것은 보상 범위다.
            SetKillSwitch(db, 0);
            HideSessionTable(db);
            var old = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            Assert.Null(SidOf(old));
            RestoreSessionTable(db);
            Assert.Equal(1, SessionCount(db));   // 출발선: 일하는 PC 의 행 하나뿐

            // ③ 세션 INSERT 가 끝나는 순간 회전이 실패하게 만든다(G-B13 과 같은 결정적 경주).
            StealRefreshTokenWhenSessionInserted(db);
            try
            {
                var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                    () => svc.RefreshAsync(
                        new RefreshTokenRequest { RefreshToken = old.RefreshToken, UserAgent = WindowsUa }));

                // 🔴 **트랜잭션 안**에서 터진 그 401 인가 — 회전 전 확인("로그아웃된")이면
                //   보상 호출에 닿지도 못한다. 3차 시험이 정확히 그래서 아무것도 재지 않았다.
                Assert.Contains("이미 사용된", ex.Message);
                Assert.DoesNotContain("로그아웃된", ex.Message);
            }
            finally
            {
                DropSessionInsertTrigger(db);   // 확인 조회가 트리거에 걸리지 않게 먼저 뗀다
            }

            // ④ 🔴 본체 — 보상은 **자기가 만든 행 하나만** 지웠다. 일하는 PC 는 그대로다.
            Assert.Equal(1, SessionCount(db));
            Assert.True(IsAlive(db, workingSid!),
                "보상 삭제가 일하고 있는 PC 의 세션까지 지웠다 — 갱신 실패 한 번으로 남을 끊는다(#20).");
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B13c</b> — <b>회전 준비</b>(연결 열기·<c>BeginTransaction</c>)가 터져도
    /// 그 갱신이 방금 만든 <c>user_sessions</c> 행이 <b>남지 않는다</b> (5차 · [4] R-2 세 번째 경로).
    /// </summary>
    /// <remarks>
    /// <para>
    /// [무엇을 막는 게이트인가] 3차는 회전 실패 경로가 <i>"둘뿐"</i> 이라 적고 <c>catch</c> 두 곳만 감쌌다.
    /// 그런데 <c>OpenAsync</c> 와 <c>conn.BeginTransaction()</c> 은 <b><c>try</c> 밖</b>이었다.
    /// 🔴 가상 위험이 아니다 — 운영 코드의 바로 위 주석이 <i>"EF/Dapper 가 커넥션을 암묵적으로 닫아둔
    /// 상태면 BeginTransaction 이 'open and available Connection' 예외로 터진다"</i> 를
    /// <b>과거 실제 사고</b>로 기록하고 있다.
    /// </para>
    /// <para>
    /// 🔴 <b>왜 결함 주입인가</b> — <c>BeginTransaction</c> 을 DB 상태만으로 결정적으로 터뜨릴 방법이 없다.
    /// ⇒ <c>BeginTransactionFailingConnection</c> 이 <b>그 한 동작만</b> 던지고 나머지는 실제
    /// <c>MySqlConnection</c> 에 그대로 넘긴다. SQL 은 전부 실제 DB 에 간다 —
    /// ⚠️ 이것은 <b>상태 만들기</b>다. 판정(행이 남았나)은 운영 코드와 실제 표가 한다.
    /// </para>
    /// <para>
    /// 🔴 <b>양성 조건</b>을 함께 잰다 — 세션 INSERT 가 <b>실제로 돌았는지</b>를
    /// <c>AFTER INSERT</c> 트리거의 흔적으로 확인한다. 그러지 않으면 INSERT 가 아예 안 돌아도
    /// <c>0 == 0</c> 으로 통과한다(빈 통과).
    /// </para>
    /// <para>
    /// 🔴 <b>음성 대조군</b> — <c>AuthService</c> 의 <b>회전 준비 <c>try/catch</c></b>(5차 신설)를 지우면
    /// 세션 행이 <b>1건 남아 FAIL</b> 한다. 또 그 <c>catch</c> 의 <c>throw;</c> 를 빼면
    /// <b>원인 예외가 사라져</b> 아래 <c>ThrowsAsync&lt;InvalidOperationException&gt;</c> 가 FAIL 한다.
    /// 🟢 <b>5차에 앞의 것을 실제로 지워 FAIL 을 확인했다</b>(1 FAIL / 2 PASS · 로그는 5차 개발명세서 §4).
    /// ⚠️ 평소 개발 PC 에서는 <c>[SKIP]</c> 이다 — 기본 계측 경로는 CI <c>db-gate</c> 잡이다.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task G_B13c_회전_준비가_터져도_갱신이_만든_세션_행이_남지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B13c 회전 준비 실패 보상")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            // ① 옛 토큰 모양(`sid` 없음)을 운영 경로로 만든다 — 갱신이 새 세션을 만드는 조건.
            HideSessionTable(db);
            var old = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            Assert.Null(SidOf(old));
            RestoreSessionTable(db);
            Assert.Equal(0, SessionCount(db));   // 출발선: 세션 행이 없다

            // ② 같은 DB 에, `BeginTransaction` 만 터지는 연결로 운영 서비스를 한 번 더 세운다.
            var faulty = new BeginTransactionFailingConnection(
                db, "The Connection property has not been initialized. [게이트 주입 G-B13c]");
            var faultySvc = new AuthService(new GateUnitOfWork(faulty), new GateUserLookup(_user));

            MarkSessionInsert(db);   // 양성 조건 — INSERT 가 실제로 돌았는지 흔적을 남긴다
            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => faultySvc.RefreshAsync(
                        new RefreshTokenRequest { RefreshToken = old.RefreshToken, UserAgent = WindowsUa }));

                // 🔴 원인 예외가 **그대로** 왔다 — 보상이 401/500 으로 바꿔치지 않았다.
                Assert.Contains("게이트 주입 G-B13c", ex.Message);
                Assert.Equal(1, faulty.BeginAttempts);
            }
            finally
            {
                DropSessionInsertMarker(db);
            }

            // ③ 양성 조건 — 세션 INSERT 는 실제로 돌았다(빈 통과가 아니다).
            Assert.Equal(1, AlertCount(db, "gate_b13c_session_inserted"));

            // ④ 🔴 본체 — 주인 없는 세션 행이 남지 않았다.
            Assert.Equal(0, SessionCount(db));
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B13d</b> — <b>이어받은 세션</b>(<c>sid</c> 있는 토큰)의 갱신에서 회전 준비가 터져도
    /// <b>일하고 있는 그 세션 행은 살아남는다</b> (6차 · [4] V-2 · PM 결재 조건 ②).
    /// </summary>
    /// <remarks>
    /// <para>
    /// [무엇을 막는 게이트인가] 보상 인자는 <c>isNewSession &amp;&amp; sessionRecorded ? sessionId : null</c> 인데
    /// <c>sessionRecorded</c> 는 이어받은 경로에서 <b>항상 <c>true</c></b> 로 시작하고
    /// (<i>"세션 번호를 이어받은 경우는 행이 이미 있다"</i>) <c>sessionId</c> 는 <b>지금 일하고 있는 그 번호</b>다.
    /// ⇒ <b><c>isNewSession &amp;&amp;</c> 한 조각이 살아 있는 세션을 지키는 유일한 보호대</b>다.
    /// 그것이 없으면 <b>갱신 실패 한 번이 일하는 PC 를 끊는다</b>(#20) — 결재가
    /// <i>"과잉 봉합 방지"</i> 로 요구한 바로 그 사고다.
    /// </para>
    /// <para>
    /// 🔴 <b>왜 5차까지 무계측이었나</b> — G-B13·B13b·B13c 는 셋 다 <c>HideSessionTable</c> 로
    /// <b><c>sid</c> 없는</b> 토큰을 만들어 쓰므로 <b>전부 <c>isNewSession == true</c></b> 다.
    /// ⇒ 그 조각을 지워도 <b>FAIL 이 0건</b>이었다([4] V-2 실측).
    /// </para>
    /// <para>
    /// ⬛ [낡은 서술 · 5차 개발명세서 §5 #6] <i>"이어받은 경로에는 그 사이에 <c>user_sessions</c> 쓰기가
    /// 없다 ⇒ 이어받은 갱신 + tx 401 조합은 <b>구조상 결정적 재현 불가</b>"</i> — <b>틀렸다</b>([4] V-2).
    /// 5차가 스스로 만든 <b>③경로</b>(회전 준비 예외)는 <c>deleted == 0</c> 이 <b>필요 없다.</b>
    /// <c>sid</c> 있는 토큰 + <c>BeginTransactionFailingConnection</c> 이면 트리거·경주 없이
    /// 이어받은 세션으로 보상 호출에 그대로 닿는다. <i>"재현 불가"</i> 는 <b>보호대가 작동하는 덕에
    /// 관측되지 않는다</b>는 순환 논증이었다 — 그 근거를 믿고 게이트를 안 만들면 보호대가 언제 빠져도 모른다.
    /// </para>
    /// <para>
    /// 🔴 <b>양성 조건</b>을 함께 잰다 — ⓐ 토큰에 <c>sid</c> 가 실렸다(= 이어받은 경로다) ·
    /// ⓑ 회전 준비가 <b>실제로 터졌다</b>(<c>BeginAttempts == 1</c> + 주입 메시지가 그대로 왔다) ·
    /// ⓒ 갱신이 <b>새 행을 만들지 않았다</b>(행 수가 1 그대로 = <c>isNewSession</c> 이 거짓이었다).
    /// 그러지 않으면 <i>"보상이 안 불렸다"</i> 와 <i>"애초에 그 경로에 안 갔다"</i> 를 가를 수 없다.
    /// </para>
    /// <para>
    /// 🔴 <b>음성 대조군</b> — <c>AuthService</c> 회전 준비 <c>catch</c> 의 보상 인자에서
    /// <c>isNewSession &amp;&amp;</c> 를 지우면 <b>일하는 세션 행이 지워져 0건이 되어 FAIL</b> 한다.
    /// 🟢 <b>6차에 실제로 지워 FAIL 을 확인했다</b>(로그는 6차 개발명세서 §4).
    /// ⚠️ 평소 개발 PC 에서는 <c>[SKIP]</c> 이다 — 기본 계측 경로는 CI <c>db-gate</c> 잡이다.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task G_B13d_이어받은_세션의_갱신이_터져도_일하는_세션_행은_살아남는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B13d 이어받은 세션 보호")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            // ① 일하고 있는 PC — 정상 로그인. 이 토큰에는 `sid` 가 실린다(= 이어받은 갱신 경로).
            var working = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var workingSid = SidOf(working);

            // 양성 조건 ⓐ — `sid` 가 있어야 `isNewSession == false` 다. 없으면 G-B13c 와 같은 것을 재게 된다.
            Assert.NotNull(workingSid);
            Assert.Equal(1, SessionCount(db));
            Assert.True(IsAlive(db, workingSid!));   // 살아 있어야 절C Guard 가 그냥 반환한다

            // ② 같은 DB 에, `BeginTransaction` 만 터지는 연결로 운영 서비스를 한 번 더 세운다.
            //    ⚠️ 트리거도 경주도 필요 없다 — ③경로는 `deleted == 0` 을 요구하지 않는다([4] V-2).
            var faulty = new BeginTransactionFailingConnection(
                db, "The Connection property has not been initialized. [게이트 주입 G-B13d]");
            var faultySvc = new AuthService(new GateUnitOfWork(faulty), new GateUserLookup(_user));

            // ③ 일하고 있는 그 토큰으로 갱신 → 회전 준비에서 터진다.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => faultySvc.RefreshAsync(
                    new RefreshTokenRequest { RefreshToken = working.RefreshToken, UserAgent = WindowsUa }));

            // 양성 조건 ⓑ — 회전 준비가 실제로 터졌고 원인 예외가 그대로 왔다(보상이 바꿔치지 않았다).
            Assert.Contains("게이트 주입 G-B13d", ex.Message);
            Assert.Equal(1, faulty.BeginAttempts);

            // ④ 🔴 본체 — 보상이 **일하고 있는 세션을 건드리지 않았다.**
            //    ⚠️ 이 줄이 ⓒ 보다 **먼저** 와야 한다 — 보호대를 빼면 행이 사라져 행 수도 같이 틀리는데,
            //      그때 먼저 터지는 것이 행 수라면 실패 메시지가 *"행을 만들었나"* 로 읽혀 원인을 가린다.
            Assert.True(IsAlive(db, workingSid!),
                "보상 삭제가 이어받은(일하고 있는) 세션 행을 지웠다 — 갱신 실패 한 번으로 남의 PC 를 끊는다(#20).");

            // 양성 조건 ⓒ — 새 행도 만들지 않았다(= `isNewSession` 이 거짓인 경로를 태웠다).
            Assert.Equal(1, SessionCount(db));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B17 ~ G-B28 — 동시로그인 차단 켜기(PC 1 · 모바일 FREE) + 로그아웃 이 로그인만 + F-4
    //   (20260928작2 절F · 설계 docs/설계/erp/로그아웃기기단위_F4_설계_20260928.md §8)
    //   공통: 세션·토큰은 운영 LoginAsync/Logout 으로 만든다. 「갱신이 된다」는 회전된 토큰으로 2회차까지 본다.
    //   refresh **폐기 자체**를 재는 게이트는 생존 확인을 꺼(SetValiditySwitch 0) 거절 출처를 「로그아웃된 토큰」으로 고정한다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-B17 ①</b> (L 로그인) — PC 로그인 → <b>모바일 로그인</b> → PC 갱신 <b>2회 성공</b>.
    /// </summary>
    /// <remarks>
    /// [무엇을 막나] 1.3.46 로그인은 계정 refresh 를 <b>전부</b> 지웠다 ⇒ 휴대폰 로그인 한 번에 PC 가 갱신 때 튕겼다.
    /// <para>🔴 음성 대조군 — <c>LoginAsync</c> 에 <c>DELETE FROM refresh_tokens WHERE user_id</c> 를 되살리면
    /// PC 갱신이 「로그아웃된 토큰」 401 로 FAIL 한다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B17_1_모바일_로그인이_PC_의_갱신을_끊지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B17① L 로그인 PC→모바일")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            var pc = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var mobile = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
            Assert.Equal("pc", SessionKind(db, SidOf(pc)!));
            Assert.Equal("mobile", SessionKind(db, SidOf(mobile)!));

            var after = await RefreshTwiceAsync(svc, pc, WindowsUa);
            Assert.Equal(SidOf(pc), SidOf(after));
        }
    }

    /// <summary>🔴 <b>G-B17 ②</b> (L 로그인 · 역방향) — 모바일 로그인 → <b>PC 로그인</b> → 모바일 갱신 <b>2회 성공</b>.</summary>
    /// <remarks>🔴 음성 대조군 — G-B17 ① 과 같다(전삭 복원 → 모바일 갱신 401).</remarks>
    [Fact]
    public async Task G_B17_2_PC_로그인이_모바일의_갱신을_끊지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B17② L 로그인 모바일→PC")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            var mobile = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
            var pc = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            Assert.Equal("pc", SessionKind(db, SidOf(pc)!));

            var after = await RefreshTwiceAsync(svc, mobile, IPhoneUa);
            Assert.Equal(SidOf(mobile), SidOf(after));
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B18</b> (L 로그아웃) — PC·모바일 → <b>모바일 <c>sid</c> 로 로그아웃</b> ⇒ 모바일 갱신 401(「로그아웃된 토큰」) ·
    /// PC 갱신 <b>2회 성공</b> · refresh 의 <c>session_id</c> = 토큰의 <c>sid</c>.
    /// </summary>
    /// <remarks>
    /// 사장님 원문 <i>"휴대폰에서 로그아웃을 하던, pc에서 로그아웃을 하던 끊기면 안되지."</i>
    /// <para>🔴 음성 대조군 — ⓐ 로그아웃 폐기를 <c>WHERE user_id</c> 로 되돌리면 PC 갱신 401 ·
    /// ⓑ <c>session_id</c> 폐기 문장을 빼면 모바일 갱신이 성공해 FAIL.
    /// (생존 확인을 꺼 두므로 거절 출처는 폐기 검사 하나다 — 설계 §8 공통.)</para>
    /// </remarks>
    [Fact]
    public async Task G_B18_로그아웃은_그_로그인의_refresh_만_폐기한다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B18 L 로그아웃")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetValiditySwitch(db, 0);   // 거절 출처를 폐기 검사로 고정한다(가드 ② 가 먼저 막지 않게)

            var pc = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var mobile = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
            var pcSid = SidOf(pc);
            var mobileSid = SidOf(mobile);
            Assert.NotNull(pcSid);
            Assert.NotNull(mobileSid);

            // 불변식 — refresh_tokens.session_id = 그 토큰의 sid (DB-130 · 설계 §2)
            Assert.Equal(1, ActiveRefreshCount(db, pcSid!));
            Assert.Equal(1, ActiveRefreshCount(db, mobileSid!));

            var ctl = NewAuthController(svc, db, IPhoneUa, sid: mobileSid);
            await ctl.Logout(CancellationToken.None);

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.RefreshAsync(
                    new RefreshTokenRequest { RefreshToken = mobile.RefreshToken, UserAgent = IPhoneUa }));
            Assert.Contains("로그아웃된 토큰", ex.Message, StringComparison.Ordinal);

            var pcAfter = await RefreshTwiceAsync(svc, pc, WindowsUa);   // 🔴 PC 는 계속 쓴다
            Assert.Equal(pcSid, SidOf(pcAfter));
        }
    }

    /// <summary>
    /// 🔴 <b>G-B19</b> — <c>sid</c> <b>없는</b> 로그아웃 ⇒ 다른 세션 행 2개 · 갱신 2개 <b>생존</b>.
    /// </summary>
    /// <remarks>
    /// <c>sid</c> 가 없는 로그인은 세션 행이 원래 없다 ⇒ 종전 <c>WHERE user_id</c> 전삭은 <b>남의 행만</b> 지웠다(설계 §2).
    /// <para>🔴 음성 대조군 — <c>Logout</c> 의 <c>sid</c> 없음 갈래를 전삭(<c>DELETE … WHERE user_id</c>)으로 되돌리면
    /// 행 2개가 사라져 FAIL 한다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B19_sid_없는_로그아웃은_남의_세션을_지우지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B19 sid 없는 로그아웃")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            var pc = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var mobile = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
            Assert.Equal(2, SessionCount(db));

            var ctl = NewAuthController(svc, db, WindowsUa, sid: null);   // 🔴 토큰에 sid 가 없다(옛 토큰)
            await ctl.Logout(CancellationToken.None);

            Assert.Equal(2, SessionCount(db));
            await RefreshTwiceAsync(svc, pc, WindowsUa);
            await RefreshTwiceAsync(svc, mobile, IPhoneUa);
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B20</b> (개정1 재정의) — <b>죽은 PC 정리 = PC 만</b>.
    /// PC1·모바일 로그인 → 둘 다 <b>만료</b>로 → PC2 <b>비강제</b> 로그인 성공 ⇒ PC1 갱신 401(「로그아웃된 토큰」) ·
    /// 모바일 갱신 성공 · PC2 갱신 2회 성공 · PC1·모바일 세션 <b>행 생존</b>.
    /// </summary>
    /// <remarks>
    /// ⬛ 개정 전 G-B20(「밀어내기 = PC 만」)은 K-4 (다)로 밀어내기가 없어져 재정의됐다.
    /// <para>🔴 음성 대조군 — ⓐ 정리 줄을 빼면 PC1 이 가드 문장(「다른 컴퓨터에서 사용 중이어서…」)으로 거절돼
    /// 메시지 단언이 FAIL · ⓑ <c>device_kind = 'pc'</c> 조건을 빼면 모바일 갱신 401 로 FAIL · ⓒ 는 G-B20c.</para>
    /// </remarks>
    [Fact]
    public async Task G_B20_죽은_PC_정리는_만료된_PC_의_refresh_만_폐기한다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B20 죽은 PC 정리")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var mobile = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
            var sid1 = SidOf(pc1)!;
            var mobileSid = SidOf(mobile)!;
            ExpireSession(db, sid1);        // 로그아웃 없이 자리를 떠 8h 지난 모양
            ExpireSession(db, mobileSid);   // 모바일도 만료 — 조건(device_kind)만이 모바일을 지키는지 잰다

            var pc2 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));   // 비강제 — 만료라 409 없음
            var sid2 = SidOf(pc2)!;

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.RefreshAsync(
                    new RefreshTokenRequest { RefreshToken = pc1.RefreshToken, UserAgent = WindowsUa }));
            Assert.Contains("로그아웃된 토큰", ex.Message, StringComparison.Ordinal);   // 🔴 폐기에서 막혔다(가드 아님)

            await RefreshTwiceAsync(svc, mobile, IPhoneUa);                 // 🔴 모바일은 안 건드렸다
            var pc2After = await RefreshTwiceAsync(svc, pc2, WindowsUa);    // 새 PC 는 정상
            Assert.Equal(sid2, SidOf(pc2After));

            Assert.NotNull(SessionKind(db, sid1));        // 세션 **행**은 지우지 않는다(가드 ⑦·접속 현황이 읽는다)
            Assert.NotNull(SessionKind(db, mobileSid));
        }
    }

    /// <summary>
    /// 🔴 <b>G-B20c</b> — 갱신 경로(<c>isLogin == false</c>)는 죽은 PC 의 refresh 를 <b>건드리지 않는다</b>.
    /// </summary>
    /// <remarks>
    /// 사용자가 없는 자동 동작이 남의 토큰을 건드리면 안 된다(설계 §3-1 · 종전 원칙 유지).
    /// 옛 토큰(<c>sid</c> 없음)의 갱신이 새 PC 세션을 만들 때 <c>EnforceSinglePcLoginAsync(isLogin: false)</c> 를 탄다.
    /// <para>🔴 음성 대조군 — 정리 줄의 <c>if (isLogin)</c> 조건을 빼면 만료된 PC1 의 refresh 가 폐기돼 FAIL 한다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B20c_갱신이_만드는_새_PC_세션은_죽은_PC_의_refresh_를_폐기하지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B20c 갱신은 정리 안 함")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            // 옛 토큰 모양(sid 없음)을 운영 경로로 만든다 — G-B12 와 같은 방식
            HideSessionTable(db);
            var old = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            Assert.Null(SidOf(old));
            RestoreSessionTable(db);

            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid1 = SidOf(pc1)!;
            ExpireSession(db, sid1);
            Assert.Equal(1, ActiveRefreshCount(db, sid1));   // 출발선: PC1 refresh 는 살아 있다

            var refreshed = await svc.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = old.RefreshToken, UserAgent = WindowsUa });
            Assert.Equal("pc", SessionKind(db, SidOf(refreshed)!));   // 양성 조건 — 새 PC 세션을 만든 갱신이었다

            Assert.Equal(1, ActiveRefreshCount(db, sid1));   // 🔴 갱신은 남의 refresh 를 폐기하지 않는다
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B21 ①</b> (켜진 채 출하) — 출하 DDL · <c>tenant_settings</c> 행 <b>없음</b> ⇒ PC2 로그인 <b>409</b>.
    /// </summary>
    /// <remarks>
    /// K-2 (가) 행 없음 = 켬. <c>tenant_settings</c> 행을 만드는 곳은 설정 저장 하나뿐이라, <c>?? 0</c> 이면
    /// 설정을 한 번도 저장 안 한 고객사가 <b>조용히</b> 안 켜진다(설계 §5).
    /// <para>🔴 음성 대조군 — 로그인 판정의 <c>?? 1</c> 을 <c>?? 0</c> 으로 되돌리면 PC2 가 들어와 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B21_1_행이_없는_고객사도_PC_두번째_로그인은_409_다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B21① 행 없음 = 켬")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            Assert.Equal(0, db.ExecuteScalar<int>("SELECT COUNT(*) FROM tenant_settings"));   // 양성 조건 — 행 없음

            await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            await Assert.ThrowsAsync<ConcurrentPcLoginException>(
                () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa)));
        }
    }

    /// <summary>🔴 <b>G-B21 ②</b> — 출하 DDL · 행 <b>있음</b>(기본값) ⇒ 새 칸 기본값 1 · PC2 로그인 <b>409</b>.</summary>
    /// <remarks>🔴 음성 대조군 — 출하 DDL 의 <c>enforce_one_pc_login</c> 을 <c>DEFAULT 0</c> 으로 바꾸면 FAIL.</remarks>
    [Fact]
    public async Task G_B21_2_출하DDL_기본값으로_켜진_채_나간다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B21② 출하 DDL 기본값 1")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            Assert.Equal("1", ColumnDefault(db, "enforce_one_pc_login"));
            Assert.Equal("0", ColumnDefault(db, "enforce_single_pc_login"));   // 옛 칸은 무접촉(#37)
            db.Execute("INSERT INTO tenant_settings (tenant_id) VALUES (@TenantId)", new { TenantId });
            Assert.Equal(1, CurrentOnePcSwitch(db));

            await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            await Assert.ThrowsAsync<ConcurrentPcLoginException>(
                () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa)));
        }
    }

    /// <summary>
    /// 🔴 <b>G-B21 ③</b> — 칸이 없던 <b>기존 DB</b> + <c>DB-131</c> ⇒ 새 칸 기본값·기존 행 <b>1</b> · 옛 칸 <b>0</b> 그대로 ·
    /// 재적용 멱등 · 칸이 <c>DEFAULT 0</c> 으로 이미 있던 DB 도 기본값 1 로 고정.
    /// </summary>
    /// <remarks>
    /// <para>🔴 음성 대조군 — DB-131 의 ② <c>MODIFY</c> 를 빼면 마지막 단언(기본값 1)이 FAIL.</para>
    /// <para>⚠️ 옛 칸 값은 이 마이그가 건드리지 않는다(G-B8 그대로) — 1.3.46 재게시가 곧 「꺼진 상태」 되돌림이다.</para>
    /// </remarks>
    [Fact]
    public void G_B21_3_기존DB_에_DB131_을_적용하면_켜지고_옛칸은_그대로다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B21③ DB-131 기존 DB")) return;

        var migration = Path.Combine(
            RepoRoot(), "src", "HitPan.API", "Migrations", "SQL", "DB-131_tenant_settings_one_pc_login.sql");
        Assert.True(File.Exists(migration), $"DB-131 이 없다: {migration}");

        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();

        // DB-129 직후(= 이 마이그 직전)의 모양을 명시적으로 만든다 — 새 칸 없음 · 행 2건 · 옛 칸 0.
        db.Execute("ALTER TABLE tenant_settings DROP COLUMN enforce_one_pc_login");
        db.Execute("INSERT INTO tenant_settings (tenant_id) VALUES (@TenantId), ('99999999-9999-9999-9999-999999999999')",
            new { TenantId });
        Assert.Null(ColumnDefault(db, "enforce_one_pc_login"));

        RunSqlFile(migration);
        RunSqlFile(migration);   // 멱등 — 두 번 돌려도 같다

        Assert.Equal("1", ColumnDefault(db, "enforce_one_pc_login"));
        Assert.Equal(2, db.ExecuteScalar<int>("SELECT COUNT(*) FROM tenant_settings WHERE enforce_one_pc_login = 1"));
        Assert.Equal(0, CurrentKillSwitch(db));   // 🔴 옛 칸은 0 그대로(되돌림 안전장치 · #37)

        // ② MODIFY 를 재는 자리 — 칸이 DEFAULT 0 으로 이미 있던 DB
        db.Execute("ALTER TABLE tenant_settings MODIFY enforce_one_pc_login tinyint(1) NOT NULL DEFAULT 0");
        RunSqlFile(migration);
        Assert.Equal("1", ColumnDefault(db, "enforce_one_pc_login"));
    }

    /// <summary>
    /// 🔴 <b>G-B21 ④</b> — 칸이 없던 기존 <c>refresh_tokens</c> + <c>DB-130</c> ⇒ <c>session_id</c> 칸·색인 생성 · 재적용 멱등.
    /// </summary>
    /// <remarks>🔴 음성 대조군 — DB-130 이 없거나 색인 문장이 빠지면 FAIL.</remarks>
    [Fact]
    public void G_B21_4_기존DB_에_DB130_을_두번_적용하면_칸과_색인이_생긴다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B21④ DB-130 기존 DB")) return;

        var migration = Path.Combine(
            RepoRoot(), "src", "HitPan.API", "Migrations", "SQL", "DB-130_refresh_tokens_session_id.sql");
        Assert.True(File.Exists(migration), $"DB-130 이 없다: {migration}");

        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();

        db.Execute("ALTER TABLE refresh_tokens DROP INDEX idx_user_session");
        db.Execute("ALTER TABLE refresh_tokens DROP COLUMN session_id");

        RunSqlFile(migration);
        RunSqlFile(migration);

        Assert.Equal(1, db.ExecuteScalar<int>(
            @"SELECT COUNT(*) FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'refresh_tokens' AND COLUMN_NAME = 'session_id'
                 AND DATA_TYPE = 'varchar' AND CHARACTER_MAXIMUM_LENGTH = 36 AND IS_NULLABLE = 'YES'"));
        Assert.Equal(2, db.ExecuteScalar<int>(
            @"SELECT COUNT(*) FROM information_schema.STATISTICS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'refresh_tokens' AND INDEX_NAME = 'idx_user_session'"));
    }

    /// <summary>
    /// 🔴🔴 <b>G-B22</b> (모바일 FREE) — 켬 · 모바일 → PC → 모바일 로그인 <b>전부 성공</b> · 셋 다 갱신 성공.
    /// </summary>
    /// <remarks>
    /// 사장님 원문 <i>"모바일 접속은 FREE"</i>.
    /// <para>🔴 음성 대조군 — 판정 조회의 <c>device_kind = 'pc'</c> 를 빼면 PC 로그인이 앞 모바일 세션을 「다른 PC」로 세어 409 로 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B22_모바일은_세지도_막지도_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B22 모바일 FREE")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            var m1 = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
            var pc = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var m2 = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
            Assert.Equal(3, SessionCount(db));

            await RefreshTwiceAsync(svc, m1, IPhoneUa);
            await RefreshTwiceAsync(svc, pc, WindowsUa);
            await RefreshTwiceAsync(svc, m2, IPhoneUa);
        }
    }

    /// <summary>🔴 <b>G-B23</b> (비상 끔) — 새 칸 <c>0</c> ⇒ PC2 로그인 <b>성공</b>.</summary>
    /// <remarks>🔴 음성 대조군 — 스위치 조회(<c>if (enabled == 0) return;</c>)를 빼면 409 로 FAIL.</remarks>
    [Fact]
    public async Task G_B23_새칸을_0_으로_내리면_PC_두대가_들어온다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B23 비상 끔")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 0);

            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var pc2 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            Assert.Equal("pc", SessionKind(db, SidOf(pc1)!));
            Assert.Equal("pc", SessionKind(db, SidOf(pc2)!));
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B24</b> (F-4 로그인) — refresh 저장이 <b>실패</b>하면 로그인 예외 · 세션 행 <b>0</b> ⇒
    /// 원인을 걷은 뒤 같은 계정 PC 재로그인이 <b>409 로 막히지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// K-4 (다)로 탈출 버튼이 없다 ⇒ 자기 잔여 행이 남으면 최대 8h 잠긴다. 이 게이트가 <b>유일한 방어</b>다(작지 §6).
    /// <para>🔴 양성 조건 — 세션 INSERT 가 <b>실제로 돌았다</b>(표식 트리거 1건). 아니면 0 == 0 빈 통과다.</para>
    /// <para>🔴 음성 대조군 — <c>LoginAsync</c> 의 보상 호출(<c>CompensateNewSessionRowAsync</c>)을 빼면 행 1 · 재로그인 409 로 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B24_로그인_refresh_저장이_터지면_세션_행을_되돌려_재로그인이_막히지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B24 F-4 로그인 보상")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            FailInsertsInto(db, "gate_b24_fail_refresh", "refresh_tokens");
            MarkSessionInsert(db);
            try
            {
                var ex = await Assert.ThrowsAnyAsync<Exception>(
                    () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa)));
                Assert.Contains("gate_b24_fail_refresh", ex.Message, StringComparison.Ordinal);   // 원인 예외 그대로
            }
            finally
            {
                DropTrigger(db, "gate_b24_fail_refresh");
                DropSessionInsertMarker(db);
            }

            Assert.Equal(1, AlertCount(db, "gate_b13c_session_inserted"));   // 양성 조건 — INSERT 는 돌았다
            Assert.Equal(0, SessionCount(db));                                 // 🔴 되돌렸다

            var again = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));   // 🔴 409 가 아니다
            Assert.Equal("pc", SessionKind(db, SidOf(again)!));
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B25</b> (접속기록) — PC 로그인 · 모바일 로그인 · 모바일 로그아웃 ⇒ <c>audit_trail</c> <c>user_session</c> <b>3행</b>
    /// (<c>entity_id</c> = <c>sid</c> · <c>device_kind</c> 값 · 테넌트) · 기록 쓰기가 실패해도 로그인 <b>성공</b>.
    /// </summary>
    /// <remarks>
    /// 사장님 원문 <i>"로그에, 어느 기기에서 로그인 했는지 남겨두는 정도만."</i>
    /// <para>🔴 음성 대조군 — 기록 호출을 빼면 0행 · <c>SessionAccessTrail.WriteAsync</c> 가 실패를 던지게 하면 로그인 예외로 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B25_로그인_로그아웃이_접속기록을_남기고_기록_실패는_로그인을_막지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B25 접속기록")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            var pc = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var mobile = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
            var pcSid = SidOf(pc)!;
            var mobileSid = SidOf(mobile)!;

            var ctl = NewAuthController(svc, db, IPhoneUa, sid: mobileSid);
            await ctl.Logout(CancellationToken.None);

            var rows = AccessTrail(db);
            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.Equal(TenantId, r.TenantId));
            Assert.All(rows, r => Assert.Equal(UserId, r.UserId));
            Assert.Contains(rows, r => r.Action == "login" && r.EntityId == pcSid && r.Kind == "pc");
            Assert.Contains(rows, r => r.Action == "login" && r.EntityId == mobileSid && r.Kind == "mobile");
            Assert.Contains(rows, r => r.Action == "logout" && r.EntityId == mobileSid && r.Kind == "mobile");

            // 기록 쓰기가 터져도 로그인은 막지 않는다(#20 · 설계 §6)
            FailInsertsInto(db, "gate_b25_fail_audit", "audit_trail");
            try
            {
                var m2 = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
                Assert.NotNull(SidOf(m2));
            }
            finally
            {
                DropTrigger(db, "gate_b25_fail_audit");
            }
            Assert.Equal(3, AccessTrail(db).Count);   // 양성 조건 — 트리거가 실제로 막았다
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B26</b> (K-4 다) — <b>먼저 들어온 PC 가 이긴다.</b> PC1 로그인 → PC2 <b><c>force: true</c></b> ⇒ 409 ·
    /// 세션 행 PC1 <b>1개만</b> · PC1 갱신 2회 성공 · <c>pc_session_forced_out</c> 0건 · PC2 비강제 재시도도 409.
    /// </summary>
    /// <remarks>
    /// 사장님 원문 <i>"「다른 PC에서 사용 중입니다」 안내가 뜨고, 로그인 불가."</i>
    /// 옛 화면(1.3.46 캐시)이 force 를 보내도 서버가 무시한다 — 두 PC 는 안 선다.
    /// <para>🔴 음성 대조군 — force 갈래(<c>if (!force) throw</c> + <c>DELETE</c>)를 되살리면 PC2 가 들어오고 PC1 행이 사라져 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B26_두번째_PC_는_force_를_보내도_409_이고_앞_PC_는_계속_쓴다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B26 먼저 들어온 PC 가 이긴다")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid1 = SidOf(pc1)!;

            await Assert.ThrowsAsync<ConcurrentPcLoginException>(
                () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa, force: true)));
            Assert.Equal(1, SessionCount(db));
            Assert.True(IsAlive(db, sid1));
            Assert.Equal(0, AlertCount(db, "pc_session_forced_out"));

            var pc1After = await RefreshTwiceAsync(svc, pc1, WindowsUa);   // 🔴 앞 PC 는 계속 쓴다
            Assert.Equal(sid1, SidOf(pc1After));

            await Assert.ThrowsAsync<ConcurrentPcLoginException>(
                () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa)));
            Assert.Equal(1, SessionCount(db));
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B27</b> (K-5) — 막은 시도를 남긴다: <c>security_alerts</c> <c>pc_login_blocked</c> <b>1건</b>
    /// (테넌트·사용자 일치) · 틀린 비밀번호·모바일은 늘지 않는다 · 기록 쓰기가 실패해도 응답은 <b>409 그대로</b>.
    /// </summary>
    /// <remarks>
    /// <para>🔴 음성 대조군 — 기록 호출을 빼면 0건 · 기록 실패를 던지게(<c>catch</c> 제거) 하면 409 가 아닌 예외로 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B27_두번째_PC_시도는_보안경고_1건을_남기고_기록실패에도_409_다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B27 409 시도 기록")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            await Assert.ThrowsAsync<ConcurrentPcLoginException>(
                () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa)));

            Assert.Equal(1, AlertCount(db, "pc_login_blocked"));
            Assert.Equal(1, db.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM security_alerts
                   WHERE alert_type = 'pc_login_blocked' AND tenant_id = @TenantId AND user_id = @UserId",
                new { TenantId, UserId }));

            // 틀린 비밀번호 — 비밀번호 확인 뒤에만 남긴다
            var wrong = NewLoginRequest("pc", WindowsUa);
            wrong.Password = "wrong-" + Guid.NewGuid().ToString("N");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.LoginAsync(wrong));
            Assert.Equal(1, AlertCount(db, "pc_login_blocked"));

            // 모바일 — 세지도 막지도 않는다
            await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
            Assert.Equal(1, AlertCount(db, "pc_login_blocked"));

            // 기록 쓰기가 터져도 409 는 그대로다
            FailInsertsInto(db, "gate_b27_fail_alert", "security_alerts");
            try
            {
                await Assert.ThrowsAsync<ConcurrentPcLoginException>(
                    () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa)));
            }
            finally
            {
                DropTrigger(db, "gate_b27_fail_alert");
            }
            Assert.Equal(1, AlertCount(db, "pc_login_blocked"));   // 양성 조건 — 트리거가 실제로 막았다
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B28</b> (K-3 나) — 생존 확인 <b>행 없음 = 켬</b>: <c>tenant_settings</c> 행 없음 · 로그인 → 세션 행 삭제 ⇒
    /// ① 보호 API(미들웨어) 401 ② 갱신 401(「접속이 종료되었습니다」) · 명시적 <c>0</c> 이면 둘 다 종전대로 통과.
    /// </summary>
    /// <remarks>
    /// ⚠️ 이 변경은 1.3.46 동작을 바꾼다 — 행 없는 고객사의 죽은 세션이 10초 안에 401(작지 §6 리스크).
    /// <para>🔴 음성 대조군 — 미들웨어 <c>v != 0</c> 을 <c>v == 1</c> 로 되돌리면 ① 통과로 FAIL ·
    /// 가드 <c>validity == 0</c> 을 <c>!= 1</c> 로 되돌리면 ② 통과로 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B28_생존확인_행이_없으면_켬이다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B28 생존 확인 행 없음 = 켬")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            Assert.Equal(0, db.ExecuteScalar<int>("SELECT COUNT(*) FROM tenant_settings"));   // 양성 조건 — 행 없음

            var login = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid = SidOf(login)!;
            Assert.Equal(1, db.Execute("DELETE FROM user_sessions WHERE session_id = @Sid", new { Sid = sid }));

            // ① 미들웨어
            var (ctx, nextCalled) = await RunSessionValidityAsync(db, sid);
            Assert.False(nextCalled, "행 없는 고객사에서 죽은 세션이 통과했다 — 생존 확인이 조용히 꺼져 있다.");
            Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);

            // ② 갱신 가드
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.RefreshAsync(
                    new RefreshTokenRequest { RefreshToken = login.RefreshToken, UserAgent = WindowsUa }));
            Assert.Contains("접속이 종료되었습니다", ex.Message, StringComparison.Ordinal);

            // 명시적 0 — 비상 끔은 종전 그대로 통과
            SetValiditySwitch(db, 0);
            var (ctx0, next0) = await RunSessionValidityAsync(db, sid);
            Assert.True(next0);
            Assert.Equal(StatusCodes.Status200OK, ctx0.Response.StatusCode);
            var refreshed = await svc.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = login.RefreshToken, UserAgent = WindowsUa });
            Assert.Equal(sid, SidOf(refreshed));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B29 ~ G-B33 · W-1 — 개정2 병렬이슈 PI-2~PI-7 봉합을 동작으로 잰다
    //   (20260928작2 절O · 설계 docs/설계/erp/로그아웃기기단위_F4_설계_20260928.md §13-7)
    //   G-B34(끝내기 표)는 K-7 = 나 ⇒ 절N 미착수라 없다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>G-B29</b> (PI-3 로그인 경주) — 같은 계정 PC 로그인 두 개를 <b>서로 다른 연결</b>로 동시에 ⇒
    /// 성공 <b>1</b> · 409 <b>1</b> · PC 세션 행 <b>1</b>.
    /// </summary>
    /// <remarks>
    /// <para>창은 <c>BEFORE INSERT ON user_sessions</c> 트리거의 <c>SLEEP(2)</c> 로 <b>결정적으로</b> 넓힌다 —
    /// 두 로그인이 판정을 마친 뒤 세션·refresh 를 넣기 전까지 2초가 벌어진다(설계 §13-7 은 1초 · 여유를 늘렸다).</para>
    /// <para>⚠️ 한 연결을 두 작업이 나눠 쓰지 않는다(#16) — 운영처럼 요청마다 자기 연결이다(<c>NewAuthServiceWithOwnUser</c>).</para>
    /// <para>🔴 음성 대조군 — <c>TryAcquireLoginLockAsync</c>(<c>GET_LOCK</c>)를 빼면 둘 다 판정을 통과해 성공 2 · 행 2 로 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B29_같은_계정_PC_로그인_둘이_동시에_와도_하나만_들어온다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B29 로그인 경주")) return;
        SetUpFreshInstall();

        using var admin = new MySqlConnection(DbConnString());
        admin.Open();
        SetKillSwitch(admin, 1);
        admin.Execute(
            "CREATE TRIGGER gate_b29_slow_session BEFORE INSERT ON user_sessions FOR EACH ROW "
            + "SET @gate_b29_slept = SLEEP(2)");

        var (svcA, dbA) = NewAuthServiceWithOwnUser();
        var (svcB, dbB) = NewAuthServiceWithOwnUser();
        Exception?[] outcomes;
        using (dbA)
        using (dbB)
        {
            try
            {
                var a = Task.Run(() => CaptureAsync(() => svcA.LoginAsync(NewLoginRequest("pc", WindowsUa))));
                var b = Task.Run(() => CaptureAsync(() => svcB.LoginAsync(NewLoginRequest("pc", WindowsUa))));
                outcomes = new[] { await a, await b };
            }
            finally
            {
                DropTrigger(admin, "gate_b29_slow_session");
            }
        }

        var succeeded = outcomes.Count(o => o is null);
        var blocked = outcomes.Count(o => o is ConcurrentPcLoginException);
        Assert.True(succeeded == 1 && blocked == 1,
            "동시 로그인 결과가 「성공 1 · 409 1」이 아니다 — "
            + string.Join(" / ", outcomes.Select(o => o is null ? "성공" : o.GetType().Name + ": " + o.Message)));
        Assert.Equal(1, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM user_sessions WHERE device_kind = 'pc'"));
    }

    /// <summary>
    /// 🔴🔴 <b>G-B30 ⓐ</b> (PI-4 로그아웃 실패) — refresh 폐기가 터지면 로그아웃은 <b>500</b> · 세션 행 <b>생존</b> · refresh 도 그대로.
    /// </summary>
    /// <remarks>
    /// 종전은 실패해도 200 「로그아웃 완료」였다 ⇒ 화면은 조용히 로그아웃했고 남은 접속이 다른 PC 를 최대 8h 막았다.
    /// <para>🔴 음성 대조군 — <c>Logout</c> 의 실패 갈래를 <c>Ok</c> 로 되돌리면 200 이라 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B30_1_로그아웃_refresh_폐기가_터지면_500_이다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B30ⓐ 로그아웃 폐기 실패 500")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            var pc = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid = SidOf(pc)!;

            db.Execute(
                "CREATE TRIGGER gate_b30a_fail_revoke BEFORE UPDATE ON refresh_tokens FOR EACH ROW "
                + "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'gate_b30a_fail_revoke'");
            Microsoft.AspNetCore.Mvc.IActionResult result;
            try
            {
                result = await NewAuthController(svc, db, WindowsUa, sid: sid).Logout(CancellationToken.None);
            }
            finally
            {
                DropTrigger(db, "gate_b30a_fail_revoke");
            }

            var obj = Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
            Assert.True(IsAlive(db, sid));                  // 끝나지 않은 로그아웃 — 행이 남았다
            Assert.Equal(1, ActiveRefreshCount(db, sid));   // 양성 조건 — 트리거가 실제로 폐기를 막았다
            Assert.DoesNotContain(AccessTrail(db), r => r.Action == "logout");   // 끝나지 않은 로그아웃은 적지 않는다
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B30 ⓑ</b> (PI-4 · PI-5) — refresh 폐기는 됐고 <b>세션 행 삭제가 터지면</b> 500 ⇒ 원인을 걷은 뒤
    /// 같은 계정 PC2 로그인이 <b>성공</b>한다(남은 행에 쓸 수 있는 refresh 가 없다).
    /// </summary>
    /// <remarks>
    /// <para>🔴 음성 대조군 — 「살아 있는 PC」 술어(<c>LivePcSessionPredicate</c>)에서 <c>EXISTS</c> 를 빼면 남은 행이 세어져 PC2 409 로 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B30_2_로그아웃_행삭제가_터져도_다른_PC_는_들어온다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B30ⓑ 로그아웃 반쪽 실패")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);
            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid1 = SidOf(pc1)!;

            db.Execute(
                "CREATE TRIGGER gate_b30b_fail_delete BEFORE DELETE ON user_sessions FOR EACH ROW "
                + "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'gate_b30b_fail_delete'");
            Microsoft.AspNetCore.Mvc.IActionResult result;
            try
            {
                result = await NewAuthController(svc, db, WindowsUa, sid: sid1).Logout(CancellationToken.None);
            }
            finally
            {
                DropTrigger(db, "gate_b30b_fail_delete");
            }

            var obj = Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
            Assert.True(IsAlive(db, sid1));                  // 양성 조건 — 행 삭제가 실제로 막혔다
            Assert.Equal(0, ActiveRefreshCount(db, sid1));   // ① refresh 폐기는 됐다

            var pc2 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));   // 🔴 409 가 아니다
            Assert.Equal("pc", SessionKind(db, SidOf(pc2)!));
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B31</b> (PI-5 잔여 행은 안 잠근다) — refresh 저장 실패 + <b>보상 DELETE 도 실패</b> ⇒ 잔여 행 1 ·
    /// 원인을 걷은 뒤 재로그인 <b>성공</b> · 대조: 그 잔여 행에 쓸 수 있는 refresh 를 붙이면 <b>409</b>.
    /// </summary>
    /// <remarks>
    /// <para>G-B24 는 보상이 <b>성공</b>하는 길만 잰다. 연결이 상한 것이 원인이면 보상도 같이 죽는다 — 그때의 방어가 절L 술어다.</para>
    /// <para>🔴 음성 대조군 — 술어에서 <c>EXISTS</c> 를 빼면 잔여 행이 세어져 재로그인 409 로 FAIL.
    /// 대조(쓸 수 있는 refresh 를 붙이면 409)는 잔여 행이 <b>살아 있는 PC 모양</b>이라는 양성 조건이다 — 술어가 행을 아예 안 보는 것이 아님을 잰다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B31_보상까지_실패한_잔여_행은_재로그인을_잠그지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B31 잔여 행은 안 잠근다")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            FailInsertsInto(db, "gate_b31_fail_refresh", "refresh_tokens");
            db.Execute(
                "CREATE TRIGGER gate_b31_fail_delete BEFORE DELETE ON user_sessions FOR EACH ROW "
                + "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'gate_b31_fail_delete'");
            MarkSessionInsert(db);
            try
            {
                var ex = await Assert.ThrowsAnyAsync<Exception>(
                    () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa)));
                Assert.Contains("gate_b31_fail_refresh", ex.Message, StringComparison.Ordinal);   // 원인 예외 그대로
            }
            finally
            {
                DropTrigger(db, "gate_b31_fail_refresh");
                DropTrigger(db, "gate_b31_fail_delete");
                DropSessionInsertMarker(db);
            }

            Assert.Equal(1, AlertCount(db, "gate_b13c_session_inserted"));   // 양성 조건 — INSERT 는 돌았다
            Assert.Equal(1, SessionCount(db));                                 // 양성 조건 — 보상이 실제로 실패했다
            var residual = db.ExecuteScalar<string>("SELECT session_id FROM user_sessions LIMIT 1")!;
            Assert.True(IsAlive(db, residual));

            var again = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));   // 🔴 409 가 아니다
            var againSid = SidOf(again)!;
            Assert.Equal("pc", SessionKind(db, againSid));

            // 대조 — 재로그인을 로그아웃으로 치우고, 잔여 행에 쓸 수 있는 refresh 를 붙이면 그 행이 「살아 있는 PC」다.
            var logout = await NewAuthController(svc, db, WindowsUa, sid: againSid).Logout(CancellationToken.None);
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(logout);
            AttachUsableRefresh(db, residual);
            await Assert.ThrowsAsync<ConcurrentPcLoginException>(
                () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa)));
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B31b</b> ([4] N-3) — refresh 저장 중에 <b>원인 연결을 실제로 죽인다</b>(<c>KILL CONNECTION</c>) ⇒
    /// 보상이 <b>복제한 새 연결</b>로 세션 행을 지운다(잔여 0 · 「새 연결로 못 했다」 경고 0) · 재로그인 성공.
    /// </summary>
    /// <remarks>
    /// <para>[무엇을 재나] 트리거 SIGNAL 로는 연결이 안 죽는다 — G-B24·G-B31 은 「같은 연결로도 지울 수 있는」 실패만 쟀다.
    /// 절L ① 의 목적은 <b>연결이 상한 경우</b>다. 9/11 백업 사고가 「열린 연결에서 꺼낸 연결 문자열에 비밀번호가 없었다」였다 —
    /// <c>Clone()</c> 도 같은 함정이면 새 연결은 늘 실패하고, 이 게이트가 그 경고를 잡는다.</para>
    /// <para>⚠️ CI <c>db-gate</c> 는 비밀번호가 있는 계정(<c>HITPAN_DB_PASS</c>)이다 — 비밀번호가 빈 환경이면 이 게이트는 함정을 못 가른다.</para>
    /// <para>🔴 음성 대조군 — 복제 대신 <c>new MySqlConnection(열린 연결.ConnectionString)</c>(비밀번호가 빠진 문자열)으로 바꾸면
    /// 「새 연결로 못 했다」 경고가 찍혀 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B31b_원인_연결이_죽어도_보상은_새_연결로_세션_행을_지운다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B31b 원인 연결 죽음 보상")) return;
        SetUpFreshInstall();

        using var admin = new MySqlConnection(DbConnString());
        admin.Open();
        SetKillSwitch(admin, 1);
        if (DbGateEnvironment.IsCi)
        {
            Assert.False(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HITPAN_DB_PASS")),
                "CI 계정에 비밀번호가 없다 — 복제 연결의 비밀번호 보존을 가를 수 없다.");
        }

        var (svc, db) = NewAuthService();
        var threadId = db.ServerThread;
        admin.Execute(
            "CREATE TRIGGER gate_b31b_hold_refresh BEFORE INSERT ON refresh_tokens FOR EACH ROW "
            + "SET @gate_b31b_slept = SLEEP(20)");
        MarkSessionInsert(admin);

        var traces = new CollectingTraceListener();
        System.Diagnostics.Trace.Listeners.Add(traces);
        Exception? loginFailure;
        var held = false;
        var killed = false;
        var login = Task.Run(() => CaptureAsync(() => svc.LoginAsync(NewLoginRequest("pc", WindowsUa))));
        try
        {
            // 로그인 연결이 refresh INSERT(트리거 SLEEP) 안에 들어간 순간을 기다렸다가 죽인다.
            // ⬛ [낡은 조건 · run 36387241626 FAIL] `INFO LIKE '%refresh_tokens%'` 만 봤다 — 트리거 안에서는 INFO 가
            //   트리거의 문장(SET … SLEEP)으로 보일 수 있어 못 잡았고, 죽이지 못한 연결이 계정 잠금(GET_LOCK)을 쥔 채
            //   풀에 돌아가 뒤 게이트 20여 건이 「잠시 후 다시 시도해 주세요」로 연쇄 FAIL 했다.
            for (var i = 0; i < 150 && !held; i++)
            {
                held = admin.ExecuteScalar<int>(
                    @"SELECT COUNT(*) FROM information_schema.PROCESSLIST
                       WHERE ID = @Tid
                         AND (INFO LIKE '%refresh_tokens%' OR INFO LIKE '%gate_b31b_slept%' OR STATE = 'User sleep')",
                    new { Tid = threadId }) > 0;
                if (!held) await Task.Delay(100);
            }
        }
        finally
        {
            // 🔴 준비가 성립했든 못 했든 로그인 연결은 **반드시 죽인다** — 살려 두면 계정 잠금을 쥔 채 다음 게이트를 막는다.
            try
            {
                admin.Execute($"KILL CONNECTION {threadId}");
                killed = true;
            }
            catch (MySqlException ex)
            {
                Console.Error.WriteLine($"[G-B31b] 로그인 연결 종료 실패(이미 끝났을 수 있다): {ex.Message}");
            }
            loginFailure = await login;
            System.Diagnostics.Trace.Listeners.Remove(traces);
            DropTrigger(admin, "gate_b31b_hold_refresh");
            DropSessionInsertMarker(admin);
            db.Dispose();
        }

        Assert.True(held && killed, "로그인 연결이 refresh INSERT 에 닿기 전·후를 못 가렸다 — 준비가 성립하지 않는다.");
        Assert.NotNull(loginFailure);                                    // 양성 조건 — 로그인이 실제로 실패했다
        Assert.IsNotType<ConcurrentPcLoginException>(loginFailure);
        Assert.Equal(1, AlertCount(admin, "gate_b13c_session_inserted"));   // 양성 조건 — 세션 INSERT 는 돌았다

        var freshFailed = traces.Messages.Where(m => m.Contains("보상 삭제를 새 연결로 못 했다", StringComparison.Ordinal)).ToList();
        Assert.True(freshFailed.Count == 0, "복제 연결 보상이 실패했다(N-3) — " + string.Join(" | ", freshFailed));
        Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM user_sessions"));   // 🔴 새 연결이 지웠다

        var (svc2, db2) = NewAuthService();
        using (db2)
        {
            var again = await svc2.LoginAsync(NewLoginRequest("pc", WindowsUa));   // 🔴 409 가 아니다
            Assert.Equal("pc", SessionKind(db2, SidOf(again)!));
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B32 ①</b> (PI-6 회전 뒤에도 센다) — PC1 로그인 → 갱신 1회 ⇒ PC2 로그인 <b>409</b> ·
    /// 회전된 refresh 의 <c>session_id</c> = PC1 <c>sid</c>.
    /// </summary>
    /// <remarks>
    /// 절L 술어는 「그 세션의 쓸 수 있는 refresh」를 본다 ⇒ 회전 INSERT 가 <c>session_id</c> 를 빠뜨리면 첫 갱신 뒤 차단이 <b>조용히</b> 꺼진다.
    /// <para>🔴 음성 대조군 — 회전 INSERT 의 <c>SessionId = rotatedSessionId</c> 를 빼면(NULL) PC2 가 들어와 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B32_1_갱신_한번_뒤에도_두번째_PC_는_409_다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B32① 회전 뒤 차단")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid1 = SidOf(pc1)!;
            var rotated = await svc.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = pc1.RefreshToken, UserAgent = WindowsUa });
            Assert.Equal(sid1, SidOf(rotated));

            await Assert.ThrowsAsync<ConcurrentPcLoginException>(
                () => svc.LoginAsync(NewLoginRequest("pc", WindowsUa)));
            Assert.Equal(1, ActiveRefreshCount(db, sid1));   // 회전된 행이 sid 를 달고 있다
        }
    }

    /// <summary>
    /// 🔴 <b>G-B32 ②</b> (G-B18 을 회전 토큰으로) — PC·모바일 각각 갱신 1회 → 모바일 <c>sid</c> 로그아웃 ⇒
    /// 모바일 <b>회전 토큰</b> 갱신 401(「로그아웃된 토큰」) · PC <b>회전 토큰</b> 갱신 2회 성공.
    /// </summary>
    /// <remarks>🔴 음성 대조군 — 회전 INSERT 의 <c>session_id</c> 를 빼면 로그아웃 폐기가 0행이라 모바일 갱신이 성공해 FAIL.</remarks>
    [Fact]
    public async Task G_B32_2_회전된_토큰도_로그아웃이_그_로그인만_폐기한다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B32② 회전 토큰 로그아웃")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetValiditySwitch(db, 0);   // 거절 출처를 폐기 검사로 고정한다(G-B18 과 같다)

            var pc = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var mobile = await svc.LoginAsync(NewLoginRequest("mobile", IPhoneUa));
            var pcR = await svc.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = pc.RefreshToken, UserAgent = WindowsUa });
            var mobileR = await svc.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = mobile.RefreshToken, UserAgent = IPhoneUa });
            var mobileSid = SidOf(mobileR)!;

            await NewAuthController(svc, db, IPhoneUa, sid: mobileSid).Logout(CancellationToken.None);

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.RefreshAsync(
                    new RefreshTokenRequest { RefreshToken = mobileR.RefreshToken, UserAgent = IPhoneUa }));
            Assert.Contains("로그아웃된 토큰", ex.Message, StringComparison.Ordinal);

            var pcAfter = await RefreshTwiceAsync(svc, pcR, WindowsUa);
            Assert.Equal(SidOf(pc), SidOf(pcAfter));
        }
    }

    /// <summary>
    /// 🔴 <b>G-B32 ③</b> (G-B20 을 회전 토큰으로) — PC1 갱신 1회 → 만료 → PC2 로그인 ⇒ PC1 <b>회전 토큰</b> 갱신 401(「로그아웃된 토큰」).
    /// </summary>
    /// <remarks>🔴 음성 대조군 — 회전 INSERT 의 <c>session_id</c> 를 빼면 죽은 PC 정리가 회전 토큰을 못 가르고
    /// 가드 문장(「다른 컴퓨터에서 사용 중이어서…」)으로 거절돼 메시지 단언이 FAIL.</remarks>
    [Fact]
    public async Task G_B32_3_회전된_토큰도_죽은_PC_정리가_폐기한다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B32③ 회전 토큰 죽은 PC 정리")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);

            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var pc1R = await svc.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = pc1.RefreshToken, UserAgent = WindowsUa });
            var sid1 = SidOf(pc1R)!;
            ExpireSession(db, sid1);

            var pc2 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));   // 만료라 409 없음

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.RefreshAsync(
                    new RefreshTokenRequest { RefreshToken = pc1R.RefreshToken, UserAgent = WindowsUa }));
            Assert.Contains("로그아웃된 토큰", ex.Message, StringComparison.Ordinal);   // 🔴 폐기에서 막혔다(가드 아님)

            var pc2After = await RefreshTwiceAsync(svc, pc2, WindowsUa);
            Assert.Equal(SidOf(pc2), SidOf(pc2After));
        }
    }

    /// <summary>
    /// 🔴 <b>G-B33</b> (PI-7) — <c>sid</c> 없는 옛 refresh 를 <b>UA 빈 값</b>으로 갱신 ⇒ 새 세션 행은 <b><c>pc</c></b>.
    /// </summary>
    /// <remarks>
    /// 갱신에는 신고값이 없어 UA 가 비면 판정이 싼 칸(<c>mobile</c>)으로 떨어졌다 ⇒ UA 를 비운 갱신 한 번이면 차단 밖 세션이 생겼다.
    /// <para>🔴 음성 대조군 — <c>RefreshAsync</c> 의 <c>IsNullOrWhiteSpace(request.UserAgent)</c> → <c>pc</c> 갈래를 빼면 <c>mobile</c> 로 FAIL.</para>
    /// </remarks>
    [Fact]
    public async Task G_B33_UA_가_빈_옛토큰_갱신은_pc_로_센다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B33 UA 공백 옛 refresh")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            // 옛 토큰 모양(sid 없음)을 운영 경로로 만든다 — G-B12 와 같은 방식
            HideSessionTable(db);
            var old = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            RestoreSessionTable(db);
            Assert.Null(SidOf(old));   // 양성 조건 — 새 세션을 만드는 갱신 갈래를 탄다

            var refreshed = await svc.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = old.RefreshToken, UserAgent = "" });
            var sid = SidOf(refreshed);
            Assert.NotNull(sid);
            Assert.Equal("pc", SessionKind(db, sid!));
        }
    }

    /// <summary>
    /// 🔴 <b>G-B33b</b> ([4] N-6 · PI-7 대칭) — <b>로그인</b>도 UA 가 비면 신고값(<c>mobile</c>·없음)과 무관하게 <b><c>pc</c></b> 로 센다.
    /// </summary>
    /// <remarks>🔴 음성 대조군 — <c>LoginAsync</c> 의 <c>IsNullOrWhiteSpace(request.UserAgent)</c> → <c>pc</c> 갈래를 빼면 <c>mobile</c> 로 FAIL.</remarks>
    [Fact]
    public async Task G_B33b_UA_가_빈_로그인은_신고값이_mobile_이어도_pc_로_센다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B33b UA 공백 로그인")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 0);   // 두 번 로그인하는 준비 — 판정이 아니라 기기 종류만 잰다

            var claimedMobile = await svc.LoginAsync(NewLoginRequest("mobile", ""));
            Assert.Equal("pc", SessionKind(db, SidOf(claimedMobile)!));

            var noClaim = await svc.LoginAsync(NewLoginRequest(null, null));
            Assert.Equal("pc", SessionKind(db, SidOf(noClaim)!));
        }
    }

    /// <summary>
    /// 🔴 <b>W-1</b> (PI-2 갱신 판정표) — <c>RefreshGate.Decide</c> 를 <b>실제로</b> 부른다(DB 불필요 · 소스 링크).
    /// 401 + 저장소 같음 = 지움 · 401 + 다름 = 남이 돌렸다(성공) · 5xx·연결 실패·408·429 = <b>유지</b>.
    /// 🔴 <b>개정3 절P(설계 §14-1 · R-1)</b> — 400·403·404·409 도 <b>유지</b>(⬛ 개정2: 403 = 지움). 지움의 문은 401 하나.
    /// </summary>
    /// <remarks>🔴 음성 대조군 — 「비 2xx 전부 지움」으로 되돌리면 유지·남이 돌렸다 줄이 FAIL.
    /// 개정2 판(4xx 전부 비교 후 지움)으로 되돌리면 400·403·404·409 줄이 FAIL.</remarks>
    [Theory]
    [InlineData(200, "a", "a", "Saved")]
    [InlineData(204, "a", "a", "Saved")]
    [InlineData(401, "a", "b", "OtherTabRotated")]
    [InlineData(401, "a", "a", "Clear")]
    [InlineData(401, "a", null, "Clear")]
    // ⬛ [낡은 기대값 · 개정2] [InlineData(403, "a", "a", "Clear")] — 개정3 절P 로 뒤집혔다.
    [InlineData(403, "a", "a", "Keep")]
    [InlineData(400, "a", "a", "Keep")]
    [InlineData(404, "a", "a", "Keep")]
    [InlineData(409, "a", "a", "Keep")]
    [InlineData(409, "a", "b", "Keep")]
    [InlineData(408, "a", "a", "Keep")]
    [InlineData(429, "a", "a", "Keep")]
    [InlineData(500, "a", "a", "Keep")]
    [InlineData(502, "a", "a", "Keep")]
    [InlineData(503, "a", "b", "Keep")]
    [InlineData(null, "a", "a", "Keep")]
    public void W_1_갱신판정표는_지워야_할_때만_지운다(int? status, string? sent, string? current, string expected)
    {
        Assert.Equal(expected, HitPan.Web.Services.RefreshGate.Decide(status, sent, current).ToString());
    }

    /// <summary>
    /// 🔴 <b>W-2</b> (개정3 절R · 설계 §14-3 · R-5) — 「유지」 상한: 연속 Keep <b>3회 이상 AND 첫 Keep 부터 60초 이상</b>이면
    /// 안내 <b>한 번</b> · 그 밖 순서는 안내 없음 · <b>어느 줄도 결과가 지움(Clear)이 아니다</b>(DB 불필요 · 소스 링크).
    /// </summary>
    /// <remarks>
    /// 🔴 음성 대조군 — ① 상한을 끄면 「60초 걸친 3회」 줄에 안내가 없어 FAIL ② 상한에서 <c>Clear</c> 를 돌려주면
    /// 「Clear 아님」 검사가 FAIL.
    /// </remarks>
    [Theory]
    [InlineData("K0 K90", "")]                         // 2회뿐 — 90초가 지나도 없음
    [InlineData("K0 K5 K10", "")]                      // 3회지만 10초 안 — 순간 끊김
    [InlineData("K0 K30 K60", "2")]                    // 3회 · 60초 — 세 번째에서 한 번
    [InlineData("K0 K30 K60 K90 K120", "2")]           // 그 뒤 Keep 은 안내 없음(중복 금지)
    [InlineData("K0 K30 S45 K50 K70 K100", "")]        // Saved 가 줄을 끊는다 — 다시 세면 50초뿐
    [InlineData("K0 K30 S45 K50 K70 K100 K111", "6")]  // 다시 센 줄이 61초 · 4회 — 그때 한 번
    [InlineData("K0 K30 C45 K50 K70 K100", "")]        // Clear 도 끊는다
    [InlineData("K0 K30 O45 K50 K70 K100", "")]        // OtherTabRotated 도 끊는다
    [InlineData("K0 K30 K60 S70 K80 K110 K140", "2 6")] // 끊긴 뒤 새 줄은 다시 한 번 안내할 수 있다
    public void W_2_유지_상한은_3회_60초에_한번만_안내하고_지우지_않는다(string script, string expectedShowAt)
    {
        var t0 = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var state = default(HitPan.Web.Services.KeepStreak);
        var shownAt = new List<int>();
        var steps = script.Split(' ');
        for (var i = 0; i < steps.Length; i++)
        {
            var decision = steps[i][0] switch
            {
                'K' => HitPan.Web.Services.RefreshDecision.Keep,
                'S' => HitPan.Web.Services.RefreshDecision.Saved,
                'C' => HitPan.Web.Services.RefreshDecision.Clear,
                _ => HitPan.Web.Services.RefreshDecision.OtherTabRotated
            };
            var at = t0.AddSeconds(int.Parse(steps[i][1..], System.Globalization.CultureInfo.InvariantCulture));
            var step = HitPan.Web.Services.RefreshGate.NextKeepStreak(state, decision, at);

            // 🔴 상한은 판정을 바꾸지 않는다 — Keep 을 넣었으면 Keep 이 나온다(지움 금지 · 설계 §14-3)
            if (decision == HitPan.Web.Services.RefreshDecision.Keep)
            {
                Assert.NotEqual(HitPan.Web.Services.RefreshDecision.Clear, step.Decision);
            }
            Assert.Equal(decision, step.Decision);

            if (step.ShowDegradedNotice) shownAt.Add(i);
            state = step.State;
        }

        Assert.Equal(expectedShowAt, string.Join(' ', shownAt));
    }

    /// <summary>
    /// 🔴🔴 <b>G-B35</b> (개정3 절Q ③ · 설계 §14-2 · R-1 서버) — 갱신 중 DB 가 <b>1452</b> 로 터지면 컨트롤러 응답은 <b>500</b>.
    /// </summary>
    /// <remarks>
    /// 종전은 예외가 컨트롤러를 빠져나가 전역 미들웨어가 <c>MySqlException</c> 1452 → <b>409</b> 로 바꿨다 ⇒ 화면은 4xx 를
    /// 「서버가 판정했다」로 읽고 멀쩡한 로그인을 지웠다. G-B30 과 같은 호출 방식(컨트롤러 직접 · 트리거로 실패 만들기).
    /// <para>🔴 음성 대조군 — <c>Refresh</c> 의 ③ <c>catch (Exception)</c> 를 지우면 예외가 새어 나와 FAIL(실서버에선 409).</para>
    /// </remarks>
    [Fact]
    public async Task G_B35_갱신이_서버_오류로_터지면_500_이다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B35 갱신 서버 오류 500")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            var pc = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid = SidOf(pc)!;

            db.Execute(
                "CREATE TRIGGER gate_b35_fail_rotate BEFORE INSERT ON refresh_tokens FOR EACH ROW "
                + "SIGNAL SQLSTATE '23000' SET MYSQL_ERRNO = 1452, MESSAGE_TEXT = 'gate_b35_fail_rotate'");
            Microsoft.AspNetCore.Mvc.IActionResult result;
            try
            {
                result = await NewAuthController(svc, db, WindowsUa).Refresh(
                    new RefreshTokenRequest { RefreshToken = pc.RefreshToken }, CancellationToken.None);
            }
            finally
            {
                DropTrigger(db, "gate_b35_fail_rotate");
            }

            var obj = Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
            Assert.Equal(1, ActiveRefreshCount(db, sid));   // 양성 조건 — 회전이 실제로 막혀 옛 refresh 가 그대로다
        }
    }

    /// <summary>
    /// 🔴🔴 <b>G-B36</b> (개정3 절Q ② · X-1 = (가) · PM 결재 9/28) — 다른 PC 가 살아 있는데 <c>sid</c> 없는 옛 refresh 로 갱신
    /// ⇒ <b>401</b>(<c>other_pc_in_use</c>) · 세션 행 <b>새로 안 생김</b>.
    /// </summary>
    /// <remarks>
    /// 종전은 <c>ConcurrentPcLoginException</c>(<c>Exception</c> 직계)이 컨트롤러를 빠져나가 500 ⇒ 화면은 「유지」로 영원히 재시도하고
    /// 「다른 PC에서 사용 중입니다」를 한 번도 못 봤다. 준비는 G-B33 과 같다(<c>HideSessionTable</c> 로 옛 토큰 모양).
    /// <para>⬛ [낡은 대조군] 「② catch 를 지우면 500」 — ② 는 도달 경로가 없다(서비스가 401 로 바꾼다 · <c>AuthService.cs:989-997</c>).</para>
    /// <para>🔴 음성 대조군 — 서비스의 변환(<c>ConcurrentPcLoginException</c> → <c>UnauthorizedAccessException</c>)과 컨트롤러 ② 를 함께 빼면
    /// 예외가 ③ 으로 가 500 이라 FAIL(② 만 남기면 ② 가 401 을 내므로 대조가 안 된다).</para>
    /// </remarks>
    [Fact]
    public async Task G_B36_다른_PC_사용중_갱신은_401_이고_세션_행이_안_생긴다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B36 갱신 중 다른 PC 사용 중 401")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            // 옛 토큰 모양(sid 없음)을 운영 경로로 만든다 — G-B33 과 같은 준비
            HideSessionTable(db);
            var old = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            RestoreSessionTable(db);
            Assert.Null(SidOf(old));   // 양성 조건 — 새 세션을 만드는 갱신 갈래를 탄다

            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            Assert.NotNull(SidOf(pc1));
            var before = SessionCount(db);

            var result = await NewAuthController(svc, db, WindowsUa).Refresh(
                new RefreshTokenRequest { RefreshToken = old.RefreshToken }, CancellationToken.None);

            var obj = Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
            Assert.Equal(StatusCodes.Status401Unauthorized, obj.StatusCode);
            // ⬛ [낡은 단언] `Assert.Contains("other_pc_in_use", …)` — 본선 run 36392248553 FAIL: 응답이 컨트롤러 ② 가 아니라
            //   ① 에서 나간다(서비스가 이미 401 로 바꾼다 · AuthService.cs:989-997). 🔴 동작만 잰다: 401 + 다른 PC 안내 + 세션 행 안 생김.
            //   JSON 은 한글을 \uXXXX 로 이스케이프하므로 **역직렬화한 값**으로 비교한다.
            using var body = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(obj.Value));
            Assert.Contains("다른 컴퓨터에서 사용 중", body.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Equal(before, SessionCount(db));   // 거절된 갱신이 세션 행을 남기지 않았다
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 판정 규칙 — "모르는 것은 싼 칸으로" (DB 불필요 · build 잡에서 돈다)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>기기 종류 판정 규칙</b>이 히트판의 결정과 같은가: <b>"모르는 것은 싼 칸으로"</b>.
    /// </summary>
    /// <remarks>
    /// 글자가 아니라 <b>실제 메서드</b>를 부른다.
    /// <para>
    /// 🔴 20260927작2 절A — 종전 이 시험은 <c>AuthService.NormalizeDeviceKindForTests</c> 를 불렀는데
    /// 그 <b>시험 전용 창구가 제거</b>됐다(판정 자리를 하나로 모았다). 이제 판정 본체
    /// <c>DeviceTypeResolver</c> 의 <c>public static</c> 을 직접 부른다 —
    /// 시험을 위해 운영에 창구를 내지 않는다.
    /// </para>
    /// <para>
    /// 근거: 20260815 아키텍처명세서 §3 #4 — 모르는 값을 <c>pc</c> 로 보내
    /// <b>고객이 쓰지도 않은 비싼 자리에 돈을 냈다.</b>
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("pc", "pc")]
    [InlineData("PC", "pc")]         // 대소문자는 같은 값이다
    [InlineData("mobile", "mobile")]
    [InlineData("tablet", "mobile")] // 태블릿은 휴대기기 (사장님 판정) — 세션 표는 칸이 둘뿐이다
    [InlineData("", "mobile")]       // 안 보냈으면 싼 칸
    [InlineData(null, "mobile")]     // 모르는 값도 싼 칸
    [InlineData("무엇인가", "mobile")]
    public void 기기종류_판정은_모르면_싼칸이다(string? input, string expected)
    {
        Assert.Equal(expected,
            DeviceTypeResolver.ToSessionDeviceKind(DeviceTypeResolver.ResolveDeviceType(input, null)));
    }

    // ══════════════════════════════════════════════════════════════
    // 표 읽기 · 상태 만들기 (판정하지 않는다)
    // ══════════════════════════════════════════════════════════════

    private static string? ColumnDefault(IDbConnection db, string column) =>
        db.ExecuteScalar<string>(
            @"SELECT COLUMN_DEFAULT FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tenant_settings'
                 AND COLUMN_NAME = @Column",
            new { Column = column });

    private static int? CurrentKillSwitch(IDbConnection db) =>
        db.ExecuteScalar<int?>(
            "SELECT enforce_single_pc_login FROM tenant_settings WHERE tenant_id = @TenantId",
            new { TenantId });

    /// <summary>생존 확인 스위치의 <b>저장된 값</b>(DB-129 · 4차). 행이 없으면 <c>null</c>.</summary>
    private static int? CurrentValiditySwitch(IDbConnection db) =>
        db.ExecuteScalar<int?>(
            "SELECT enforce_session_validity FROM tenant_settings WHERE tenant_id = @TenantId",
            new { TenantId });

    /// <summary>새 칸(<c>enforce_one_pc_login</c> · DB-131)의 저장된 값. 행이 없으면 <c>null</c>.</summary>
    private static int? CurrentOnePcSwitch(IDbConnection db) =>
        db.ExecuteScalar<int?>(
            "SELECT enforce_one_pc_login FROM tenant_settings WHERE tenant_id = @TenantId",
            new { TenantId });

    /// <summary>그 <c>sid</c> 의 폐기되지 않은 refresh 행 수(읽기만 · 20260928작2).</summary>
    private static int ActiveRefreshCount(IDbConnection db, string sid) =>
        db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @UserId AND session_id = @Sid AND is_revoked = 0",
            new { UserId, Sid = sid });

    /// <summary>운영 <c>RefreshAsync</c> 를 두 번 — 회전된 토큰으로 2회차까지 성공해야 「계속 쓴다」다(설계 §8 공통).</summary>
    private static async Task<LoginResponse> RefreshTwiceAsync(AuthService svc, LoginResponse from, string ua)
    {
        var r1 = await svc.RefreshAsync(new RefreshTokenRequest { RefreshToken = from.RefreshToken, UserAgent = ua });
        return await svc.RefreshAsync(new RefreshTokenRequest { RefreshToken = r1.RefreshToken, UserAgent = ua });
    }

    /// <summary>준비 — 그 표의 INSERT 가 <b>반드시 실패</b>하게 한다(상태 만들기 · 판정 아님).</summary>
    private static void FailInsertsInto(IDbConnection db, string triggerName, string table) =>
        db.Execute(
            $"CREATE TRIGGER {triggerName} BEFORE INSERT ON {table} FOR EACH ROW "
            + $"SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '{triggerName}'");

    private static void DropTrigger(IDbConnection db, string triggerName) =>
        db.Execute($"DROP TRIGGER IF EXISTS {triggerName}");

    private sealed class AccessTrailRow
    {
        public string? Action { get; set; }
        public string? EntityId { get; set; }
        public string? Kind { get; set; }
        public string? TenantId { get; set; }
        public string? UserId { get; set; }
    }

    /// <summary>접속기록(<c>audit_trail</c> · <c>user_session</c>)을 읽는다 — 판정하지 않는다.</summary>
    private static List<AccessTrailRow> AccessTrail(IDbConnection db) =>
        db.Query<AccessTrailRow>(
            @"SELECT action_type AS Action, entity_id AS EntityId,
                     JSON_VALUE(after_value, '$.device_kind') AS Kind,
                     tenant_id AS TenantId, user_id AS UserId
                FROM audit_trail WHERE entity_type = 'user_session'").ToList();

    private static bool IsAlive(IDbConnection db, string sid) =>
        db.ExecuteScalar<int?>(
            @"SELECT CASE WHEN expires_at > UTC_TIMESTAMP(6) THEN 1 ELSE 0 END
                FROM user_sessions WHERE session_id = @Sid", new { Sid = sid }) == 1;

    /// <summary>준비 — 이미 있는 세션 행을 <b>만료 상태로</b> 만든다(8시간 지난 밤샘 모양).</summary>
    private static void ExpireSession(IDbConnection db, string sid) =>
        db.Execute(
            "UPDATE user_sessions SET expires_at = UTC_TIMESTAMP(6) - INTERVAL 1 HOUR WHERE session_id = @Sid",
            new { Sid = sid });

    /// <summary>
    /// 준비 (G-B13) — 운영 코드가 <b>세션 행을 넣는 그 순간</b> 남이 이 토큰을 먼저 써 버린 상태로 만든다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 트리거인가</b> — 회전의 <c>deleted == 0</c>(단일사용 거부 401)은 원래 *"동시 갱신 2발"* 이라야
    /// 나온다. 시험이 두 번 동시에 부르면 어느 쪽이 이길지 몰라 <b>간헐적 게이트</b>가 된다.
    /// 세션 INSERT 에 트리거를 달면 그 경주가 <b>결정적으로</b> 재현된다.
    /// <para>⚠️ 이것은 <b>상태 만들기</b>다 — 판정하지 않는다. 판정은 운영 코드가 한다.</para>
    /// </remarks>
    private static void StealRefreshTokenWhenSessionInserted(IDbConnection db) =>
        db.Execute(
            @"CREATE TRIGGER gate_b13_steal_token AFTER INSERT ON user_sessions FOR EACH ROW
                DELETE FROM refresh_tokens WHERE user_id = NEW.user_id");

    private static void DropSessionInsertTrigger(IDbConnection db) =>
        db.Execute("DROP TRIGGER IF EXISTS gate_b13_steal_token");

    /// <summary>준비 — 남이 이 계정의 refresh 토큰을 이미 소비한 상태(회전 <b>전</b> 확인에서 걸리는 모양).</summary>
    /// <remarks>
    /// ⚠️ 5차 — G-B13b 는 이 헬퍼를 더 쓰지 않는다. 이것이 만드는 401 은 <c>BeginTransaction</c> 앞에서
    /// 나므로 <b>보상 호출에 닿지 못한다</b>([4] R-1). 남겨 두는 이유는 <i>"회전 전 확인"</i> 이라는
    /// 다른 상태가 필요한 게이트가 생길 때를 위한 것이고, <b>보상 계측에는 쓰면 안 된다.</b>
    /// </remarks>
    private static void StealRefreshToken(IDbConnection db) =>
        db.Execute("DELETE FROM refresh_tokens WHERE user_id = @UserId", new { UserId });

    /// <summary>
    /// 준비 (G-B13c) — 세션 INSERT 가 <b>실제로 돌았다</b>는 흔적을 <c>security_alerts</c> 에 남긴다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>양성 조건 확인용</b>이다. 보상이 지운 뒤에는 행이 없으므로, 흔적이 없으면
    /// <i>"INSERT 가 아예 안 돌았다"</i> 와 <i>"돌았고 보상이 지웠다"</i> 를 가를 수 없다 —
    /// 그러면 <c>0 == 0</c> 인 <b>빈 통과</b>가 된다.
    /// ⚠️ 이것은 상태 만들기·기록이다. 판정은 시험 본문이 한다.
    /// (컬럼은 출하 DDL 실측 — <c>alert_id</c>·<c>alert_type</c> 만 NOT NULL, 나머지는 기본값 · #13)
    /// </remarks>
    private static void MarkSessionInsert(IDbConnection db) =>
        db.Execute(
            @"CREATE TRIGGER gate_b13c_mark_insert AFTER INSERT ON user_sessions FOR EACH ROW
                INSERT INTO security_alerts (alert_id, tenant_id, user_id, alert_type, description)
                VALUES (UUID(), NEW.tenant_id, NEW.user_id, 'gate_b13c_session_inserted', NEW.session_id)");

    private static void DropSessionInsertMarker(IDbConnection db) =>
        db.Execute("DROP TRIGGER IF EXISTS gate_b13c_mark_insert");

    /// <summary>
    /// 준비 (G-B13c) — <c>BeginTransaction</c> <b>한 동작만</b> 터지는 연결. 나머지는 실제 연결에 넘긴다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 이것이 "가짜" 가 아닌가</b> — SQL·표·트랜잭션 의미는 전부 실제 MariaDB 가 처리한다.
    /// 이 래퍼가 하는 일은 <b>고장 하나를 결정적으로 재현</b>하는 것뿐이다
    /// (운영 주석이 기록한 <i>"open and available Connection"</i> 사고 모양).
    /// 판정(<i>세션 행이 남았나</i>)은 운영 코드와 실제 표가 한다 — 이 파일의 규칙 그대로다.
    /// ⚠️ <c>DbConnection</c> 을 상속해야 한다 — <c>IUnitOfWork.GetDbConnection()</c> 의 반환형이고,
    /// Dapper 의 비동기 경로도 <c>DbConnection</c>·<c>DbCommand</c> 를 요구한다.
    /// </remarks>
    private sealed class BeginTransactionFailingConnection : DbConnection
    {
        private readonly MySqlConnection _inner;
        private readonly string _message;

        public BeginTransactionFailingConnection(MySqlConnection inner, string message)
        {
            _inner = inner;
            _message = message;
        }

        /// <summary>운영 코드가 <c>BeginTransaction</c> 을 <b>몇 번 시도했나</b>(양성 조건 확인).</summary>
        public int BeginAttempts { get; private set; }

        // ⚠️ 기반 클래스의 setter 가 [AllowNull] 이다 — 같이 붙이지 않으면 CS8765 (#19 경고 0).
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString
        {
            get => _inner.ConnectionString;
            set => _inner.ConnectionString = value!;
        }

        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;

        public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
        public override void Close() => _inner.Close();
        public override void Open() => _inner.Open();

        protected override DbCommand CreateDbCommand() => _inner.CreateCommand();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            BeginAttempts++;
            throw new InvalidOperationException(_message);
        }
    }

    /// <summary>
    /// 준비 — <b>밀어내기로 지워진</b> PC1 의 행을 <b>만료된 채로</b> 되돌려 놓는다 (F-6 재현).
    /// </summary>
    /// <remarks>
    /// ⚠️ 이것은 <b>판정이 아니라 상태 만들기</b>다. F-6 은 *"만료된 내 PC 가 부활해 다른 PC 와 공존"* 이므로
    /// 그 출발 상태를 만들어야 갱신 경로의 판정을 물어볼 수 있다.
    /// </remarks>
    private static void ReviveSessionRowAsExpired(IDbConnection db, string sid, string kind) =>
        db.Execute(
            @"INSERT INTO user_sessions
                  (session_id, user_id, tenant_id, login_at, last_active_at, expires_at, device_kind)
              VALUES (@Sid, @UserId, @TenantId,
                      UTC_TIMESTAMP(6) - INTERVAL 9 HOUR,
                      UTC_TIMESTAMP(6) - INTERVAL 9 HOUR,
                      UTC_TIMESTAMP(6) - INTERVAL 1 HOUR, @Kind)",
            new { Sid = sid, UserId, TenantId, Kind = kind });

    /// <summary>
    /// <c>AuthController</c> <b>실물</b>을 <c>DefaultHttpContext</c> 위에 세운다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>가짜는 판정에 끼어들지 않는 것들뿐이다.</b> 기기 등록(<c>ITenantDeviceService</c>)·
    /// 알림·출근·약관은 <c>DeviceFingerprint</c> 가 없거나 <c>employee_id</c> 가 비어
    /// 이 경로에서 돌지 않거나, 운영 코드가 <c>try/catch</c> 로 감싸 <b>로그인 결과를 바꾸지 않는다.</b>
    /// 우리가 재는 것은 <b>UA 덮어쓰기</b>와 <b>로그아웃 범위</b> 둘이다.
    /// </remarks>
    private AuthController NewAuthController(
        AuthService svc, MySqlConnection db, string headerUa, string? sid = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbConnection>(db);   // 로그아웃이 RequestServices 에서 꺼낸다

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Headers["User-Agent"] = headerUa;
        ctx.Items["TenantId"] = TenantId;
        ctx.Items["UserId"] = UserId;

        if (!string.IsNullOrWhiteSpace(sid))
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("sid", sid), new Claim("user_id", UserId) }, "gate"));
        }

        return new AuthController(
            svc,
            Mock.Of<IHrService>(),
            Mock.Of<ITenantDeviceService>(),
            Mock.Of<ITermsConsentService>(),
            Mock.Of<INotificationService>(),
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = ctx }
        };
    }

    /// <summary>
    /// <c>SessionValidityMiddleware</c> <b>실물</b>을 한 번 돌린다.
    /// </summary>
    /// <remarks>
    /// ⚠️ 캐시는 <b>매번 새로 만든다</b> — 미들웨어가 <c>session-validity:{tenantId}</c>(⬛ 3차까지
    /// <c>single-pc-login:{tenantId}</c> · 4차에서 스위치가 갈려 키도 바뀌었다)·
    /// <c>session-alive:{sid}</c> 를 10초 캐시하므로, 공유하면 앞 시험이 뒤 시험을 오염시킨다.
    /// </remarks>
    private static async Task<(DefaultHttpContext Ctx, bool NextCalled)> RunSessionValidityAsync(
        MySqlConnection db, string sid)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/stock/list";   // 보호 API — /api 밖이면 미들웨어가 그냥 통과시킨다
        ctx.Items["TenantId"] = TenantId;
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sid", sid), new Claim("user_id", UserId) }, "gate"));

        var nextCalled = false;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var mw = new SessionValidityMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<SessionValidityMiddleware>.Instance,
            cache);

        await mw.InvokeAsync(ctx, db);
        return (ctx, nextCalled);
    }

    // ══════════════════════════════════════════════════════════════
    // 통로 둘 — 판정하지 않는다
    // ══════════════════════════════════════════════════════════════

    /// <summary>격리 DB 연결을 운영 코드에 넘겨주는 통로. 그 밖에 아무것도 하지 않는다.</summary>
    private sealed class GateUnitOfWork : IUnitOfWork
    {
        private readonly DbConnection _conn;
        public GateUnitOfWork(DbConnection conn) => _conn = conn;

        public DbConnection GetDbConnection() => _conn;

        // 로그인 성공 시 LastLoginAt·실패카운트를 EF 로 저장하는 자리 — 축 B 판정과 무관하다.
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);

        /// <remarks>
        /// ⚠️ <b>일부러 던진다.</b> 여기 오는 길은 <c>BackfillParentEmployeeAsync</c> 하나뿐이고,
        /// 운영 코드가 그것을 <c>try/catch</c> 로 감싸 <b>로그인을 막지 않는다</b>(AuthService:100).
        /// 조용히 <c>null</c> 을 돌려주면 그 자리에서 NRE 가 나 <b>재려던 것과 무관한 실패</b>가 섞인다.
        /// </remarks>
        public IRepository<T> Repository<T>() where T : BaseEntity =>
            throw new NotSupportedException(
                "게이트는 EF 리포지토리를 태우지 않는다 — 축 B 는 Dapper 경로다(사원 백필은 운영 try/catch 가 받는다).");

        public Task<ISharedTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            throw new NotSupportedException(
                "게이트는 EF 공유 트랜잭션을 태우지 않는다 — 갱신 경로는 Dapper 트랜잭션을 직접 연다.");

        // ⚠️ 연결은 시험이 소유한다(using) — 통로가 남의 연결을 닫지 않는다.
        public void Dispose() { }
    }

    /// <summary>계정 한 줄을 돌려주는 조회. 판정·기록은 전부 운영 코드가 한다.</summary>
    private sealed class GateUserLookup : IAuthUserLookup
    {
        private readonly User _user;
        public GateUserLookup(User user) => _user = user;

        public Task<User?> FindUserByEmailAsync(string email, CancellationToken ct = default) =>
            Task.FromResult<User?>(_user);

        public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult<User?>(string.Equals(userId, _user.Id, StringComparison.Ordinal) ? _user : null);

        // 🔴 null 을 돌려준다 — employee_id 가 빈 값이면 출근·약관 곁가지가 아예 안 돈다.
        //   재는 것은 세션·기기종류·로그아웃 범위뿐이다.
        public Task<Employee?> FindActiveEmployeeByUserAsync(
            string userId, string tenantId, CancellationToken ct = default) => Task.FromResult<Employee?>(null);

        public Task<List<Employee>> FindEmployeesByTenantAsync(
            string tenantId, CancellationToken ct = default) => Task.FromResult(new List<Employee>());
    }
}
