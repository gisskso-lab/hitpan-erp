namespace HitPan.Web.Models;

/// <summary>
/// 휴직 화면 모델. 작(2026-08-13) 그룹웨어 단계6.
/// </summary>
/// <remarks>
/// 🔴 사장님(2026-08-13): <i>"복잡하게 생각할것 없이 그냥 휴직은 상태처리, 상태확인 정도로만"</i> /
/// <i>"휴직은 모든걸 다 수동으로."</i> / <i>"비고에 육아, 연수, 교육, 등 자유롭게 쓰면 됨"</i>
///
/// ⚠️ Web 은 Application 을 참조하지 않으므로 이름표가 <b>양쪽에 있다</b>.
/// 코드값을 바꾸면 두 곳을 같이 고쳐야 한다(게이트: AbsenceGuardTests 가 어긋남을 잡는다).
/// </remarks>
public sealed class AbsenceModel
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

    /// <summary>비고 — 육아·연수·교육 등 자유롭게 쓴다.</summary>
    public string? Reason { get; set; }

    /// <summary>🔴 휴직 중 지급할 금액. 사람이 직접 넣는다. 0 이면 무급.</summary>
    public decimal PayAmount { get; set; }
    public string? PayNote { get; set; }

    /// <summary>화면에 그대로 뿌리는 급여 표기.</summary>
    public string PayDisplay => PayAmount > 0 ? $"{PayAmount:#,0}원" : "무급";

    public string? ApprovalId { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; }

    public int TotalDays => (EndDate.Date - StartDate.Date).Days + 1;

    /// <summary>기간을 사람이 읽기 좋게. 실제 복직일이 있으면 함께 보여준다.</summary>
    public string PeriodDisplay
    {
        get
        {
            var basis = $"{StartDate:yyyy-MM-dd} ~ {EndDate:yyyy-MM-dd}";
            return ActualReturnDate is { } r && r.Date != EndDate.Date
                ? $"{basis} (실제 복직 {r:yyyy-MM-dd})"
                : basis;
        }
    }

    /// <summary>진행 단계에 따른 색. 화면이 상태를 한눈에 읽게 한다.</summary>
    public string StatusColor => Status switch
    {
        "active" => "Warning",
        "approved" => "Info",
        "returned" => "Success",
        "rejected" => "Error",
        "cancelled" => "Default",
        "pending" => "Primary",
        _ => "Default",
    };

    /// <summary>고칠 수 있는 상태인가. 승인 이후는 못 고친다.</summary>
    public bool CanEdit => Status is "draft" or "pending" or "rejected";
    public bool CanSubmit => Status is "draft" or "rejected";
    public bool CanApprove => Status is "draft" or "pending";
    public bool CanCancel => Status is "draft" or "pending" or "approved";
    public bool CanReturn => Status is "approved" or "active";
}

/// <summary>휴직 저장 요청. 🔴 전부 사람이 넣는다.</summary>
public sealed class SaveAbsenceModel
{
    public string? AbsenceId { get; set; }
    public string EmployeeId { get; set; } = "";
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today;

    /// <summary>비고 — 육아·연수·교육 등 자유롭게.</summary>
    public string? Reason { get; set; }

    /// <summary>휴직 중 지급할 금액. 0 이면 무급.</summary>
    public decimal PayAmount { get; set; }
    public string? PayNote { get; set; }

    public bool Submit { get; set; }
}

/// <summary>저장 결과. 결재가 실제로 올라갔는지를 사실대로 담는다.</summary>
public sealed class SaveAbsenceResultModel
{
    public string AbsenceId { get; set; } = "";
    public bool ApprovalCreated { get; set; }
    public string? ApprovalSkipReason { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>복직 처리 요청.</summary>
public sealed class ReturnFromAbsenceModel
{
    public string AbsenceId { get; set; } = "";
    public DateTime ActualReturnDate { get; set; } = DateTime.Today;
    public string? Note { get; set; }
}

/// <summary>진행 단계 이름표. ⚠️ Application 쪽 AbsenceStatusLabels 와 같아야 한다.</summary>
public static class AbsenceStatusLabels
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["draft"] = "작성중",
        ["pending"] = "결재중",
        ["approved"] = "승인(시작 전)",
        ["active"] = "휴직중",
        ["returned"] = "복직",
        ["rejected"] = "반려",
        ["cancelled"] = "취소",
    };

    public static IReadOnlyDictionary<string, string> All => Map;

    /// <summary>모르는 값은 그대로 돌려준다 — 뭉개면 잘못된 값이 정상으로 보인다.</summary>
    public static string Of(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (Map.TryGetValue(code, out var v) ? v : code);
}

/// <summary>
/// 직원의 재직 중 상태. 🔴 사장님(2026-08-13): <i>"상태처리 : 재직 휴직 연차"</i>.
/// ⚠️ Application 쪽 WorkStatusLabels 와 같아야 한다.
/// </summary>
public static class WorkStatusLabels
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["active"] = "재직",
        ["absence"] = "휴직",
        ["leave"] = "연차",
    };

    public static IReadOnlyDictionary<string, string> All => Map;

    public static string Of(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (Map.TryGetValue(code, out var v) ? v : code);
}
