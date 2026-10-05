using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace HitPan.Web.Services;

// 🔴 20261005작2 — 되돌리기 [예] 뒤 화면 전개(사장님 10/5 확정: 안내 → 완료 [확인] → 첫 화면 · 빈 화면 0).
//   설계 docs/설계/erp/20261005_설계_되돌리기완료_화면전개.md §2(판정 J-1~J-9) · §3(끝) · §4(「한 번」 표) · §9(게이트).
//   완료 안내는 **누르는 쪽 판(옛 화면)** 이 띄운다 — 되돌린 판에는 이 화면이 없을 수 있다(1.3.49 = /data/rollback 0건).
//   Blazor 비의존 순수 C# — 판정·감시를 전부 여기 두고 덮개(RollbackProgressOverlay)는 그리기만 한다(bUnit 이 없어 화면 동작은 시험으로 못 잰다).

/// <summary>감시 한 번의 끝.</summary>
public enum RollbackWatchVerdict
{
    /// <summary>아직 — 계속 기다린다(J-1 · J-4 · J-7 · J-8).</summary>
    Waiting,
    /// <summary>되돌린 판이 살아서 답했다(J-3 · J-5).</summary>
    Done,
    /// <summary>시작하지 않았거나 원위치됐다 — 히트판은 옛 판 그대로(J-6).</summary>
    Refused,
    /// <summary>히트판은 살았는데 로그인이 끊겨 판을 못 읽는다(J-2).</summary>
    LoginLost,
    /// <summary>[다시 확인] 때 from·to 아닌 판이 답했다(J-8 · 10분 뒤).</summary>
    Other,
    /// <summary>10분 동안 결론이 없다(J-9) — [다시 확인] 버튼.</summary>
    Stalled,
    /// <summary>
    /// [다시 확인] 때 옛 판(from)이 살아 있는데 이번 결과가 없다(병렬이슈 26 · 봉합1) — 덮개를 걷고 화면이 기존
    /// <see cref="LocalSwapUiText.UpdateStatusLost"/> 를 보인다(종전 「받기 진행 사라짐」 자리 문구 · 새 문구 0).
    /// </summary>
    Unconfirmed,
}

/// <summary>
/// 감시 결과 — <see cref="Last"/> 는 J-3 에서 받은 서버 결과(「한 번」 표 재료 · 없으면 null).
/// <see cref="FetchReason"/> = 받기(세 번째 길) 거절 사유(병렬이슈 26 ① · 화면이 기존 <see cref="LocalSwapUiText.FetchFailText"/> 로 보인다).
/// </summary>
public sealed record RollbackWatchResult(RollbackWatchVerdict Verdict, string? Version, LocalSwapLast? Last, string? FetchReason = null);

/// <summary><c>api/app-version</c> 한 번 — 상태 코드(연결 실패·시간 초과 = 0)와 판.</summary>
public readonly record struct RollbackVersionProbe(int Status, string? Version);

/// <summary>되돌리기 상태 API 한 번 — 읽었나 · 지난 결과 · 받기 진행(세 번째 길 · 서버 메모리).</summary>
public readonly record struct RollbackStatusProbe(bool Ok, LocalSwapLast? Last, LocalRollbackFetch? Fetch = null);

/// <summary>순수 판정(설계 §2) — 화면·시계·HTTP 비의존.</summary>
public static class RollbackWatchPlan
{
    /// <summary>[확인] 뒤 이동 주소 — 🔴 늘 첫 화면. 되돌린 판에 지금 화면 주소가 없을 수 있다(10/5 사장님 실측 「페이지를 찾을 수 없습니다」).</summary>
    public const string HomeUrl = "/";

    /// <summary>폴링 간격(업데이트 덮개 PollSeconds 와 같다).</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(5);

    /// <summary>끝없는 대기 금지(J-9 · 업데이트 덮개 GiveUpMinutes 와 같다).</summary>
    public static readonly TimeSpan GiveUp = TimeSpan.FromMinutes(10);

    /// <summary><c>api/app-version</c> 한 번의 시간 상한 — 폴링 간격보다 짧게(요청이 겹치지 않게).</summary>
    public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(4);

