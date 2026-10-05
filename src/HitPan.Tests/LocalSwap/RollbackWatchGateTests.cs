using HitPan.Web.Services;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20261005작2 — 되돌리기 [예] 뒤 화면 전개 게이트 G-W1~W10(설계 §9 · 작업지시서 §5).
/// 사장님 10/5 실측: 되돌리기는 성공했는데 ① 완료 안내 없음 ② 그 화면에 그대로 ③ 새로고침 = 「페이지를 찾을 수 없습니다」.
/// 가짜 시계·가짜 지연·가짜 응답 순서로 <see cref="RollbackWatcher"/> 루프를 **실제로** 돌린다(G-W1~W8).
/// G-W10 만 글자 검사(배선 보조 — bUnit 이 없어 화면 동작은 시험으로 못 잰다).
/// </summary>
public sealed class RollbackWatchGateTests
{
    private const string From = "1.3.50";
    private const string To = "1.3.49";
    private static readonly DateTime T0 = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    // ─────────────── 가짜 세계 ───────────────
    private sealed class World
    {
        public DateTime Now = T0;
        public readonly Queue<RollbackVersionProbe> Versions = new();
        public readonly Queue<RollbackStatusProbe> Statuses = new();
        public RollbackVersionProbe AfterVersions = new(502, null);   // 대본이 끝나면 꺼진 채로 본다
        public int StatusCalls;
        public int Polls;

        public RollbackWatcher Watcher() => new(
            _ => { Polls++; return Task.FromResult(Versions.Count > 0 ? Versions.Dequeue() : AfterVersions); },
            _ => { StatusCalls++; return Task.FromResult(Statuses.Count > 0 ? Statuses.Dequeue() : new RollbackStatusProbe(false, null)); },
            (d, _) => { Now += d; return Task.CompletedTask; },
            () => Now);

        public Task<RollbackWatchResult> Run() =>
            Watcher().RunAsync(From, To, T0, T0 + RollbackWatchPlan.GiveUp, CancellationToken.None);
    }

    private static RollbackVersionProbe V(string v) => new(200, v);
    private static RollbackVersionProbe Down(int status = 502) => new(status, null);

    private static RollbackStatusProbe S(string state, DateTime? at = null, string to = To, string mode = LocalSwapUiText.ModeRollback) =>
        new(true, new LocalSwapLast { Mode = mode, State = state, From = From, To = to, AtUtc = at ?? T0.AddSeconds(30) });

    // ─────────────── G-W1 내려가는 판 ───────────────
    [Fact(DisplayName = "G-W1 from 200·running → 502×3 → to 200·success → Done(1.3.49) · 이동 주소 \"/\"")]
    public async Task GW1_Done_on_lower_version()
    {
        var w = new World();
        w.Versions.Enqueue(V(From)); w.Statuses.Enqueue(S(LocalSwapUiText.StateRunning));
        w.Versions.Enqueue(Down()); w.Versions.Enqueue(Down(0)); w.Versions.Enqueue(Down(530));
        w.Versions.Enqueue(V(To + ".0")); w.Statuses.Enqueue(S(LocalSwapUiText.StateSuccess));

        var r = await w.Run();

        Assert.Equal(RollbackWatchVerdict.Done, r.Verdict);
        Assert.Equal(To, r.Version);
        Assert.NotNull(r.Last);
        Assert.Equal(5, w.Polls);
        Assert.Equal("/", RollbackWatchPlan.HomeUrl);
    }

    // ─────────────── G-W2 거짓 완료 차단 ───────────────
    [Fact(DisplayName = "G-W2 to 200·running 이면 아직 · to 200·success 에서 Done (J-4)")]
    public async Task GW2_Target_running_is_not_done()
    {
        var w = new World();
        w.Versions.Enqueue(V(To)); w.Statuses.Enqueue(S(LocalSwapUiText.StateRunning));
        w.Versions.Enqueue(V(To)); w.Statuses.Enqueue(S(LocalSwapUiText.StateSuccess));

        var r = await w.Run();

        Assert.Equal(RollbackWatchVerdict.Done, r.Verdict);
        Assert.Equal(2, w.Polls);   // 첫 응답에서 끝났으면 1
    }

