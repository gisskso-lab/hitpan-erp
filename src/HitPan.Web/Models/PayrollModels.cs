namespace HitPan.Web.Models;

/// <summary>
/// 급여 명세 화면 모델. 작(2026-08-13) 그룹웨어 단계8.
/// </summary>
/// <remarks>
/// 🔴 사장님(2026-08-13): <i>"급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함"</i>
///
/// ⚠️ Web 은 Application 을 참조하지 않으므로 이름표가 <b>양쪽에 있다</b>.
/// 코드값을 바꾸면 두 곳을 같이 고쳐야 한다(게이트: PayrollGuardTests 가 어긋남을 잡는다).
/// </remarks>
public sealed class PayrollSlipModel
{
    public string SlipId { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string? DeptName { get; set; }

    public int PayYear { get; set; }
    public int PayMonth { get; set; }
    public DateTime? PayDate { get; set; }

    public decimal TotalPayment { get; set; }
    public decimal TotalDeduct { get; set; }
    public decimal NetPayment { get; set; }

    public string Status { get; set; } = "draft";
    public string StatusDisplay => PayrollStatusLabels.Of(Status);

    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? AbsenceId { get; set; }
    public string? Memo { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<PayrollSlipLineModel> Lines { get; set; } = new();

    public string PeriodDisplay => $"{PayYear}년 {PayMonth}월분";

    public string StatusColor => Status switch
    {
        "confirmed" => "Info",
        "paid" => "Success",
        "cancelled" => "Default",
        _ => "Warning",
    };

    public bool CanEdit => Status is "draft";
    public bool CanConfirm => Status is "draft";
    public bool CanPay => Status is "confirmed";
    public bool CanCancel => Status is "draft" or "confirmed";

    /// <summary>휴직이 걸린 달인가. 화면이 표시해 담당자가 지나치지 않게 한다.</summary>
    public bool HasAbsence => !string.IsNullOrWhiteSpace(AbsenceId);
}

/// <summary>급여 항목 한 줄. 🔴 이름도 사람이 적는다.</summary>
public sealed class PayrollSlipLineModel
{
    public string LineId { get; set; } = "";
    public string SlipId { get; set; } = "";
    public string LineType { get; set; } = "payment";
    public string ItemName { get; set; } = "";
    public decimal Amount { get; set; }
    public int SortOrder { get; set; }
    public bool IsTaxable { get; set; } = true;
    public string? Memo { get; set; }
}

/// <summary>급여 명세 저장 요청.</summary>
public sealed class SavePayrollSlipModel
{
    public string? SlipId { get; set; }
    public string EmployeeId { get; set; } = "";
    public int PayYear { get; set; } = DateTime.Today.Year;
    public int PayMonth { get; set; } = DateTime.Today.Month;
    public DateTime? PayDate { get; set; }
    public string? Memo { get; set; }
    public List<PayrollSlipLineModel> Lines { get; set; } = new();
}

/// <summary>
/// 그 달 급여를 만들 때 참고할 것들. 🔴 자동으로 채우지 않는다.
/// </summary>
public sealed class PayrollContextModel
{
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string? DeptName { get; set; }
    public string? Position { get; set; }
    public string WorkStatus { get; set; } = "active";

    public string? AbsenceId { get; set; }
    public decimal? AbsencePayAmount { get; set; }
    public string? AbsenceReason { get; set; }
    public DateTime? AbsenceStart { get; set; }
    public DateTime? AbsenceEnd { get; set; }

    public string? ExistingSlipId { get; set; }
    public string? ExistingStatus { get; set; }

    public List<string> Notes { get; set; } = new();

    public string WorkStatusDisplay => WorkStatusLabels.Of(WorkStatus);
    public bool HasSlip => !string.IsNullOrWhiteSpace(ExistingSlipId);
    public bool IsOnAbsence => !string.IsNullOrWhiteSpace(AbsenceId);
}

/// <summary>퇴직금. 🔴 금액을 사람이 넣는다.</summary>
public sealed class SeverancePaymentModel
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

    public string? CalcBasis { get; set; }
    public string? Memo { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public decimal ServiceYears => ServiceDays > 0 ? Math.Round(ServiceDays / 365.25m, 2) : 0m;
    public bool CanEdit => Status is "draft";
    public bool CanConfirm => Status is "draft";
}

/// <summary>퇴직금 저장 요청.</summary>
public sealed class SaveSeveranceModel
{
    public string? SeveranceId { get; set; }
    public string EmployeeId { get; set; } = "";
    public DateTime JoinDate { get; set; } = DateTime.Today;
    public DateTime ResignDate { get; set; } = DateTime.Today;
    public int ServiceDays { get; set; }
    public decimal AvgWage { get; set; }
    public decimal SeveranceAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public string PayType { get; set; } = "direct";
    public DateTime? PayDate { get; set; }
    public string? CalcBasis { get; set; }
    public string? Memo { get; set; }
}

/// <summary>진행 단계 이름표. ⚠️ Application 쪽과 같아야 한다.</summary>
public static class PayrollStatusLabels
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["draft"] = "작성중",
        ["confirmed"] = "확정",
        ["paid"] = "지급완료",
        ["cancelled"] = "취소",
    };