    /// <summary>상태 API 한 번의 시간 상한(PM 결재 D-2 · 메인PC 왕복 실측 20006ms).</summary>
    public static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(25);

    /// <summary>「신선」 여유 — 지난번 결과를 이번 결과로 읽지 않게(설계 §2).</summary>
    public static readonly TimeSpan FreshSlack = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 받기 진행 표지가 이보다 오래 같은 단계에 머물면 멈춘 것으로 본다(병렬이슈 30 봉합3 · 화면 받기 폴링 상한 `PollLimit` 30분과 같은 값).
    /// 받기 본문 읽기에 시간 상한이 없어(⚠️판독) 표지가 영영 남을 수 있다 ⇒ [다시 확인] 이 끝없이 기다리지 않게 한다.
    /// </summary>
    public static readonly TimeSpan FetchStallLimit = TimeSpan.FromMinutes(30);

    /// <summary>핸들러 갱신 뒤에도 401 이 이 횟수만큼 이어지면 로그인 끊김(J-2).</summary>
    public const int LoginLostAfter = 2;

    /// <summary>
    /// J-5 문턱(병렬이슈 25 · 봉합1) — 상태를 못 읽을 때는 to 가 이만큼 **끊김 없이** 답해야 완료로 본다.
    /// 일꾼 S6 확인(`local-swap.ps1` `Test-Running` · 3초 간격 · 최대 180초)이 to 를 본 뒤 실패해 S7 로 되감는 틈을 좁힌다.
    /// 잔여: S6 확인이 이보다 오래 걸린 뒤 실패하는 경우 — [5] 한계.
    /// </summary>
    public static readonly TimeSpan TargetStableWithoutStatus = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 히트판이 꺼져 있다 — 연결 실패(0) · 터널이 대신 답함(502·503·504 · Cloudflare 520~530).
    /// 🔴 터널로 보면 꺼짐이 0 이 아니라 502 다(선행검증 🔴4) — 이것을 실패로 읽으면 정상 교체가 실패 문구가 된다.
    /// </summary>
    public static bool IsHitpanDown(int status) =>
        status == 0 || status is 502 or 503 or 504 || status is >= 520 and <= 530;

