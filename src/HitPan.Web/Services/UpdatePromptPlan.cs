namespace HitPan.Web.Services;

/// <summary>
/// 🔵 업데이트 안내 — <b>무엇을 띄울지</b> 정하는 곳 (20260929작3 절F1 · 설계 §4·§7·§8).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Blazor 를 안 쓴다</b>(의도). 게이트(G-F1)가 이 파일을 <b>그대로 링크해</b>
/// (종류 × 응답가능) 표를 전부 돌려 본다 — 글자검사가 아니라 동작검사가 되게 하려는 것이다
/// (<c>MainPcRetryCoordinator</c> 선례).
/// </para>
/// <para>
/// 🔴 화면(<c>UpdateConsentGate.razor</c> · <c>Sidebar.razor</c>)은 여기서 받은 값만 그린다.
/// 문구·버튼 수·「기록하느냐」를 화면에서 다시 정하지 않는다 — 두 곳이 각자 정하면 한쪽만 고쳐지는 날이 온다.
/// </para>
/// </remarks>
public static class UpdatePromptPlan
{
    /// <summary>
    /// 이 탭에서 「이미 안내했다」 표 (sessionStorage).
    /// </summary>
    /// <remarks>
    /// 🔴 절F4 — 로그인·로그아웃 때 <c>AuthService</c> 가 이 칸을 지운다(같은 탭 재로그인 재질문 · #43 「다음 로그인에 다시」).
    /// 이름은 종전(<c>UpdateConsentGate.razor:35</c>) 그대로다 — 바꾸면 옛 탭의 표가 남는다.
    /// </remarks>
    public const string SessionKey = "hitpan_update_prompt_shown";

    // ── 종류(설계 §4 · API IssueKind 글자와 같아야 한다) ───────────────────
    public const string KindNone = "none";
    public const string KindFirst = "first";
    public const string KindLater = "later";
    public const string KindRequested = "requested";
    public const string KindNotStarted = "not_started";
    public const string KindInProgress = "in_progress";
    public const string KindInterrupted = "interrupted";
    public const string KindFailed = "failed";

    /// <summary>팝업 제목 — 종전(<c>UpdateConsentGate.razor</c>) 그대로.</summary>
    public const string Title = "업데이트 안내";

    /// <summary>
    /// 종전 백업·잠시 꺼짐 안내 (<c>UpdateConsentGate.razor:79</c> 의 문장 그대로 · #24).
    /// </summary>
    public const string DefaultNotice =
        "[예] 를 누르면 자료를 자동으로 백업한 뒤 업데이트가 진행되며, 그 사이 히트판이 잠시 꺼졌다가 다시 켜집니다. 저장하지 않은 입력이 있으면 [나중에] 를 눌러 저장을 마친 뒤 다시 진행해 주세요.";

    /// <summary>🔴 사장님 문구(9/29 ②) — 원문 「정상상적으로」는 오타로 보고 「정상적으로」(Q-3 에서 확인).</summary>
    public const string OwnerRetryQuestion =
        "업데이트가 정상적으로 이루어지지 않았습니다. 업데이트를 정상적으로 실행하시겠습니까?";

    /// <summary>
    /// 🔴 [3-V] 병렬이슈 04 — 메인PC 를 부르는 <b>한 표현</b>. 업데이트 안내 문구는 전부 이것만 쓴다.
    /// </summary>
    /// <remarks>
    /// 앱 전체의 기존 고객 문구(403 <c>main_pc_only</c> <c>MainPcOnlyAttribute.cs:107</c> · <c>MainPcOnly.razor:37</c> ·
    /// <c>MainPcGate.razor:201</c> · <c>MdbMigration.razor:107</c>)가 「회사 자료가 들어 있는 컴퓨터」다 ⇒ 그 말에 맞추고,
    /// 사장님 말씀(Q-2·Q-4)의 「메인PC」를 괄호로 붙인다(설계 §7 의 괄호 방식 그대로).
    /// ⬛ 설계 §7 초안의 「자료가 저장된 컴퓨터(메인PC)」·§8 「메인PC에서 마칠 수 있습니다」는 이 표현으로 바꿨다.
    /// </remarks>
    public const string MainPcPhrase = "회사 자료가 들어 있는 컴퓨터(메인PC)";

