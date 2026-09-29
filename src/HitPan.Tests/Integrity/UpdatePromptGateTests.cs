using System.Net;
using System.Text;
using HitPan.Web.Services;
using Microsoft.JSInterop;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260929작3 갈래 F — 업데이트 미완료 안내 (작업지시서 §5 G-F1 · G-F2 · G-F3).
/// </summary>
/// <remarks>
/// <para>
/// 전부 <b>실제 코드를 불러</b> 잰다(링크 · <c>HitPan.Tests.csproj</c>). 소스 글자를 읽지 않는다.
/// </para>
/// <para>
/// 대조군(빼면 FAIL)은 개발명세서 F 에 적었다 — 봉합을 뺀 채 이 파일을 돌려 FAIL 을 실제로 확인했다.
/// </para>
/// </remarks>
public sealed class UpdatePromptGateTests
{
    private static readonly string[] PromptKinds =
    {
        UpdatePromptPlan.KindFirst,
        UpdatePromptPlan.KindLater,
        UpdatePromptPlan.KindNotStarted,
        UpdatePromptPlan.KindInterrupted,
        UpdatePromptPlan.KindFailed,
    };

    private static UpdateStatusSnapshot S(
        string? kind, bool canRespond, bool needsPrompt,
        string? latest = "1.3.50", string? issueText = null, bool updateAvailable = true) =>
        UpdatePromptPlan.Snapshot("1.3.49", updateAvailable, latest, null, kind, issueText, canRespond, needsPrompt);

    // ═══════════════════════════════════════════════════════════════
    // G-F1 — (종류 × 응답가능) → 팝업 유무 · 문구 · 버튼 수 · 기록 여부
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-F1a 메인PC(CanRespond) · 묻는 종류 5개 → [예]/[나중에] 2버튼 · 기록한다 · 종류별 문구")]
    public void GF1a_MainPc_PromptKinds_RespondTwoButtons()
    {
        foreach (var kind in PromptKinds)
        {
            var plan = UpdatePromptPlan.Decide(S(kind, canRespond: true, needsPrompt: true, issueText: "사유 한 줄"), null, force: false);

            Assert.True(plan.Show, kind);
            Assert.Equal(UpdatePopupMode.Respond, plan.Mode);
            Assert.Equal(2, plan.ButtonCount);
            Assert.Equal("예", plan.ConfirmText);
            Assert.Equal("나중에", plan.CancelText);
            Assert.True(plan.RecordsConsent, kind);
            Assert.Equal("1.3.50", plan.Version);

            if (kind == UpdatePromptPlan.KindFirst)
            {
                Assert.Contains("새 버전 1.3.50 이(가) 나왔습니다. 업데이트하시겠습니까?", plan.Message);
                Assert.DoesNotContain(UpdatePromptPlan.OwnerRetryQuestion, plan.Message);
            }
            else
            {
                // 🔴 사장님 문구 — 「정상적으로」(오타 「정상상적으로」 아님)
                Assert.StartsWith(UpdatePromptPlan.OwnerRetryQuestion, plan.Message);
                Assert.Contains("사유 한 줄", plan.Message);
            }

            // 종전 백업·잠시 꺼짐 안내(#24)가 함께 간다.
            Assert.Contains(UpdatePromptPlan.DefaultNotice, plan.Message);
        }
    }

    [Fact(DisplayName = "G-F1b 다른 PC(CanRespond=false) · 묻는 종류 5개 → [확인] 1버튼 · 기록 0 · [예] 글자 없음")]
    public void GF1b_OtherPc_PromptKinds_InfoOnlyNoRecord()
    {
        foreach (var kind in PromptKinds)
        {
            var plan = UpdatePromptPlan.Decide(S(kind, canRespond: false, needsPrompt: true), null, force: false);

            Assert.True(plan.Show, kind);
            Assert.Equal(UpdatePopupMode.InfoOnly, plan.Mode);
            Assert.Equal(1, plan.ButtonCount);
            Assert.Equal("확인", plan.ConfirmText);
            Assert.Null(plan.CancelText);
            Assert.False(plan.RecordsConsent, kind);          // 🔴 비메인은 기록 호출 0
            Assert.Contains(UpdatePromptPlan.MainPcPhrase, plan.Message);
            Assert.DoesNotContain("[예]", plan.Message);
            Assert.DoesNotContain("실행하시겠습니까", plan.Message);
        }
    }