    // ─────────────── G-W3 거절 · 신선 ───────────────
    [Fact(DisplayName = "G-W3 from 200·refused(이번) → 첫 폴링에 Refused · 지난 refused(3시간 전)는 이번 결과가 아니다")]
    public async Task GW3_Refused_only_when_fresh()
    {
        var w = new World();
        w.Versions.Enqueue(V(From)); w.Statuses.Enqueue(S(LocalSwapUiText.StateRefused));
        var r = await w.Run();
        Assert.Equal(RollbackWatchVerdict.Refused, r.Verdict);
        Assert.Equal(1, w.Polls);

        // 대조 — 3시간 전 refused 는 지난번 결과. 거절로 읽으면 안 된다(10분 뒤 Stalled 로 간다).
        var stale = new World();
        stale.Versions.Enqueue(V(From)); stale.Statuses.Enqueue(S(LocalSwapUiText.StateRefused, T0.AddHours(-3)));
        var r2 = await stale.Run();
        Assert.Equal(RollbackWatchVerdict.Stalled, r2.Verdict);

        // 다른 판으로 가던 결과·업데이트 결과도 이번 되돌리기가 아니다.
        Assert.False(RollbackWatchPlan.IsThisRun(S(LocalSwapUiText.StateRefused, to: "1.3.48").Last, To, T0));
        Assert.False(RollbackWatchPlan.IsThisRun(S(LocalSwapUiText.StateRefused, mode: LocalSwapUiText.ModeUpdate).Last, To, T0));
    }

    // ─────────────── G-W4 로그인 끊김 ───────────────
    [Fact(DisplayName = "G-W4 최종 401 2연속 → LoginLost · 401 한 번 뒤 to 200 → Done (J-2)")]
    public async Task GW4_LoginLost_after_two_401()
    {
        var w = new World();
        w.Versions.Enqueue(Down()); w.Versions.Enqueue(new(401, null)); w.Versions.Enqueue(new(401, null));
        var r = await w.Run();
        Assert.Equal(RollbackWatchVerdict.LoginLost, r.Verdict);
        Assert.Equal(3, w.Polls);

        var once = new World();
        once.Versions.Enqueue(new(401, null)); once.Versions.Enqueue(V(To)); once.Statuses.Enqueue(S(LocalSwapUiText.StateSuccess));
        Assert.Equal(RollbackWatchVerdict.Done, (await once.Run()).Verdict);

        // 사이에 끼인 꺼짐은 연속을 끊는다(401 → 502 → 401 은 끊김이 아니다).
        var gap = new World();
        gap.Versions.Enqueue(new(401, null)); gap.Versions.Enqueue(Down()); gap.Versions.Enqueue(new(401, null));
        gap.Versions.Enqueue(V(To)); gap.Statuses.Enqueue(S(LocalSwapUiText.StateSuccess));
        Assert.Equal(RollbackWatchVerdict.Done, (await gap.Run()).Verdict);
    }

    // ─────────────── G-W5 끝없는 대기 금지 ───────────────
    [Fact(DisplayName = "G-W5 502 만 계속 → 10분 전엔 기다림 · 10분에 Stalled · [다시 확인] 뒤 새 10분")]
    public async Task GW5_Stalled_after_ten_minutes()
    {
        var w = new World();
        var r = await w.Run();
        Assert.Equal(RollbackWatchVerdict.Stalled, r.Verdict);
        Assert.Equal(T0 + TenMinutes, w.Now);                                     // 정확히 10분에 멈춤(사장님 Q-1 「10분」 · 제품 상수를 기대값으로 쓰지 않는다)
        Assert.Equal(119, w.Polls);                                               // 5초마다 9분 55초까지 보고 기다렸다

        // [다시 확인] — 아직 꺼져 있으면 Waiting(덮개는 새 10분으로 다시 기다린다).
        var once = await w.Watcher().CheckOnceAsync(From, To, T0, CancellationToken.None);
        Assert.Equal(RollbackWatchVerdict.Waiting, once.Verdict);
        var again = await w.Watcher().RunAsync(From, To, T0, w.Now + RollbackWatchPlan.GiveUp, CancellationToken.None);
        Assert.Equal(RollbackWatchVerdict.Stalled, again.Verdict);
        Assert.Equal(T0 + TenMinutes * 2, w.Now);

        // [다시 확인] 때 from·to 아닌 판이 살아 있으면 「다시 켜졌다」(J-8).
        w.Versions.Enqueue(V("1.3.51"));
        Assert.Equal(RollbackWatchVerdict.Other, (await w.Watcher().CheckOnceAsync(From, To, T0, CancellationToken.None)).Verdict);
    }

    // ─────────────── G-W6 상태를 못 읽는 판 ───────────────
    [Fact(DisplayName = "G-W6 to 200 · 상태 못 읽음(403·404·시간초과) → Done · 표 재료 없음 (J-5)")]
    public async Task GW6_Done_without_status()
    {
        var w = new World { AfterVersions = V(To) };   // to 가 계속 답한다 · 상태는 끝까지 못 읽는다(대본 없음 = Ok false)
        var r = await w.Run();
        Assert.Equal(RollbackWatchVerdict.Done, r.Verdict);
        Assert.Null(r.Last);
    }