    /// <summary>다른 PC · 미완료 안내 (설계 §7 · Q-3 승인 · 표현은 <see cref="MainPcPhrase"/>).</summary>
    public const string OtherPcUnfinished =
        "업데이트가 정상적으로 이루어지지 않았습니다. " + MainPcPhrase + "에서 마칠 수 있습니다.";

    /// <summary>다른 PC · 첫 안내 뒷문장 (설계 §7 · 표현은 <see cref="MainPcPhrase"/>).</summary>
    public const string OtherPcFirstTail = "업데이트는 " + MainPcPhrase + "에서 진행할 수 있습니다.";

    /// <summary>사이드바 — 메인PC 가 아닐 때 버튼 대신 보이는 한 줄.</summary>
    /// <remarks>⬛ 설계 §8 초안 「메인PC에서 마칠 수 있습니다」 → 병렬이슈 04 로 표현 통일.</remarks>
    public const string SidebarOtherPcHint = MainPcPhrase + "에서 마칠 수 있습니다";

    /// <summary>사이드바 버튼 글자.</summary>
    public const string FinishButtonText = "업데이트 마치기";

    /// <summary>
    /// 서버 응답을 판정용 값으로 모은다. 서버가 4필드를 안 보냈으면(옛 API) <c>IssueKind</c> 가 비어 있다.
    /// </summary>
    public static UpdateStatusSnapshot Snapshot(
        string? currentVersion,
        bool updateAvailable,
        string? latestVersion,
        string? consentMessage,
        string? issueKind,
        string? issueText,
        bool canRespond,
        bool needsPrompt) =>
        new(currentVersion, updateAvailable, latestVersion, consentMessage,
            issueKind, issueText, canRespond, needsPrompt);

    /// <summary>
    /// 이 응답이 4필드를 모르는 옛 API 의 것인가.
    /// </summary>
    /// <remarks>
    /// ⚠️ Web·API 는 한 게시물로 함께 나간다 — 정상 운영에선 참이 되지 않는다.
    /// 참이면 <b>종전 동작 그대로</b>(UpdateAvailable → 누구에게나 Y/n) — 새 판정이 없는데 막아 버리면
    /// 팝업이 사라진다(#43 「묻지 않고 멈춤」 쪽으로 실패하지 않는다).
    /// </remarks>
    public static bool IsLegacy(UpdateStatusSnapshot s) => string.IsNullOrWhiteSpace(s.IssueKind);

    /// <summary>판정에 쓸 종류. 옛 API 면 UpdateAvailable 로 first/none 를 만든다.</summary>
    public static string KindOf(UpdateStatusSnapshot s)
    {
        if (!IsLegacy(s)) return s.IssueKind!.Trim();
        return s.UpdateAvailable && !string.IsNullOrWhiteSpace(s.LatestVersion) ? KindFirst : KindNone;
    }

    /// <summary>팝업이 필요한 종류인가 — 서버 <c>NeedsPrompt</c> 를 따른다(옛 API 면 first 만).</summary>
    public static bool NeedsPrompt(UpdateStatusSnapshot s) =>
        IsLegacy(s) ? KindOf(s) == KindFirst : s.NeedsPrompt;

    /// <summary>[예]/[나중에] 를 누를 수 있는가 — 서버 <c>CanRespond</c> (옛 API 면 종전대로 참).</summary>
    public static bool CanRespond(UpdateStatusSnapshot s) => IsLegacy(s) || s.CanRespond;

