using System.Text.Json;

namespace HitPan.Web.Services;

/// <summary>
/// 🔴 20260930작1 갈래 F — 수동 업데이트·수동 되돌리기 화면의 <b>고객 문구와 사유 코드 → 문구 표</b>.
/// </summary>
/// <remarks>
/// <para>
/// 근거: 작업지시서 <c>docs/운영기록/20260930작1_PC안롤백_작업지시서.md</c> §9(사장님 원문 — 자동/수동 용어 ·
/// 메인컴퓨터 관리자만 · 경고) · §10 · 설계 §8 · §13-8(문구).
/// 용어는 사장님 정의 그대로 — <b>자동</b> = 로그인 팝업 · <b>수동</b> = 메뉴로 직접.
/// </para>
/// <para>
/// 🔴 Blazor 비의존 순수 C# — <c>HitPan.Tests.csproj</c> 가 소스를 링크해 <b>실제로 불러</b> 잰다(개발용어 0 · #23·#24).
/// </para>
/// <para>
/// ⚠️ 사유 코드 이름은 갈래 A 소유 계약(<c>docs/설계/erp/20260930_계약_local-swap_request.md</c>)이 정한다.
/// ⬛ 이 파일을 처음 쓸 때(F) 그 문서가 아직 없어 잠정 이름이었다 → 🟢 I-WEB(9/30)이 계약 §6 · 실제 API 상수와 맞췄다(C-1~C-7).
/// 모르는 코드는 <see cref="ReasonText"/> 가 「고객센터」 한 줄로 받는다 — 화면이 비지 않는다.
/// </para>
/// <para>
/// 🔴 메인PC 문구는 계약 표의 「대표 컴퓨터」가 아니라 작3 통일안 <see cref="UpdatePromptPlan.MainPcPhrase"/>(PM §11 판정).
/// </para>
/// </remarks>
public static class LocalSwapUiText
{
    // ───────────── 주소 (화면) ─────────────
    public const string UpdatePath = "/data/update";
    public const string RollbackPath = "/data/rollback";

    // ───────────── 주소 (API) ─────────────
    // 🔴 20260930작1 I-WEB C-6 — 실제 컨트롤러 라우트(읽기만 대조):
    //   LocalRollbackController  [Route("api/system/local-rollback")] · [HttpGet] 상태 · [HttpPost] 시작(본문 { ticket })
    //   ManualUpdateController   [Route("api/manual-update")] · [HttpGet("check")] · [HttpPost("apply")] · [HttpGet("status")]
    // 게이트 F-C1 이 컨트롤러 소스의 라우트와 이 상수를 맞대 본다(어긋나면 FAIL).
    // ⬛ 옛 잠정 주소(F): api/system/local-rollback/status · api/system/manual-update{,/status} — 컨트롤러에 없는 주소였다.
    public const string ApiRollbackStatus = "api/system/local-rollback";
    public const string ApiRollbackStart = "api/system/local-rollback";
    public const string ApiUpdateCheck = "api/manual-update/check";
    public const string ApiUpdateStart = "api/manual-update/apply";
    public const string ApiUpdateJob = "api/manual-update/status";

    /// <summary>
    /// 🔴 [3-V] 적발 04 반영(PM 9/30) — 수동 업데이트 [예] 요청 본문. 버전·경로·주소를 싣지 않는다(서버가 계산 · 화면 값 불신).
    /// 서버 <c>ManualUpdateApplyRequest</c> 는 입구(<c>entry</c>) 한 칸뿐이고 비우면 <c>menu</c> 로 본다.
    /// </summary>
    public static readonly object EmptyBody = new { };

    /// <summary>
    /// 🔴 C-5 — 되돌리기 [예] 요청 본문 = 조회 때 받은 <b>1회용 확인 번호 하나</b>(서버 <c>LocalRollbackStartBody { Ticket }</c>).
    /// 확인 번호는 버전·경로가 아니다 — 무엇으로 되돌릴지는 서버가 번호에 묶어 둔 값으로 다시 판정한다(병렬이슈 04 와 양립).
    /// </summary>
    public static object RollbackStartBody(string? ticket) => new { ticket = ticket ?? string.Empty };

    // ───────────── 메뉴 · 첫 줄 (설계 §13-8) ─────────────
    public const string MenuUpdate = "최신 버전 확인/업데이트";
    public const string MenuRollback = "이전 버전으로 되돌리기";
    public const string FirstLine = "평소에는 로그인할 때 자동으로 안내됩니다. 안내가 오지 않을 때 쓰는 메뉴입니다.";

