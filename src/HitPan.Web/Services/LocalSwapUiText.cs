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
/// 이 파일을 쓸 때 그 문서가 아직 없었다 ⇒ 설계 §8·§13-8 의 사유를 낱말로 옮긴 <b>잠정 이름</b>이다(계약 대조 필요).
/// 모르는 코드는 <see cref="ReasonText"/> 가 「고객센터」 한 줄로 받는다 — 화면이 비지 않는다.
/// </para>
/// </remarks>
public static class LocalSwapUiText
{
    // ───────────── 주소 (화면) ─────────────
    public const string UpdatePath = "/data/update";
    public const string RollbackPath = "/data/rollback";

    // ───────────── 주소 (API · 잠정 — 계약 대조 필요) ─────────────
    // 되돌리기 POST 주소는 설계 §0 원문(`POST /api/system/local-rollback`). 나머지는 같은 모양으로 붙였다.
    public const string ApiRollbackStatus = "api/system/local-rollback/status";
    public const string ApiRollbackStart = "api/system/local-rollback";
    public const string ApiUpdateStatus = "api/system/manual-update/status";
    public const string ApiUpdateStart = "api/system/manual-update";

    /// <summary>
    /// 🔴 [3-V] 적발 04 반영(PM 9/30) — [예] 요청 본문. 버전·경로·주소를 싣지 않는다(서버가 계산 · 화면 값 불신).
    /// </summary>
    public static readonly object EmptyBody = new { };

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

    public const string AlreadyLatest = "지금 쓰시는 버전이 최신입니다.";
    public const string CheckFailed = "버전 정보를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요.";
    public const string StartFailed = "시작하지 못했습니다. 잠시 후 다시 시도해 주세요. 계속 안 되면 고객센터로 연락 주세요.";

    /// <summary>수동 업데이트가 막혔을 때 다음 단계(사장님 9/30 「그것도 안된다면, 이전버젼으로 되돌리기 메뉴로」).</summary>
    public const string NextStepRollback = "업데이트가 계속 안 되면 「이전 버전으로 되돌리기」 메뉴를 쓰실 수 있습니다.";

    // ───────────── 사유 코드 (잠정 — 계약 대조 필요) ─────────────
    public const string ReasonMainPcOnly = "main_pc_only";
    public const string ReasonAdminOnly = "admin_only";
    public const string ReasonNoMaterial = "no_material";
    public const string ReasonUpdateInProgress = "update_in_progress";
    public const string ReasonSwapInProgress = "swap_in_progress";
    public const string ReasonDiskLow = "disk_low";
    public const string ReasonFeedUnavailable = "feed_unavailable";
    public const string ReasonBackupFailed = "backup_failed";
    public const string ReasonAlreadyLatest = "already_latest";
    public const string ReasonVerifyFailed = "verify_failed";
    public const string ReasonSchedulerUnavailable = "scheduler_unavailable";

    public const string UnknownReason = "지금은 할 수 없습니다. 고객센터로 연락 주세요.";

    /// <summary>
    /// 사유 코드 → 고객 문구. 모르는 코드·빈 값은 <see cref="UnknownReason"/>(화면이 비지 않는다 · 코드 글자를 그대로 내보이지 않는다).
    /// </summary>
    public static string ReasonText(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        ReasonMainPcOnly => $"{UpdatePromptPlan.MainPcPhrase}에서만 할 수 있습니다. 히트판이 설치된 그 컴퓨터에서 열어 주세요.",
        ReasonAdminOnly => "관리자 계정으로만 할 수 있습니다.",
        ReasonNoMaterial => "이 컴퓨터에 되돌릴 이전 버전이 없습니다. 고객센터로 연락 주세요.",
        ReasonUpdateInProgress => "업데이트가 진행 중입니다. 끝난 뒤 다시 열어 주세요.",
        ReasonSwapInProgress => "다른 업데이트나 되돌리기가 진행 중입니다. 끝난 뒤 다시 열어 주세요.",
        ReasonDiskLow => "저장 공간이 부족합니다. 컴퓨터의 빈 공간을 늘린 뒤 다시 시도해 주세요.",
        ReasonFeedUnavailable => "새 버전을 받아 올 수 없습니다(인터넷 연결 확인).",
        ReasonBackupFailed => "자료 백업에 실패해 업데이트를 멈췄습니다.",
        ReasonAlreadyLatest => AlreadyLatest,
        ReasonVerifyFailed => "받은 새 버전이 올바르지 않아 업데이트를 멈췄습니다. 고객센터로 연락 주세요.",
        ReasonSchedulerUnavailable => "이 컴퓨터에서는 지금 이 메뉴를 쓸 수 없습니다. 고객센터로 연락 주세요.",
        _ => UnknownReason,
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