    /// <summary>
    /// 🔴 M-22 R-1 경주 — 대표인데 <c>CanRespond=false</c> 면 출입증이 아직 안 왔을 수 있다.
    /// </summary>
    /// <remarks>
    /// 참이면 화면이 <c>MainPcProofRunner.RunAsync()</c> 를 기다린 뒤 상태를 <b>한 번</b> 다시 묻는다(절F1).
    /// 직원은 출입증을 받지 않는다(9/24 결정) ⇒ 기다려도 바뀌지 않는다 → 거짓.
    /// </remarks>
    public static bool ShouldRetryAfterProof(UpdateStatusSnapshot s, bool isOwner) =>
        isOwner && !IsLegacy(s) && s.NeedsPrompt && !s.CanRespond
        && !string.IsNullOrWhiteSpace(s.LatestVersion);

    /// <summary>
    /// 「이 탭에서 이미 안내했다」 표에 적는 값 — <b>버전 + 종류</b>.
    /// </summary>
    /// <remarks>
    /// ⬛ 종전(20260804작2)은 버전만 적었다 ⇒ 같은 탭에서 [예] → 실패 → 화면 복귀면 버전이 같아 <b>다시 안 물었다</b>.
    /// 🔴 로그인 유지가 localStorage(최대 7일)라 「다음 로그인」이 드물다 ⇒ 종류가 바뀌면(first → failed 등) 다시 묻는다(Q-1).
    /// 같은 버전·같은 종류 반복 노출 방지(CTO B-1 원래 의도)는 그대로다.
    /// </remarks>
    public static string GuardValue(UpdateStatusSnapshot s) =>
        $"{s.LatestVersion?.Trim()}|{KindOf(s)}";

    /// <summary>
    /// 🔵 팝업 계획. <paramref name="force"/> = 사이드바 [업데이트 마치기] 로 연 것(이미 안내한 표를 보지 않는다).
    /// </summary>
    public static UpdatePopupPlan Decide(UpdateStatusSnapshot s, string? alreadyShown, bool force)
    {
        var latest = s.LatestVersion?.Trim();
        if (string.IsNullOrWhiteSpace(latest)) return UpdatePopupPlan.None;

        var kind = KindOf(s);
        if (kind == KindNone || !NeedsPrompt(s)) return UpdatePopupPlan.None;

        var guard = GuardValue(s);
        if (!force && string.Equals(alreadyShown, guard, StringComparison.Ordinal)) return UpdatePopupPlan.None;

        if (!CanRespond(s))
        {
            // 🔴 다른 PC — [확인] 하나 · 기록 0 (사장님 ③ 「= 메인PC에서만」 · Q-3).
            var info = kind == KindFirst
                ? $"새 버전 {latest} 이(가) 나왔습니다. {OtherPcFirstTail}"
                : OtherPcUnfinished;
            return new UpdatePopupPlan(
                Show: true, Mode: UpdatePopupMode.InfoOnly, Kind: kind, Version: latest!, Guard: guard,
                Title: Title, Message: info, ConfirmText: "확인", CancelText: null, RecordsConsent: false);
        }

        var notice = string.IsNullOrWhiteSpace(s.ConsentMessage) ? DefaultNotice : s.ConsentMessage!;

        // 헌법 #14: Razor 에 raw string 금지 — 여기서 $"...\n..." 로 조립해 화면에 넘긴다.
        string message;
        if (kind == KindFirst)
        {
            // 종전 문구 그대로(UpdateConsentGate.razor:82).
            message = $"새 버전 {latest} 이(가) 나왔습니다. 업데이트하시겠습니까?\n\n{notice}";
        }
        else
        {
            // 🔴 사유(IssueText · 설계 §7 매핑 결과)를 함께 보인다 — 저장공간 부족 뒤 [예] 반복이
            //   다시 막히는 이유를 누르는 자리에서 알게 한다(작업지시서 §6 리스크 · P-1).
            var reason = string.IsNullOrWhiteSpace(s.IssueText) ? string.Empty : $"\n\n{s.IssueText!.Trim()}";
            message = $"{OwnerRetryQuestion}{reason}\n\n{notice}";
        }

        return new UpdatePopupPlan(
            Show: true, Mode: UpdatePopupMode.Respond, Kind: kind, Version: latest!, Guard: guard,
            Title: Title, Message: message, ConfirmText: "예", CancelText: "나중에", RecordsConsent: true);
    }

