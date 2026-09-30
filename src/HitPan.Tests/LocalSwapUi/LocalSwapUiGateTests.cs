using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HitPan.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Tests.LocalSwapUi;

/// <summary>
/// 🔴 20260930작1 갈래 F — 수동 업데이트·수동 되돌리기 화면 게이트(작업지시서 §10 G-L1 · 설계 §13-8 문구).
/// </summary>
/// <remarks>
/// 문구 표·클라이언트는 <b>실제로 불러</b> 잰다(링크 · <c>HitPan.Tests.csproj</c>).
/// G-L1 배선(.razor)만은 Blazor 렌더러가 없어 소스를 읽는다 — 대신 <b>같은 검사기를 망가뜨린 사본에 돌려 FAIL 을 확인하는
/// 음성대조군을 시험 안에 둔다</b>(F-L1c).
/// </remarks>
public sealed class LocalSwapUiGateTests
{
    private static readonly string[] KnownCodes =
    {
        LocalSwapUiText.ReasonMainPcOnly, LocalSwapUiText.ReasonAdminOnly, LocalSwapUiText.ReasonNoMaterial,
        LocalSwapUiText.ReasonUpdateInProgress, LocalSwapUiText.ReasonSwapInProgress, LocalSwapUiText.ReasonDiskLow,
        LocalSwapUiText.ReasonFeedUnavailable, LocalSwapUiText.ReasonBackupFailed, LocalSwapUiText.ReasonAlreadyLatest,
        LocalSwapUiText.ReasonVerifyFailed, LocalSwapUiText.ReasonSchedulerUnavailable,
    };

    private static IEnumerable<string> AllCustomerTexts()
    {
        foreach (var c in KnownCodes) yield return LocalSwapUiText.ReasonText(c);
        yield return LocalSwapUiText.ReasonText(null);
        yield return LocalSwapUiText.ReasonText("zzz_unknown");
        foreach (var s in new[] { 0, 401, 403, 404, 500 }) yield return LocalSwapUiText.HttpFailureText(s, null);
        yield return LocalSwapUiText.UpdateConfirm("1.3.50");
        yield return LocalSwapUiText.RollbackConfirm("1.3.48");
        yield return LocalSwapUiText.UpdateFailed("1.3.49");
        yield return LocalSwapUiText.MenuUpdate;
        yield return LocalSwapUiText.MenuRollback;
        yield return LocalSwapUiText.FirstLine;
        yield return LocalSwapUiText.WhoCanUse;
        yield return LocalSwapUiText.SidebarIssueLink;
        yield return LocalSwapUiText.LoginButtonNotice;
        yield return LocalSwapUiText.RollbackWarning;
        yield return LocalSwapUiText.RollbackOnlyOneStep;
        yield return LocalSwapUiText.DataKept;
        yield return LocalSwapUiText.UpdateStarted;
        yield return LocalSwapUiText.RollbackStarted;
        yield return LocalSwapUiText.AlreadyLatest;
        yield return LocalSwapUiText.CheckFailed;
        yield return LocalSwapUiText.StartFailed;
        yield return LocalSwapUiText.NextStepRollback;
    }

    // ═══════════════ 문구 표 ═══════════════

    [Fact(DisplayName = "F-T1 사유 코드 → 문구 · 아는 코드는 전부 제 문구 · 모르는 코드·빈 값은 「고객센터」 한 줄 · 코드 글자 노출 0")]
    public void FT1_ReasonMapping()
    {
        foreach (var c in KnownCodes)
        {
            var t = LocalSwapUiText.ReasonText(c);
            Assert.False(string.IsNullOrWhiteSpace(t));
            Assert.NotEqual(LocalSwapUiText.UnknownReason, t);
            Assert.DoesNotContain(c, t);
            Assert.Equal(t, LocalSwapUiText.ReasonText(" " + c.ToUpperInvariant() + " "));   // 대소문자·공백 무관
        }

        Assert.Equal(LocalSwapUiText.UnknownReason, LocalSwapUiText.ReasonText(null));
        Assert.Equal(LocalSwapUiText.UnknownReason, LocalSwapUiText.ReasonText(""));
        Assert.Equal(LocalSwapUiText.UnknownReason, LocalSwapUiText.ReasonText("zzz_unknown"));
        Assert.Contains("고객센터", LocalSwapUiText.UnknownReason);

        // 사유마다 문구가 다르다(한 문구로 뭉개지 않는다).
        var distinct = KnownCodes.Select(LocalSwapUiText.ReasonText).Distinct().Count();
        Assert.Equal(KnownCodes.Length, distinct);
    }

