using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작12) — <b>로그인 전 업데이트 탈출구가 실제로 열려 있는가.</b>
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 근거(2026-08-25)</b>: <i>"로그인 전에 강제 업데이트 창구를 만드는 이유는
/// <b>로그인에서 갇히는 경우를 실제 경험</b>했음. <b>테스트 계정이라 망정이지 고객사였으면 대형사고</b>"</i>
/// </para>
/// <para>
/// 🔴 <b>실측(2026-08-25, loopback)</b>: <c>GET update-status-local</c> → <b>200</b> 인데
/// <c>POST update-consent-local</c> → <b>401</b>.
/// ⇒ 「지금 업데이트」 버튼은 **배포 이후 한 번도 눌린 적이 없다.**
/// 안내는 떴는데 눌러도 아무 일이 안 났다 — <b>탈출구가 그려져만 있고 막혀 있었다.</b>
/// 작10 이 조회(GET)만 <c>TenantMiddleware</c> 면제에 넣고 실행(POST)을 빠뜨렸다.
/// </para>
/// <para>
/// <b>사장님 결재 B안</b> — 표시·실행 <b>모두 터널 허용</b> + <b>멱등</b> + <b>rate limit</b>.
/// 갇힌 사람은 터널로 들어온다. loopback 제한은 정작 그 사람을 막고 있었다.
/// 남은 위험은 "가짜 버전 설치"가 아니라(버전 대조·서명검증이 막는다) <b>반복 재시작(DoS)</b> 뿐이고,
/// 그건 <b>같은 버전 재승인을 새 INSERT 없이 돌려보내면</b> 사라진다.
/// </para>
/// <para>
/// 🔴 이 게이트는 봉합을 빼면 실제로 FAIL 한다(가짜 게이트 누적 16번의 교훈).
/// </para>
/// </remarks>
public class PreLoginUpdateGateTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"{path} 가 있어야 한다");
        return File.ReadAllText(path);
    }

    /// <summary>주석을 걷어낸 실제 코드만 남긴다(거짓 경보 방지).</summary>
    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return t.Length > 0
                       && !t.StartsWith("//", StringComparison.Ordinal)
                       && !t.StartsWith("*", StringComparison.Ordinal)
                       && !t.StartsWith("/*", StringComparison.Ordinal)
                       && !t.StartsWith("///", StringComparison.Ordinal);
            }));

    private static string TenantMiddleware() =>
        CodeLines(Read("src", "HitPan.API", "Middleware", "TenantMiddleware.cs"));

    private static string AuthController() =>
        CodeLines(Read("src", "HitPan.API", "Controllers", "AuthController.cs"));

    // ───────────────────────────────────────────────────────────────
    // 🔴 실측 401 — 실행 경로가 미들웨어에 막혀 있었다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>실행(POST)도 인증 면제여야 한다.</b> 조회만 열고 실행을 막으면 버튼이 401 로 죽는다.
    /// </summary>
    [Fact]
    public void 업데이트_실행경로가_인증면제여야_한다()
    {
        var mw = TenantMiddleware();

        Assert.Contains("/api/auth/update-status-local", mw, StringComparison.Ordinal);
        // 🔴 이 한 줄이 없어서 「지금 업데이트」 가 배포 내내 401 이었다.
        Assert.Contains("/api/auth/update-consent-local", mw, StringComparison.Ordinal);

        // 면제를 넓히지 않았는지 — /api/auth 통째 개방 금지(작10 원칙 계승).
        Assert.DoesNotContain("StartsWithSegments(\"/api/auth\")", mw, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>터널에서도 조회·실행이 되어야 한다</b>(사장님 결재 B안).
    /// 갇힌 사람은 터널로 들어온다 — <c>viaTunnel</c> 이면 즉시 <c>NotFound</c> 로 끊는 코드가 남아 있으면 안 된다.
    /// </summary>
    [Fact]
    public void 터널접속을_404로_끊지_않아야_한다()
    {
        var ctrl = AuthController();

        // "viaTunnel 이면 return NotFound()" 형태의 차단이 살아 있으면 B안 위반이다.
        // 공백·줄바꿈을 지우고 본다 — 서식만 바꿔 빠져나가지 못하게.
        var squashed = new string(ctrl.Where(c => !char.IsWhiteSpace(c)).ToArray());

        Assert.DoesNotContain("if(viaTunnel||remoteisnull||!System.Net.IPAddress.IsLoopback(remote))returnNotFound();",
            squashed, StringComparison.Ordinal);
        Assert.DoesNotContain("if(viaTunnel||remoteisnull||!System.Net.IPAddress.IsLoopback(remote)){returnNotFound();}",
            squashed, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>멱등 — 같은 버전 재승인은 새 INSERT 를 만들지 않는다.</b>
    /// loopback 을 푼 자리를 메우는 핵심 방어다. 이게 없으면 반복 호출로 재기동을 계속 시킬 수 있다(DoS).
    /// </summary>
    [Fact]
    public void 같은버전_재승인은_멱등이어야_한다()
    {
        var ctrl = AuthController();

        // 승인 존재 여부를 먼저 세고(SELECT COUNT), 있으면 INSERT 를 건너뛴다.
        Assert.Contains("SELECT COUNT(*) FROM local_update_consents", ctrl, StringComparison.Ordinal);
        Assert.Contains("already > 0", ctrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>전용 rate limit 이 걸려 있어야 한다</b>(B안 2차 방어).
    /// 기존 쓰기 제한(분당 600)은 재기동을 부르는 주소엔 무의미하게 헐겁다.
    /// </summary>
    [Fact]
    public void 업데이트_트리거에_전용_레이트리밋이_있어야_한다()
    {
        var rl = CodeLines(Read("src", "HitPan.API", "Middleware", "RateLimitMiddleware.cs"));

        Assert.Contains("/api/auth/update-consent-local", rl, StringComparison.Ordinal);
        Assert.Contains("UpdateTriggers", rl, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────────────────────────────
    // 🔴 사장님 오더 — 「최신버젼업데이트」 버튼
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>로그인 버튼 아래에 「최신버젼업데이트」 버튼이 있어야 한다</b>(사장님 오더).
    /// 화면 순서: 아이디 → 비밀번호 → 로그인 → <b>최신업데이트</b>.
    /// </summary>
    [Fact]
    public void 로그인_아래에_최신버젼업데이트_버튼이_있어야_한다()
    {
        var login = Read("src", "HitPan.Web", "Pages", "Sales", "..", "..", "Pages", "Login.razor");

        Assert.Contains("최신버젼업데이트", login, StringComparison.Ordinal);
        Assert.Contains("ManualUpdateAsync", login, StringComparison.Ordinal);

        // 🔴 순서 — 로그인 버튼보다 **뒤**에 있어야 한다(사장님이 그린 순서).
        var loginBtn = login.IndexOf("OnClick=\"SubmitAsync\"", StringComparison.Ordinal);
        var updateBtn = login.IndexOf("OnClick=\"ManualUpdateAsync\"", StringComparison.Ordinal);
        Assert.True(loginBtn > 0, "로그인 버튼이 있어야 한다");
        Assert.True(updateBtn > loginBtn,
            "「최신버젼업데이트」 는 로그인 버튼 **아래**에 있어야 한다(사장님 오더 순서)");
    }

    /// <summary>
    /// 🔴 <b>배너가 아니라 상시 노출이어야 한다.</b>
    /// 종전 배너는 <c>_updateAvailable</c> 일 때만 떠서, 조회가 실패하면(터널 404)
    /// 화면에 <b>탈출구가 하나도 안 보였다.</b> 버튼은 조건 없이 항상 있어야 한다.
    /// </summary>
    [Fact]
    public void 최신버젼업데이트_버튼은_상시노출이어야_한다()
    {
        var login = Read("src", "HitPan.Web", "Pages", "Login.razor");

        var updateBtn = login.IndexOf("OnClick=\"ManualUpdateAsync\"", StringComparison.Ordinal);
        Assert.True(updateBtn > 0, "「최신버젼업데이트」 버튼이 있어야 한다");

        // 버튼 앞 600자 안에 @if (_updateAvailable) 로 감싸는 조건이 없어야 한다.
        var before = login.Substring(Math.Max(0, updateBtn - 600), Math.Min(600, updateBtn));
        Assert.DoesNotContain("@if (_updateAvailable)", before, StringComparison.Ordinal);

        // 조회 실패를 화면이 말해야 한다 — 침묵하면 고장인지 최신인지 구분 못 한다.
        Assert.Contains("_updateCheckFailed", login, StringComparison.Ordinal);
    }
}