    /// <summary>두 화면 머리의 문(門) 안내 — 작3 통일 표현(<see cref="UpdatePromptPlan.MainPcPhrase"/>) 그대로.</summary>
    public static string WhoCanUse =>
        $"이 메뉴는 {UpdatePromptPlan.MainPcPhrase}에서 관리자 계정으로만 쓸 수 있습니다.";

    /// <summary>사이드바 업데이트 이슈 줄 아래 이동 링크 글자(작3 줄은 무변경 · 링크만 더한다).</summary>
    public const string SidebarIssueLink = "자료관리 › 최신 버전 확인/업데이트";

    /// <summary>로그인창 [최신버젼업데이트] 를 눌렀을 때(L-1 (가)) — 로그인 뒤 곧장 수동 업데이트 화면.</summary>
    public const string LoginButtonNotice =
        "관리자 계정으로 로그인하시면 「최신 버전 확인/업데이트」 화면으로 바로 이동합니다.";

    // ───────────── [예] 전 문구 ─────────────

    /// <summary>수동 업데이트 [예] 전(설계 §13-8).</summary>
    public static string UpdateConfirm(string? to) =>
        $"히트판을 새 버전({Ver(to)})으로 바꿉니다. 먼저 자료를 백업합니다. 약 10분 동안 히트판이 멈춥니다. " +
        "입력하신 자료는 그대로 남습니다.";

    /// <summary>되돌리기 [예] 전 경고(설계 §8 팝업 + §13-8 경고 · 사장님 「경고는 당연히 해야지」).</summary>
    public static string RollbackConfirm(string? to) =>
        $"히트판을 이전 버전({Ver(to)})으로 되돌립니다. " +
        RollbackWarning + " " +
        "입력하신 자료(거래·재고·장부 등)는 지금 그대로 남습니다. " +
        "약 5분 동안 히트판이 멈춥니다. 되돌린 뒤 새 버전 안내가 다시 나오면 그때 [예]를 누르시면 됩니다.";

    /// <summary>되돌리기 화면에 늘 보이는 경고(팝업과 같은 말).</summary>
    public const string RollbackWarning =
        "새 버전에서 생긴 기능은 사라집니다. 로그인할 때마다 새 버전 안내가 다시 나옵니다.";

    /// <summary>바로 이전 한 판만(사장님 「바로 이전버젼으로 되돌리는 것으로 한정」).</summary>
    public const string RollbackOnlyOneStep = "바로 이전 버전 한 단계로만 되돌릴 수 있습니다.";

    public const string DataKept = "입력하신 자료(거래·재고·장부 등)는 지워지지 않습니다.";

    // ───────────── 진행 · 결과 ─────────────
    public const string UpdateStarted =
        "업데이트를 시작했습니다. 약 10분 동안 히트판이 멈춥니다. 끝나면 다시 로그인해 주세요.";

    public const string RollbackStarted = "되돌리는 중입니다. 약 5분 뒤 다시 로그인해 주세요.";

    /// <summary>수동 업데이트 실패 → 원래 판으로 돌려 둠(설계 §13-8) · 단계 ③ 안내 포함.</summary>
    public static string UpdateFailed(string? from) =>
        $"업데이트하지 못해 원래 버전({Ver(from)})으로 돌려 두었습니다. " +
        "계속 안 되면 「이전 버전으로 되돌리기」 또는 고객센터로 연락 주세요.";

    /// <summary>[예] 뒤 진행 상태가 사라졌을 때(204) — 결과는 다음 로그인 뒤 이 화면에서 보인다.</summary>
    public const string UpdateStatusLost =
        "진행 상황을 더 확인하지 못했습니다. 몇 분 뒤 이 화면을 다시 열어 결과를 확인해 주세요.";

    public const string AlreadyLatest = "지금 쓰시는 버전이 최신입니다.";
    public const string CheckFailed = "버전 정보를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요.";
    public const string StartFailed = "시작하지 못했습니다. 잠시 후 다시 시도해 주세요. 계속 안 되면 고객센터로 연락 주세요.";

    /// <summary>수동 업데이트가 막혔을 때 다음 단계(사장님 9/30 「그것도 안된다면, 이전버젼으로 되돌리기 메뉴로」).</summary>
    public const string NextStepRollback = "업데이트가 계속 안 되면 「이전 버전으로 되돌리기」 메뉴를 쓰실 수 있습니다.";