    [Fact(DisplayName = "F-T2 고객 문구 — 개발용어·「AI·Claude」 0 (#23·#24) · 메인PC 표현은 작3 한 가지")]
    public void FT2_CustomerText_Clean()
    {
        var banned = new[]
        {
            "AI", "Claude", "CS", "consent", "rollback", "update", "swap", "request", "staging", "main_pc",
            "워치독", "마이그", "API", "JSON", "서버", "에러", "403", "401", "null",
            "대표 컴퓨터", "자료가 저장된 컴퓨터", "정상상적으로",
        };
        foreach (var t in AllCustomerTexts())
        {
            Assert.False(string.IsNullOrWhiteSpace(t));
            foreach (var b in banned) Assert.DoesNotContain(b, t);
        }

        Assert.Contains(UpdatePromptPlan.MainPcPhrase, LocalSwapUiText.ReasonText(LocalSwapUiText.ReasonMainPcOnly));
        Assert.Contains(UpdatePromptPlan.MainPcPhrase, LocalSwapUiText.WhoCanUse);
        Assert.Contains("관리자", LocalSwapUiText.WhoCanUse);
    }

    [Fact(DisplayName = "F-T3 되돌리기 경고 — 새 버전 기능은 사라짐 · 자료는 남음 · 대상 판 · 바로 이전 한 판 (사장님 「경고는 당연히」)")]
    public void FT3_RollbackWarning()
    {
        var m = LocalSwapUiText.RollbackConfirm("1.3.48");
        Assert.Contains("1.3.48", m);
        Assert.Contains("새 버전에서 생긴 기능은 사라집니다", m);
        Assert.Contains("그대로 남습니다", m);
        Assert.Contains("지워지지 않습니다", LocalSwapUiText.DataKept);
        Assert.Contains("바로 이전 버전 한 단계", LocalSwapUiText.RollbackOnlyOneStep);

        var u = LocalSwapUiText.UpdateConfirm("1.3.50");
        Assert.Contains("1.3.50", u);
        Assert.Contains("백업", u);
        Assert.Contains("그대로 남습니다", u);

        Assert.Contains("이전 버전으로 되돌리기", LocalSwapUiText.UpdateFailed("1.3.49"));
        Assert.Contains("1.3.49", LocalSwapUiText.UpdateFailed("1.3.49"));
    }

    [Fact(DisplayName = "F-T4 HTTP 실패 → 문구 · 본문 코드가 먼저 · 403 코드없음 = 관리자 아님 · 401 = 다시 로그인")]
    public void FT4_HttpFailure()
    {
        Assert.Equal(LocalSwapUiText.ReasonText(LocalSwapUiText.ReasonMainPcOnly),
            LocalSwapUiText.HttpFailureText(403, LocalSwapUiText.ReasonMainPcOnly));
        Assert.Equal(LocalSwapUiText.ReasonText(LocalSwapUiText.ReasonAdminOnly), LocalSwapUiText.HttpFailureText(403, null));
        Assert.Contains("다시 로그인", LocalSwapUiText.HttpFailureText(401, null));
        Assert.Equal(LocalSwapUiText.ReasonText(LocalSwapUiText.ReasonDiskLow),
            LocalSwapUiText.HttpFailureText(409, LocalSwapUiText.ReasonDiskLow));
    }

