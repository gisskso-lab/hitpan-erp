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
            StateSuccess => $"지난번 {what}가 끝났습니다. 지금 {Ver(to)} 버전을 쓰고 계십니다.",
            StateRefused => $"지난번 {what}는 시작하지 않았습니다(바뀐 것은 없습니다). " + ReasonText(reason),
            StateReverted => isUpdate
                ? CauseText(reason) + UpdateFailed(from)
                : CauseText(reason) + $"이전 버전으로 되돌리지 못해 원래 버전({Ver(from)})으로 돌려 두었습니다. " +
                  "입력하신 자료는 그대로 남습니다. 계속 안 되면 고객센터로 연락 주세요.",
            StateBroken => $"지난번 {what} 도중 문제가 생겼습니다. " + BrokenAdvice,
            _ => null,
        };
    }

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

    private static string Ver(string? v) => string.IsNullOrWhiteSpace(v) ? "확인 중" : v.Trim();
}