    /// <summary>
    /// 🔵 사이드바 버전 줄 아래 한 줄(절F5). 보일 것이 없으면 <c>null</c>.
    /// </summary>
    /// <remarks>
    /// 제목은 설계 §4 표의 「사이드바」 칸 · 둘째 줄은 서버 <c>IssueText</c>(설계 §7 매핑 결과).
    /// 🔴 버튼은 <b>같은 게이트의 같은 팝업</b>을 연다(<c>UpdatePromptBus</c>) — 여기서 동의를 쓰지 않는다(#43 금지 3).
    /// </remarks>
    public static UpdateSidebarLine? SidebarLine(UpdateStatusSnapshot? s)
    {
        if (s is null || IsLegacy(s)) return null;

        var kind = KindOf(s);
        if (kind == KindNone) return null;

        var latest = s.LatestVersion?.Trim();
        var headline = kind switch
        {
            KindFirst => string.IsNullOrWhiteSpace(latest) ? "새 버전 있음" : $"새 버전 {latest} 있음",
            KindLater => "업데이트 미완료 · 나중에 선택",
            KindRequested => "업데이트 준비 중",
            KindNotStarted => "업데이트가 시작되지 않았습니다",
            KindInProgress => "업데이트 진행 중",
            KindInterrupted => "업데이트 중 중단됨",
            _ => "업데이트 미완료",
        };

        var text = s.IssueText?.Trim();
        var detail = string.IsNullOrWhiteSpace(text) || string.Equals(text, headline, StringComparison.Ordinal)
            ? null
            : text;

        var needs = s.NeedsPrompt && !string.IsNullOrWhiteSpace(latest);
        var showButton = needs && s.CanRespond;
        var hint = needs && !s.CanRespond ? SidebarOtherPcHint : null;

        return new UpdateSidebarLine(kind, headline, detail, showButton, hint);
    }
}

/// <summary>서버 <c>update-status</c> 응답 가운데 판정에 쓰는 값.</summary>
public sealed record UpdateStatusSnapshot(
    string? CurrentVersion,
    bool UpdateAvailable,
    string? LatestVersion,
    string? ConsentMessage,
    string? IssueKind,
    string? IssueText,
    bool CanRespond,
    bool NeedsPrompt);

/// <summary>팝업 모양.</summary>
public enum UpdatePopupMode
{
    /// <summary>띄우지 않는다.</summary>
    None,
    /// <summary>[예]/[나중에] — 메인PC. 누른 것을 기록한다.</summary>
    Respond,
    /// <summary>[확인] 하나 — 다른 PC. 아무것도 기록하지 않는다.</summary>
    InfoOnly,
}

/// <summary>팝업 계획. <c>CancelText</c> 가 null 이면 버튼이 하나다.</summary>
public sealed record UpdatePopupPlan(
    bool Show,
    UpdatePopupMode Mode,
    string Kind,
    string Version,
    string Guard,
    string Title,
    string Message,
    string ConfirmText,
    string? CancelText,
    bool RecordsConsent)
{
    public static readonly UpdatePopupPlan None = new(
        false, UpdatePopupMode.None, UpdatePromptPlan.KindNone, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, null, false);

    /// <summary>버튼 수 — 게이트(G-F1)가 센다.</summary>
    public int ButtonCount => !Show ? 0 : CancelText is null ? 1 : 2;
}

/// <summary>사이드바 한 줄. <c>ShowFinishButton</c> 와 <c>Hint</c> 는 동시에 있지 않다.</summary>
public sealed record UpdateSidebarLine(
    string Kind,
    string Headline,
    string? Detail,
    bool ShowFinishButton,
    string? Hint);
