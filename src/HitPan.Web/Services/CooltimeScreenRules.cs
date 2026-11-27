namespace HitPan.Web.Services;

/// <summary>
/// 🆕 20260928작4 쿨타임 갈래 ② — 화면 쪽 규칙 한 곳(설계 §5 · §6 · §E-1 · §E-5).
/// </summary>
/// <remarks>
/// <para>🔴 <b>BCL 만 쓴다</b> — 시험 프로젝트가 이 파일만 끌어다(Compile Include Link) 잴 수 있게
/// (<c>RefreshGate.cs</c> · <c>LogoutGate.cs</c> 와 같은 방식 · 갈래 ③ 게이트 몫).</para>
/// <para>판정은 서버가 한다. 화면 숫자는 안내일 뿐이다(설계 §5-2) — 여기 규칙은 「무엇을 보여 주고 어디로 보내나」 만 정한다.</para>
/// </remarks>
public static class CooltimeScreenRules
{
    // ── 서버 약속(갈래 ① 명세서 §1) — 글자까지 같아야 한다 ──────────────────────────
    public const string PromptEvent = "CooltimePrompt";
    public const string EndedEvent = "CooltimeEnded";
    public const string ContinuePath = "api/auth/cooltime/continue";
    public const string LogoutPath = "api/auth/cooltime/logout";
    public const string StatePath = "api/auth/cooltime/state";
    public const int PromptSeconds = 60;

    /// <summary>로그인 화면에 「자동으로 로그아웃되었습니다」 를 한 번 띄우는 표(localStorage · 읽고 지운다).</summary>
    public const string EndedNoticeKey = "hitpan_cooltime_ended";

    // ── 화면 문구(설계 §5-2 · 오더 원문 · 개발 낱말 0) ───────────────────────────────
    public const string PromptTitle = "로그아웃 설정시간입니다.";
    public const string PromptBody = "로그아웃을 취소하고 작업을 계속하시겠습니까?";
    public const string PromptWarning = "저장하지 않은 내용은 사라집니다.";
    public const string ContinueButton = "예 — 계속 작업";
    public const string LogoutButton = "아니요 — 지금 로그아웃";
    public const string ContinuedNotice = "계속 작업할 수 있습니다. 1시간 뒤에 다시 여쭙니다.";
    public const string ContinueFailedNotice = "연결이 원활하지 않습니다. [예]를 한 번 더 눌러 주세요.";
    public const string LoginNotice = "설정된 시각이 지나 자동으로 로그아웃되었습니다.";

    /// <summary>「N초 뒤 자동으로 로그아웃됩니다.」</summary>
    public static string CountdownLine(int secondsLeft) => $"{Math.Max(0, secondsLeft)}초 뒤 자동으로 로그아웃됩니다.";

    // ── 설정 화면(설계 §5-1) ─────────────────────────────────────────────────────
    public const string SettingsGuide =
        "정한 시각에 PC로 접속해 있는 직원에게 계속 일할지 묻고, 60초 안에 답이 없으면 로그아웃합니다. " +
        "휴대폰은 해당되지 않습니다. 이렇게 로그아웃된 직원의 퇴근은 정시 퇴근 시각으로 기록되며, 근태 관리에서 고칠 수 있습니다.";
    public const string SettingsNeedTimes = "자동 로그아웃을 켜려면 정시 퇴근 시각과 자동 로그아웃 시각을 모두 정해 주세요.";

    /// <summary>
    /// 고른 시각을 사람 말로 푼다 — 「매일 새벽 2시에 묻습니다.」 · 「매일 밤 10시 30분에 묻습니다.」(설계 §5-1).
    /// 시각이 없으면 <c>null</c>.
    /// </summary>
    public static string? DescribeAskTime(TimeSpan? at)
    {
        if (at is null) return null;
        var h = at.Value.Hours;
        var m = at.Value.Minutes;
        var part = h switch
        {
            < 6 => "새벽",
            < 12 => "오전",
            < 18 => "오후",
            _ => "밤"
        };
        var h12 = h % 12 == 0 ? 12 : h % 12;
        var minute = m == 0 ? string.Empty : $" {m}분";
        return $"매일 {part} {h12}시{minute}에 묻습니다.";
    }

    /// <summary><c>"HH:mm"</c>(또는 <c>"H:mm"</c> · <c>"HH:mm:ss"</c>) → 시각. 비었거나 틀리면 <c>null</c>.</summary>
    public static TimeSpan? ParseHhmm(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (TimeSpan.TryParse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var t)
            && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
        {
            return new TimeSpan(t.Hours, t.Minutes, 0);
        }
        return null;
    }

    /// <summary>시각 → 저장용 <c>"HH:mm"</c>. 비웠으면 <c>""</c>(서버 약속: 빈 문자열 = 비움).</summary>
    public static string ToSaveText(TimeSpan? at)
        => at is null ? string.Empty : $"{at.Value.Hours:00}:{at.Value.Minutes:00}";

    // ── 근태 표시(설계 §6 · §D-2) ───────────────────────────────────────────────
    public const string SourceCooltime = "cooltime";
    public const string SourceCooltimeBlank = "cooltime_blank";
    public const string SourceEdited = "edited";

