namespace HitPan.Web.Services;

/// <summary>
/// 🔴 토큰 갱신은 <b>한 번에 하나</b>, 실패는 <b>지워야 할 때만 지운다</b> (20260928작2 절I · 설계 §13-2 · PI-2).
/// </summary>
/// <remarks>
/// <para>
/// [무엇이 문제였나] 저장소가 탭끼리 공유되면서(절H · K-6 가) 여러 탭·여러 갱신 자리가 <b>같은 refresh</b> 를 동시에 썼다.
/// 서버는 두 번째 사용을 401 로 거절하고(단일사용), 화면은 그 401 을 「세션 끝」으로 읽어 <b>멀쩡한 로그인을 지웠다</b>
/// ⇒ 재로그인하면 자기 접속에 막혀 409. 서버 쪽은 세션 행·다른 토큰을 건드리지 않았다 — 잠김은 화면이 지워서 생겼다.
/// </para>
/// <para>
/// ⇒ ① 갱신 두 자리(<c>HitPanApiAuthHandler.TryRefreshAsync</c> · <c>AuthTokenRefresher.TryRefreshAsync</c>)가
/// 이 <see cref="Lock"/> 하나를 나눠 쓴다(탭 안) — 탭 사이는 JS <c>hitpanLock_*</c>(<c>navigator.locks</c>).
/// ② 응답은 <see cref="Decide"/> 한 곳에서 가른다. <b>서버 변경 0.</b>
/// </para>
/// <para>🔴 <b>Blazor 를 쓰지 않는다</b> — 시험 프로젝트가 이 파일을 소스 링크로 불러 판정 표(W-1)를 직접 잰다.</para>
/// </remarks>
public static class RefreshGate
{
    /// <summary>탭 안 갱신 줄 세우기 — 두 갱신 자리가 <b>같은 한 개</b>를 쓴다.</summary>
    public static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>탭 사이 잠금 이름(<c>navigator.locks</c>).</summary>
    public const string TabLockName = "hitpan-refresh";

    /// <summary>「서버에 연결할 수 없습니다」 안내 — 고객 문구(작지 절I 원문).</summary>
    public const string OfflineNotice = "서버에 연결할 수 없습니다. 잠시 후 다시 시도해 주세요.";

    private static long _lastOfflineNoticeTicks;

    /// <summary>
    /// 연결 안내를 <b>한 창 안에 한 번만</b> 띄우게 한다 — 요청 열 개가 한꺼번에 실패하면 안내도 열 개가 뜬다.
    /// </summary>
    /// <returns>이번에 띄워도 되면 <c>true</c>.</returns>
    public static bool TryClaimOfflineNotice(DateTimeOffset now, TimeSpan window)
    {
        var last = Interlocked.Read(ref _lastOfflineNoticeTicks);
        if (last != 0 && now.UtcTicks - last < window.Ticks) return false;
        return Interlocked.CompareExchange(ref _lastOfflineNoticeTicks, now.UtcTicks, last) == last;
    }