    // ───────────── 사유 코드 (계약 §6 · 실제 API 상수 이름 그대로) ─────────────
    // 🔴 20260930작1 I-WEB C-1~C-3 — 이름의 진실원은 API 상수 SwapReasons(갈래 A)·ManualUpdateReasons(갈래 U).
    // 게이트 F-C2 가 두 상수 묶음을 <b>리플렉션으로 전부</b> 읽어 「고객센터」 한 줄로 떨어지는 코드가 0 인지 잰다.
    // ⬛ 옛 잠정 이름(F) — no_material · feed_unavailable · already_latest · scheduler_unavailable 은 서버가 주지 않는 이름이라 뺐다
    //    (각각 no_previous_version · feed_unreachable · no_newer_version · task_register_failed 로 옮김).

    /// <summary>화면 안에서만 쓰는 표식 — <c>TenantAdminOnly</c> 정책 403 은 본문이 없다(계약 §6 「(403 정책)」).</summary>
    public const string ReasonAdminOnly = "admin_only";
    public const string ReasonOk = "ok";
    public const string ReasonMainPcOnly = "main_pc_only";
    public const string ReasonNotWindows = "not_windows";
    public const string ReasonNoPreviousVersion = "no_previous_version";
    public const string ReasonRollbackChainBlocked = "rollback_chain_blocked";
    public const string ReasonUpdateInProgress = "update_in_progress";
    public const string ReasonSwapInProgress = "swap_in_progress";
    public const string ReasonCooldown = "cooldown";
    public const string ReasonDiskLow = "disk_low";
    public const string ReasonTicketInvalid = "ticket_invalid";
    public const string ReasonScriptMissing = "script_missing";
    public const string ReasonFolderUnsafe = "folder_unsafe";
    public const string ReasonTaskRegisterFailed = "task_register_failed";
    public const string ReasonRequestInvalid = "request_invalid";
    public const string ReasonMaterialInvalid = "material_invalid";
    public const string ReasonHashMismatch = "hash_mismatch";
    public const string ReasonSafetyNetFailed = "safety_net_failed";
    public const string ReasonStopFailed = "stop_failed";
    public const string ReasonSwapFailed = "swap_failed";
    public const string ReasonVerifyFailed = "verify_failed";
    public const string ReasonRevertFailed = "revert_failed";
    public const string ReasonFeedUnreachable = "feed_unreachable";
    public const string ReasonSignatureInvalid = "signature_invalid";
    public const string ReasonNoNewerVersion = "no_newer_version";
    public const string ReasonBackupFailed = "backup_failed";
    public const string ReasonDownloadFailed = "download_failed";
    /// <summary>갈래 U 의 임시 코드(런처 연결 전) — I-API 가 연결하면 사라진다. 그 전까지도 화면은 제 문구로 받는다.</summary>
    public const string ReasonLauncherNotWired = "launcher_not_wired";
    // 20260930작1 봉합 06ⓑ(갈래 L · 계약 §6 봉합 3줄) — 런처가 묵은 요청·남은 작업을 정리할 때
    public const string ReasonWorkerNotStarted = "worker_not_started";
    public const string ReasonWorkerInterrupted = "worker_interrupted";
    public const string ReasonSwapInterrupted = "swap_interrupted";
    // 20260930작1 봉합2 B2(설계 15-2 · 계약 §6 봉합 2차) — 워치독 .old 남음(런처·일꾼 S0 refused) · 지난 교체 잔재(일꾼 success·refused)
    public const string ReasonUpdateCleanupPending = "update_cleanup_pending";
    public const string ReasonCleanupPending = "cleanup_pending";
    public const string ReasonCarryPending = "carry_pending";

    public const string UnknownReason = "지금은 할 수 없습니다. 고객센터로 연락 주세요.";

