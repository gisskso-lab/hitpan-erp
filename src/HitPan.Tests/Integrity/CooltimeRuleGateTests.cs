using HitPan.API.Services.Cooltime;
using HitPan.Application.Services;
using HitPan.Web.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 쿨타임(설정 시각 자동 로그아웃) — <b>DB 없는</b> 게이트(순수 규칙을 실제로 불러 값을 본다).
/// </summary>
/// <remarks>
/// 근거: 설계 <c>docs/설계/erp/쿨타임_자동로그아웃_설계_20260928.md</c> §7 · §D · §E ·
/// 개발명세서 <c>docs/개발/erp/20261127_쿨타임1b_갈래3_게이트_개발명세서.md</c>.
/// <para>DB 를 쓰는 게이트는 <see cref="CooltimeDbGateTests"/> 에 있다(CI <c>db-gate</c> 잡이 유일한 계측 경로).</para>
/// <para>🔴 화면 쪽 규칙은 <c>HitPan.Web/Services/CooltimeScreenRules.cs</c> 를 <b>소스 링크</b>해 부른다(시험 프로젝트 Compile Include ·
/// ApiErrorText 와 같은 방식). 글자를 세지 않고 함수에 값을 넣어 받은 값을 본다.</para>
/// </remarks>
public sealed partial class CooltimeRuleGateTests
{
    // ══════════════════════════════════════════════════════════════
    // G-C18 ⓐ (설계 §E E-1) — 푸시 연결 토큰은 부를 때마다 지금 것
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C18 ⓐ 푸시 연결 토큰 — 두 번 부르는 사이 저장소 토큰이 바뀌면 두 번째는 새 토큰 · 대조(첫 토큰 붙잡기)는 옛 토큰을 준다")]
    public async Task G_C18a_토큰은_부를_때마다_지금_것()
    {
        var stored = "token-1";
        var refreshCalls = 0;
        Task<string?> Read() => Task.FromResult<string?>(stored);
        Task<bool> Refresh() { refreshCalls++; return Task.FromResult(true); }

        var first = await CooltimeScreenRules.ResolveHubTokenAsync(Read, Refresh, _ => false);
        stored = "token-2";                                   // 다른 탭의 갱신 · 로그인 바뀜
        var second = await CooltimeScreenRules.ResolveHubTokenAsync(Read, Refresh, _ => false);

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(0, refreshCalls);                        // 만료 아님 = 갱신 길 안 탐

        // 🔴 음성 대조군 ⓔ(시험 안) — 옛 판(NotificationClient.cs 의 ⬛ 줄 「() => Task.FromResult(token)」)과 같은 꼴은
        //    같은 순서에서 두 번째도 첫 토큰을 준다 ⇒ 위 단언이 그 꼴을 실제로 가른다.
        stored = "token-1";
        var pinned = await Read();
        Func<Task<string?>> oldProvider = () => Task.FromResult<string?>(pinned);
        stored = "token-2";
        Assert.NotEqual(second, await oldProvider());
    }

    [Fact(DisplayName = "G-C18 ⓐ 곧 만료면 갱신 길을 타고 결과와 무관하게 저장소를 다시 읽는다 · 로그인 없으면 null")]
    public async Task G_C18a_만료면_갱신_뒤_다시_읽는다()
    {
        var stored = "old";
        var refreshed = 0;
        Task<string?> Read() => Task.FromResult<string?>(stored);
        Task<bool> RefreshOk() { refreshed++; stored = "new"; return Task.FromResult(true); }
        Task<bool> RefreshFailButOtherTab() { refreshed++; stored = "other-tab"; return Task.FromResult(false); }

        Assert.Equal("new", await CooltimeScreenRules.ResolveHubTokenAsync(Read, RefreshOk, t => t == "old"));
        stored = "old";
        Assert.Equal("other-tab", await CooltimeScreenRules.ResolveHubTokenAsync(Read, RefreshFailButOtherTab, t => t == "old"));
        Assert.Equal(2, refreshed);

        stored = "";
        Assert.Null(await CooltimeScreenRules.ResolveHubTokenAsync(Read, RefreshOk, _ => true));
    }

