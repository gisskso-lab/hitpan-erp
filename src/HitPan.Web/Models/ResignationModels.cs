namespace HitPan.Web.Models;

/// <summary>전자 퇴직서(사직서) — 작20260824작2 [4].</summary>
public class ResignationLetterModel
{
    public string ResignationId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? DeptName { get; set; }
    public string? PositionName { get; set; }

    public string ResignType { get; set; } = "voluntary";

    /// <summary>화면에 보이는 한글 이름. 🔴 고객 노출 — 영문 코드가 뜨면 안 된다.</summary>
    public string ResignTypeLabel { get; set; } = string.Empty;

    public DateTime DesiredDate { get; set; }
    public DateTime? ActualDate { get; set; }
    public string? Reason { get; set; }
    public string? HandoverTo { get; set; }
    public string? HandoverNote { get; set; }
    public string? ReturnItems { get; set; }

    public string Status { get; set; } = "draft";
    public string StatusLabel { get; set; } = string.Empty;

    public string? ApprovalId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectReason { get; set; }
    public DateTime? SignedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>사직서 작성·수정 요청.</summary>
public class SaveResignationModel
{
    public string? ResignationId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string ResignType { get; set; } = "voluntary";
    public DateTime DesiredDate { get; set; } = DateTime.Today.AddDays(30);
    public string? Reason { get; set; }
    public string? HandoverTo { get; set; }
    public string? HandoverNote { get; set; }
    public string? ReturnItems { get; set; }
}