    /// <summary>
    /// 사유 코드 → 고객 문구. 모르는 코드·빈 값은 <see cref="UnknownReason"/>(화면이 비지 않는다 · 코드 글자를 그대로 내보이지 않는다).
    /// 사유마다 <b>무슨 일이 있었는지 · 바뀐 것이 있는지 · 다음에 무엇을 하면 되는지</b>를 따로 적는다(「고객센터」 한 줄 몰아넣기 금지).
    /// </summary>
    public static string ReasonText(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        ReasonMainPcOnly => $"{UpdatePromptPlan.MainPcPhrase}에서만 할 수 있습니다. 히트판이 설치된 그 컴퓨터에서 열어 주세요.",
        ReasonAdminOnly => "관리자 계정으로만 할 수 있습니다.",
        ReasonNotWindows => "이 컴퓨터에서는 할 수 없습니다. 히트판이 설치된 컴퓨터에서 열어 주세요.",
        ReasonNoPreviousVersion => "이 컴퓨터에 되돌릴 이전 버전이 없습니다. 고객센터로 연락 주세요.",
        ReasonRollbackChainBlocked =>
            "이미 한 번 되돌린 버전입니다. 한 단계보다 더 앞으로는 되돌릴 수 없습니다. 새 버전 안내가 오면 그때 업데이트해 주세요.",
        ReasonUpdateInProgress => "업데이트가 진행 중입니다. 끝난 뒤 다시 열어 주세요.",
        ReasonSwapInProgress => "다른 업데이트나 되돌리기가 진행 중입니다. 끝난 뒤 다시 열어 주세요.",
        ReasonCooldown => "방금 버전을 바꾸었습니다. 10분쯤 지난 뒤 다시 해 주세요.",
        ReasonDiskLow => "저장 공간이 부족합니다. 컴퓨터의 빈 공간을 늘린 뒤 다시 시도해 주세요.",
        ReasonTicketInvalid => "확인한 지 시간이 너무 지났습니다. [다시 확인]을 누른 뒤 한 번 더 해 주세요.",
        ReasonScriptMissing =>
            "이 컴퓨터의 히트판에 버전을 바꾸는 기능이 온전히 설치되어 있지 않아 아무것도 바꾸지 않았습니다. 고객센터로 연락 주세요.",
        ReasonFolderUnsafe =>
            "히트판이 설치된 폴더를 안전하게 쓸 수 없어 아무것도 바꾸지 않았습니다. 고객센터로 연락 주세요.",
        ReasonTaskRegisterFailed =>
            "버전을 바꾸는 작업을 시작하지 못해 아무것도 바꾸지 않았습니다. 컴퓨터를 다시 켠 뒤 한 번 더 해 보시고, 계속 안 되면 고객센터로 연락 주세요.",
        ReasonRequestInvalid =>
            "요청 내용이 맞지 않아 아무것도 바꾸지 않았습니다. 이 화면을 다시 연 뒤 해 보시고, 계속 안 되면 고객센터로 연락 주세요.",
        ReasonMaterialInvalid =>
            "보관해 둔 이전 버전 파일이 온전하지 않아 아무것도 바꾸지 않았습니다. 고객센터로 연락 주세요.",
        ReasonHashMismatch => "새 버전을 받아 오지 못했습니다(받은 파일이 온전하지 않습니다). 잠시 후 다시 해 주세요.",
        ReasonSafetyNetFailed =>
            "바꾸다 전원이 꺼질 때를 대비한 안전 장치를 준비하지 못해 아무것도 바꾸지 않았습니다. 고객센터로 연락 주세요.",
        ReasonStopFailed => CauseText(ReasonStopFailed) + "원래 버전으로 돌려 두었습니다.",
        ReasonSwapFailed => CauseText(ReasonSwapFailed) + "원래 버전으로 돌려 두었습니다.",
        ReasonVerifyFailed => CauseText(ReasonVerifyFailed) + "원래 버전으로 돌려 두었습니다.",
        ReasonRevertFailed => BrokenAdvice,
        ReasonFeedUnreachable => "새 버전 정보를 받아 오지 못했습니다. 인터넷 연결을 확인한 뒤 다시 해 주세요.",
        ReasonSignatureInvalid =>
            "받아 온 새 버전이 히트판 정품인지 확인되지 않아 멈췄습니다. 아무것도 바꾸지 않았습니다. 고객센터로 연락 주세요.",
        ReasonNoNewerVersion => AlreadyLatest,
        ReasonBackupFailed => "자료 백업에 실패해 업데이트를 멈췄습니다. 아무것도 바꾸지 않았습니다. 잠시 후 다시 해 주세요.",
        ReasonDownloadFailed => "새 버전 파일을 끝까지 받지 못했습니다. 인터넷 연결을 확인한 뒤 다시 해 주세요.",
        ReasonLauncherNotWired =>
            "이 버전의 히트판에서는 아직 이 메뉴로 버전을 바꿀 수 없습니다. 아무것도 바꾸지 않았습니다. 고객센터로 연락 주세요.",
        ReasonWorkerNotStarted => "예약해 둔 작업이 시작되지 않아 아무것도 바꾸지 않았습니다. 다시 해 주세요.",
        ReasonWorkerInterrupted => "버전을 바꾸던 도중 멈췄습니다. " + BrokenAdvice,
        ReasonSwapInterrupted => "이전 교체가 끝까지 되지 않았습니다. 고객센터로 연락 주세요.",
        ReasonUpdateCleanupPending =>
            "이전 자동 업데이트의 정리가 아직 끝나지 않았습니다. 다음 자동 업데이트가 끝나면 다시 쓰실 수 있습니다.",
        ReasonCleanupPending =>
            "지난 교체에서 쓰던 프로그램 파일 일부를 아직 치우지 못했습니다. 다음 업데이트·되돌리기 때 먼저 치웁니다. 쓰시는 데는 지장 없습니다.",
        ReasonCarryPending =>
            "일부 첨부 파일을 아직 제자리로 옮기지 못했습니다. 파일은 지워지지 않고 보관돼 있으며, 다음 업데이트·되돌리기 때 먼저 옮깁니다.",
        _ => UnknownReason,
    };