    /// <summary>
    /// G-C18 ⓐ 배선 — 위 함수가 <b>실제 연결 설정에 물려 있는지</b>. 이것만 글자 확인이다(WASM 연결 객체는 시험이 띄울 수 없다).
    /// 동작은 위 두 시험이 잰다. 음성 대조 ⓔ = <c>NotificationClient.cs</c> 의 공급자를 첫 토큰 붙잡기로 되돌리면 이 시험이 FAIL.
    /// </summary>
    [Fact(DisplayName = "G-C18 ⓐ 배선 — NotificationClient 의 AccessTokenProvider = GetHubTokenAsync → ResolveHubTokenAsync (살아 있는 줄 기준)")]
    public void G_C18a_배선()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Services", "NotificationClient.cs"));
        var live = string.Join('\n', src.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.Contains("options.AccessTokenProvider = GetHubTokenAsync;", live);
        Assert.Contains("CooltimeScreenRules.ResolveHubTokenAsync(", live);
        Assert.DoesNotContain("AccessTokenProvider = () => Task.FromResult", live);
    }

    // ══════════════════════════════════════════════════════════════
    // G-C14 화면 짝 (설계 §E E-5) — 401 · 409 · ended:false = 끝남
    // ══════════════════════════════════════════════════════════════

    // 🆕 11/27 고침 차례(§273 V1 · §275 E-5) — 409 · ended:false = 「다시 확인」(Recheck). 이전 기대값(409·2xx = 끝남)은 아래 ⬛ 줄.
    //   ⬛ [낡은 기대 · 11/27 이전] [InlineData(409, CooltimeScreenRules.CallOutcome.Ended)] (계속) · [InlineData(200|409, …Ended, false)] (로그아웃)
    [Theory(DisplayName = "G-C14 화면 짝 — [계속] 응답: 200 계속 · 401 끝남 · 409 다시 확인 · 5xx/연결실패 실패")]
    [InlineData(200, CooltimeScreenRules.CallOutcome.Continued)]
    [InlineData(401, CooltimeScreenRules.CallOutcome.Ended)]
    [InlineData(409, CooltimeScreenRules.CallOutcome.Recheck)]
    [InlineData(500, CooltimeScreenRules.CallOutcome.Failed)]
    [InlineData(503, CooltimeScreenRules.CallOutcome.Failed)]
    [InlineData(null, CooltimeScreenRules.CallOutcome.Failed)]
    public void G_C14_화면_계속_응답(int? status, CooltimeScreenRules.CallOutcome expected)
        => Assert.Equal(expected, CooltimeScreenRules.ContinueOutcome(status));

    [Theory(DisplayName = "G-C14 화면 짝 — [로그아웃] 응답(본문 못 읽음): 2xx·409 다시 확인 · 401 끝남 · 503 만 한 번 더")]
    [InlineData(200, CooltimeScreenRules.CallOutcome.Recheck, false)]
    [InlineData(401, CooltimeScreenRules.CallOutcome.Ended, false)]
    [InlineData(409, CooltimeScreenRules.CallOutcome.Recheck, false)]
    [InlineData(503, CooltimeScreenRules.CallOutcome.Failed, true)]
    [InlineData(500, CooltimeScreenRules.CallOutcome.Failed, false)]
    [InlineData(null, CooltimeScreenRules.CallOutcome.Failed, false)]
    public void G_C14_화면_로그아웃_응답(int? status, CooltimeScreenRules.CallOutcome expected, bool retry)
    {
        Assert.Equal(expected, CooltimeScreenRules.LogoutOutcome(status));
        Assert.Equal(retry, CooltimeScreenRules.ShouldRetryLogout(status));
    }

