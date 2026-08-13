namespace HitPan.Application.DTOs.Leave;

/// <summary>
/// 휴직 — 기간이 있는 부재. 작(2026-08-13) 그룹웨어 단계6.
/// </summary>
/// <remarks>
/// 🔴 <b>사장님이 정한 범위</b>(2026-08-13):
/// <list type="bullet">
///   <item><i>"복잡하게 생각할것 없이 그냥 휴직은 상태처리, 상태확인 정도로만 써도 될듯."</i></item>
///   <item><i>"휴직은 모든걸 다 수동으로."</i></item>
///   <item><i>"휴직시 급여 : 텍스트 박스로 수동입력 → 그러면 자연스럽게 급여, 회계이슈도 해결될듯"</i></item>
///   <item><i>"비고에 육아, 연수, 교육, 등 자유롭게 쓰면 됨"</i></item>
/// </list>
/// ⇒ 계산도, 판정도, 종류 목록도 없다. <b>기간·급여·비고를 사람이 넣고, 상태를 본다.</b>
///
/// 🔴 <b>휴가(<c>leave_requests</c>)와 왜 나눴는지</b> — 실측 근거를 남긴다.
/// 나중에 "하나로 합치면 되잖아" 가 반드시 다시 나오기 때문이다.
/// <list type="number">
///   <item>휴가 표의 <c>leave_days</c> 는 <c>decimal(3,1)</c> = 최대 99.9일.
///         육아휴직 1년 6개월(548일)을 넣으면 <b>MySQL 이 거부한다</b>
///         (실측: <c>ERROR 1264 Out of range value for column 'leave_days'</c>).</item>
///   <item>휴가가 승인되면 <c>LeaveBalanceHelper</c> 가 <c>annual_leave_used</c> 를 자동으로 더한다.
///         휴직을 휴가로 올리면 <b>연차 잔여가 마이너스</b>가 되어 복직 후 연차를 못 쓴다.</item>
/// </list>
/// </remarks>
public sealed class AbsenceDto
{
    public string AbsenceId { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string? DeptName { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? ActualReturnDate { get; set; }

    public string Status { get; set; } = "draft";
    public string StatusDisplay => AbsenceStatusLabels.Of(Status);

    /// <summary>
    /// 비고. 🔴 사장님: <i>"비고에 육아, 연수, 교육, 등 자유롭게 쓰면 됨"</i>.
    /// 종류를 고르게 하지 않는다 — 회사마다 부르는 이름이 다르고, 목록에 없는 사유가 늘 생긴다.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 🔴 휴직 중 지급할 금액. 사람이 직접 넣는다. 0 이면 무급.
    /// 이 숫자 하나로 급여·회계가 정리된다 — 우리가 무급/유급/일부지급을 판단할 이유가 없다.
    /// </summary>
    public decimal PayAmount { get; set; }
    public string? PayNote { get; set; }

    /// <summary>화면에 그대로 뿌리는 급여 표기.</summary>
    public string PayDisplay => PayAmount > 0 ? $"{PayAmount:#,0}원" : "무급";

    public string? ApprovalId { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectReason { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>신청 기간의 날수. <b>연차에서 깎이지 않는다</b> — 보여주기 위한 값이다.</summary>
    public int TotalDays => (EndDate.Date - StartDate.Date).Days + 1;

    /// <summary>오늘 기준으로 휴직 중인가.</summary>
    public bool IsOnLeaveToday =>
        Status == "active"
        && StartDate.Date <= DateTime.Today
        && DateTime.Today <= EndDate.Date;
}

/// <summary>휴직 신청·수정 요청.</summary>
/// <remarks>
/// 🔴 <b>전부 사람이 넣는다</b>(사장님 2026-08-13: <i>"휴직은 모든걸 다 수동으로"</i>).
/// 서버는 기간을 계산하지 않고, 금액을 판단하지 않고, 사유를 분류하지 않는다.
/// </remarks>
public sealed class SaveAbsenceRequest
{
    public string? AbsenceId { get; set; }
    public string EmployeeId { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>비고 — 육아·연수·교육 등 자유롭게 쓴다.</summary>
    public string? Reason { get; set; }

    /// <summary>휴직 중 지급할 금액. 0 이면 무급.</summary>
    public decimal PayAmount { get; set; }
    public string? PayNote { get; set; }

    /// <summary>true 면 저장과 동시에 결재에 올린다.</summary>
    public bool Submit { get; set; }
}

/// <summary>저장 결과. 🔴 결재가 실제로 올라갔는지를 <b>사실대로</b> 돌려준다(단계3 P0-1 교훈).</summary>
public sealed class SaveAbsenceResult
{
    public string AbsenceId { get; set; } = "";
    public bool ApprovalCreated { get; set; }
    /// <summary>결재가 안 올라갔으면 그 이유. 올라갔으면 null.</summary>
    public string? ApprovalSkipReason { get; set; }
    /// <summary>사람이 봐야 할 것들. 저장을 막지는 않는다.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// 그 달 휴직자와 정해 둔 급여 금액. 🔴 <b>급여(단계8)·회계가 여기서 가져간다.</b>
/// </summary>
/// <remarks>
/// 사장님(2026-08-13): <i>"급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함"</i> /
/// <i>"각 고객사 니즈나 사정도 부합시킬 수 있고."</i>
///
/// 그 말이 맞다. 회사마다 육아휴직에 얹어주는 곳·무급인 곳·몇 달만 주는 곳이 다 다른데,
/// 자동 계산으로는 못 맞춘다. <b>금액을 직접 받으면 어떤 회사든 그대로 된다.</b>
/// ⇒ 급여는 이 금액을 <b>그대로 쓰기만</b> 하고 다시 계산하지 않는다.
/// </remarks>
public sealed class AbsencePayDto
{
    public string AbsenceId { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? ActualReturnDate { get; set; }

    /// <summary>사람이 정해 둔 금액. 0 이면 무급.</summary>
    public decimal PayAmount { get; set; }
    public string? PayNote { get; set; }

    /// <summary>비고 — 육아·연수·교육 등.</summary>
    public string? Reason { get; set; }
    public string Status { get; set; } = "";
}

/// <summary>복직 처리 요청.</summary>
public sealed class ReturnFromAbsenceRequest
{
    public string AbsenceId { get; set; } = "";
    /// <summary>실제 복직일. 예정보다 이르거나 늦을 수 있다.</summary>
    public DateTime ActualReturnDate { get; set; }
    public string? Note { get; set; }
}

/// <summary>휴직 진행 단계의 화면 이름.</summary>
public static class AbsenceStatusLabels
{
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Active = "active";
    public const string Returned = "returned";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [Draft] = "작성중",
        [Pending] = "결재중",
        [Approved] = "승인(시작 전)",
        [Active] = "휴직중",
        [Returned] = "복직",
        [Rejected] = "반려",
        [Cancelled] = "취소",
    };

    public static IReadOnlyDictionary<string, string> All => Map;

    /// <summary>
    /// 모르는 값이 오면 <b>그 값을 그대로 돌려준다.</b>
    /// 뭉개면 잘못 들어간 값이 화면에서 정상으로 보인다.
    /// </summary>
    public static string Of(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (Map.TryGetValue(code, out var v) ? v : code);
}

/// <summary>
/// 직원의 재직 중 상태. 🔴 사장님(2026-08-13): <i>"상태처리 : 재직 휴직 연차"</i>.
/// </summary>
/// <remarks>
/// ⚠️ <c>is_active</c>(재직/퇴사)와 <b>층이 다르다.</b>
/// <c>is_active</c> 는 회사와 관계가 있느냐고, 이것은 <b>재직 중 지금 어떤 상태냐</b>다.
/// 종전엔 재직/퇴사 둘뿐이라 휴직자를 넣을 자리가 없었다 —
/// 퇴사로 두면 복직 근거가 사라지고, 재직으로 두면 급여가 그대로 나간다.
/// </remarks>
public static class WorkStatusLabels
{
    public const string Active = "active";
    public const string Absence = "absence";
    public const string Leave = "leave";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [Active] = "재직",
        [Absence] = "휴직",
        [Leave] = "연차",
    };

    public static IReadOnlyDictionary<string, string> All => Map;

    public static string Of(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (Map.TryGetValue(code, out var v) ? v : code);
}