    /// <summary>
    /// 바꾸다 실패해 원위치한 경우(<c>reverted</c>)의 원인 한 마디 — 끝에 공백 하나. 원인이 따로 없으면 빈 글자.
    /// </summary>
    private static string CauseText(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        ReasonStopFailed => "히트판을 잠시 멈추지 못했습니다. ",
        ReasonSwapFailed => "프로그램 파일을 바꾸다 문제가 생겼습니다. ",
        ReasonVerifyFailed => "바꾼 뒤 히트판이 제때 켜지지 않았습니다. ",
        _ => string.Empty,
    };

    /// <summary>원위치까지 확인하지 못한 경우(<c>broken</c> · <c>revert_failed</c>) — 설계 §5 부팅 안전망이 되살린다 ⇒ 다시 켜기부터.</summary>
    public const string BrokenAdvice =
        "원래 버전으로 돌려 놓았는지 확인하지 못했습니다. 컴퓨터를 한 번 다시 켜 주세요. " +
        "그래도 히트판이 열리지 않거나 이상하면 바로 고객센터로 연락 주세요. 입력하신 자료는 지워지지 않습니다.";

    // ───────────── 끝 상태 (계약 §5 · §7 — 다음 로그인 뒤 알림) ─────────────
    // 🔴 C-7 — request.json 끝 상태(success·refused·reverted·broken) + reason 을 GET 이 돌려준다(LocalSwapLastResult).
    public const string StateRequested = "requested";
    public const string StateRunning = "running";
    public const string StateSuccess = "success";
    public const string StateRefused = "refused";
    public const string StateReverted = "reverted";
    public const string StateBroken = "broken";

    public const string ModeUpdate = "update";
    public const string ModeRollback = "rollback";

    /// <summary>지난 교체 한 번의 결과 → 고객 문구. 모르는 상태는 null(화면이 그 줄을 안 그린다).</summary>
    public static string? EndStateText(string? mode, string? state, string? reason, string? from, string? to)
    {
        var what = (mode ?? string.Empty).Trim().ToLowerInvariant() == ModeRollback ? "되돌리기" : "업데이트";
        var isUpdate = what == "업데이트";
        return (state ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            StateRequested or StateRunning => $"{what}가 진행 중입니다. 끝난 뒤 다시 로그인해 주세요.",
            StateSuccess => $"지난번 {what}가 끝났습니다. 지금 {Ver(to)} 버전을 쓰고 계십니다." + SuccessNote(reason),
            StateRefused => $"지난번 {what}는 시작하지 않았습니다(바뀐 것은 없습니다). " + ReasonText(reason),
            StateReverted => isUpdate
                ? CauseText(reason) + UpdateFailed(from)
                : CauseText(reason) + $"이전 버전으로 되돌리지 못해 원래 버전({Ver(from)})으로 돌려 두었습니다. " +
                  "입력하신 자료는 그대로 남습니다. 계속 안 되면 고객센터로 연락 주세요.",
            StateBroken => $"지난번 {what} 도중 문제가 생겼습니다. " + BrokenAdvice,
            _ => null,
        };
    }

    /// <summary>
    /// 20260930작1 봉합2 B2(설계 15-2) — <c>success</c> 뒤 잔재 사유(<c>cleanup_pending</c>·<c>carry_pending</c>)는 성공 줄 뒤에 그 문구를 붙인다
    /// (앞 공백 하나 · 색은 성공 그대로). 그 밖의 사유·빈 값은 빈 글자 — 종전 성공 줄 그대로.
    /// </summary>
    private static string SuccessNote(string? reason) => (reason ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        ReasonCleanupPending or ReasonCarryPending => " " + ReasonText(reason),
        _ => string.Empty,
    };

    /// <summary>끝 상태의 무게(화면 알림 색) — 0 알림 · 1 주의 · 2 위험 · 3 성공.</summary>
    public static int EndStateLevel(string? state) => (state ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        StateSuccess => 3,
        StateBroken => 2,
        StateReverted or StateRefused => 1,
        _ => 0,
    };