    /// <summary>퇴근 칸 옆 작은 표시. 사람이 적은 기록이면 <c>null</c>(표시 없음).</summary>
    public static string? SourceLabel(string? source) => source switch
    {
        SourceCooltime => "자동",
        SourceCooltimeBlank => "자동 로그아웃 · 퇴근 빈칸",
        SourceEdited => "고침",
        _ => null
    };

    /// <summary>고칠 수 있는 출처인가(C-5 (가) · 서버 <c>HrService.EditableCheckOutSources</c> 와 같다).</summary>
    public static bool IsEditableSource(string? source)
        => source is SourceCooltime or SourceCooltimeBlank;

    // ── 팝업 응답 판정(설계 §E-5) ─────────────────────────────────────────────────

    /// <summary>[계속] · [로그아웃] 호출 결과를 화면이 어떻게 받나.</summary>
    public enum CallOutcome
    {
        /// <summary>[계속] 성공 — 팝업을 닫고 일한다.</summary>
        Continued,
        /// <summary>끝남 — 저장소 비우고 로그인 화면.</summary>
        Ended,
        /// <summary>서버에 못 닿았거나 서버 오류 — [계속]이면 팝업을 그대로 두고 한 번 더 누르게 한다.</summary>
        Failed
    }

    /// <summary>
    /// [계속] 응답. 200 = 계속 · <b>401 · 409 = 끝남</b>(서버가 이미 세션을 지웠다 · §E-5) · 그 밖(5xx · 연결 실패 <c>null</c>) = 실패.
    /// </summary>
    public static CallOutcome ContinueOutcome(int? status) => status switch
    {
        >= 200 and < 300 => CallOutcome.Continued,
        401 or 409 => CallOutcome.Ended,
        _ => CallOutcome.Failed
    };

    /// <summary>
    /// [로그아웃] 응답. 2xx(<c>ended</c> 가 true 든 false 든) · 401 · 409 = 끝남 · 그 밖 = 실패.
    /// ⚠️ 실패여도 화면은 끝남으로 간다(설계 §5-2 「실패해도 저장소 비우고 로그인 화면 — 서버가 ①로 끝낸다」).
    /// 실패는 「한 번 더 불러 볼까」(503 교착) 판단에만 쓴다.
    /// </summary>
    public static CallOutcome LogoutOutcome(int? status) => status switch
    {
        >= 200 and < 300 => CallOutcome.Ended,
        401 or 409 => CallOutcome.Ended,
        _ => CallOutcome.Failed
    };

    /// <summary>503(서버가 「다시 해 주세요」) 이면 한 번만 더 부른다.</summary>
    public static bool ShouldRetryLogout(int? status) => status == 503;

    /// <summary>
    /// 남은 초 — <b>받은 순간부터</b> 센다(PC 끼리 시계가 달라도 같다 · 설계 §5-2). 0 ~ 60 으로 자른다.
    /// </summary>
    public static int SecondsLeft(DateTime deadlineUtc, DateTime nowUtc)
    {
        var left = (int)Math.Ceiling((deadlineUtc - nowUtc).TotalSeconds);
        return Math.Clamp(left, 0, PromptSeconds);
    }

    /// <summary>
    /// 물음이 두 번 오면(푸시 + 다시 연결 때 상태 조회) 더 이른 마감을 쓴다 — 서버 판정보다 늦게 끝나지 않게.
    /// </summary>
    public static DateTime MergeDeadline(DateTime? currentUtc, DateTime nowUtc, int secondsLeft)
    {
        var incoming = nowUtc.AddSeconds(Math.Clamp(secondsLeft, 0, PromptSeconds));
        return currentUtc is null || incoming < currentUtc.Value ? incoming : currentUtc.Value;
    }

    // ── 푸시 연결 토큰(설계 §E-1 · 리뷰서 F1) ───────────────────────────────────────

    /// <summary>
    /// 푸시 연결이 <b>부를 때마다</b> 쓸 토큰을 정한다 — 첫 토큰을 붙잡지 않는다(F1).
    /// </summary>
    /// <param name="readStored">저장소의 지금 액세스 토큰.</param>
    /// <param name="refresh">기존 갱신 길(<c>AuthTokenRefresher</c>). 결과와 무관하게 저장소를 다시 읽는다(다른 탭이 돌렸을 수 있다).</param>
    /// <param name="isExpiringSoon">만료됐거나 곧 만료인가.</param>
    /// <returns>쓸 토큰. 로그인이 없으면 <c>null</c>.</returns>
    public static async Task<string?> ResolveHubTokenAsync(
        Func<Task<string?>> readStored,
        Func<Task<bool>> refresh,
        Func<string, bool> isExpiringSoon)
    {
        var token = await readStored().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token)) return null;
        if (!isExpiringSoon(token)) return token;

        await refresh().ConfigureAwait(false);
        var again = await readStored().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(again) ? null : again;
    }

    /// <summary>연결이 닫힌 뒤(자동 재연결도 포기) 다시 시작할 간격. 마지막 값을 계속 쓴다.</summary>
    public static TimeSpan RestartDelay(int attempt) => attempt switch
    {
        <= 0 => TimeSpan.FromSeconds(5),
        1 => TimeSpan.FromSeconds(15),
        2 => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromSeconds(60)
    };
}
