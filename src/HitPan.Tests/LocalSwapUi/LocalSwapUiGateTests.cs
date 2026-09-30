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
    // ⬛ F 잠정 이름(no_material · feed_unavailable · already_latest · scheduler_unavailable) → 🟢 I-WEB: 계약 §6 · API 상수 이름.
    private static readonly string[] KnownCodes =
    {
        LocalSwapUiText.ReasonMainPcOnly, LocalSwapUiText.ReasonAdminOnly, LocalSwapUiText.ReasonNotWindows,
        LocalSwapUiText.ReasonNoPreviousVersion, LocalSwapUiText.ReasonRollbackChainBlocked,
        LocalSwapUiText.ReasonUpdateInProgress, LocalSwapUiText.ReasonSwapInProgress, LocalSwapUiText.ReasonCooldown,
        LocalSwapUiText.ReasonDiskLow, LocalSwapUiText.ReasonTicketInvalid, LocalSwapUiText.ReasonScriptMissing,
        LocalSwapUiText.ReasonFolderUnsafe, LocalSwapUiText.ReasonTaskRegisterFailed, LocalSwapUiText.ReasonRequestInvalid,
        LocalSwapUiText.ReasonMaterialInvalid, LocalSwapUiText.ReasonHashMismatch, LocalSwapUiText.ReasonSafetyNetFailed,
        LocalSwapUiText.ReasonStopFailed, LocalSwapUiText.ReasonSwapFailed, LocalSwapUiText.ReasonVerifyFailed,
        LocalSwapUiText.ReasonRevertFailed, LocalSwapUiText.ReasonFeedUnreachable, LocalSwapUiText.ReasonSignatureInvalid,
        LocalSwapUiText.ReasonNoNewerVersion, LocalSwapUiText.ReasonBackupFailed, LocalSwapUiText.ReasonDownloadFailed,
        LocalSwapUiText.ReasonLauncherNotWired,
    };

    private static readonly string[] EndStates = { "requested", "running", "success", "refused", "reverted", "broken" };
    private static readonly string[] Stages =
        { "checking", "downloading", "verifying", "backing_up", "handing_off", "handed_off", "refused", "zzz" };

    private static IEnumerable<string> AllCustomerTexts()
    {
        foreach (var c in KnownCodes) yield return LocalSwapUiText.ReasonText(c);
        foreach (var mode in new[] { "update", "rollback" })
        foreach (var st in EndStates)
        foreach (var rs in new[] { null, "verify_failed", "disk_low", "revert_failed" })
            yield return LocalSwapUiText.EndStateText(mode, st, rs, "1.3.49", "1.3.50")!;
        foreach (var sg in Stages) yield return LocalSwapUiText.StageText(sg);
        yield return LocalSwapUiText.BrokenAdvice;
        yield return LocalSwapUiText.UpdateStatusLost;
        yield return LocalSwapUiText.LastCaption(new DateTime(2026, 9, 30, 5, 5, 0, DateTimeKind.Utc));
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
        Assert.Equal("no_previous_version", LocalSwapUiText.ExtractCode("{\"Reason\":\"no_previous_version\"}"));
        // 서버 409 모양 그대로(되돌리기 { started:false, reason } · 수동 업데이트 { reason }).
        Assert.Equal("cooldown", LocalSwapUiText.ExtractCode("{\"started\":false,\"reason\":\"cooldown\"}"));
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
        // I-WEB: 서버 ManualUpdateCheckResult 모양(canStart 칸은 서버에 없다 — 화면 DTO 가 계산).
        var h = new FakeHandler(r => r.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, "{\"currentVersion\":\"1.3.49\",\"latestVersion\":\"1.3.50\",\"updateAvailable\":true,\"reason\":\"ok\",\"busy\":false}")
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
        Assert.Equal("GET /" + LocalSwapUiText.ApiUpdateCheck, h.Calls[0]);
        Assert.Equal("POST /" + LocalSwapUiText.ApiUpdateStart, h.Calls[1]);

        // 🔴 [3-V] 적발 04 — [예] 본문에 버전·경로 0(서버가 계산).
        Assert.Equal("{}", h.Bodies.Single());
    }

    [Fact(DisplayName = "F-T8 적발 04 + C-5 — 되돌리기 [예] 본문 = 확인 번호 한 칸뿐(버전·경로 0) · 음성대조군(버전 실은 본문)은 검사에 걸린다")]
    public async Task FT8_StartBody_TicketOnly_NoVersionNoPath()
    {
        // ⬛ F: 본문 {} · 응답 {accepted} → 🟢 I-WEB: 서버 LocalRollbackStartBody { Ticket } · 202 { started, reason }.
        const string ticket = "0123456789abcdef0123456789abcdef";
        var h = new FakeHandler(_ => Json(HttpStatusCode.Accepted, "{\"started\":true,\"reason\":\"ok\"}"));
        var c = new LocalRollbackClient(new HttpClient(h) { BaseAddress = new Uri("http://localhost/") },
            NullLogger<LocalRollbackClient>.Instance);
        var r = await c.StartAsync(ticket);
        Assert.True(r.Ok && r.Data!.Started);

        var body = h.Bodies.Single();
        using (var doc = System.Text.Json.JsonDocument.Parse(body))
        {
            var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "ticket" }, names);                               // 칸은 번호 하나
            Assert.Equal(ticket, doc.RootElement.GetProperty("ticket").GetString());
        }

        static bool CarriesValue(string b) => Regex.IsMatch(b, "[0-9]+\\.[0-9]+|[\\\\/]|version|path|material|to\"|from\"", RegexOptions.IgnoreCase);
        Assert.False(CarriesValue(body));
        Assert.True(CarriesValue("{\"targetVersion\":\"1.3.48\"}"));                 // 대조군 — 옛 모양은 걸린다
        Assert.True(CarriesValue("{\"ticket\":\"x\",\"to\":\"1.3.48\"}"));           // 대조군 — 번호에 판을 곁들여도 걸린다
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
        var r = await c2.StartAsync("0123456789abcdef0123456789abcdef");
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

    // ═══════════════ I-WEB 계약 대조 (C-1~C-7) — 화면 ↔ 실제 API ═══════════════

    /// <summary>컨트롤러 소스에서 「방식 주소」 목록을 뽑는다(클래스 [Route] + 메서드 [HttpGet/Post("…")]).</summary>
    private static HashSet<string> ControllerRoutes(string src)
    {
        var baseRoute = Regex.Match(src, "\\[Route\\(\"([^\"]+)\"\\)\\]").Groups[1].Value;
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(src, "\\[Http(Get|Post|Put|Delete)(?:\\(\"([^\"]*)\"\\))?\\]"))
        {
            var sub = m.Groups[2].Value;
            set.Add(m.Groups[1].Value.ToUpperInvariant() + " " + (sub.Length == 0 ? baseRoute : baseRoute + "/" + sub));
        }
        return set;
    }

    [Fact(DisplayName = "F-C1 (C-6) 화면이 부르는 주소 = 실제 컨트롤러 라우트 · 음성대조군(F 옛 잠정 주소·바꾼 라우트)은 걸린다")]
    public void FC1_Routes_MatchControllers()
    {
        var ctl = Path.Combine(RepoRoot(), "src", "HitPan.API", "Controllers");
        var rb = ControllerRoutes(File.ReadAllText(Path.Combine(ctl, "LocalRollbackController.cs")));
        var mu = ControllerRoutes(File.ReadAllText(Path.Combine(ctl, "ManualUpdateController.cs")));

        Assert.Contains("GET " + LocalSwapUiText.ApiRollbackStatus, rb);
        Assert.Contains("POST " + LocalSwapUiText.ApiRollbackStart, rb);
        Assert.Contains("GET " + LocalSwapUiText.ApiUpdateCheck, mu);
        Assert.Contains("POST " + LocalSwapUiText.ApiUpdateStart, mu);
        Assert.Contains("GET " + LocalSwapUiText.ApiUpdateJob, mu);

        // 음성대조군 ① — F 가 쓰던 잠정 주소는 컨트롤러에 없다(이 검사가 어긋남을 잡는다).
        Assert.DoesNotContain("GET api/system/local-rollback/status", rb);
        Assert.DoesNotContain("GET api/system/manual-update/status", mu);
        Assert.DoesNotContain("POST api/system/manual-update", mu);
        // 음성대조군 ② — 라우트를 바꾼 사본이면 화면 주소가 빠진다.
        var mutated = ControllerRoutes(File.ReadAllText(Path.Combine(ctl, "ManualUpdateController.cs"))
            .Replace("[HttpPost(\"apply\")]", "[HttpPost(\"start\")]"));
        Assert.DoesNotContain("POST " + LocalSwapUiText.ApiUpdateStart, mutated);
    }

    private static IEnumerable<string> ConstStrings(Type t) =>
        t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    [Fact(DisplayName = "F-C2 (C-1~C-3) 서버 사유 코드(SwapReasons·ManualUpdateReasons) 전부 제 문구 · 계약 §6 코드 전부 · 끝 상태·진행 단계 전부 · 음성대조군(F 옛 이름)은 「고객센터」로 떨어진다")]
    public void FC2_EveryServerCode_HasItsOwnText()
    {
        var serverCodes = ConstStrings(typeof(HitPan.API.Services.LocalSwap.SwapReasons))
            .Concat(ConstStrings(typeof(HitPan.API.Services.ManualUpdate.ManualUpdateReasons)))
            .Where(c => c != "ok").Distinct().ToArray();
        Assert.True(serverCodes.Length >= 25, "서버 사유 코드를 읽어야 한다: " + serverCodes.Length);

        // 계약 §6 표(U 예약 포함) — 문서에 적힌 코드. 서버 상수와 따로 적어 두 쪽 누락을 모두 잡는다.
        var contractCodes = new[]
        {
            "main_pc_only", "not_windows", "no_previous_version", "update_in_progress", "swap_in_progress", "disk_low",
            "ticket_invalid", "script_missing", "task_register_failed", "request_invalid", "material_invalid",
            "hash_mismatch", "safety_net_failed", "stop_failed", "swap_failed", "verify_failed", "revert_failed",
            "feed_unreachable", "signature_invalid", "no_newer_version", "backup_failed", "download_failed",
        };

        foreach (var c in serverCodes.Concat(contractCodes).Distinct())
        {
            var t = LocalSwapUiText.ReasonText(c);
            Assert.True(t != LocalSwapUiText.UnknownReason, "문구 없음(고객센터로 떨어짐): " + c);
            Assert.DoesNotContain(c, t);
        }

        // 계약 §6 「원래 버전으로 돌려 두었습니다」 3종은 그 말을 담는다.
        foreach (var c in new[] { "stop_failed", "swap_failed", "verify_failed" })
            Assert.Contains("원래 버전으로 돌려 두었습니다", LocalSwapUiText.ReasonText(c));

        // 끝 상태(계약 §5) — 서버 SwapStates 전부 · 두 모드 모두 문구가 있다.
        foreach (var st in ConstStrings(typeof(HitPan.API.Services.LocalSwap.SwapStates)))
        foreach (var mode in ConstStrings(typeof(HitPan.API.Services.LocalSwap.SwapModes)))
            Assert.False(string.IsNullOrWhiteSpace(LocalSwapUiText.EndStateText(mode, st, null, "1.3.49", "1.3.50")), st + "/" + mode);

        // 진행 단계 — 서버 ManualUpdateStages 전부 제 문구(모르는 단계의 기본 문구로 떨어지지 않는다).
        var fallback = LocalSwapUiText.StageText("zzz_unknown");
        foreach (var sg in ConstStrings(typeof(HitPan.API.Services.ManualUpdate.ManualUpdateStages)).Where(s => s != "refused"))
            Assert.NotEqual(fallback, LocalSwapUiText.StageText(sg));

        // 음성대조군 — F 옛 잠정 이름은 서버가 주지 않는다 ⇒ 매핑이 없고 「고객센터」로 떨어진다(= 검사가 누락을 잡는다).
        foreach (var old in new[] { "no_material", "feed_unavailable", "already_latest", "scheduler_unavailable" })
        {
            Assert.DoesNotContain(old, serverCodes);
            Assert.Equal(LocalSwapUiText.UnknownReason, LocalSwapUiText.ReasonText(old));
        }
        Assert.Null(LocalSwapUiText.EndStateText("update", "zzz", null, null, null));
    }

    [Fact(DisplayName = "F-C3 (C-5·C-6·C-7) 서버 DTO 를 서버 직렬화 그대로 보내면 화면 DTO 가 칸을 다 받는다(확인 번호·지난 결과·진행 단계) · 음성대조군(F 옛 모양)은 놓친다")]
    public async Task FC3_ServerDto_RoundTrip()
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);   // ASP.NET Core 기본
        const string ticket = "0123456789abcdef0123456789abcdef";
        var at = new DateTime(2026, 9, 30, 5, 0, 0, DateTimeKind.Utc);

        var rbJson = System.Text.Json.JsonSerializer.Serialize(new HitPan.API.Services.LocalRollback.LocalRollbackStatus(
            true, "ok", "1.3.50", "1.3.49", "prev", ticket, 10,
            new HitPan.API.Services.LocalRollback.LocalSwapLastResult("update", "reverted", "verify_failed", "1.3.50", "1.3.51", at)), web);
        var h1 = new FakeHandler(_ => Json(HttpStatusCode.OK, rbJson));
        var rb = await new LocalRollbackClient(new HttpClient(h1) { BaseAddress = new Uri("http://localhost/") },
            NullLogger<LocalRollbackClient>.Instance).GetStatusAsync();
        Assert.True(rb.Ok);
        Assert.True(rb.Data!.CanRollback);
        Assert.Equal("1.3.49", rb.Data.TargetVersion);
        Assert.Equal(ticket, rb.Data.Ticket);
        Assert.Equal(10, rb.Data.TicketMinutes);
        Assert.Equal("reverted", rb.Data.Last!.State);
        Assert.Equal("verify_failed", rb.Data.Last.Reason);
        Assert.Equal(at, rb.Data.Last.AtUtc.ToUniversalTime());

        var chkJson = System.Text.Json.JsonSerializer.Serialize(new HitPan.API.Services.ManualUpdate.ManualUpdateCheckResult(
            "1.3.49", "1.3.50", true, "ok", 1234, null, false), web);
        var jobJson = System.Text.Json.JsonSerializer.Serialize(new HitPan.API.Services.ManualUpdate.ManualUpdateJobStatus(
            "j1", "backing_up", null, "1.3.49", "1.3.50", at, at), web);
        var h2 = new FakeHandler(r => r.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, chkJson)
            : Json(HttpStatusCode.Accepted, jobJson));
        var mu = new ManualUpdateClient(new HttpClient(h2) { BaseAddress = new Uri("http://localhost/") },
            NullLogger<ManualUpdateClient>.Instance);
        var chk = await mu.CheckAsync();
        Assert.True(chk.Ok && chk.Data!.CanStart);
        Assert.Equal("1.3.50", chk.Data.LatestVersion);
        var job = await mu.StartAsync();
        Assert.True(job.Ok);
        Assert.Equal("backing_up", job.Data!.Stage);

        // 바쁨·새 판 없음이면 [업데이트 하기] 가 꺼진다.
        Assert.False(new ManualUpdateStatus { UpdateAvailable = true, LatestVersion = "1.3.50", Reason = "ok", Busy = true }.CanStart);
        Assert.False(new ManualUpdateStatus { UpdateAvailable = false, Reason = "no_newer_version" }.CanStart);

        // 204(진행 상태 없음) — 예외 없이 Status 204.
        var h3 = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var none = await new ManualUpdateClient(new HttpClient(h3) { BaseAddress = new Uri("http://localhost/") },
            NullLogger<ManualUpdateClient>.Instance).GetJobAsync();
        Assert.False(none.Ok);
        Assert.Equal(204, none.Status);
        Assert.Equal("GET /" + LocalSwapUiText.ApiUpdateJob, h3.Calls.Single());

        // 음성대조군 — F 옛 모양({ accepted }) 으로 서버 202 { started:true } 를 읽으면 「시작 안 됨」으로 오판한다.
        var startJson = "{\"started\":true,\"reason\":\"ok\"}";
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<OldFStartShape>(startJson, web)!.Accepted);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<LocalSwapStartResult>(startJson, web)!.Started);
    }

    private sealed class OldFStartShape
    {
        public bool Accepted { get; set; }
    }

    [Fact(DisplayName = "F-T9 (C-7) 끝 상태 문구 — 원위치는 원래 판·원인·다음 단계 · 망가짐은 다시 켜기+고객센터 · 낡은 기록은 안 보인다")]
    public void FT9_EndState()
    {
        var rev = LocalSwapUiText.EndStateText("update", "reverted", "verify_failed", "1.3.49", "1.3.50")!;
        Assert.Contains("1.3.49", rev);
        Assert.Contains("제때 켜지지", rev);
        Assert.Contains("이전 버전으로 되돌리기", rev);          // 단계 ③ 안내

        var rbRev = LocalSwapUiText.EndStateText("rollback", "reverted", "swap_failed", "1.3.50", "1.3.49")!;
        Assert.Contains("1.3.50", rbRev);
        Assert.DoesNotContain("이전 버전으로 되돌리기」", rbRev);  // 되돌리기 실패에 되돌리기를 권하지 않는다

        var broken = LocalSwapUiText.EndStateText("rollback", "broken", "revert_failed", "1.3.50", "1.3.49")!;
        Assert.Contains("다시 켜", broken);
        Assert.Contains("고객센터", broken);

        Assert.Contains("1.3.50", LocalSwapUiText.EndStateText("update", "success", null, "1.3.49", "1.3.50")!);
        Assert.Contains(LocalSwapUiText.ReasonText("cooldown"), LocalSwapUiText.EndStateText("rollback", "refused", "cooldown", "1.3.50", "1.3.49")!);

        Assert.Equal(3, LocalSwapUiText.EndStateLevel("success"));
        Assert.Equal(2, LocalSwapUiText.EndStateLevel("broken"));
        Assert.Equal(1, LocalSwapUiText.EndStateLevel("reverted"));

        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(LocalSwapUiText.ShouldShowLast("success", "1.3.49", "1.3.50", now.AddHours(-1), "1.3.50", now));
        Assert.False(LocalSwapUiText.ShouldShowLast("success", "1.3.49", "1.3.50", now.AddHours(-1), "1.3.51", now));   // 그 뒤 또 바뀜
        Assert.True(LocalSwapUiText.ShouldShowLast("reverted", "1.3.49", "1.3.50", now.AddHours(-1), "1.3.49", now));
        Assert.False(LocalSwapUiText.ShouldShowLast("reverted", "1.3.49", "1.3.50", now.AddHours(-1), "1.3.50", now));
        Assert.False(LocalSwapUiText.ShouldShowLast("broken", "1.3.49", "1.3.50", now.AddHours(-25), "1.3.49", now));   // 하루 지남
        Assert.False(LocalSwapUiText.ShouldShowLast("zzz", "1.3.49", "1.3.50", now, "1.3.49", now));
    }
}
