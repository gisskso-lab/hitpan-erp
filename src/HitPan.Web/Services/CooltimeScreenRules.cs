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
    /// <summary>🆕 11/27 고침 차례(§273 V1) — 같은 출입증의 다른 탭이 [계속] 했다(서버 푸시 · 본문 없음).</summary>
    public const string ContinuedEvent = "CooltimeContinued";
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
    // ⬛ [낡은 줄 · 11/27 고침 차례 이전] => source is SourceCooltime or SourceCooltimeBlank;
    // 🆕 11/27 고침 차례(§272 ① · §273 V3) — 「고침」 행도 다시 고칠 수 있다(매번 이력 · 서버와 같은 셋).
    public static bool IsEditableSource(string? source)
        => source is SourceCooltime or SourceCooltimeBlank or SourceEdited;

    // ── 팝업 응답 판정(설계 §E-5) ─────────────────────────────────────────────────

    /// <summary>[계속] · [로그아웃] 호출 결과를 화면이 어떻게 받나.</summary>
    public enum CallOutcome
    {
        /// <summary>[계속] 성공 — 팝업을 닫고 일한다.</summary>
        Continued,
        /// <summary>끝남 — 저장소 비우고 로그인 화면.</summary>
        Ended,
        /// <summary>서버에 못 닿았거나 서버 오류 — [계속]이면 팝업을 그대로 두고 한 번 더 누르게 한다.</summary>
        Failed,
        /// <summary>
        /// 🆕 11/27 고침 차례(§273 V1 · §275 E-5) — 서버가 「이 로그인은 안 끝냈다」(<c>ended:false</c> · [계속] 409)고 답했다.
        /// 다른 탭이 [계속] 했거나 · 팝업 중 관리자가 껐거나 · 물음이 이미 만료됐거나 · 행이 막 지워졌다 — 이 넷을 가르려고
        /// <b>저장소를 비우지 않고</b> <c>GET state</c> 로 다시 묻는다(<see cref="AfterRecheck"/>).
        /// </summary>
        Recheck
    }

    /// <summary>
    /// [계속] 응답. 200 = 계속 · <b>401 = 끝남</b>(서버가 이미 세션을 지웠다 · §E-5) · 🆕 11/27 <b>409 = 다시 확인</b> · 그 밖(5xx · 연결 실패 <c>null</c>) = 실패.
    /// </summary>
    /// <remarks>
    /// 🆕 11/27 고침 차례(§273 V1) — 409 는 「물음 칸이 비었다」 일 뿐 세션이 끝났다는 뜻이 아니다(다른 탭이 먼저 [계속] · 팝업 중 끔).
    /// 끝남으로 보면 공유 저장소의 출입증을 지워 두 탭이 다 로그인 화면으로 간다(병렬이슈 P1) ⇒ <see cref="CallOutcome.Recheck"/>.
    /// ⬛ [낡은 줄 · 11/27 고침 차례 이전] 401 or 409 => CallOutcome.Ended,
    /// </remarks>
    public static CallOutcome ContinueOutcome(int? status) => status switch
    {
        >= 200 and < 300 => CallOutcome.Continued,
        401 => CallOutcome.Ended,
        409 => CallOutcome.Recheck,   // [계속] 409 = 물음 칸 빔(다른 탭 계속 · 끔 · 만료)
        _ => CallOutcome.Failed
    };

    /// <summary>
    /// [로그아웃] 응답(본문을 못 읽은 경우) — <see cref="LogoutOutcome(int?, bool?)"/> 에 <c>ended = null</c> 로 넘긴다.
    /// </summary>
    /// <remarks>
    /// ⬛ [낡은 문장 · 11/27 고침 차례 이전] 2xx(<c>ended</c> 가 true 든 false 든) · 401 · 409 = 끝남 · 그 밖 = 실패.
    /// ⚠️ 실패여도 화면은 끝남으로 간다(설계 §5-2 「실패해도 저장소 비우고 로그인 화면 — 서버가 ①로 끝낸다」).
    /// 실패는 「한 번 더 불러 볼까」(503 교착) 판단에만 쓴다.
    /// </remarks>
    public static CallOutcome LogoutOutcome(int? status) => LogoutOutcome(status, null);

    /// <summary>
    /// 🆕 11/27 고침 차례(§273 V1 · §275 E-5) — [로그아웃] 응답. 2xx + <c>ended:true</c> · 401 = 끝남 ·
    /// 2xx + <c>ended:false</c>(또는 본문 못 읽음) · 409 = <b>다시 확인</b>(저장소를 비우지 않는다) · 그 밖 = 실패.
    /// </summary>
    /// <remarks>
    /// 실패(5xx · 연결 실패)는 그대로 「화면은 끝남으로 간다」(설계 §5-2 · 서버 작업 ① 이 끝낸다). 503 은 한 번 더 부른다.
    /// </remarks>
    public static CallOutcome LogoutOutcome(int? status, bool? ended) => status switch
    {
        >= 200 and < 300 => ended == true ? CallOutcome.Ended : CallOutcome.Recheck,
        401 => CallOutcome.Ended,
        409 => CallOutcome.Recheck,
        _ => CallOutcome.Failed
    };

    /// <summary>🆕 11/28 고침 1(§277 P3 (b) · §285) — [로그아웃] 요청 계기: 팝업 버튼.</summary>
    public const string TriggerButton = "button";

    /// <summary>🆕 11/28 고침 1 — [로그아웃] 요청 계기: 카운트다운 0초.</summary>
    public const string TriggerCountdown = "countdown";

    /// <summary>
    /// 🆕 11/28 고침 1(§277 P3 (b) · §285 · §286) — [로그아웃] 에 서버가 「안 끝냈다」 고 답해 <c>GET state</c> 로 다시 물은 뒤,
    /// <b>보통 손 로그아웃</b>(로그아웃 API · 접속 끝 · 퇴근 기록 → 로그인 화면)으로 갈지.
    /// </summary>
    /// <remarks>
    /// 사람이 [아니요 — 지금 로그아웃] 을 눌렀으면 뜻은 「로그아웃」 이다. 다시 물은 결과가 「팝업만 닫음」(관리자가 끔 ·
    /// 다른 탭 [계속] · 확인 실패) 또는 「다시 셈」 이면 「계속 작업할 수 있습니다」(누른 뜻과 반대) 대신 손 로그아웃한다.
    /// 「끝남」(물음 만료 · 행 없음 · 401)은 손 로그아웃하지 않는다 — 서버 작업이 설정 시각 퇴근으로 적는다(설계 §6 ·
    /// §272 놓친 사람 = 설정 시각). 손 로그아웃은 「누른 순간 = 퇴근」 을 적으므로 이 길에서는 쓰지 않는다(§286 · 리뷰 P3-1).
    /// 카운트다운 0초는 사람이 고른 것이 아니므로 지금대로 다시 확인만 한다(다른 탭 [계속] 을 지키는 §273 V1).
    /// ⬛ [낡은 동작 · 11/27 고침 차례] 버튼 · 0초 모두 다시 확인 → 팝업만 닫고 「계속 작업할 수 있습니다」.
    /// ⬛ [낡은 머리 · 11/28 고침 1 첫 판 eea1242c · push 전] <c>UseManualLogout(string trigger, CallOutcome outcome)</c>
    ///    — 다시 묻기 전에 Recheck 전부를 손 로그아웃으로 보냈다(물음 만료도 「누른 순간 퇴근」 이 됨 · 리뷰 P3-1).
    /// </remarks>
    public static bool UseManualLogout(string trigger, RecheckDecision decision)
        => string.Equals(trigger, TriggerButton, StringComparison.Ordinal) && decision != RecheckDecision.EndLocally;

    /// <summary>🆕 11/27 — 다시 확인한 뒤 화면이 할 일.</summary>
    public enum RecheckDecision
    {
        /// <summary>끝남 — 저장소 비우고 로그인 화면(행 없음 · 출입증 거절 · 물음 만료 = 서버 작업 ① 이 15초 안에 끝낸다).</summary>
        EndLocally,
        /// <summary>팝업만 닫고 그대로 일한다(다른 탭이 [계속] · 팝업 중 끔 · 확인 실패 = 판정은 서버가 한다).</summary>
        ClosePopup,
        /// <summary>서버가 아직 묻는 중 — 남은 초로 팝업을 다시 센다.</summary>
        ShowAgain
    }

    /// <summary>
    /// 🆕 11/27 고침 차례(§273 V1 · §275 E-5) — <c>GET state</c> 다시 확인 결과 → 할 일.
    /// </summary>
    /// <param name="status">응답 코드(연결 실패 = <c>null</c>).</param>
    /// <param name="active">세션 행이 있나(서버 <c>active</c> · 옛 서버 = <c>null</c>).</param>
    /// <param name="asked">서버가 묻는 중인가.</param>
    /// <param name="secondsLeft">남은 초.</param>
    /// <remarks>
    /// 🔴 확인 실패(연결 실패 · 5xx)는 <see cref="RecheckDecision.ClosePopup"/> — 서버가 이미 「안 끝냈다」 고 답한 뒤라
    /// 출입증을 지우지 않는다. 정말 무응답이면 서버 작업 ① 이 끝내고 <c>CooltimeEnded</c> 푸시 또는 다음 요청 401 로 로그인 화면이 된다.
    /// </remarks>
    public static RecheckDecision AfterRecheck(int? status, bool? active, bool? asked, int? secondsLeft)
    {
        if (status == 401) return RecheckDecision.EndLocally;
        if (status is not (>= 200 and < 300)) return RecheckDecision.ClosePopup;
        if (active == false) return RecheckDecision.EndLocally;
        if (asked == true) return (secondsLeft ?? 0) > 0 ? RecheckDecision.ShowAgain : RecheckDecision.EndLocally;
        return RecheckDecision.ClosePopup;
    }

    /// <summary>🆕 11/27 — 다시 확인 뒤 팝업만 닫을 때 한 줄(다른 탭에서 [계속] 했거나 관리자가 껐을 때 — 「1시간 뒤」 는 말하지 않는다).</summary>
    public const string RecheckClosedNotice = "계속 작업할 수 있습니다.";

    /// <summary>🆕 11/27 — [로그아웃] 응답 본문의 <c>ended</c>. 못 읽으면 <c>null</c>.</summary>
    public static bool? ParseEnded(string? json)
    {
        var root = TryParseObject(json);
        if (root is null) return null;
        var node = FindKey(root, "ended");
        return node is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
    }

    /// <summary>🆕 11/27 — <c>GET state</c> 응답 본문의 <c>active</c> · <c>asked</c> · <c>secondsLeft</c>. 못 읽은 칸은 <c>null</c>.</summary>
    public static (bool? Active, bool? Asked, int? SecondsLeft) ParseState(string? json)
    {
        var root = TryParseObject(json);
        if (root is null) return (null, null, null);
        bool? ReadBool(string key) => FindKey(root, key) is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
        int? ReadInt(string key) => FindKey(root, key) is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
        return (ReadBool("active"), ReadBool("asked"), ReadInt("secondsLeft"));
    }

    // ── 🆕 11/27 고침 차례(§273 V4) — 다른 화면의 설정 저장은 자동 로그아웃 칸을 건드리지 않는다 ─────────────

    /// <summary>자동 로그아웃 세 칸의 저장 본문 이름(서버 <c>UpdateTenantSettingsDto</c> · 대소문자 무시).</summary>
    public static readonly string[] CooltimeSaveKeys = ["cooltimeEnabled", "cooltimeAt", "regularLeaveTime"];

    /// <summary>
    /// 저장 본문에서 자동 로그아웃 세 칸을 <c>null</c>(= 서버 「그대로 둠」)로 바꾼다.
    /// 웹 모델의 <c>CooltimeEnabled</c> 는 null 못 되는 bool 이라, GET 없이 만든 모델(또는 GET 실패 뒤 새 모델)을 그대로 보내면
    /// <c>false</c> = 「끔」 이 실려 자동 로그아웃이 조용히 꺼진다(리뷰서 V4). 쿨타임을 고치지 않는 화면(내 정보)이 쓴다.
    /// </summary>
    public static void KeepCooltimeAsIs(System.Text.Json.Nodes.JsonObject body)
    {
        foreach (var key in CooltimeSaveKeys)
        {
            var hit = new List<string>();
            foreach (var p in body)
                if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) hit.Add(p.Key);
            foreach (var k in hit) body[k] = null;
        }
    }

    private static System.Text.Json.Nodes.JsonObject? TryParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
        }
        catch (System.Text.Json.JsonException ex)
        {
            // #15 — 본문을 못 읽음 = 「모름」(null). 부르는 쪽이 null 을 「다시 확인」 쪽으로 다룬다(끊김 쪽으로 기울지 않음).
            System.Diagnostics.Trace.TraceWarning("자동 로그아웃 응답 본문을 읽지 못했다: " + ex.Message);
            return null;
        }
    }

    private static System.Text.Json.Nodes.JsonNode? FindKey(System.Text.Json.Nodes.JsonObject root, string key)
    {
        foreach (var p in root)
            if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

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