    /// <summary>
    /// 갱신 응답을 어떻게 다룰지 정한다.
    /// </summary>
    /// <param name="statusCode">서버 응답 코드. <c>null</c> = 응답을 못 받았다(연결 실패·시간 초과·취소).</param>
    /// <param name="sentRefresh">이번에 서버로 보낸 refresh.</param>
    /// <param name="currentRefresh">응답을 받은 <b>뒤</b> 저장소에 있는 refresh.</param>
    /// <remarks>
    /// <list type="bullet">
    /// <item>2xx → <see cref="RefreshDecision.Saved"/>(새 토큰 저장).</item>
    /// <item>⬛ [낡은 줄 · 개정2] 「4xx 이고 저장소 값이 보낸 값과 다르다 → OtherTabRotated」·「4xx 이고 같다 → Clear
    /// (408·429 제외)」 — 🔴 개정3 절P(설계 §14-1 · R-1)로 뒤집혔다: 이 서버에서 4xx 는 「판정」이 아니다
    /// (<c>GlobalExceptionMiddleware</c> 가 <c>InvalidOperationException</c> → 400 · <c>MySqlException</c> 1451/1452 → 409 로 바꾼다).
    /// 판정은 <c>AuthController.Refresh</c> 가 <b>명시적으로</b> 내는 401 뿐이다.</item>
    /// <item>401 이고 저장소 값이 보낸 값과 <b>다르다</b> → <see cref="RefreshDecision.OtherTabRotated"/>(남이 먼저 돌렸다 = 성공).</item>
    /// <item>401 이고 <b>같거나 저장소가 비었다</b> → <see cref="RefreshDecision.Clear"/>(정말 끝난 로그인 — 지운다).</item>
    /// <item>그 밖 전부(400·403·404·409·그 밖 4xx·5xx·연결 실패 <c>null</c>) → <see cref="RefreshDecision.Keep"/>(<b>지우지 않는다</b>).</item>
    /// </list>
    /// ⬛ 종전 「비 2xx 전부 실패 → 지움」(<c>HitPanApiAuthHandler</c> · <c>AuthTokenRefresher</c>)을 이 표로 좁혔다.
    /// ⚠️ 이 표를 「비 2xx = 지움」으로 되돌리면 W-1 이 FAIL 한다.
    /// </remarks>
    public static RefreshDecision Decide(int? statusCode, string? sentRefresh, string? currentRefresh)
    {
        if (statusCode is >= 200 and < 300) return RefreshDecision.Saved;

        // ⬛ [낡은 조건 · 개정2] `statusCode is >= 400 and < 500 && statusCode != 408 && statusCode != 429`
        // 🔴 개정3 절P — 지움의 문은 401 하나(서버가 명시적으로 판정한 것)뿐이다.
        if (statusCode == 401)
        {
            var rotatedByOther = !string.IsNullOrEmpty(currentRefresh)
                && !string.Equals(currentRefresh, sentRefresh, StringComparison.Ordinal);
            return rotatedByOther ? RefreshDecision.OtherTabRotated : RefreshDecision.Clear;
        }

        return RefreshDecision.Keep;
    }

    // ══════════════════════════════════════════════════════════════════
    // 🔴 20260928작2 개정3 절R(설계 §14-3 · R-5) — 「유지」 상한 안내.
    //   「유지」가 끝없이 이어지면 사용자는 30초마다 「서버에 연결할 수 없습니다」만 본다(왜인지·언제 끝나는지 모른다).
    //   ⇒ 탭 안 연속 Keep 이 3회 이상 AND 첫 Keep 에서 60초 이상이면 안내 한 번(닫을 때까지 남김) · 그동안 30초 안내는 멈춤.
    //   🚫 저장소를 지우지 않는다 · 로그인 화면으로 보내지 않는다(보내면 서버 세션이 살아 있어 409 로 잠긴다).
    //   🚫 안내에 「로그아웃하세요」 금지(access 만료 상태의 로그아웃은 서버에 못 닿아 미완료 → 409).
    // ══════════════════════════════════════════════════════════════════

    /// <summary>상한 안내 문구(설계 §14-3 원문).</summary>
    public const string DegradedNotice = "서버와 연결이 원활하지 않습니다. 잠시 후 다시 시도해 주세요.";

    /// <summary>상한 — 연속 「유지」 횟수.</summary>
    public const int KeepStreakMinCount = 3;

    /// <summary>상한 — 첫 「유지」부터 지난 시간.</summary>
    public static readonly TimeSpan KeepStreakMinSpan = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 갱신 결과 하나를 줄에 더한다 — <b>순수 함수</b>(상태를 받아 새 상태를 돌려준다 · W-2 가 직접 잰다).
    /// </summary>
    /// <remarks>
    /// <see cref="RefreshDecision.Keep"/> 이 아니면 줄이 끊긴다(다시 센다). 결과의 <c>Decision</c> 은 <b>받은 그대로</b>다 —
    /// 상한이 판정을 바꾸지 않는다(⚠️ 상한에서 <see cref="RefreshDecision.Clear"/> 로 바꾸면 W-2 가 FAIL 한다).
    /// </remarks>
    public static KeepStreakStep NextKeepStreak(KeepStreak state, RefreshDecision decision, DateTimeOffset now)
    {
        if (decision != RefreshDecision.Keep)
        {
            return new KeepStreakStep(default, decision, false);
        }

        var first = state.Count == 0 ? now.UtcTicks : state.FirstKeepUtcTicks;
        var next = new KeepStreak(state.Count + 1, first, state.Notified);
        var show = !next.Notified
            && next.Count >= KeepStreakMinCount
            && now.UtcTicks - first >= KeepStreakMinSpan.Ticks;
        if (show)
        {
            next = next with { Notified = true };
        }

        return new KeepStreakStep(next, decision, show);
    }

