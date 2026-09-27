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
/// 🔴 <b>G-B1 ~ G-B13</b> — 축 B 봉합이 <b>동작으로</b> 성립하는가 (20260927작2 절I · <b>3차</b>에서 G-B13 추가).
/// </summary>
/// <remarks>
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
    private static void SetKillSwitch(IDbConnection db, int on) =>
        db.Execute(
            @"INSERT INTO tenant_settings (tenant_id, enforce_single_pc_login)
              VALUES (@TenantId, @On)
              ON DUPLICATE KEY UPDATE enforce_single_pc_login = @On",
            new { TenantId, On = on });

    private static string? SessionKind(IDbConnection db, string sid) =>
        db.ExecuteScalar<string>(
            "SELECT device_kind FROM user_sessions WHERE session_id = @Sid", new { Sid = sid });

    private static int SessionCount(IDbConnection db) =>
        db.ExecuteScalar<int>("SELECT COUNT(*) FROM user_sessions");

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
    /// 🔴 음성 대조군 — <c>AuthService.cs:122</c> 를 신고값 단독 판정(옛 <c>NormalizeDeviceKind</c> 식)으로
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
    /// 🔴 음성 대조군 — <c>AuthService.cs:160</c> 의 <c>InsertSessionAsync(...)</c> <b>호출 줄을 주석</b>하면
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
    /// <para>🔴 음성 대조군 — <c>AuthController.cs:60</c> 의
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
    // G-B4 / G-B10 — 미들웨어가 킬스위치를 읽는다 (F-1 전면 잠금)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>G-B4</b> — <c>sid</c> 있는 토큰 + <b>세션 행 없음</b> + 킬스위치 <c>0</c>
    /// ⇒ 보호 API <b>200</b> (F-1 = 고객이 끌 수도 없던 전면 잠금).
    /// </summary>
    /// <remarks>
    /// 🔴 음성 대조군 — 미들웨어의 축 B 킬스위치 판독(<c>IsSinglePcLoginEnabledAsync</c> 호출)을 제거하면
    /// 곧바로 생존 조회로 내려가 <b>401</b> 이 되어 FAIL 한다.
    /// <para>🔴 판정 자리도 같이 잰다 — 킬스위치는 <c>sid</c> 판독 <b>뒤</b>, 생존 조회 <b>앞</b>이어야 한다.
    /// 앞에 두면 <c>sid</c> 없는 옛 토큰까지 DB 를 한 번 물고(금지 #1b 경로), 뒤에 두면 이미 401 이 나간 뒤다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B4_킬스위치가_꺼져_있으면_세션_행이_없어도_통과한다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B4 킬스위치 끔 → 통과")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();
        SetKillSwitch(db, 0);

        var (ctx, nextCalled) = await RunSessionValidityAsync(db, sid: "sid-not-in-table");

        Assert.True(nextCalled, "킬스위치가 꺼져 있는데 요청이 끊겼다 — F-1 전면 잠금이 그대로다.");
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.Equal(0, SessionCount(db));   // 행이 없는 상태였음을 확인한다(양성 조건 확인)
    }

    /// <summary>
    /// 🔴 <b>G-B10</b> — <c>sid</c> 있음 + 세션 <b>죽음</b> + 킬스위치 <b>ON</b> ⇒ <b>401</b>.
    /// </summary>
    /// <remarks>
    /// 작1 에서 <b>무시험</b>이던 분기다 — F-1 잠금이 실제로 나오는 자리이므로,
    /// *"켜면 끊는다"* 가 성립하는지를 반대편에서 고정한다.
    /// <para>🔴 음성 대조군 — 세션 생존 확인(<c>session-alive</c> 조회와 <c>if (!alive)</c>)을 제거하면
    /// 200 이 되어 FAIL 한다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B10_킬스위치가_켜져_있고_세션이_죽었으면_401_이다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B10 킬스위치 켬 → 401")) return;
        SetUpFreshInstall();

        using var db = new MySqlConnection(DbConnString());
        db.Open();
        SetKillSwitch(db, 1);

        var (ctx, nextCalled) = await RunSessionValidityAsync(db, sid: "sid-already-dead");

        Assert.False(nextCalled, "죽은 세션인데 요청이 통과했다 — 밀어내기가 즉시 반영되지 않는다.");
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
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
    /// <para>🔴 음성 대조군 — <c>AuthService.cs:166</c> 의 <c>sessionRecorded ? sessionId : null</c> 을
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

            // PC2 — 사용자가 [그 PC 끊고 여기서 쓰기] 를 눌러 들어온 새 컴퓨터(운영 경로 그대로)
            var pc2 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa, force: true));
            var sid2 = SidOf(pc2);
            Assert.NotNull(sid2);

            // PC1 의 행은 밀어내기로 지워졌다 — F-6 재현을 위해 **만료된 상태로 되돌려 놓는다**(준비).
            ReviveSessionRowAsExpired(db, sid1!, "pc");

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.RefreshAsync(
                    new RefreshTokenRequest { RefreshToken = pc1.RefreshToken, UserAgent = WindowsUa }));

            // 고객 언어로만 말한다 — 개발용어 금지
            Assert.DoesNotContain("session", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(IsAlive(db, sid1!), "거절했는데도 PC1 세션이 되살아났다 — PC 2대가 공존한다(F-6).");
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
    /// <para>🔴 음성 대조군 — <c>AuthController.cs:389</c> 의 <c>WHERE session_id</c> 를
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
            // 킬스위치는 꺼 둔다 — 여기서 재는 것은 로그아웃 범위이고, 차단이 섞이면 축이 흐려진다.
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
        SetKillSwitch(db, 1);
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
    /// <para>🔴 음성 대조군 — <c>EnforceSinglePcLoginAsync</c> 의 감사 INSERT 를 지우면 0건이 되어 FAIL 한다.</para>
    /// </remarks>
    [Fact]
    public async Task G_B9_밀어내기는_보안_흔적을_남긴다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B9 밀어내기 감사")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            SetKillSwitch(db, 1);   // 켜야 밀어내기 경로로 들어간다

            var pc1 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid1 = SidOf(pc1);
            Assert.NotNull(sid1);
            Assert.Equal(0, AlertCount(db, "pc_session_forced_out"));   // 출발점

            var pc2 = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa, force: true));
            var sid2 = SidOf(pc2);

            Assert.Null(SessionKind(db, sid1!));                        // 밀려났다
            Assert.Equal("pc", SessionKind(db, sid2!));                 // 새 PC 가 들어왔다
            Assert.Equal(1, AlertCount(db, "pc_session_forced_out"));   // 🔴 흔적이 남았다
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
    /// (실제로 지워 FAIL 을 확인한 기록은 개발명세서 §4 에 있다.)
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
    /// 🔴 <b>G-B13b</b> — 보상 삭제가 <b>이어받은 세션은 건드리지 않는다</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 잔여 행을 없애려고 <c>user_id</c> 를 전삭하거나 이어받은 세션까지 지우면,
    /// <b>일하고 있는 PC 가 갱신 실패 한 번으로 끊긴다</b>(#20 흐름은 안 끊긴다).
    /// 이 시험은 그 과잉 봉합을 막는다 — 같은 회전 실패인데 <b>행이 살아 있어야</b> 한다.
    /// </remarks>
    [Fact]
    public async Task G_B13b_회전이_실패해도_이어받은_세션은_지우지_않는다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-B13b 이어받은 세션 보존")) return;
        SetUpFreshInstall();

        var (svc, db) = NewAuthService();
        using (db)
        {
            // 정상 로그인 — `sid` 가 실린다(= 갱신이 이어받는 세션).
            var first = await svc.LoginAsync(NewLoginRequest("pc", WindowsUa));
            var sid = SidOf(first);
            Assert.NotNull(sid);
            Assert.Equal(1, SessionCount(db));

            // 남이 그 토큰을 먼저 써 버렸다 — 회전은 401 이다(세션 INSERT 는 아예 없다).
            StealRefreshToken(db);

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.RefreshAsync(
                    new RefreshTokenRequest { RefreshToken = first.RefreshToken, UserAgent = WindowsUa }));
            Assert.Contains("로그아웃된", ex.Message);   // 이 경로는 회전 전 확인에서 걸린다

            // 🔴 일하고 있는 PC 의 세션은 그대로다.
            Assert.Equal(1, SessionCount(db));
            Assert.True(IsAlive(db, sid!), "갱신이 실패했다고 이어받은 세션을 지웠다 — 일하는 PC 를 끊는다(#20).");
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

    /// <summary>준비 (G-B13b) — 남이 이 계정의 refresh 토큰을 이미 소비한 상태.</summary>
    private static void StealRefreshToken(IDbConnection db) =>
        db.Execute("DELETE FROM refresh_tokens WHERE user_id = @UserId", new { UserId });

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
    /// ⚠️ 캐시는 <b>매번 새로 만든다</b> — 미들웨어가 <c>single-pc-login:{tenantId}</c>·
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
