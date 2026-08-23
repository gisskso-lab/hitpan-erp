namespace HitPan.Application.DTOs.Employee;

/// <summary>
/// 전자 퇴직서(사직서) — 작20260824작2 [4].
/// </summary>
/// <remarks>
/// 🔴 <b>관리자 퇴사 처리(<c>employees.is_resigned</c>)와 다른 자리다.</b>
/// 그쪽은 <b>처리한 결과</b>이고 이쪽은 <b>직원이 올리는 문서</b>다.
/// 문서가 수리되면 그때 기존 퇴사 처리가 돈다 — 그 로직은 건드리지 않는다.
/// </remarks>
public sealed class ResignationLetterDto
{
    public string ResignationId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? DeptName { get; set; }
    public string? PositionName { get; set; }

    /// <summary>자발(voluntary) · 권고사직(recommended) · 계약만료(expired) · 정년(retirement)</summary>
    public string ResignType { get; set; } = "voluntary";

    /// <summary>화면에 보이는 한글 이름. 🔴 고객 노출 — 영문 코드가 뜨면 안 된다.</summary>
    public string ResignTypeLabel { get; set; } = string.Empty;

    /// <summary>희망 퇴사일 — 직원이 적는 날.</summary>
    public DateTime DesiredDate { get; set; }

    /// <summary>실제 퇴사일 — 회사가 수리하며 정한 날. 희망일과 다를 수 있다.</summary>
    public DateTime? ActualDate { get; set; }

    public string? Reason { get; set; }
    public string? HandoverTo { get; set; }
    public string? HandoverNote { get; set; }
    public string? ReturnItems { get; set; }

    /// <summary>draft · pending · approved · rejected · completed · withdrawn</summary>
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
public sealed class SaveResignationRequest
{
    /// <summary>있으면 수정, 없으면 신규.</summary>
    public string? ResignationId { get; set; }

    /// <summary>
    /// 누구의 사직서인가.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>서버가 검증한다.</b> 일반 직원이 남의 사직서를 쓰지 못하게
    /// 자기 <c>employee_id</c> 로 강제 치환한다(2026-06-23 LV-W-03 과 같은 자리 —
    /// 그때 타인 명의 연차 신청이 가능했다).
    /// </remarks>
    public string EmployeeId { get; set; } = string.Empty;

    public string ResignType { get; set; } = "voluntary";
    public DateTime DesiredDate { get; set; }
    public string? Reason { get; set; }
    public string? HandoverTo { get; set; }
    public string? HandoverNote { get; set; }
    public string? ReturnItems { get; set; }
}

/// <summary>사직서 수리(승인) 요청 — 회사가 실제 퇴사일을 정한다.</summary>
public sealed class AcceptResignationRequest
{
    /// <summary>
    /// 실제 퇴사일.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>희망일을 그대로 쓰지 않는다.</b> 인수인계가 안 끝나 늦춰지는 일이 흔하다.
    /// 반자동 원칙 — 시스템이 단정하지 않고 사람에게 받는다(사장님 2026-08-12).
    /// </remarks>
    public DateTime ActualDate { get; set; }

    public string? Comment { get; set; }
}