    [Fact(DisplayName = "G-F1c 준비 중·진행 중(NeedsPrompt=false)·none 은 메인PC 든 아니든 팝업 0")]
    public void GF1c_NoPromptKinds_NeverShow()
    {
        foreach (var kind in new[] { UpdatePromptPlan.KindRequested, UpdatePromptPlan.KindInProgress, UpdatePromptPlan.KindNone })
        {
            foreach (var canRespond in new[] { true, false })
            {
                var plan = UpdatePromptPlan.Decide(S(kind, canRespond, needsPrompt: false), null, force: true);
                Assert.False(plan.Show, $"{kind}/{canRespond}");
                Assert.Equal(0, plan.ButtonCount);
                Assert.False(plan.RecordsConsent);
            }
        }
    }

    [Fact(DisplayName = "G-F1d 이미 안내한 표(버전+종류) — 같으면 0 · 종류가 바뀌면 다시 · 사이드바(force)는 표 무시")]
    public void GF1d_Guard_VersionAndKind()
    {
        var failed = S(UpdatePromptPlan.KindFailed, canRespond: true, needsPrompt: true);
        var guard = UpdatePromptPlan.GuardValue(failed);
        Assert.Equal("1.3.50|failed", guard);

        Assert.False(UpdatePromptPlan.Decide(failed, guard, force: false).Show);
        Assert.True(UpdatePromptPlan.Decide(failed, guard, force: true).Show);

        // [예] 뒤 실패 — 종전(버전만)이면 같은 탭에서 영영 안 물었다(Q-1 「반드시 다시 묻는다」).
        Assert.True(UpdatePromptPlan.Decide(failed, "1.3.50|first", force: false).Show);
        Assert.True(UpdatePromptPlan.Decide(failed, "1.3.50", force: false).Show);   // 옛 표(버전만)
    }

    [Fact(DisplayName = "G-F1e 옛 API(4필드 없음)는 종전 그대로 — 새 버전이면 누구에게나 Y/n")]
    public void GF1e_LegacyServer_KeepsOldBehavior()
    {
        var legacy = S(kind: null, canRespond: false, needsPrompt: false, updateAvailable: true);
        var plan = UpdatePromptPlan.Decide(legacy, null, force: false);
        Assert.True(plan.Show);
        Assert.Equal(UpdatePopupMode.Respond, plan.Mode);
        Assert.Equal("1.3.50|first", plan.Guard);

        var nothing = S(kind: null, canRespond: false, needsPrompt: false, updateAvailable: false);
        Assert.False(UpdatePromptPlan.Decide(nothing, null, force: false).Show);
        Assert.Null(UpdatePromptPlan.SidebarLine(nothing));
    }

    [Fact(DisplayName = "G-F1f M-22 R-1 — 대표+응답불가+묻는 종류일 때만 왕복 대기 후 재조회")]
    public void GF1f_RetryAfterProof_OnlyOwnerNotRespondable()
    {
        var notYet = S(UpdatePromptPlan.KindFailed, canRespond: false, needsPrompt: true);
        Assert.True(UpdatePromptPlan.ShouldRetryAfterProof(notYet, isOwner: true));
        Assert.False(UpdatePromptPlan.ShouldRetryAfterProof(notYet, isOwner: false));   // 직원은 출입증을 안 받는다
        Assert.False(UpdatePromptPlan.ShouldRetryAfterProof(
            S(UpdatePromptPlan.KindFailed, canRespond: true, needsPrompt: true), isOwner: true));
        Assert.False(UpdatePromptPlan.ShouldRetryAfterProof(
            S(UpdatePromptPlan.KindInProgress, canRespond: false, needsPrompt: false), isOwner: true));
    }