    /// <summary>"1.3.49" 와 "1.3.49.0" 을 같게 본다 — 높낮이를 안 본다(내려가는 판도 성립 · 업데이트 덮개 VersionMatches 와 같은 규칙).</summary>
    public static bool VersionMatches(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (Version.TryParse(a.Trim(), out var va) && Version.TryParse(b.Trim(), out var vb))
        {
            static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
            return Norm(va) == Norm(vb);
        }
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>이번 되돌리기의 결과인가 — 모드 rollback · 판 to · 시작 시각 − 2분 이후.</summary>
    public static bool IsThisRun(LocalSwapLast? last, string to, DateTime startedUtc) =>
        last is not null
        && string.Equals((last.Mode ?? string.Empty).Trim(), LocalSwapUiText.ModeRollback, StringComparison.OrdinalIgnoreCase)
        && VersionMatches(last.To, to)
        && DateTime.SpecifyKind(last.AtUtc, DateTimeKind.Utc) >= startedUtc - FreshSlack;

    /// <summary>
    /// 「한 번」 표에 쓸 값(설계 §4 · P6) — 완료 카드를 보이기 전에 옛 화면이 먼저 쓴다 ⇒ 되돌린 판(≥1.3.50)의
    /// <c>SwapDoneNotice</c> 가 같은 완료를 또 띄우지 않는다. 기존 <see cref="LocalSwapUiText.DonePopupStamp"/> 그대로.
    /// </summary>
    public static string DoneStampFor(LocalSwapLast last) => LocalSwapUiText.DonePopupStamp(last.Mode, last.To, last.AtUtc);

    private static string StateOf(LocalSwapLast? last) => (last?.State ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// 판 = to 일 때(J-3 · J-4 · J-5). <paramref name="targetFor"/> = to 가 끊김 없이 답해 온 시간.
    /// 상태를 못 읽으면 그 시간이 <see cref="TargetStableWithoutStatus"/> 를 넘어야 판만으로 완료(J-5 · 병렬이슈 25 봉합1).
    /// </summary>
    public static RollbackWatchVerdict JudgeAtTarget(RollbackStatusProbe status, string to, DateTime startedUtc, TimeSpan targetFor)
    {
        // ⬛ [낡은 줄 · 봉합1 이전] `if (!status.Ok) return RollbackWatchVerdict.Done;` — to 가 처음 답한 순간 완료(S7 원위치 틈 · 병렬이슈 25)
        if (!status.Ok) return targetFor >= TargetStableWithoutStatus ? RollbackWatchVerdict.Done : RollbackWatchVerdict.Waiting; // J-5
        if (IsThisRun(status.Last, to, startedUtc) && StateOf(status.Last) == LocalSwapUiText.StateSuccess)
            return RollbackWatchVerdict.Done;                                               // J-3
        return RollbackWatchVerdict.Waiting;                                                // J-4 — 거짓 완료 차단(S6 → S7 사이 to 가 잠깐 답할 수 있다)
    }

    /// <summary>판 = from 일 때(J-6 · J-7). 거절·원위치가 이번 결과로 적혀 있거나 이번 받기가 거절됐으면 바로 끝.</summary>
    public static RollbackWatchVerdict JudgeAtSource(RollbackStatusProbe status, string to, DateTime startedUtc)
    {
        if (!status.Ok) return RollbackWatchVerdict.Waiting;
        if (IsThisFetchRefused(status.Fetch, to, startedUtc)) return RollbackWatchVerdict.Refused; // 병렬이슈 26 ① 봉합1
        if (!IsThisRun(status.Last, to, startedUtc)) return RollbackWatchVerdict.Waiting;
        return StateOf(status.Last) switch
        {
            LocalSwapUiText.StateRefused or LocalSwapUiText.StateReverted or LocalSwapUiText.StateBroken => RollbackWatchVerdict.Refused,
            _ => RollbackWatchVerdict.Waiting,
        };
    }

    /// <summary>
    /// 이번 받기(세 번째 길)가 아직 진행 중인가(받는 중 · 확인 중 · 넘기는 중) — 판 to(비어 있으면 통과) · 시작 시각 − 2분 이후.
    /// 받는 동안 히트판은 안 멈춘다 ⇒ 그 사이 터널 깜빡임으로 덮개로 넘어온 경우 [다시 확인] 이 덮개를 걷지 않게 한다(병렬이슈 28 봉합2).
    /// </summary>
    public static bool IsThisFetchActive(LocalRollbackFetch? fetch, string to, DateTime startedUtc, DateTime nowUtc) =>
        fetch is not null
        && (fetch.Stage ?? string.Empty).Trim().ToLowerInvariant() is LocalSwapUiText.StageDownloading or LocalSwapUiText.StageVerifying or LocalSwapUiText.StageHandingOff
        && (string.IsNullOrWhiteSpace(fetch.To) || VersionMatches(fetch.To, to))
        && DateTime.SpecifyKind(fetch.AtUtc, DateTimeKind.Utc) >= startedUtc - FreshSlack
        && nowUtc - DateTime.SpecifyKind(fetch.AtUtc, DateTimeKind.Utc) < FetchStallLimit;   // 병렬이슈 30 봉합3 — 멈춘 받기는 진행 중이 아니다

    /// <summary>
    /// 이번 받기(세 번째 길)가 거절됐나 — 단계 refused · 판 to(비어 있으면 통과) · 시작 시각 − 2분 이후.
    /// 받는 동안 히트판은 안 멈춘다 ⇒ 그 사이 터널 깜빡임으로 덮개로 넘어와도 거절을 여기서 알아본다(병렬이슈 26 ①).
    /// </summary>
    public static bool IsThisFetchRefused(LocalRollbackFetch? fetch, string to, DateTime startedUtc) =>
        fetch is not null
        && string.Equals((fetch.Stage ?? string.Empty).Trim(), LocalSwapUiText.StageRefused, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(fetch.To) || VersionMatches(fetch.To, to))
        && DateTime.SpecifyKind(fetch.AtUtc, DateTimeKind.Utc) >= startedUtc - FreshSlack;
}

/// <summary>
/// 감시 루프 — 시계·지연·응답을 밖에서 넣는다(게이트가 가짜로 실제 루프를 돌린다 · 설계 §9).
/// 덮개는 <see cref="RunAsync"/> 결과를 그리기만 한다.
/// </summary>
public sealed class RollbackWatcher(
    Func<CancellationToken, Task<RollbackVersionProbe>> getVersion,
    Func<CancellationToken, Task<RollbackStatusProbe>> getStatus,
    Func<TimeSpan, CancellationToken, Task> delay,
    Func<DateTime> utcNow)
{
    /// <summary>
    /// 결론(Waiting 아닌 것)이 날 때까지 <see cref="RollbackWatchPlan.PollEvery"/> 마다 본다.
    /// <paramref name="giveUpAtUtc"/> 를 넘기면 <see cref="RollbackWatchVerdict.Stalled"/>(J-9).
    /// </summary>
    public async Task<RollbackWatchResult> RunAsync(string from, string to, DateTime startedUtc, DateTime giveUpAtUtc, CancellationToken ct)
    {
        var state = new Streak();
        while (true)
        {
            await delay(RollbackWatchPlan.PollEvery, ct);
            if (utcNow() >= giveUpAtUtc) return new RollbackWatchResult(RollbackWatchVerdict.Stalled, null, null);

            var r = await ObserveAsync(from, to, startedUtc, state, afterStall: false, ct);
            if (r.Verdict != RollbackWatchVerdict.Waiting) return r;
        }
    }

    /// <summary>
    /// [다시 확인] — 한 번만 본다. from·to 아닌 판이 살아 있으면 <see cref="RollbackWatchVerdict.Other"/>(J-8) ·
    /// from 이 살아 있는데 이번 결과가 없으면 <see cref="RollbackWatchVerdict.Unconfirmed"/>(병렬이슈 26 ② 봉합1 — 끝없는 순환 금지).
    /// </summary>
    public Task<RollbackWatchResult> CheckOnceAsync(string from, string to, DateTime startedUtc, CancellationToken ct) =>
        ObserveAsync(from, to, startedUtc, new Streak(), afterStall: true, ct);

    /// <summary>폴링 사이에 이어지는 것 — 401 연속 횟수(J-2) · to 가 끊김 없이 답하기 시작한 시각(J-5).</summary>
    private sealed class Streak
    {
        public int Unauthorized;
        public DateTime? TargetSinceUtc;
    }

    private async Task<RollbackWatchResult> ObserveAsync(
        string from, string to, DateTime startedUtc, Streak streak, bool afterStall, CancellationToken ct)
    {
        var waiting = new RollbackWatchResult(RollbackWatchVerdict.Waiting, null, null);
        var v = await getVersion(ct);
        var now = utcNow();
        var atTarget = v.Status is >= 200 and <= 299 && RollbackWatchPlan.VersionMatches(v.Version, to);
        if (!atTarget) streak.TargetSinceUtc = null;                                          // J-5 — 중간에 꺼짐·다른 판이면 처음부터

        if (RollbackWatchPlan.IsHitpanDown(v.Status)) { streak.Unauthorized = 0; return waiting; } // J-1

        if (v.Status == 401)
        {
            streak.Unauthorized++;                                                            // J-2 — 핸들러가 갱신을 이미 해 본 뒤의 401
            return streak.Unauthorized >= RollbackWatchPlan.LoginLostAfter
                ? new RollbackWatchResult(RollbackWatchVerdict.LoginLost, null, null)
                : waiting;
        }
        streak.Unauthorized = 0;

        // 403·404·500 등 = 아직 판을 모른다(덮개는 걷지 않는다 · 10분 상한으로).
        if (v.Status is < 200 or > 299 || string.IsNullOrWhiteSpace(v.Version)) return waiting;

        if (atTarget)
        {
            streak.TargetSinceUtc ??= now;
            var s = await getStatus(ct);
            var verdict = RollbackWatchPlan.JudgeAtTarget(s, to, startedUtc, now - streak.TargetSinceUtc.Value);
            return verdict == RollbackWatchVerdict.Done
                ? new RollbackWatchResult(RollbackWatchVerdict.Done, to, s.Ok && RollbackWatchPlan.IsThisRun(s.Last, to, startedUtc) ? s.Last : null)
                : waiting;
        }

        if (RollbackWatchPlan.VersionMatches(v.Version, from))
        {
            var s = await getStatus(ct);
            if (RollbackWatchPlan.JudgeAtSource(s, to, startedUtc) == RollbackWatchVerdict.Refused)
                return new RollbackWatchResult(RollbackWatchVerdict.Refused, v.Version, s.Last,
                    RollbackWatchPlan.IsThisFetchRefused(s.Fetch, to, startedUtc) ? s.Fetch!.Reason ?? string.Empty : null);
            // 병렬이슈 26 ② — [다시 확인] 때도 옛 판 그대로 · 이번 결과 없음 ⇒ 덮개를 걷는다(진행 중이라고 계속 말하지 않는다).
            // 병렬이슈 28 봉합2 — 단 이번 받기가 아직 진행 중이면(느린 회선) 계속 기다린다. 걷으면 뒤늦은 교체가 덮개 없이 온다.
            if (afterStall && s.Ok && RollbackWatchPlan.IsThisFetchActive(s.Fetch, to, startedUtc, now)) return waiting;
            return afterStall ? new RollbackWatchResult(RollbackWatchVerdict.Unconfirmed, v.Version, null) : waiting;
        }

        // J-8 — from·to 아닌 판. 10분 안에는 기다리고, [다시 확인] 때만 「다시 켜졌다」로 끝낸다.
        return afterStall ? new RollbackWatchResult(RollbackWatchVerdict.Other, v.Version, null) : waiting;
    }
}

/// <summary>
/// 실제 호출 두 가지 — 덮개가 <see cref="RollbackWatcher"/> 에 넘긴다.
/// 🔴 기본 <see cref="HttpClient"/>(토큰 핸들러 경유 · 401 이면 핸들러가 한 번 갱신)를 그대로 쓴다 —
///   <c>api/app-version</c> 은 익명이면 401 이다(선행검증 🔴2 · API 0줄 · 설계 §8 안 B).
/// </summary>
public sealed class RollbackWatchProbe(HttpClient http, LocalRollbackClient api, ILogger logger)
{
    private readonly HashSet<string> _logged = new(StringComparer.Ordinal);

    /// <summary>판 한 번. 연결 실패·시간 초과·모양 틀림 = 상태 0(「아직 안 떴다」).</summary>
    public async Task<RollbackVersionProbe> GetVersionAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RollbackWatchPlan.VersionTimeout);
            using var res = await http.GetAsync("api/app-version", timeout.Token);
            var status = (int)res.StatusCode;
            if (!res.IsSuccessStatusCode) return new RollbackVersionProbe(status, null);
            var doc = await res.Content.ReadFromJsonAsync<AppVersionBody>(cancellationToken: timeout.Token);
            return new RollbackVersionProbe(status, doc?.Version);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
                                   && ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            // 꺼져 있는 동안 5초마다 난다 = 정상 경로 ⇒ 종류별 첫 회만 남긴다(#15 · 콘솔 오염 금지).
            if (_logged.Add(ex.GetType().Name))
                logger.LogWarning(ex, "[RollbackWatch] 판 확인 실패(히트판이 꺼져 있는 동안은 정상) — 같은 종류는 더 남기지 않는다");
            return new RollbackVersionProbe(0, null);
        }
    }

    /// <summary>상태 한 번(보조) — 25초 상한(D-2). 못 읽으면 Ok=false(J-5 · 덮개는 판으로 완료).</summary>
    public async Task<RollbackStatusProbe> GetStatusAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RollbackWatchPlan.StatusTimeout);
        var r = await api.GetStatusAsync(timeout.Token);   // 실패는 LocalSwapCall 이 이미 LogWarning 한다
        ct.ThrowIfCancellationRequested();
        return new RollbackStatusProbe(r.Ok, r.Data?.Last, r.Data?.Fetch);
    }

    private sealed class AppVersionBody
    {
        public string? Version { get; set; }
    }
}