    [Fact(DisplayName = "F-T5 본문 코드 추출 — {error} · {code} · {reason} · JSON 아님 = null")]
    public void FT5_ExtractCode()
    {
        Assert.Equal("main_pc_only", LocalSwapUiText.ExtractCode("{\"error\":\"main_pc_only\",\"message\":\"x\"}"));
        Assert.Equal("disk_low", LocalSwapUiText.ExtractCode("{\"code\":\"disk_low\"}"));
        Assert.Equal("no_material", LocalSwapUiText.ExtractCode("{\"Reason\":\"no_material\"}"));
        Assert.Null(LocalSwapUiText.ExtractCode("<html>oops</html>"));
        Assert.Null(LocalSwapUiText.ExtractCode(""));
        Assert.Null(LocalSwapUiText.ExtractCode("[1,2]"));
    }

    // ═══════════════ 클라이언트 (대역 핸들러로 실제 요청을 센다) ═══════════════

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Calls { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add(request.Method.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.Content is not null) Bodies.Add(await request.Content.ReadAsStringAsync(ct));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact(DisplayName = "F-T6 수동 업데이트 클라이언트 — 자동 경로(update-consent*) 호출 0 · 403 main_pc_only → 메인PC 문구")]
    public async Task FT6_ManualUpdateClient()
    {
        var h = new FakeHandler(r => r.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, "{\"currentVersion\":\"1.3.49\",\"latestVersion\":\"1.3.50\",\"updateAvailable\":true,\"canStart\":true}")
            : Json(HttpStatusCode.Forbidden, "{\"error\":\"main_pc_only\"}"));
        var c = new ManualUpdateClient(new HttpClient(h) { BaseAddress = new Uri("http://localhost/") },
            NullLogger<ManualUpdateClient>.Instance);

        var s = await c.CheckAsync();
        Assert.True(s.Ok);
        Assert.Equal("1.3.50", s.Data!.LatestVersion);
        Assert.True(s.Data.CanStart);

        var st = await c.StartAsync();
        Assert.False(st.Ok);
        Assert.Equal(403, st.Status);
        Assert.Equal("main_pc_only", st.Reason);
        Assert.Contains(UpdatePromptPlan.MainPcPhrase, st.FailureText);

        Assert.Equal(2, h.Calls.Count);
        Assert.All(h.Calls, x => Assert.DoesNotContain("update-consent", x));
        Assert.Equal("GET /" + LocalSwapUiText.ApiUpdateStatus, h.Calls[0]);
        Assert.Equal("POST /" + LocalSwapUiText.ApiUpdateStart, h.Calls[1]);