    // ══════════════════════════════════════════════════════════════
    // 🆕 11/27 고침 차례 — V1 여러 탭 · E-5 끔 · V4 설정 저장 (리뷰서 20261127_쿨타임1b_갈래2_3V4)
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "V1·E-5 [로그아웃] 응답 본문: 2xx+ended:true 끝남 · 2xx+ended:false/모름 다시 확인(출입증 그대로) · 401 끝남")]
    [InlineData(200, true, CooltimeScreenRules.CallOutcome.Ended)]
    [InlineData(200, false, CooltimeScreenRules.CallOutcome.Recheck)]
    [InlineData(200, null, CooltimeScreenRules.CallOutcome.Recheck)]
    [InlineData(401, null, CooltimeScreenRules.CallOutcome.Ended)]
    [InlineData(409, null, CooltimeScreenRules.CallOutcome.Recheck)]
    [InlineData(500, null, CooltimeScreenRules.CallOutcome.Failed)]
    [InlineData(null, null, CooltimeScreenRules.CallOutcome.Failed)]
    public void V1_로그아웃_본문_판정(int? status, bool? ended, CooltimeScreenRules.CallOutcome expected)
        => Assert.Equal(expected, CooltimeScreenRules.LogoutOutcome(status, ended));

    [Theory(DisplayName = "V1·E-5 다시 확인 → 할 일: 401·행 없음·물음 만료 = 끝남 · 안 물음(다른 탭 계속 · 끔)·확인 실패 = 팝업만 닫음 · 묻는 중 = 다시 셈")]
    [InlineData(401, null, null, null, CooltimeScreenRules.RecheckDecision.EndLocally)]
    [InlineData(200, false, false, 0, CooltimeScreenRules.RecheckDecision.EndLocally)]   // 서버가 막 지움(세션 확인 캐시 틈)
    [InlineData(200, true, true, 0, CooltimeScreenRules.RecheckDecision.EndLocally)]     // 물음 만료 — 서버 작업 ① 이 끝낸다
    [InlineData(200, true, false, 0, CooltimeScreenRules.RecheckDecision.ClosePopup)]    // 다른 탭이 [계속] · 팝업 중 끔
    [InlineData(200, null, false, 0, CooltimeScreenRules.RecheckDecision.ClosePopup)]    // 옛 서버(active 칸 없음)
    [InlineData(200, true, true, 30, CooltimeScreenRules.RecheckDecision.ShowAgain)]
    [InlineData(null, null, null, null, CooltimeScreenRules.RecheckDecision.ClosePopup)] // 연결 실패 — 출입증을 지우지 않는다
    [InlineData(500, null, null, null, CooltimeScreenRules.RecheckDecision.ClosePopup)]
    public void V1_다시확인_판정(int? status, bool? active, bool? asked, int? secondsLeft, CooltimeScreenRules.RecheckDecision expected)
        => Assert.Equal(expected, CooltimeScreenRules.AfterRecheck(status, active, asked, secondsLeft));

    [Fact(DisplayName = "V1 음성 대조 「탭 2개 · 한 탭 계속 → 다른 탭 0초」 — 서버가 실제로 내는 본문(ended:false · state active:true asked:false)으로 흘리면 로그인 유지 · 옛 판정(2xx = 끝남)이면 로그아웃")]
    public void V1_탭2개_한탭계속_다른탭_0초()
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        // 서버 본문 — 컨트롤러 Logout 의 Ok(new { ended = … }) · State 의 Ok(CooltimeStateResponse) 와 같은 직렬화(ASP.NET 기본 = Web)
        var logoutBody = System.Text.Json.JsonSerializer.Serialize(new { ended = false }, web);
        var stateBody = System.Text.Json.JsonSerializer.Serialize(new CooltimeStateResponse(false, 0, DateTime.UtcNow.AddHours(1)), web);

        // 탭 B 0초: logout 200 ended:false → 다시 확인 → state 200 active:true asked:false → 팝업만 닫음
        var outcome = CooltimeScreenRules.LogoutOutcome(200, CooltimeScreenRules.ParseEnded(logoutBody));
        Assert.Equal(CooltimeScreenRules.CallOutcome.Recheck, outcome);
        var s = CooltimeScreenRules.ParseState(stateBody);
        Assert.Equal(true, s.Active);
        Assert.Equal(false, s.Asked);
        Assert.Equal(CooltimeScreenRules.RecheckDecision.ClosePopup, CooltimeScreenRules.AfterRecheck(200, s.Active, s.Asked, s.SecondsLeft));

        // 행 지운 직후(서버 엔진 = active:false) → 끝남
        var goneBody = System.Text.Json.JsonSerializer.Serialize(new CooltimeStateResponse(false, 0, null, Active: false), web);
        var g = CooltimeScreenRules.ParseState(goneBody);
        Assert.Equal(CooltimeScreenRules.RecheckDecision.EndLocally, CooltimeScreenRules.AfterRecheck(200, g.Active, g.Asked, g.SecondsLeft));

        // 대조 — 옛 판정(2xx 면 ended 무관 끝남)이었다면 탭 B 가 공유 출입증을 지운다
        static CooltimeScreenRules.CallOutcome OldLogoutOutcome(int? st) => st is >= 200 and < 300 or 401 or 409
            ? CooltimeScreenRules.CallOutcome.Ended : CooltimeScreenRules.CallOutcome.Failed;
        Assert.Equal(CooltimeScreenRules.CallOutcome.Ended, OldLogoutOutcome(200));
        Assert.NotEqual(OldLogoutOutcome(200), outcome);
    }

    [Fact(DisplayName = "V1 배선 — 서버 「계속됨」 이벤트 이름 화면 = 서버 · 알림 연결이 받고 팝업이 구독 · 팝업이 본문 ended 로 판정(살아 있는 줄 기준)")]
    public void V1_계속됨_배선()
    {
        Assert.Equal(CooltimeWire.ContinuedEvent, CooltimeScreenRules.ContinuedEvent);
        static string Live(string path) => string.Join('\n', File.ReadAllText(path).Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        var notify = Live(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Services", "NotificationClient.cs"));
        Assert.Contains("hub.On(CooltimeScreenRules.ContinuedEvent,", notify);
        Assert.Contains("OnCooltimeContinued?.Invoke();", notify);
        var prompt = Live(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Components", "Common", "CooltimePrompt.razor"));
        Assert.Contains("Notify.OnCooltimeContinued += OnContinued;", prompt);
        Assert.Contains("CooltimeScreenRules.LogoutOutcome(status, ended)", prompt);
        Assert.Contains("await RecheckAsync();", prompt);
        var engine = Live(Path.Combine(RepoRoot(), "src", "HitPan.API", "Services", "Cooltime", "CooltimeEngine.cs"));
        Assert.Contains("if (n == 1) await _pusher.ContinuedAsync(tenantId, sessionId, ct)", engine);
    }

    // ══════════════════════════════════════════════════════════════
    // 🆕 11/28 고침 1(§277 P3 (b) · §285 · §286) — [아니요 — 지금 로그아웃] 을 눌렀는데 서버가 안 끝냄
    //   → 다시 물어 「끝남」 이 아니면 보통 손 로그아웃 · 「끝남」(물음 만료 · 행 없음)은 지금대로(서버가 설정 시각 퇴근)
    // ⬛ [낡은 시험 · eea1242c · push 전] 고침1_버튼_서버안끝냄_손로그아웃(trigger, status, ended) — 다시 묻기 전 판정을 쟀다(리뷰 P3-1 로 바꿈)
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "고침1 판정: 버튼 + 다시 물음 결과 팝업만 닫음 · 다시 셈 = 손 로그아웃 · 끝남 = 아님 · 0초 = 언제나 아님")]
    [InlineData(CooltimeScreenRules.TriggerButton, CooltimeScreenRules.RecheckDecision.ClosePopup, true)]   // 팝업 중 끔 · 다른 탭 [계속] · 확인 실패
    [InlineData(CooltimeScreenRules.TriggerButton, CooltimeScreenRules.RecheckDecision.ShowAgain, true)]    // 아직 묻는 중인데 안 끝냄(경주) — 누른 뜻대로
    [InlineData(CooltimeScreenRules.TriggerButton, CooltimeScreenRules.RecheckDecision.EndLocally, false)]  // 물음 만료 · 행 없음 — 서버가 설정 시각 퇴근
    [InlineData(CooltimeScreenRules.TriggerCountdown, CooltimeScreenRules.RecheckDecision.ClosePopup, false)] // 0초 = 사람이 고른 것 아님(§273 V1)
    [InlineData(CooltimeScreenRules.TriggerCountdown, CooltimeScreenRules.RecheckDecision.ShowAgain, false)]
    [InlineData(CooltimeScreenRules.TriggerCountdown, CooltimeScreenRules.RecheckDecision.EndLocally, false)]
    public void 고침1_다시물은뒤_손로그아웃_판정(string trigger, CooltimeScreenRules.RecheckDecision decision, bool expected)
        => Assert.Equal(expected, CooltimeScreenRules.UseManualLogout(trigger, decision));

    [Theory(DisplayName = "고침1 흐름(서버 실제 본문): 버튼 → logout ended:false → state 응답 → 손 로그아웃 여부 · 물음 만료 · 행 없음 · 401 = 아님 · 끔 · 연결 실패 = 손 로그아웃")]
    [InlineData(200, true, false, 0, true)]      // 팝업 중 관리자가 끔(행 있음 · 안 물음) → 손 로그아웃
    [InlineData(null, null, null, null, true)]   // 상태 확인 연결 실패 → 손 로그아웃(AuthService 가 실패면 「미완료」 안내)
    [InlineData(500, null, null, null, true)]
    [InlineData(200, true, true, 0, false)]      // 물음 만료 — 서버 작업 ① 이 설정 시각 퇴근(리뷰 P3-1)
    [InlineData(200, false, false, 0, false)]    // 행 없음 — 이미 끝남
    [InlineData(401, null, null, null, false)]
    public void 고침1_버튼_흐름(int? stateStatus, bool? active, bool? asked, int? secondsLeft, bool expectManual)
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var logoutBody = System.Text.Json.JsonSerializer.Serialize(new { ended = false }, web);
        Assert.Equal(CooltimeScreenRules.CallOutcome.Recheck,
            CooltimeScreenRules.LogoutOutcome(200, CooltimeScreenRules.ParseEnded(logoutBody)));
        var decision = CooltimeScreenRules.AfterRecheck(stateStatus, active, asked, secondsLeft);
        Assert.Equal(expectManual, CooltimeScreenRules.UseManualLogout(CooltimeScreenRules.TriggerButton, decision));
    }

    [Fact(DisplayName = "고침1 음성 대조 「팝업 중 끔 → [지금 로그아웃]」 — 서버 실제 본문으로 흘리면 손 로그아웃 · 옛 흐름(계기 없이 다시 확인)이면 팝업만 닫고 「계속 작업할 수 있습니다」")]
    public void 고침1_팝업중끔_지금로그아웃_대조()
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var stateBody = System.Text.Json.JsonSerializer.Serialize(new CooltimeStateResponse(false, 0, null), web);
        var s = CooltimeScreenRules.ParseState(stateBody);
        var decision = CooltimeScreenRules.AfterRecheck(200, s.Active, s.Asked, s.SecondsLeft);
        Assert.Equal(CooltimeScreenRules.RecheckDecision.ClosePopup, decision);   // 옛 흐름의 끝 = 팝업만 닫음
        Assert.StartsWith("계속 작업할 수 있습니다", CooltimeScreenRules.RecheckClosedNotice);
        Assert.True(CooltimeScreenRules.UseManualLogout(CooltimeScreenRules.TriggerButton, decision));
    }

    /// <summary>🆕 11/28 고침 1(리뷰 P3-5) — 주석 줄을 뺀 소스에서 메서드 하나의 몸(중괄호 짝)만 잘라 낸다.</summary>
    private static string MethodBody(string live, string signature)
    {
        var at = live.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, "메서드를 못 찾음: " + signature);
        var open = live.IndexOf('{', at);
        var depth = 0;
        for (var i = open; i < live.Length; i++)
        {
            if (live[i] == '{') depth++;
            else if (live[i] == '}' && --depth == 0) return live.Substring(open, i - open + 1);
        }
        throw new Xunit.Sdk.XunitException("중괄호 짝이 안 맞음: " + signature);
    }

    [Fact(DisplayName = "고침1 배선(메서드 몸 단위 · 리뷰 P3-5): 버튼 = TriggerButton · 0초 = TriggerCountdown · 로그아웃 다시 확인에 계기 넘김 · 다시 확인 안에서 손 로그아웃 판정이 switch 보다 먼저 · 손 로그아웃 = AuthService.LogoutAsync")]
    public void 고침1_배선()
    {
        static string Live(string path) => string.Join('\n', File.ReadAllText(path).Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        var prompt = Live(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Components", "Common", "CooltimePrompt.razor"));

        var logoutNow = MethodBody(prompt, "private async Task LogoutNowAsync()");
        Assert.Contains("FinishWithLogoutAsync(CooltimeScreenRules.TriggerButton)", logoutNow);
        Assert.DoesNotContain("TriggerCountdown", logoutNow);

        var countdown = MethodBody(prompt, "private async Task RunCountdownAsync(");
        Assert.Contains("FinishWithLogoutAsync(CooltimeScreenRules.TriggerCountdown)", countdown);
        Assert.DoesNotContain("TriggerButton", countdown);

        var finish = MethodBody(prompt, "private async Task FinishWithLogoutAsync(");
        Assert.Contains("await RecheckAsync(trigger);", finish);
        Assert.DoesNotContain("await RecheckAsync();", finish);
        Assert.DoesNotContain("ManualLogoutAsync", finish);   // 다시 묻기 전에 손 로그아웃하지 않는다(P3-1)

        var recheck = MethodBody(prompt, "private async Task RecheckAsync(");
        var judge = recheck.IndexOf("CooltimeScreenRules.UseManualLogout(trigger, decision)", StringComparison.Ordinal);
        var sw = recheck.IndexOf("switch (decision)", StringComparison.Ordinal);
        Assert.True(judge > 0 && sw > judge, "손 로그아웃 판정이 switch 보다 먼저 와야 한다");
        var branch = recheck.Substring(judge, sw - judge);
        Assert.Contains("await ManualLogoutAsync();", branch);
        Assert.Contains("return;", branch);

        var manual = MethodBody(prompt, "private async Task ManualLogoutAsync()");
        Assert.Contains("await AuthService.LogoutAsync();", manual);
        Assert.Contains("Nav.NavigateTo(\"/login\", forceLoad: true);", manual);
        Assert.DoesNotContain("EndedNoticeKey", manual);   // 사람이 고른 로그아웃 — 「자동 로그아웃」 안내 표 없음
        Assert.Contains("@inject IAuthService AuthService", prompt);
    }

    [Fact(DisplayName = "고침1 계기 글자 화면 = 서버(리뷰 P3-6): 서버가 0초를 가르는 글자 = TriggerCountdown · 버튼 = 기본(그 밖)")]
    public void 고침1_계기글자_화면과서버()
    {
        var ctl = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.API", "Controllers", "CooltimeController.cs"));
        Assert.Contains($"string.Equals(request?.Trigger, \"{CooltimeScreenRules.TriggerCountdown}\", StringComparison.OrdinalIgnoreCase)", ctl);
        Assert.NotEqual(CooltimeScreenRules.TriggerCountdown, CooltimeScreenRules.TriggerButton, StringComparer.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "V4 내 정보 저장 — 자동 로그아웃 세 칸을 null(= 그대로)로 보낸다 · 서버 DTO 가 null 로 받는다 · 대조(안 지움) = false(끔) 가 실린다 · 배선")]
    public void V4_내정보_저장은_자동로그아웃을_그대로_둔다()
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        // 웹 모델(새로 만든 것)의 모양 — 켬/끔 = null 못 되는 bool 의 기본값 false · 시각 두 칸 null
        System.Text.Json.Nodes.JsonObject Body() => new()
        {
            ["industryType"] = "manufacturing",
            ["cooltimeEnabled"] = false,
            ["cooltimeAt"] = null,
            ["regularLeaveTime"] = null
        };

        var kept = Body();
        CooltimeScreenRules.KeepCooltimeAsIs(kept);
        var dto = System.Text.Json.JsonSerializer.Deserialize<HitPan.Application.DTOs.Settings.UpdateTenantSettingsDto>(kept.ToJsonString(), web)!;
        Assert.Null(dto.CooltimeEnabled);
        Assert.Null(dto.CooltimeAt);
        Assert.Null(dto.RegularLeaveTime);
        Assert.Equal("manufacturing", dto.IndustryType);   // 다른 칸은 그대로 간다

        // 대조 — 지우지 않으면 서버는 false = 「끔」 으로 받는다(리뷰서 V4 의 조용히 꺼짐)
        var raw = System.Text.Json.JsonSerializer.Deserialize<HitPan.Application.DTOs.Settings.UpdateTenantSettingsDto>(Body().ToJsonString(), web)!;
        Assert.Equal(false, raw.CooltimeEnabled);

        static string Live(string path) => string.Join('\n', File.ReadAllText(path).Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        var page = Live(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Pages", "Settings", "UserInfoPage.razor.cs"));
        Assert.Contains("SettingsSvc.SaveKeepCooltimeAsync(current)", page);
        Assert.DoesNotContain("SettingsSvc.SaveAsync(current)", page);
        var svc = Live(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Services", "SettingsService.cs"));
        Assert.Contains("CooltimeScreenRules.KeepCooltimeAsIs(body);", svc);
    }

    // ══════════════════════════════════════════════════════════════
    // G-C15 화면 몫 · C-5 대상 (설계 §D-2 안 A)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C15 화면 몫 — cooltime_blank = 「자동 로그아웃 · 퇴근 빈칸」 · cooltime = 「자동」 · edited = 「고침」 · 사람 = 표시 없음")]
    public void G_C15_표시_낱말()
    {
        Assert.Equal("자동 로그아웃 · 퇴근 빈칸", CooltimeScreenRules.SourceLabel("cooltime_blank"));
        Assert.Equal("자동", CooltimeScreenRules.SourceLabel("cooltime"));
        Assert.Equal("고침", CooltimeScreenRules.SourceLabel("edited"));
        Assert.Null(CooltimeScreenRules.SourceLabel(null));
    }

    // 🆕 11/27 고침 차례(§272 ① · §273 V3) — 대상에 「고침」(edited) 이 들어간다(다시 고치기 허용 · 매번 이력).
    //   ⬛ [낡은 기대 · 11/27 이전] new[] { CooltimeWire.SourceCooltime, CooltimeWire.SourceCooltimeBlank }
    [Fact(DisplayName = "C-5 고치기 대상 — 화면(IsEditableSource) = 서버(HrService.EditableCheckOutSources) = 엔진 값(CooltimeWire) + 고침(edited)")]
    public void C5_고치기_대상_화면_서버_같다()
    {
        Assert.Equal(new[] { CooltimeWire.SourceCooltime, CooltimeWire.SourceCooltimeBlank, CooltimeScreenRules.SourceEdited }.OrderBy(x => x),
                     HrService.EditableCheckOutSources.OrderBy(x => x));
        Assert.True(CooltimeScreenRules.IsEditableSource("edited"));
        foreach (var s in new[] { "cooltime", "cooltime_blank", "edited", null, "" })
            Assert.Equal(s is not null && HrService.EditableCheckOutSources.Contains(s), CooltimeScreenRules.IsEditableSource(s));
    }

    [Fact(DisplayName = "주소·이벤트 대조 — 화면 약속(CooltimeScreenRules) = 서버 약속(CooltimeWire · CooltimeController 경로)")]
    public void 주소_이벤트_대조()
    {
        Assert.Equal(CooltimeWire.PromptEvent, CooltimeScreenRules.PromptEvent);
        Assert.Equal(CooltimeWire.EndedEvent, CooltimeScreenRules.EndedEvent);
        Assert.Equal(CooltimeWire.PromptSeconds, CooltimeScreenRules.PromptSeconds);

        // 컨트롤러 경로는 특성에서 읽는다(글자 복사 아님 — 반사로 실제 값을 꺼낸다)
        var route = (Microsoft.AspNetCore.Mvc.RouteAttribute)Attribute.GetCustomAttribute(
            typeof(HitPan.API.Controllers.CooltimeController), typeof(Microsoft.AspNetCore.Mvc.RouteAttribute))!;
        string RoutePath(string action)
        {
            var m = typeof(HitPan.API.Controllers.CooltimeController).GetMethod(action)!;
            var t = m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), false)
                     .Cast<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single().Template;
            return route.Template + "/" + t;
        }
        Assert.Equal(CooltimeScreenRules.ContinuePath, RoutePath("Continue"));
        Assert.Equal(CooltimeScreenRules.LogoutPath, RoutePath("Logout"));
        Assert.Equal(CooltimeScreenRules.StatePath, RoutePath("State"));
    }

    // ══════════════════════════════════════════════════════════════
    // 엔진 순수 규칙 — G-C8 · G-C9 · G-C11 · G-C19 의 시각 판정 몸통
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "설정 시각 C = 가장 최근에 지난 그 시각(오늘 지났으면 오늘 · 아니면 어제)")]
    [InlineData("2026-11-27 22:00:15", "22:00", "2026-11-27 22:00:00")]
    [InlineData("2026-11-27 21:59:59", "22:00", "2026-11-26 22:00:00")]
    [InlineData("2026-11-28 08:00:00", "22:00", "2026-11-27 22:00:00")]   // G-C11 서버 켜짐 다음 날 08:00
    [InlineData("2026-11-28 02:00:00", "02:00", "2026-11-28 02:00:00")]   // 경계 = 지남
    public void C_가장_최근_설정시각(string now, string at, string expected)
        => Assert.Equal(DateTime.Parse(expected),
            CooltimeEngine.MostRecentCooltimeLocal(DateTime.Parse(now), TimeSpan.Parse(at)));

    [Theory(DisplayName = "정시 퇴근 = 출근 뒤 처음 오는 그 시각 — 주간 같은 날 · 야간 다음 날 · 19:00 출근(18:00)은 다음 날")]
    [InlineData("2026-11-27 09:00", "18:00", "2026-11-27 18:00")]   // G-C8
    [InlineData("2026-11-27 22:00", "06:00", "2026-11-28 06:00")]   // 야간
    [InlineData("2026-11-27 19:00", "18:00", "2026-11-28 18:00")]   // G-C15 의 「아직 안 온 시각」
    public void 정시퇴근_첫_시각(string checkIn, string leave, string expected)
        => Assert.Equal(DateTime.Parse(expected),
            CooltimeEngine.FirstRegularLeaveAfter(DateTime.Parse(checkIn), TimeSpan.Parse(leave)));

    [Fact(DisplayName = "근무 시간 = 퇴근 − 출근(소수 1자리) — 09:00 → 18:00 = 9.0")]
    public void 근무시간()
    {
        Assert.Equal(9.0, CooltimeEngine.WorkHours(DateTime.Parse("2026-11-27 09:00"), DateTime.Parse("2026-11-27 18:00")));
        Assert.Equal(9.7, CooltimeEngine.WorkHours(DateTime.Parse("2026-11-27 09:00"), DateTime.Parse("2026-11-27 18:40")));
    }

    [Fact(DisplayName = "G-C19 규칙(E-2) — C ≤ 마지막 켬·시각 바꿈이면 건너뜀 · 기록 없음(NULL)이면 안 건너뜀")]
    public void G_C19_건너뛰기_규칙()
    {
        var c = DateTime.Parse("2026-11-27 04:00:00");
        Assert.True(CooltimeEngine.IsBeforeSettingChange(c, DateTime.Parse("2026-11-27 05:00:00")));
        Assert.True(CooltimeEngine.IsBeforeSettingChange(c, c));
        Assert.False(CooltimeEngine.IsBeforeSettingChange(c, DateTime.Parse("2026-11-27 03:59:59")));
        Assert.False(CooltimeEngine.IsBeforeSettingChange(c, null));
    }

    [Fact(DisplayName = "서버 로컬 ↔ UTC 왕복이 같은 값을 돌려준다(엔진 C 변환 · 근태 기준 순간)")]
    public void 로컬_UTC_왕복()
    {
        var local = DateTime.Parse("2026-11-27 22:00:00");
        Assert.Equal(local, CooltimeEngine.UtcToServerLocal(CooltimeEngine.ServerLocalToUtc(local)));
    }

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
}
