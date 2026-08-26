namespace HitPan.Application.DTOs.Payroll;

/// <summary>
/// 급여 명세. 작(2026-08-13) 그룹웨어 단계8.
/// </summary>
/// <remarks>
/// 🔴 <b>사장님이 정한 방식</b>(2026-08-13):
/// <i>"급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함"</i> /
/// <i>"각 고객사 니즈나 사정도 부합시킬 수 있고."</i>
///
/// ⇒ 4대보험 요율·간이세액표를 <b>우리가 계산하지 않는다.</b> 금액을 받는다.
/// 계산은 회사가 쓰던 방식(세무사·엑셀·공단 프로그램)을 그대로 쓰고,
/// 히트판은 <b>담고·명세서로 뽑고·이력을 남긴다.</b>
///
/// 🔴 <b>보호는 권한 계층이 한다</b>(사장님: <i>"권한 계층분리로 급여를 관리해도 충분히 됨"</i>).
/// 금액은 평문이고 <c>menu_code='PAYROLL'</c> 권한으로 막는다 —
/// 컬럼 암호화는 DB 파일을 훔쳐갔을 때만 의미가 있어 정작 내부자 열람을 못 막는다.
/// </remarks>
public sealed class PayrollSlipDto
{
    public string SlipId { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string? DeptName { get; set; }

    public int PayYear { get; set; }
    public int PayMonth { get; set; }
    /// <summary>실제 지급일. 귀속월과 다르다(2월분을 3월에 주는 회사가 많다).</summary>
    public DateTime? PayDate { get; set; }

    public decimal TotalPayment { get; set; }
    public decimal TotalDeduct { get; set; }
    public decimal NetPayment { get; set; }

    public string Status { get; set; } = "draft";
    public string StatusDisplay => PayrollStatusLabels.Of(Status);

    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>이 달에 휴직이 걸려 있으면 그 건(단계6 연동).</summary>
    public string? AbsenceId { get; set; }

    public string? Memo { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<PayrollSlipLineDto> Lines { get; set; } = new();

    /// <summary>"2026년 8월분" 처럼 사람이 읽는 표기.</summary>
    public string PeriodDisplay => $"{PayYear}년 {PayMonth}월분";

    /// <summary>확정 전에만 고칠 수 있다. 확정한 급여가 뒤에서 바뀌면 명세서가 거짓이 된다.</summary>
    public bool CanEdit => Status is "draft";
    public bool CanConfirm => Status is "draft";
    public bool CanCancel => Status is "draft" or "confirmed";
}

/// <summary>
/// 급여 항목 한 줄. 🔴 <b>이름도 사람이 적는다.</b>
/// </summary>
/// <remarks>
/// 회사마다 수당이 다르다 — 식대·차량유지비·직책수당 / 야근수당·근속수당.
/// 칸을 고정하면(기본급·수당1·수당2…) 회사가 늘리고 싶을 때 못 늘린다.
/// </remarks>
public sealed class PayrollSlipLineDto
{
    public string LineId { get; set; } = "";
    public string SlipId { get; set; } = "";

    /// <summary>payment=지급 · deduct=공제</summary>
    public string LineType { get; set; } = "payment";
    public string ItemName { get; set; } = "";
    public decimal Amount { get; set; }

    public int SortOrder { get; set; }
    public bool IsTaxable { get; set; } = true;
    public string? Memo { get; set; }
}

/// <summary>급여 명세 저장 요청. 🔴 금액을 전부 사람이 넣는다.</summary>
public sealed class SavePayrollSlipRequest
{
    public string? SlipId { get; set; }
    public string EmployeeId { get; set; } = "";
    public int PayYear { get; set; }
    public int PayMonth { get; set; }
    public DateTime? PayDate { get; set; }
    public string? Memo { get; set; }

    /// <summary>
    /// 항목들. 🔴 합계는 <b>서버가 이 줄들을 더해서</b> 낸다 —
    /// 화면이 보내온 합계를 믿으면 줄과 합계가 어긋난 명세가 저장된다.
    /// </summary>
    public List<PayrollSlipLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 그 달 급여를 만들 때 <b>참고할 것들</b>. 🔴 자동으로 채우지 않는다.
/// </summary>
/// <remarks>
/// 사장님: <i>"휴직시 급여 : 텍스트 박스로 수동입력 → 그러면 자연스럽게 급여, 회계이슈도 해결될듯"</i>
///
/// 단계6 에서 <b>사람이 정해 둔 휴직 급여</b>를 가져와 보여준다.
/// ⚠️ <b>보여주기만</b> 한다 — 급여 명세에 자동으로 넣지 않는다.
/// 넣을지 말지는 담당자가 정한다(반자동 원칙).
/// </remarks>
public sealed class PayrollContextDto
{
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string? DeptName { get; set; }
    public string? Position { get; set; }

    /// <summary>재직 중 상태(재직·휴직·연차). 휴직자를 그냥 지나치면 안 된다.</summary>
    public string WorkStatus { get; set; } = "active";

    /// <summary>이 달에 걸린 휴직이 있으면 그 건.</summary>
    public string? AbsenceId { get; set; }
    /// <summary>단계6 에서 사람이 정해 둔 휴직 중 지급액. 있으면 참고한다.</summary>
    public decimal? AbsencePayAmount { get; set; }
    public string? AbsenceReason { get; set; }
    public DateTime? AbsenceStart { get; set; }
    public DateTime? AbsenceEnd { get; set; }

    /// <summary>이미 그 달 명세가 있으면 그 id. 이중 작성을 막는다.</summary>
    public string? ExistingSlipId { get; set; }
    public string? ExistingStatus { get; set; }

    /// <summary>담당자가 봐야 할 것들. 계산을 막지는 않는다.</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// 퇴직금. 🔴 <b>금액을 사람이 넣는다.</b> 법정 산식을 우리가 돌리지 않는다.
/// </summary>
/// <remarks>
/// 산식(평균임금 × 30일 × 재직일수/365)이 있지만:
/// <list type="bullet">
///   <item>평균임금에 상여·연차수당을 어떻게 넣는지가 회사마다 다르고 <b>다툼이 잦다</b></item>
///   <item>퇴직연금(DB·DC·IRP)이면 산식 자체가 다르다</item>
///   <item>틀리면 <b>법적 분쟁</b>이 된다</item>
/// </list>
/// ⚠️ 법정 퇴직금은 <b>최소</b>다 — 더 줄 순 있어도 덜 주면 위법이다.
/// 그래서 더더욱 우리가 계산해 넣으면 안 된다.
/// </remarks>
public sealed class SeverancePaymentDto
{
    public string SeveranceId { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string? DeptName { get; set; }

    public DateTime JoinDate { get; set; }
    public DateTime ResignDate { get; set; }
    public int ServiceDays { get; set; }

    public decimal AvgWage { get; set; }
    public decimal SeveranceAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }

    public string PayType { get; set; } = "direct";
    public string PayTypeDisplay => SeverancePayTypeLabels.Of(PayType);
    public DateTime? PayDate { get; set; }

    public string Status { get; set; } = "draft";
    public string StatusDisplay => PayrollStatusLabels.Of(Status);

    /// <summary>산정 근거. 분쟁 시 설명해야 한다.</summary>
    public string? CalcBasis { get; set; }
    public string? Memo { get; set; }

    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>근속 연수(참고 표기). 계산에 쓰지 않는다.</summary>
    public decimal ServiceYears => ServiceDays > 0 ? Math.Round(ServiceDays / 365.25m, 2) : 0m;

    public bool CanEdit => Status is "draft";
    public bool CanConfirm => Status is "draft";
}

/// <summary>퇴직금 저장 요청. 🔴 금액을 전부 사람이 넣는다.</summary>
public sealed class SaveSeveranceRequest
{
    public string? SeveranceId { get; set; }
    public string EmployeeId { get; set; } = "";
    public DateTime JoinDate { get; set; }
    public DateTime ResignDate { get; set; }
    public int ServiceDays { get; set; }

    public decimal AvgWage { get; set; }
    public decimal SeveranceAmount { get; set; }
    public decimal TaxAmount { get; set; }

    public string PayType { get; set; } = "direct";
    public DateTime? PayDate { get; set; }
    public string? CalcBasis { get; set; }
    public string? Memo { get; set; }
}

/// <summary>진행 단계 이름표.</summary>
public static class PayrollStatusLabels
{
    public const string Draft = "draft";
    public const string Confirmed = "confirmed";
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [Draft] = "작성중",
        [Confirmed] = "확정",
        [Paid] = "지급완료",
        [Cancelled] = "취소",
    };

    public static IReadOnlyDictionary<string, string> All => Map;

    /// <summary>모르는 값은 그대로 돌려준다 — 뭉개면 잘못된 값이 정상으로 보인다.</summary>
    public static string Of(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (Map.TryGetValue(code, out var v) ? v : code);
}

/// <summary>퇴직금 지급 방식 이름표. 회사가 고른다 — 우리가 판정하지 않는다.</summary>
public static class SeverancePayTypeLabels
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["direct"] = "회사 직접지급",
        ["db"] = "퇴직연금 DB형",
        ["dc"] = "퇴직연금 DC형",
        ["irp"] = "IRP 계좌",
    };

    public static IReadOnlyDictionary<string, string> All => Map;

    public static string Of(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (Map.TryGetValue(code, out var v) ? v : code);
}

/// <summary>급여 항목 구분 이름표.</summary>
public static class PayrollLineTypeLabels
{
    public const string Payment = "payment";
    public const string Deduct = "deduct";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [Payment] = "지급",
        [Deduct] = "공제",
    };

    public static IReadOnlyDictionary<string, string> All => Map;

    public static string Of(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (Map.TryGetValue(code, out var v) ? v : code);
}

// ══════════════════════════════════════════════════════════════════════
//  급여명세서 일괄 메일발송 — 20260826작6 W4
// ══════════════════════════════════════════════════════════════════════

/// <summary>발송 불가 사유. 🔴 <b>하나로 뭉치지 않는다</b> — 경리가 무엇을 고칠지 알아야 한다.</summary>
public static class PayslipSendBlockReasons
{
    /// <summary>직원 이메일이 비어 있다 ⇒ 사원관리에서 입력.</summary>
    public const string NoEmail = "no_email";

    /// <summary>결재가 아직 승인되지 않았다 ⇒ 대표이사 결재 대기.</summary>
    public const string NotApproved = "not_approved";

    /// <summary>명세서가 확정되지 않았다 ⇒ 급여 확정 먼저.</summary>
    public const string NotConfirmed = "not_confirmed";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [NoEmail] = "이메일 없음",
        [NotApproved] = "결재 미승인",
        [NotConfirmed] = "미확정",
    };

    public static string Of(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (Map.TryGetValue(code, out var v) ? v : code);
}

/// <summary>
/// 발송 대상 한 사람. <b>보낼 수 있는지</b> 와 <b>못 보내면 왜인지</b> 를 함께 담는다.
/// </summary>
/// <remarks>
/// 🔴 <c>RecipientEmail</c> 을 <b>그대로 노출</b>한다 — 경리가 눈으로 보고 오입력을 잡는다.
/// 오입력이면 <b>남의 메일함에 그 직원 연봉</b>이 간다(§4).
/// </remarks>
public sealed class PayslipSendTargetDto
{
    public string SlipId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? DeptName { get; set; }
    public string? RecipientEmail { get; set; }

    /// <summary>보낼 수 있는가.</summary>
    public bool CanSend { get; set; }

    /// <summary>못 보내는 사유 코드. 보낼 수 있으면 <c>null</c>.</summary>
    public string? BlockReason { get; set; }

    /// <summary>사유 한글 이름표.</summary>
    public string BlockReasonLabel => PayslipSendBlockReasons.Of(BlockReason);
}

/// <summary>
/// 발송 전 확인 화면이 받는 것. 🔴 <b>숫자만 주지 않는다</b> — 이름·주소·사유를 함께 준다(§4).
/// </summary>
public sealed class PayslipSendPreviewDto
{
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>결재 기능이 켜져 있는가. 꺼져 있으면 확정만으로 발송 가능하다.</summary>
    public bool ApprovalRequired { get; set; }

    public List<PayslipSendTargetDto> Targets { get; set; } = new();

    public int SendableCount => Targets.Count(t => t.CanSend);
    public int BlockedCount => Targets.Count(t => !t.CanSend);
}

/// <summary>일괄 발송 요청. 🔴 화면이 고른 <b>명세서 id 목록</b>을 받는다.</summary>
/// <remarks>
/// ⚠️ 서버는 이 목록을 <b>그대로 믿지 않는다</b> — 각 건을 다시 판정해서
/// 못 보낼 것이면 <b>안 보낸다</b>. 화면을 우회한 요청으로 미결재 명세서가 나가면 안 된다.
/// </remarks>
public sealed class SendPayslipMailRequest
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<string> SlipIds { get; set; } = new();
}

/// <summary>발송 결과 한 건.</summary>
public sealed class PayslipSendResultItemDto
{
    public string SlipId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? RecipientEmail { get; set; }
    public bool Success { get; set; }

    /// <summary>실패 사유(사람이 읽는 문장). 성공이면 <c>null</c>.</summary>
    public string? Error { get; set; }
}

/// <summary>일괄 발송 결과. 🔴 <b>뭉뚱그린 "발송 완료" 를 주지 않는다</b>(§4).</summary>
public sealed class SendPayslipMailResponse
{
    public List<PayslipSendResultItemDto> Items { get; set; } = new();

    public int SuccessCount => Items.Count(i => i.Success);
    public int FailedCount => Items.Count(i => !i.Success);

    /// <summary>실패한 명세서 id — 화면이 <b>실패분만 재발송</b>할 때 그대로 다시 넘긴다.</summary>
    public List<string> FailedSlipIds => Items.Where(i => !i.Success).Select(i => i.SlipId).ToList();
}