        // 🔴 [3-V] 적발 04 — [예] 본문에 버전·경로 0(서버가 계산).
        Assert.Equal("{}", h.Bodies.Single());
    }

    [Fact(DisplayName = "F-T8 적발 04 — 되돌리기 [예] 본문 = 빈 객체 · 음성대조군(버전 실은 본문)은 검사에 걸린다")]
    public async Task FT8_StartBody_NoVersionNoPath()
    {
        var h = new FakeHandler(_ => Json(HttpStatusCode.OK, "{\"accepted\":true}"));
        var c = new LocalRollbackClient(new HttpClient(h) { BaseAddress = new Uri("http://localhost/") },
            NullLogger<LocalRollbackClient>.Instance);
        var r = await c.StartAsync();
        Assert.True(r.Ok && r.Data!.Accepted);

        static bool CarriesValue(string body) => Regex.IsMatch(body, "[0-9]+\\.[0-9]+|[\\\\/]|version|path", RegexOptions.IgnoreCase);
        Assert.False(CarriesValue(h.Bodies.Single()));
        Assert.True(CarriesValue("{\"targetVersion\":\"1.3.48\"}"));   // 대조군 — 옛 모양은 걸린다
    }

    [Fact(DisplayName = "F-T7 되돌리기 클라이언트 — 403 본문없음 = 관리자 문구 · 연결 끊김 = 예외 대신 Status 0")]
    public async Task FT7_LocalRollbackClient()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var c = new LocalRollbackClient(new HttpClient(h) { BaseAddress = new Uri("http://localhost/") },
            NullLogger<LocalRollbackClient>.Instance);

        var s = await c.GetStatusAsync();
        Assert.False(s.Ok);
        Assert.Equal(LocalSwapUiText.ReasonText(LocalSwapUiText.ReasonAdminOnly), s.FailureText);
        Assert.Equal("GET /" + LocalSwapUiText.ApiRollbackStatus, h.Calls.Single());

        var broken = new FakeHandler(_ => throw new HttpRequestException("끊김"));
        var c2 = new LocalRollbackClient(new HttpClient(broken) { BaseAddress = new Uri("http://localhost/") },
            NullLogger<LocalRollbackClient>.Instance);
        var r = await c2.StartAsync();
        Assert.False(r.Ok);
        Assert.Equal(0, r.Status);
        Assert.Equal("POST /" + LocalSwapUiText.ApiRollbackStart, broken.Calls.Single());
    }

    // ═══════════════ G-L1 로그인창 버튼 ═══════════════

    [Fact(DisplayName = "F-L1a L-1 (가) — [최신버젼업데이트] 를 눌렀으면 로그인 뒤 /data/update · 아니면 /")]
    public void FL1a_PostLoginPath()
    {
        Assert.Equal("/data/update", LocalSwapUiText.PostLoginPath(true));
        Assert.Equal("/", LocalSwapUiText.PostLoginPath(false));
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.True(dir is not null, "레포 루트를 찾아야 한다");
        return dir!;
    }

    /// <summary>
    /// 로그인창 업데이트 버튼(아이콘 SystemUpdateAlt)의 OnClick 처리기 본문이 익명 예약 경로에 닿으면 위반 목록을 돌려준다.
    /// 처리기 본문 안에서 부르는 같은 파일의 함수도 한 단계 따라간다(ManualUpdateAsync → StartUpdateAsync 를 잡기 위해).
    /// </summary>
    internal static List<string> LoginButtonViolations(string razor)
    {
        var v = new List<string>();
        var btn = Regex.Match(razor, "<MudButton(?:(?!</MudButton>).)*?OnClick=\"(?<h>\\w+)\"(?:(?!</MudButton>).)*?SystemUpdateAlt",
            RegexOptions.Singleline);
        if (!btn.Success) { v.Add("버튼을 못 찾음"); return v; }
        var handler = btn.Groups["h"].Value;

        var body = MethodBody(razor, handler);
        if (body is null) { v.Add("처리기 본문을 못 찾음: " + handler); return v; }

        var bodies = new List<string> { body };
        foreach (Match call in Regex.Matches(body, "\\b(?<m>\\w+Async)\\s*\\("))
        {
            var inner = MethodBody(razor, call.Groups["m"].Value);
            if (inner is not null) bodies.Add(inner);
        }

        foreach (var b in bodies)
        {
            if (b.Contains("update-consent")) v.Add(handler + ": update-consent 호출");
            if (Regex.IsMatch(b, "\\bStartUpdateAsync\\s*\\(")) v.Add(handler + ": StartUpdateAsync 호출");
            if (b.Contains("PostAsJsonAsync")) v.Add(handler + ": POST 호출");
        }

        if (!Regex.IsMatch(razor, "PostLoginPath\\(\\s*_goManualUpdateAfterLogin\\s*\\)"))
            v.Add("로그인 뒤 목적지가 수동 업데이트 표시를 안 본다");

        // 🔴 [3-V] 적발 04 — 열린 이동 금지: 주소창 값(returnUrl 등)을 받아 이동하지 않는다.
        if (Regex.IsMatch(razor, "SupplyParameterFromQuery|returnUrl|ReturnUrl|redirect_uri", RegexOptions.IgnoreCase))
            v.Add("주소창 값으로 이동하는 길이 있다(열린 이동)");

        // 🔴 적발 04 — 약관 이동이 목적지 이동보다 먼저(첫 로그인 절차를 건너뛰지 않는다).
        var terms = razor.IndexOf("NavigateTo(\"/terms\"", StringComparison.Ordinal);
        var dest = razor.IndexOf("PostLoginPath(", StringComparison.Ordinal);
        if (terms < 0 || dest < 0 || terms > dest) v.Add("약관 이동이 목적지 이동보다 뒤에 있다");
        return v;
    }

    private static string? MethodBody(string src, string name)
    {
        var m = Regex.Match(src, "(?:private|public|protected)[^\\n;{]*\\b" + Regex.Escape(name) + "\\s*\\([^)]*\\)\\s*\\{");
        if (!m.Success) return null;
        var i = m.Index + m.Length;
        var depth = 1;
        var start = i;
        while (i < src.Length && depth > 0)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}') depth--;
            i++;
        }
        return src.Substring(start, i - start);
    }

    [Fact(DisplayName = "F-L1b G-L1 — 로그인창 버튼 경로에서 update-consent-local·StartUpdateAsync 호출 0 · 목적지 = 수동 업데이트")]
    public void FL1b_LoginButton_NoAnonymousConsent()
    {
        var razor = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Pages", "Login.razor"));
        Assert.Empty(LoginButtonViolations(razor));

        // #1 — 옛 함수는 지우지 않았다(설계 §13-4 「StartUpdateAsync 는 지우지 않는다」).
        Assert.Contains("private async Task StartUpdateAsync()", razor);
        Assert.Contains("private async Task ManualUpdateAsync()", razor);
    }

    [Fact(DisplayName = "F-L1c G-L1 음성대조군 — 버튼이 옛 처리기(→StartUpdateAsync)를 부르는 사본 · 처리기가 직접 예약하는 사본은 FAIL")]
    public void FL1c_NegativeControl()
    {
        var razor = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Pages", "Login.razor"));

        var old = razor.Replace("OnClick=\"GoManualUpdateAfterLogin\"", "OnClick=\"ManualUpdateAsync\"");
        Assert.NotEqual(razor, old);
        Assert.Contains(LoginButtonViolations(old), x => x.Contains("StartUpdateAsync"));

        var direct = razor.Replace("_goManualUpdateAfterLogin = true;", "_goManualUpdateAfterLogin = true; _ = StartUpdateAsync();");
        Assert.NotEqual(razor, direct);
        Assert.NotEmpty(LoginButtonViolations(direct));

        var noDest = razor.Replace("PostLoginPath(_goManualUpdateAfterLogin)", "PostLoginPath(false)");
        Assert.NotEqual(razor, noDest);
        Assert.NotEmpty(LoginButtonViolations(noDest));

        // 적발 04 대조군 — returnUrl 에 외부 주소를 받아 이동하는 사본은 FAIL.
        var open = razor.Replace("private bool _goManualUpdateAfterLogin;",
            "private bool _goManualUpdateAfterLogin; [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }");
        Assert.NotEqual(razor, open);
        Assert.Contains(LoginButtonViolations(open), x => x.Contains("열린 이동"));
    }

    [Fact(DisplayName = "F-L1d 적발 04 — 로그인 뒤 목적지는 고정 경로 둘 중 하나 · 바깥 주소(//·http) 0")]
    public void FL1d_FixedDestinationOnly()
    {
        foreach (var b in new[] { true, false })
        {
            var p = LocalSwapUiText.PostLoginPath(b);
            Assert.Contains(p, new[] { "/", LocalSwapUiText.UpdatePath });
            Assert.StartsWith("/", p);
            Assert.False(p.StartsWith("//", StringComparison.Ordinal));
            Assert.DoesNotContain(":", p);
        }
    }
}