    [Fact(DisplayName = "G-F1g 사이드바 — 메인PC 는 [업데이트 마치기] · 다른 PC 는 안내 한 줄 · 진행 중은 둘 다 없음")]
    public void GF1g_SidebarLine()
    {
        var main = UpdatePromptPlan.SidebarLine(S(UpdatePromptPlan.KindFailed, true, true, issueText: "컴퓨터 저장공간이 부족해 업데이트하지 못했습니다."));
        Assert.NotNull(main);
        Assert.True(main!.ShowFinishButton);
        Assert.Null(main.Hint);
        Assert.Equal("컴퓨터 저장공간이 부족해 업데이트하지 못했습니다.", main.Detail);

        var other = UpdatePromptPlan.SidebarLine(S(UpdatePromptPlan.KindLater, false, true));
        Assert.NotNull(other);
        Assert.False(other!.ShowFinishButton);
        Assert.Equal(UpdatePromptPlan.SidebarOtherPcHint, other.Hint);

        var running = UpdatePromptPlan.SidebarLine(S(UpdatePromptPlan.KindInProgress, true, false));
        Assert.NotNull(running);
        Assert.Equal("업데이트 진행 중", running!.Headline);
        Assert.False(running.ShowFinishButton);
        Assert.Null(running.Hint);

        Assert.Null(UpdatePromptPlan.SidebarLine(S(UpdatePromptPlan.KindNone, true, false)));
    }

    [Fact(DisplayName = "G-F1h 고객 문구 — 오타·개발용어 0 · 메인PC 표현은 한 가지(병렬이슈 04)")]
    public void GF1h_CustomerText_Clean()
    {
        var texts = new List<string>();
        foreach (var kind in PromptKinds)
        {
            foreach (var canRespond in new[] { true, false })
            {
                texts.Add(UpdatePromptPlan.Decide(S(kind, canRespond, true), null, false).Message);
                var line = UpdatePromptPlan.SidebarLine(S(kind, canRespond, true))!;
                texts.Add(line.Headline);
                if (line.Hint is not null) texts.Add(line.Hint);
            }
        }

        foreach (var t in texts)
        {
            Assert.DoesNotContain("정상상적으로", t);
            foreach (var banned in new[] { "AI", "Claude", "CS", "마이그", "교차검증", "detail", "consent" })
                Assert.DoesNotContain(banned, t);

            // 🔴 병렬이슈 04 — 설계 초안 표현이 섞이지 않는다.
            Assert.DoesNotContain("자료가 저장된 컴퓨터", t);
        }

        Assert.Equal("회사 자료가 들어 있는 컴퓨터(메인PC)", UpdatePromptPlan.MainPcPhrase);
    }

    [Fact(DisplayName = "G-F1i 방아쇠 한 곳 — 사이드바 요청은 게이트에 걸린 처리 하나만 부른다 · 없으면 false")]
    public async Task GF1i_Bus_SingleTrigger()
    {
        var bus = new UpdatePromptBus();
        Assert.False(await bus.RequestPromptAsync());

        var calls = 0;
        Func<Task> gate = () => { calls++; return Task.CompletedTask; };
        bus.AttachPrompt(gate);
        Assert.True(await bus.RequestPromptAsync());
        Assert.Equal(1, calls);

        // 다른 처리를 떼려 해도 게이트 것은 남는다.
        bus.DetachPrompt(() => Task.CompletedTask);
        Assert.True(await bus.RequestPromptAsync());
        Assert.Equal(2, calls);

        bus.DetachPrompt(gate);
        Assert.False(await bus.RequestPromptAsync());

        UpdateStatusSnapshot? seen = null;
        bus.StatusChanged += s => seen = s;
        var before = bus.Sequence;
        var snap = S(UpdatePromptPlan.KindFailed, true, true);
        bus.Publish(snap);
        Assert.Same(snap, seen);
        Assert.Same(snap, bus.Latest);
        Assert.Equal(before + 1, bus.Sequence);
    }