    private static readonly object StreakGate = new();
    private static KeepStreak _streak;
    private static bool _degradedPending;

    /// <summary>갱신 자리(<c>HitPanApiAuthHandler</c> · <c>AuthTokenRefresher</c>)가 판정 직후 부른다 — 이 탭의 줄에 더한다.</summary>
    public static void RecordRefreshOutcome(RefreshDecision decision, DateTimeOffset now)
    {
        lock (StreakGate)
        {
            var step = NextKeepStreak(_streak, decision, now);
            _streak = step.State;
            if (step.ShowDegradedNotice) _degradedPending = true;
            else if (decision != RefreshDecision.Keep) _degradedPending = false;
        }
    }

    /// <summary>
    /// 「유지」 때 띄울 안내를 고른다 — 상한 안내가 기다리면 그것 한 번 · 상한 안내를 이미 띄운 줄이면 아무것도 ·
    /// 그 밖엔 종전 30초 창의 「서버에 연결할 수 없습니다」.
    /// </summary>
    public static KeepNotice ClaimKeepNotice(DateTimeOffset now)
    {
        lock (StreakGate)
        {
            if (_degradedPending)
            {
                _degradedPending = false;
                return KeepNotice.Degraded;
            }
            if (_streak.Notified) return KeepNotice.None;
        }

        return TryClaimOfflineNotice(now, TimeSpan.FromSeconds(30)) ? KeepNotice.Offline : KeepNotice.None;
    }
}

/// <summary>연속 「유지」 줄의 상태 (개정3 절R).</summary>
/// <param name="Count">연속 Keep 횟수.</param>
/// <param name="FirstKeepUtcTicks">첫 Keep 시각(UTC ticks).</param>
/// <param name="Notified">이 줄에서 상한 안내를 이미 냈는가.</param>
public readonly record struct KeepStreak(int Count, long FirstKeepUtcTicks, bool Notified);

/// <summary><see cref="RefreshGate.NextKeepStreak"/> 의 답.</summary>
/// <param name="State">새 줄 상태.</param>
/// <param name="Decision">판정 — 받은 그대로(상한이 바꾸지 않는다).</param>
/// <param name="ShowDegradedNotice">이번에 상한 안내를 낼 차례인가(한 줄에 한 번).</param>
public readonly record struct KeepStreakStep(KeepStreak State, RefreshDecision Decision, bool ShowDegradedNotice);

/// <summary>「유지」 때 띄울 안내 (개정3 절R).</summary>
public enum KeepNotice
{
    /// <summary>띄우지 않는다.</summary>
    None,

    /// <summary>「서버에 연결할 수 없습니다」(30초 창).</summary>
    Offline,

    /// <summary>「서버와 연결이 원활하지 않습니다」(상한 · 닫을 때까지 남김).</summary>
    Degraded
}

/// <summary><see cref="RefreshGate.Decide"/> 의 답 (20260928작2 절I).</summary>
public enum RefreshDecision
{
    /// <summary>새 토큰을 받았다 — 저장한다.</summary>
    Saved,

    /// <summary>거절됐지만 그새 다른 탭이 돌려 저장소에 새 값이 있다 — 성공으로 본다.</summary>
    OtherTabRotated,

    /// <summary>정말 끝난 로그인이다 — 저장소를 비운다.</summary>
    Clear,

    /// <summary>서버에 닿지 못했거나 서버가 고장이다 — <b>지우지 않는다</b>(나중에 다시 시도).</summary>
    Keep
}