    /// <summary>
    /// 지난 결과를 보일지 — 24시간 안의 기록만 · 성공은 지금 판이 그 결과(<c>to</c>)일 때만 · 원위치는 지금 판이 <c>from</c> 일 때만
    /// (그 뒤 자동 업데이트가 또 갔으면 낡은 알림이라 안 보인다).
    /// </summary>
    public static bool ShouldShowLast(string? state, string? from, string? to, DateTime atUtc, string? currentVersion, DateTime nowUtc)
    {
        var age = nowUtc - atUtc;
        if (age < TimeSpan.Zero || age > TimeSpan.FromHours(24)) return false;
        var cur = (currentVersion ?? string.Empty).Trim();
        return (state ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            StateSuccess => string.Equals(cur, (to ?? string.Empty).Trim(), StringComparison.Ordinal),
            StateReverted => string.Equals(cur, (from ?? string.Empty).Trim(), StringComparison.Ordinal),
            StateRefused or StateBroken or StateRequested or StateRunning => true,
            _ => false,
        };
    }

    /// <summary>지난 결과 줄 머리 — 「지난 기록 · 9월 30일 14:05」(이 컴퓨터 시각).</summary>
    public static string LastCaption(DateTime atUtc) =>
        "지난 기록 · " + DateTime.SpecifyKind(atUtc, DateTimeKind.Utc).ToLocalTime().ToString("M월 d일 HH:mm");

    // ───────────── 수동 업데이트 진행 단계 (ManualUpdateStages) ─────────────
    public const string StageChecking = "checking";
    public const string StageDownloading = "downloading";
    public const string StageVerifying = "verifying";
    public const string StageBackingUp = "backing_up";
    public const string StageHandingOff = "handing_off";
    public const string StageHandedOff = "handed_off";
    public const string StageRefused = "refused";

    /// <summary>진행 단계 → 고객 문구(넘기기 전 · 이 동안은 히트판이 아직 켜져 있다).</summary>
    public static string StageText(string? stage) => (stage ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        StageChecking => "새 버전을 확인하는 중입니다…",
        StageDownloading => "새 버전을 받는 중입니다… 인터넷 속도에 따라 몇 분 걸릴 수 있습니다.",
        StageVerifying => "받은 파일이 온전한지 확인하는 중입니다…",
        StageBackingUp => "바꾸기 전에 자료를 백업하는 중입니다…",
        StageHandingOff => "버전을 바꿀 준비를 하는 중입니다…",
        StageHandedOff => UpdateStarted,
        _ => "준비하는 중입니다…",
    };

    /// <summary>
    /// HTTP 응답(상태·본문) → 고객 문구. 본문 코드가 있으면 그것이 먼저다.
    /// 401 = 다시 로그인 · 403 에 코드 없음 = 관리자 아님(<c>TenantAdminOnly</c> 정책은 본문이 없다).
    /// </summary>
    public static string HttpFailureText(int status, string? code)
    {
        if (!string.IsNullOrWhiteSpace(code)) return ReasonText(code);
        return status switch
        {
            401 => "로그인이 끝났습니다. 다시 로그인해 주세요.",
            403 => ReasonText(ReasonAdminOnly),
            404 => "이 버전의 히트판에는 아직 이 메뉴가 없습니다. 고객센터로 연락 주세요.",
            _ => StartFailed,
        };
    }