    public static IReadOnlyDictionary<string, string> All => Map;

    /// <summary>모르는 값은 그대로 돌려준다 — 뭉개면 잘못된 값이 정상으로 보인다.</summary>
    public static string Of(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (Map.TryGetValue(code, out var v) ? v : code);
}

/// <summary>급여 항목 구분 이름표. ⚠️ Application 쪽과 같아야 한다.</summary>
public static class PayrollLineTypeLabels
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["payment"] = "지급",
        ["deduct"] = "공제",
    };

    public static IReadOnlyDictionary<string, string> All => Map;

    public static string Of(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (Map.TryGetValue(code, out var v) ? v : code);
}

/// <summary>퇴직금 지급 방식 이름표. ⚠️ Application 쪽과 같아야 한다.</summary>
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

// ══════════════════════════════════════════════════════════════════════
//  급여명세서 일괄 메일발송 — 20260826작6 W6
// ══════════════════════════════════════════════════════════════════════

/// <summary>발송 대상 한 사람 — <b>보낼 수 있는지</b>와 <b>못 보내면 왜인지</b>.</summary>
/// <remarks>
/// 🔴 <c>RecipientEmail</c> 을 <b>그대로 보여준다</b> — 경리가 눈으로 오입력을 잡는다.
/// 오입력이면 <b>남의 메일함에 그 직원 연봉</b>이 간다.
/// </remarks>
public class PayslipSendTargetModel
{
    public string SlipId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? DeptName { get; set; }
    public string? RecipientEmail { get; set; }
    public bool CanSend { get; set; }
    public string? BlockReason { get; set; }
    public string BlockReasonLabel { get; set; } = string.Empty;
}

/// <summary>발송 전 확인 화면이 받는 것.</summary>
public class PayslipSendPreviewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public bool ApprovalRequired { get; set; }
    public List<PayslipSendTargetModel> Targets { get; set; } = new();

    public int SendableCount => Targets.Count(t => t.CanSend);
    public int BlockedCount => Targets.Count(t => !t.CanSend);
}

/// <summary>발송 결과 한 건.</summary>
public class PayslipSendResultItemModel
{
    public string SlipId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? RecipientEmail { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// <summary>일괄 발송 결과. 🔴 <b>뭉뚱그린 "발송 완료" 가 아니다</b>.</summary>
public class SendPayslipMailResultModel
{
    public List<PayslipSendResultItemModel> Items { get; set; } = new();

    public int SuccessCount => Items.Count(i => i.Success);
    public int FailedCount => Items.Count(i => !i.Success);

    /// <summary>실패한 명세서 id — <b>실패분만 재발송</b>할 때 그대로 다시 넘긴다.</summary>
    public List<string> FailedSlipIds => Items.Where(i => !i.Success).Select(i => i.SlipId).ToList();
}