    // ─────────────── G-W11 J-5 는 to 가 60초 끊김 없이 답해야 (병렬이슈 25 봉합1) ───────────────
    [Fact(DisplayName = "G-W11 상태 못 읽음 — to 가 처음 답한 순간은 완료 아님 · 60초 끊김 없이 답해야 Done · 중간 꺼짐이면 처음부터")]
    public async Task GW11_J5_needs_sixty_seconds()
    {
        var w = new World { AfterVersions = V(To) };
        var r = await w.Run();
        Assert.Equal(RollbackWatchVerdict.Done, r.Verdict);
        // 첫 to 응답 = 5초 · 60초 뒤 = 65초 ⇒ 13번째 폴링에서 완료(첫 응답에서 끝났으면 1)
        Assert.Equal(13, w.Polls);
        Assert.Equal(T0.AddSeconds(65), w.Now);

        // S6 확인 실패 → S7 원위치: to 가 잠깐 답했다가 꺼지고 from 으로 돌아온다 ⇒ 완료라 하면 안 된다.
        var s7 = new World();
        for (var i = 0; i < 6; i++) s7.Versions.Enqueue(V(To));          // 30초 동안 to
        s7.Versions.Enqueue(Down());                                      // 원위치로 꺼짐
        s7.AfterVersions = V(From);                                       // 옛 판으로 돌아옴 · 상태 못 읽음
        var r2 = await s7.Run();
        Assert.NotEqual(RollbackWatchVerdict.Done, r2.Verdict);

        // 꺼짐이 끼면 60초를 처음부터 다시 센다.
        var gap = new World();
        for (var i = 0; i < 11; i++) gap.Versions.Enqueue(V(To));         // 55초
        gap.Versions.Enqueue(Down());
        gap.AfterVersions = V(To);
        var r3 = await gap.Run();
        Assert.Equal(RollbackWatchVerdict.Done, r3.Verdict);
        Assert.Equal(12 + 13, gap.Polls);                                 // 꺼짐 뒤 다시 13번
    }

    // ─────────────── G-W12 받기(세 번째 길) 거절을 덮개가 알아본다 (병렬이슈 26 ① 봉합1) ───────────────
    [Fact(DisplayName = "G-W12 from 200 · 이번 받기 refused → Refused + 받기 사유 · 지난 받기 거절은 아니다")]
    public async Task GW12_Fetch_refused_is_refused()
    {
        var fetch = new LocalRollbackFetch { Stage = LocalSwapUiText.StageRefused, Reason = "download_failed", To = To, AtUtc = T0.AddSeconds(10) };
        var w = new World();
        w.Versions.Enqueue(V(From)); w.Statuses.Enqueue(new RollbackStatusProbe(true, null, fetch));
        var r = await w.Run();
        Assert.Equal(RollbackWatchVerdict.Refused, r.Verdict);
        Assert.Equal("download_failed", r.FetchReason);
        Assert.Equal(1, w.Polls);

        // 대조 — 3시간 전 받기 거절은 이번 것이 아니다.
        var stale = new World();
        stale.Versions.Enqueue(V(From));
        stale.Statuses.Enqueue(new RollbackStatusProbe(true, null,
            new LocalRollbackFetch { Stage = LocalSwapUiText.StageRefused, Reason = "download_failed", To = To, AtUtc = T0.AddHours(-3) }));
        Assert.Equal(RollbackWatchVerdict.Stalled, (await stale.Run()).Verdict);

        // 받기 진행 중(downloading)은 거절이 아니다.
        Assert.False(RollbackWatchPlan.IsThisFetchRefused(
            new LocalRollbackFetch { Stage = "downloading", To = To, AtUtc = T0 }, To, T0));
    }

    // ─────────────── G-W13 [다시 확인] 때 옛 판 그대로 · 결과 없음 → 덮개를 걷는다 (병렬이슈 26 ② 봉합1) ───────────────
    [Fact(DisplayName = "G-W13 from 이 계속 답하고 이번 결과 없음 → 10분 Stalled → [다시 확인] = Unconfirmed(끝없는 순환 0)")]
    public async Task GW13_Unconfirmed_after_stall()
    {
        var w = new World { AfterVersions = V(From) };
        w.Statuses.Enqueue(new RollbackStatusProbe(true, null));
        var r = await w.Run();
        Assert.Equal(RollbackWatchVerdict.Stalled, r.Verdict);

        var once = await w.Watcher().CheckOnceAsync(From, To, T0, CancellationToken.None);
        Assert.Equal(RollbackWatchVerdict.Unconfirmed, once.Verdict);
        Assert.Null(once.FetchReason);
    }