    // ═══════════════════════════════════════════════════════════════
    // G-F2 — MainPcRetryCoordinator 에 update-consent 한 줄
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-F2a 대표 · update-consent 403 main_pc_only → 왕복 1 · 재시도 1 · 200")]
    public async Task GF2a_Owner_UpdateConsent_RoundTripOnceRetryOnce()
    {
        var server = new FakeConsentServer();
        var coordinator = new MainPcRetryCoordinator(() => DateTimeOffset.UtcNow);

        using var original = ConsentPost("https://x.test/api/auth/update-consent");
        var recovered = await Recover(coordinator, server, original, isOwner: true);

        Assert.NotNull(recovered);
        Assert.Equal(HttpStatusCode.OK, recovered!.StatusCode);
        Assert.Equal(1, coordinator.ProofRoundTrips);
        Assert.Equal(1, server.ChallengeCalls);
        Assert.Equal(1, server.ConsentCallsWithPass);   // 재시도 1회 — 출입증을 달고 갔다
    }

    [Fact(DisplayName = "G-F2b 직원 · update-consent 403 → 왕복 0 · 표 요청 0")]
    public async Task GF2b_Staff_UpdateConsent_NoRoundTrip()
    {
        var server = new FakeConsentServer();
        var coordinator = new MainPcRetryCoordinator(() => DateTimeOffset.UtcNow);

        using var original = ConsentPost("https://x.test/api/auth/update-consent");
        var recovered = await Recover(coordinator, server, original, isOwner: false);

        Assert.Null(recovered);
        Assert.Equal(0, coordinator.ProofRoundTrips);
        Assert.Equal(0, server.ChallengeCalls);
    }

    [Fact(DisplayName = "G-F2c 병렬이슈 04 · 익명 입구 update-consent-local 403 → 왕복 0 (경계 있는 일치)")]
    public async Task GF2c_UpdateConsentLocal_NotEligible()
    {
        var server = new FakeConsentServer();
        var coordinator = new MainPcRetryCoordinator(() => DateTimeOffset.UtcNow);

        using var original = ConsentPost("https://x.test/api/auth/update-consent-local");
        var recovered = await Recover(coordinator, server, original, isOwner: true);

        Assert.Null(recovered);
        Assert.Equal(0, coordinator.ProofRoundTrips);
        Assert.Equal(0, server.ChallengeCalls);

        Assert.False(MainPcRetryCoordinator.IsEligiblePath("/api/auth/update-consent-local"));
        Assert.False(MainPcRetryCoordinator.IsEligiblePath("api/auth/update-consent-local"));
        Assert.False(MainPcRetryCoordinator.IsEligiblePath("/api/auth/update-consentX"));
        Assert.True(MainPcRetryCoordinator.IsEligiblePath("/api/auth/update-consent"));
        Assert.True(MainPcRetryCoordinator.IsEligiblePath("api/auth/update-consent"));
    }

    [Fact(DisplayName = "G-F2d 종전 자료관리 세 갈래는 그대로 앞머리 일치 (회귀)")]
    public void GF2d_ExistingPrefixes_Unchanged()
    {
        Assert.True(MainPcRetryCoordinator.IsEligiblePath("/api/backup/settings"));
        Assert.True(MainPcRetryCoordinator.IsEligiblePath("api/backup/history"));
        Assert.True(MainPcRetryCoordinator.IsEligiblePath("/api/data-reset/run"));
        Assert.True(MainPcRetryCoordinator.IsEligiblePath("/api/migration/status"));
        Assert.False(MainPcRetryCoordinator.IsEligiblePath("/api/employee/list"));
        Assert.False(MainPcRetryCoordinator.IsEligiblePath("/api/auth/update-status"));
    }

    // ═══════════════════════════════════════════════════════════════
    // G-F3 — MainPcProofRunner 단일 비행
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-F3a 동시에 두 번 불러도 표(challenge)는 1회 · 둘 다 같은 판정")]
    public async Task GF3a_ConcurrentCalls_OneChallenge()
    {
        var handler = new FakeProofHandler { ChallengeDelay = TimeSpan.FromMilliseconds(150) };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://x.test/") };
        var runner = new MainPcProofRunner(http, new FakeJs());

        var a = runner.RunAsync();
        var b = runner.RunAsync();
        var results = await Task.WhenAll(a, b);

        Assert.Equal(1, handler.ChallengeCalls);
        Assert.Equal(1, handler.VerifyCalls);
        Assert.All(results, r => Assert.Equal("MainPcConfirmed", r));
    }

