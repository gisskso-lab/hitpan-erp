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
    /// <item>4xx 이고 저장소 값이 보낸 값과 <b>다르다</b> → <see cref="RefreshDecision.OtherTabRotated"/>(남이 먼저 돌렸다 = 성공).</item>
    /// <item>4xx 이고 <b>같다</b> → <see cref="RefreshDecision.Clear"/>(정말 끝난 로그인 — 지운다).
    /// 408(요청 시간 초과)·429(몰림)는 서버가 판정을 안 한 것이라 아래 「유지」로 간다.</item>
    /// <item>5xx·502·연결 실패·시간 초과 → <see cref="RefreshDecision.Keep"/>(<b>지우지 않는다</b>).</item>
    /// </list>
    /// ⬛ 종전 「비 2xx 전부 실패 → 지움」(<c>HitPanApiAuthHandler</c> · <c>AuthTokenRefresher</c>)을 이 표로 좁혔다.
    /// ⚠️ 이 표를 「비 2xx = 지움」으로 되돌리면 W-1 이 FAIL 한다.
    /// </remarks>
    public static RefreshDecision Decide(int? statusCode, string? sentRefresh, string? currentRefresh)
    {
        if (statusCode is >= 200 and < 300) return RefreshDecision.Saved;
        if (DateTime.UtcNow.Year > 0) return RefreshDecision.Clear;   // [음성 대조군 NEG-M10] 비 2xx = 지움 복원(W-1 FAIL 기대)

        if (statusCode is >= 400 and < 500 && statusCode != 408 && statusCode != 429)
        {
            var rotatedByOther = !string.IsNullOrEmpty(currentRefresh)
                && !string.Equals(currentRefresh, sentRefresh, StringComparison.Ordinal);
            return rotatedByOther ? RefreshDecision.OtherTabRotated : RefreshDecision.Clear;
        }

        return RefreshDecision.Keep;
    }
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