    /// <summary>
    /// 응답 본문에서 사유 코드를 꺼낸다 — <c>error</c> · <c>code</c> · <c>reason</c> 중 처음 보이는 글자.
    /// 기존 <c>MainPcOnlyAttribute</c> 는 <c>{ error: "main_pc_only" }</c> 로 준다. JSON 이 아니면 null.
    /// </summary>
    public static string? ExtractCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var n = p.Name.ToLowerInvariant();
                if ((n == "error" || n == "code" || n == "reason") && p.Value.ValueKind == JsonValueKind.String)
                {
                    var v = p.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            return null;
        }
        catch (JsonException)
        {
            // 본문이 JSON 이 아니다(HTML 오류 쪽 등) — 코드 없음으로 본다. 호출자가 상태 코드로 문구를 고른다.
            return null;
        }
    }

    /// <summary>
    /// 로그인 뒤 목적지(L-1 (가)) — 로그인창 [최신버젼업데이트] 를 눌렀으면 <see cref="UpdatePath"/>, 아니면 첫 화면.
    /// </summary>
    /// <remarks>
    /// 🔴 [3-V] 적발 04 반영(PM 9/30) — <b>고정 경로 둘 중 하나만</b> 돌려준다. 주소창 <c>returnUrl</c> 같은 바깥 값을
    /// 받지 않는다(열린 이동 금지). 약관·승인 대기·첫 설정 이동은 <c>Login.razor</c> 에서 이보다 먼저 간다.
    /// </remarks>
    public static string PostLoginPath(bool goManualUpdate) => goManualUpdate ? UpdatePath : "/";

    // ═════════════ 20260930작1 확대 1.3.50 갈래 N3 — 설계 §19 조각 B 화면 몫 · 조각 C ═════════════
    // 근거: 작업지시서 §18(18-5 X-3 보정 · X-4 · 18-6 X-8 보정) · 설계 19-1 조각 C · 19-3 화면 문구.
    // 기존 상수·ReasonText·EndStateText·StageText·ShouldShowLast 는 한 글자도 안 바꾼다(덧붙이기만 · #1).

    // ───────────── 「끝났습니다」 팝업 (조각 C) ─────────────
    public const string DoneTitleUpdate = "업데이트가 끝났습니다";
    public const string DoneTitleRollback = "되돌리기가 끝났습니다";

    /// <summary>「한 번」 표 — 이 브라우저 localStorage 한 칸(설계 19-1 조각 C).</summary>
    public const string DoneSeenStorageKey = "hitpan_swap_done_seen";

    /// <summary>모드로 팝업 제목을 고른다(되돌리기만 「되돌리기가」 · 그 밖은 「업데이트가」 — <see cref="EndStateText"/> 와 같은 갈림).</summary>
    public static string DoneTitle(string? mode) =>
        (mode ?? string.Empty).Trim().ToLowerInvariant() == ModeRollback ? DoneTitleRollback : DoneTitleUpdate;

    /// <summary>「한 번」 표에 적는 값 — <c>{mode}|{to}|{atUtc}</c>(설계 19-1). 같은 결과면 같은 글자.</summary>
    public static string DonePopupStamp(string? mode, string? to, DateTime atUtc) =>
        (mode ?? string.Empty).Trim().ToLowerInvariant() + "|" + (to ?? string.Empty).Trim() + "|" +
        DateTime.SpecifyKind(atUtc, DateTimeKind.Utc).ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// G-ND1 — 완료 팝업을 띄울지(순수 판정 · 화면 비의존).
    /// </summary>
    /// <param name="mode">지난 결과의 모드(update·rollback).</param>
    /// <param name="state">지난 결과의 끝 상태 — <c>success</c> 만 팝업(X-3 · refused·reverted·broken 은 기존 한 줄만).</param>
    /// <param name="from">지난 결과의 옛 판.</param>
    /// <param name="to">지난 결과의 새 판.</param>
    /// <param name="atUtc">지난 결과 시각.</param>
    /// <param name="currentVersion">지금 쓰는 판.</param>
    /// <param name="nowUtc">지금 시각.</param>
    /// <param name="storageOk">「한 번」 표를 읽을 수 있었나 — 못 읽으면 띄우지 않는다(매번 뜨는 쪽보다 한 줄만 남는 쪽 · 설계 19-1).</param>
    /// <param name="seenStamp">「한 번」 표에 이미 적힌 값(없으면 null).</param>
    /// <param name="updatePromptFirst">
    /// #43 업데이트 안내가 먼저인가 — 이번 접속에서 그 안내 차례이거나, 그 차례인지 아직 모르거나, 로그인 직후 첫 화면이면 true(X-4 · 설계 19-1 겹침 ②③).
    /// </param>
    /// <remarks>
    /// 기존 결과 한 줄과 같은 기준(<see cref="ShouldShowLast"/> — 24시간 안 · 성공은 지금 판 == <paramref name="to"/>)을 먼저 지나야 한다.
    /// </remarks>
    public static bool ShouldShowDonePopup(
        string? mode, string? state, string? from, string? to, DateTime atUtc,
        string? currentVersion, DateTime nowUtc, bool storageOk, string? seenStamp, bool updatePromptFirst)
    {
        if (updatePromptFirst) return false;
        if (!storageOk) return false;
        if ((state ?? string.Empty).Trim().ToLowerInvariant() != StateSuccess) return false;
        if (!ShouldShowLast(state, from, to, atUtc, currentVersion, nowUtc)) return false;
        return !string.Equals(seenStamp, DonePopupStamp(mode, to, atUtc), StringComparison.Ordinal);
    }

    // ───────────── X-3 보정 — 내부 사정은 고객에게 안 보인다 ─────────────

    /// <summary>수동 메뉴가 잠겨 거부될 때만 보이는 한 줄(X-3 보정 · 사장님 S-1 「메뉴가 잠길 때만 한 줄 안내」).</summary>
    public const string MenuLockedLine = "잠시 쓸 수 없습니다. 업무에는 지장 없습니다.";

    /// <summary>
    /// 「메뉴가 잠겨 거부」로 보는 사유 — 지난 교체·자동 업데이트의 정리가 안 끝나 메뉴가 거부하는 두 코드.
    /// <c>carry_pending</c>(첨부 일부가 안 열릴 수 있음)은 고객에게 실제 영향이 있어 여기에 넣지 않는다(X-3 보정).
    /// </summary>
    public static bool IsMenuLockedReason(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        ReasonCleanupPending or ReasonUpdateCleanupPending => true,
        _ => false,
    };

    /// <summary>화면에 보일 사유 문구 — 잠김 사유면 <see cref="MenuLockedLine"/>, 그 밖은 기존 <see cref="ReasonText"/> 그대로.</summary>
    public static string CustomerReasonText(string? code) => IsMenuLockedReason(code) ? MenuLockedLine : ReasonText(code);

    /// <summary>
    /// 화면에 보일 지난 결과 한 줄(기존 <c>ShowLast</c> 자리 · X-3 보정) — 성공 + <c>cleanup_pending</c> 은 성공 줄 하나로
    /// (「옛 파일 일부 못 치움」을 따로 붙이지 않는다) · 거부 + 잠김 사유는 <see cref="MenuLockedLine"/> ·
    /// <c>carry_pending</c> 과 그 밖은 기존 <see cref="EndStateText"/> 그대로.
    /// </summary>
    public static string? CustomerEndStateText(string? mode, string? state, string? reason, string? from, string? to)
    {
        var st = (state ?? string.Empty).Trim().ToLowerInvariant();
        var rs = (reason ?? string.Empty).Trim().ToLowerInvariant();
        if (st == StateSuccess && rs == ReasonCleanupPending)
            return EndStateText(mode, state, null, from, to);
        if (st == StateRefused && IsMenuLockedReason(reason))
        {
            var what = (mode ?? string.Empty).Trim().ToLowerInvariant() == ModeRollback ? "되돌리기" : "업데이트";
            return $"지난번 {what}는 시작하지 않았습니다(바뀐 것은 없습니다). " + MenuLockedLine;
        }
        return EndStateText(mode, state, reason, from, to);
    }

    // ───────────── 세 번째 길 — 본사 보관본 받기 (조각 B 화면 몫 · 설계 19-3) ─────────────
    /// <summary>재료 종류 글자 — 서버가 세 번째 길에서 주는 값(설계 19-1 · 화면은 보이지 않는다).</summary>
    public const string MaterialManualZip = "manual_zip";

    /// <summary>확인창 덧붙임 — 본사 보관본을 받아 오는 경우(설계 19-3 초안).</summary>
    public const string RollbackFetchConfirmNote =
        "이 컴퓨터에 이전 버전 파일이 없어 본사에 보관된 파일을 받아 옵니다. 인터넷 속도에 따라 몇 분 더 걸릴 수 있습니다.";

    /// <summary>확인창 덧붙임 — 넣어 둔 파일을 쓰는 경우(받지 않음 · 18-6 X-8 PM 보정).</summary>
    public const string RollbackPlacedConfirmNote = "준비된 이전 버전 파일로 되돌립니다.";

    /// <summary>받기 진행 단계 → 고객 문구(설계 19-3). 넘김(<c>handed_off</c>)은 기존 <see cref="RollbackStarted"/>.</summary>
    public static string FetchStageText(string? stage) => (stage ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        StageDownloading => "이전 버전 파일을 받는 중입니다… 다 받을 때까지 히트판은 그대로 쓰실 수 있습니다.",
        StageVerifying => "받은 파일이 히트판 정품인지 확인하는 중입니다…",
        StageHandingOff => "되돌리기를 시작합니다…",
        StageHandedOff => RollbackStarted,
        _ => "준비하는 중입니다…",
    };

    /// <summary>받기 실패 사유 → 고객 문구(설계 19-3 · 네 가지만 받기 전용 · 그 밖은 기존 <see cref="CustomerReasonText"/>).</summary>
    public static string FetchFailText(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        ReasonDownloadFailed => "이전 버전 파일을 끝까지 받지 못했습니다. 바뀐 것은 없습니다. 인터넷 연결을 확인한 뒤 다시 해 주세요.",
        ReasonHashMismatch => "받은 이전 버전 파일이 온전하지 않아 쓰지 않았습니다. 바뀐 것은 없습니다. 다시 해 주세요.",
        ReasonSignatureInvalid => "이전 버전 파일이 히트판 정품인지 확인되지 않아 멈췄습니다. 바뀐 것은 없습니다. 고객센터로 연락 주세요.",
        ReasonNoPreviousVersion => "본사에도 이 이전 버전 파일이 더 이상 없습니다. 바뀐 것은 없습니다. 고객센터로 연락 주세요.",
        _ => CustomerReasonText(code),
    };

    private static string Ver(string? v) => string.IsNullOrWhiteSpace(v) ? "확인 중" : v.Trim();
}
