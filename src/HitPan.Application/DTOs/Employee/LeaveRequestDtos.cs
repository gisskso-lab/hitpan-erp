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

    /// P1-2 봉합(2026-06-20): 반려 사유. DB엔 저장되는데 목록 조회에서 누락돼,
    /// 직원이 "왜 반려됐는지" 못 봤다. 목록에 내려보내 재신청 피드백을 준다.
    public string? RejectReason { get; set; }
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

/// <summary>
/// 작20260429 (사장님 결재): 대시보드 월간 연차 캘린더 응답 DTO.
/// 활성 사원 행 + 휴가 셀 목록.
/// </summary>
public sealed class LeaveCalendarDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int DayCount { get; set; }
    public List<LeaveCalendarRow> Rows { get; set; } = new();
    public int TotalLeaveCount { get; set; }
    public string? TopUserName { get; set; }
    public decimal TopUserDays { get; set; }
}

public sealed class LeaveCalendarRow
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
    public decimal AnnualLeaveRemaining => AnnualLeaveTotal - AnnualLeaveUsed;
    public List<LeaveCalendarCell> Cells { get; set; } = new();
}

public sealed class LeaveCalendarCell
{
    public DateTime Date { get; set; }
    public string LeaveType { get; set; } = "annual"; // annual / half / sick / official
    public decimal LeaveDays { get; set; }
    public string Status { get; set; } = "approved";  // pending / approved / rejected
}