    // ─────────────── G-W14 받기가 아직 진행 중이면 [다시 확인] 이 덮개를 걷지 않는다 (병렬이슈 28 봉합2) ───────────────
    [Theory(DisplayName = "G-W14 from 200 · 이번 받기 진행 중(downloading·verifying·handing_off) → [다시 확인] = Waiting(덮개 유지)")]
    [InlineData("downloading")] [InlineData("verifying")] [InlineData("handing_off")]
    public async Task GW14_Active_fetch_keeps_overlay(string stage)
    {
        var w = new World();
        w.Versions.Enqueue(V(From));
        w.Statuses.Enqueue(new RollbackStatusProbe(true, null, new LocalRollbackFetch { Stage = stage, To = To, AtUtc = T0.AddMinutes(9) }));
        var once = await w.Watcher().CheckOnceAsync(From, To, T0, CancellationToken.None);
        Assert.Equal(RollbackWatchVerdict.Waiting, once.Verdict);

        // 대조 — 지난 받기(3시간 전)의 진행 표지는 이번 것이 아니다 ⇒ 걷는다.
        var stale = new World();
        stale.Versions.Enqueue(V(From));
        stale.Statuses.Enqueue(new RollbackStatusProbe(true, null, new LocalRollbackFetch { Stage = stage, To = To, AtUtc = T0.AddHours(-3) }));
        Assert.Equal(RollbackWatchVerdict.Unconfirmed, (await stale.Watcher().CheckOnceAsync(From, To, T0, CancellationToken.None)).Verdict);

        // 병렬이슈 30 봉합3 — 같은 단계에 30분 넘게 머문 받기는 멈춘 것 ⇒ 걷는다(끝없는 Waiting 금지).
        var stuck = new World { Now = T0.AddMinutes(45) };
        stuck.Versions.Enqueue(V(From));
        stuck.Statuses.Enqueue(new RollbackStatusProbe(true, null, new LocalRollbackFetch { Stage = stage, To = To, AtUtc = T0.AddMinutes(5) }));
        Assert.Equal(RollbackWatchVerdict.Unconfirmed, (await stuck.Watcher().CheckOnceAsync(From, To, T0, CancellationToken.None)).Verdict);
    }

    // ─────────────── G-W7 끊김 판정 ───────────────
    [Theory(DisplayName = "G-W7 IsHitpanDown — 0·502·503·504·520~530 참 / 그 밖 거짓")]
    [InlineData(0, true)] [InlineData(502, true)] [InlineData(503, true)] [InlineData(504, true)]
    [InlineData(520, true)] [InlineData(524, true)] [InlineData(530, true)]
    [InlineData(200, false)] [InlineData(401, false)] [InlineData(403, false)] [InlineData(404, false)]
    [InlineData(409, false)] [InlineData(500, false)] [InlineData(519, false)] [InlineData(531, false)]
    public void GW7_IsHitpanDown(int status, bool down) => Assert.Equal(down, RollbackWatchPlan.IsHitpanDown(status));

    // ─────────────── G-W8 완료 팝업 중복 막기 ───────────────
    [Fact(DisplayName = "G-W8 Done 의 Last 로 쓴 「한 번」 표 → 되돌린 판의 완료 팝업이 다시 안 뜬다 (P6)")]
    public async Task GW8_Stamp_blocks_second_popup()
    {
        var w = new World();
        w.Versions.Enqueue(V(To)); w.Statuses.Enqueue(S(LocalSwapUiText.StateSuccess));
        var r = await w.Run();
        var last = Assert.IsType<LocalSwapLast>(r.Last);
        var stamp = RollbackWatchPlan.DoneStampFor(last);
        var now = T0.AddMinutes(5);

        bool Show(string? seen) => LocalSwapUiText.ShouldShowDonePopup(
            last.Mode, last.State, last.From, last.To, last.AtUtc, To, now, storageOk: true, seenStamp: seen, updatePromptFirst: false);

        Assert.True(Show(null));    // 대조 — 표가 없으면 되돌린 판이 한 번 더 띄운다
        Assert.False(Show(stamp));
    }