    [Fact(DisplayName = "G-F3b 끝난 뒤 다시 부르면 새로 돈다 (결과를 붙잡아 두지 않는다)")]
    public async Task GF3b_AfterCompletion_RunsAgain()
    {
        var handler = new FakeProofHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://x.test/") };
        var runner = new MainPcProofRunner(http, new FakeJs());

        await runner.RunAsync();
        await runner.RunAsync();

        Assert.Equal(2, handler.ChallengeCalls);
    }

    // ── 대역 ─────────────────────────────────────────────────────

    private static HttpRequestMessage ConsentPost(string url) =>
        new(HttpMethod.Post, url)
        {
            Content = new StringContent("{\"updateVersion\":\"1.3.50\",\"action\":\"approve\"}", Encoding.UTF8, "application/json")
        };

    private static Task<HttpResponseMessage?> Recover(
        MainPcRetryCoordinator coordinator, FakeConsentServer server, HttpRequestMessage original, bool isOwner) =>
        coordinator.TryRecoverAsync(
            original,
            server.SendAsync,
            CloneAsync,
            server.DecorateAsync,
            _ => Task.FromResult(true),
            pass => { server.SavedPass = pass; return Task.CompletedTask; },
            () => Task.FromResult(isOwner),
            (_, _) => { },
            CancellationToken.None);

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage src)
    {
        var copy = new HttpRequestMessage(src.Method, src.RequestUri);
        if (src.Content is not null)
        {
            var body = await src.Content.ReadAsStringAsync();
            copy.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        foreach (var h in src.Headers) copy.Headers.TryAddWithoutValidation(h.Key, h.Value);
        return copy;
    }

    /// <summary>출입증 없으면 403 main_pc_only, 있으면 200 — 업데이트 동의 문 하나만 흉내 낸다.</summary>
    private sealed class FakeConsentServer
    {
        public string? SavedPass { get; set; }
        public int ChallengeCalls { get; private set; }
        public int ConsentCallsWithPass { get; private set; }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri?.ToString() ?? string.Empty;

            if (path.Contains("mainpc-challenge", StringComparison.OrdinalIgnoreCase))
            {
                ChallengeCalls++;
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"challenge\":\"c-1\"}"));
            }

            if (path.Contains("mainpc-verify", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"outcome\":\"MainPcConfirmed\",\"isMainPc\":true,\"pass\":\"p-1\"}"));

            var hasPass = req.Headers.Contains("X-MainPc-Pass");
            if (hasPass) ConsentCallsWithPass++;
            return Task.FromResult(hasPass
                ? Json(HttpStatusCode.OK, "{\"ok\":true}")
                : Json(HttpStatusCode.Forbidden, "{\"error\":\"main_pc_only\"}"));
        }

        public Task DecorateAsync(HttpRequestMessage req)
        {
            if (!string.IsNullOrEmpty(SavedPass)) req.Headers.TryAddWithoutValidation("X-MainPc-Pass", SavedPass);
            return Task.CompletedTask;
        }
    }

    /// <summary>MainPcProofRunner 가 보내는 ①③ 을 세는 대역.</summary>
    private sealed class FakeProofHandler : HttpMessageHandler
    {
        private int _challenge;
        private int _verify;

        public TimeSpan ChallengeDelay { get; init; } = TimeSpan.Zero;
        public int ChallengeCalls => Volatile.Read(ref _challenge);
        public int VerifyCalls => Volatile.Read(ref _verify);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("mainpc-challenge", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _challenge);
                if (ChallengeDelay > TimeSpan.Zero) await Task.Delay(ChallengeDelay, ct);
                return Json(HttpStatusCode.OK, "{\"challenge\":\"c-1\"}");
            }

            if (path.EndsWith("mainpc-verify", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _verify);
                return Json(HttpStatusCode.OK, "{\"outcome\":\"MainPcConfirmed\",\"isMainPc\":true,\"pass\":\"p-1\"}");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        }
    }

    /// <summary>② 두드리기는 늘 성공 · 출입증 보관은 아무것도 안 한다.</summary>
    private sealed class FakeJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "hitpanMainPc.probe" && typeof(TValue) == typeof(bool))
                return ValueTask.FromResult((TValue)(object)true);
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
