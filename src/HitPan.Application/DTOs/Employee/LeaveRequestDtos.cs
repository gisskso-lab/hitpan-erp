namespace HitPan.Application.DTOs.Employee;

/// <summary>
/// 연차 신청 목록 조회 응답 DTO이다.
/// </summary>
public sealed class LeaveRequestListDto
{
    public string RequestId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string LeaveType { get; set; } = "annual";
    public decimal LeaveDays { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 연차 신청 생성 요청 DTO이다.
/// </summary>
public sealed class CreateLeaveRequest
{
    public string EmployeeId { get; set; } = string.Empty;
    public string LeaveType { get; set; } = "annual";
    public decimal LeaveDays { get; set; } = 1.0m;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// 연차 신청 승인/반려 요청 DTO이다.
/// </summary>
public sealed class ApproveLeaveRequest
{
    public string RequestId { get; set; } = string.Empty;
    public bool Approved { get; set; }
    public string? RejectReason { get; set; }
}