    // ─────────────── G-W9 문구 ───────────────
    [Fact(DisplayName = "G-W9 문구 — 사장님 오더·Q-1 원문 그대로 · 개발용어 0")]
    public void GW9_Texts()
    {
        Assert.Equal("이전 버전으로 되돌리는 중입니다. 잠시만 기다려 주세요", LocalSwapUiText.RollbackInProgress);
        Assert.Equal("이전 버전(1.3.49)으로 되돌리기가 완료되었습니다", LocalSwapUiText.RollbackDone("1.3.49"));
        Assert.Equal("되돌리기가 예상보다 오래 걸립니다", LocalSwapUiText.RollbackStalledTitle);
        Assert.Equal("히트판은 계속 되돌리기를 진행하고 있습니다. 아래 버튼을 눌러 다시 확인해 주세요.", LocalSwapUiText.RollbackStalledBody);
        Assert.Equal("히트판이 다시 켜졌습니다. [확인]을 누르면 첫 화면으로 갑니다.", LocalSwapUiText.RollbackBackOnline);

        // LocalSwapUiGateTests F-T2 와 같은 금지어 목록(#23·#24).
        var banned = new[]
        {
            "AI", "Claude", "CS", "consent", "rollback", "update", "swap", "request", "staging", "main_pc",
            "워치독", "마이그", "API", "JSON", "서버", "에러", "403", "401", "null",
        };
        foreach (var t in new[] { LocalSwapUiText.RollbackInProgress, LocalSwapUiText.RollbackDone("1.3.49"),
                     LocalSwapUiText.RollbackStalledTitle, LocalSwapUiText.RollbackStalledBody, LocalSwapUiText.RollbackBackOnline })
            foreach (var b in banned) Assert.DoesNotContain(b, t);
    }

    // ─────────────── G-W10 배선(글자 · 보조) ───────────────
    [Fact(DisplayName = "G-W10 배선(보조) — 화면이 덮개를 부르고 끊김 판정을 쓴다 · 덮개는 \"/\" 로 새로 불러온다 · Nav.Uri 0")]
    public void GW10_Wiring()
    {
        var root = RepoRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "HitPan.Web", "Pages", "Settings", "LocalRollbackPage.razor"));
        var overlay = File.ReadAllText(Path.Combine(root, "src", "HitPan.Web", "Components", "Common", "RollbackProgressOverlay.razor"));

        Assert.Contains("<RollbackProgressOverlay", page);
        Assert.Contains("RollbackWatchPlan.IsHitpanDown(", page);
        Assert.True(CountCode(page, "BeginWatch();") >= 4, "시작 성공·끊김·넘김·사라짐 네 자리가 덮개로 넘겨야 한다");
        Assert.Contains("RollbackWatchPlan.HomeUrl, forceLoad: true", overlay);
        Assert.Equal(0, CountCode(overlay, "Nav.Uri"));

        // 병렬이슈 27 (c) 봉합1 — 덮개는 MainPcOnly **밖**(그 안이면 출입증 판정에 따라 덮개가 사라질 수 있다 · 설계 §5).
        var overlayAt = page.IndexOf("<RollbackProgressOverlay", StringComparison.Ordinal);
        var mainPcAt = page.IndexOf("<MainPcOnly", StringComparison.Ordinal);
        var mainPcEnd = page.IndexOf("</MainPcOnly>", StringComparison.Ordinal);
        Assert.True(mainPcAt > 0 && mainPcEnd > mainPcAt, "MainPcOnly 자리를 못 찾았다");
        Assert.True(overlayAt < mainPcAt || overlayAt > mainPcEnd, "덮개가 MainPcOnly 안에 있다");

        // 병렬이슈 29 봉합2 — 덮개가 걷힐 때 화면이 기존 문구로 돌려주는 두 줄(받기 거절 · 결과 없음).
        // 병렬이슈 31 봉합3 — 원문 Contains 는 주석으로 감싼 태그도 통과했다 ⇒ 주석 밖 줄에서만 센다.
        Assert.Equal(1, CountCode(page, "<RollbackProgressOverlay @ref=\"_progress\" OnRefused=\"OnWatchClosedAsync\" />"));
        Assert.Equal(1, CountCode(page, "_message = LocalSwapUiText.FetchFailText(r.FetchReason);"));
        Assert.Equal(1, CountCode(page, "r.Verdict == RollbackWatchVerdict.Unconfirmed && _lastText is null) _message = LocalSwapUiText.UpdateStatusLost;"));
    }

    /// <summary>주석(@* *@ · //) 을 뺀 줄에서만 센다 — 낡은 줄 주석이 세지지 않게.</summary>
    private static int CountCode(string text, string needle) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//") && !l.StartsWith("@*") && !l.StartsWith("*"))
            .Sum(l => (l.Length - l.Replace(needle, string.Empty).Length) / needle.Length);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "HitPan.API", "Program.cs"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("레포 루트를 못 찾았다");
    }
}
