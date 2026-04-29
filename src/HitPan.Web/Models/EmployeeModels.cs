namespace HitPan.Web.Models;

/// <summary>
/// 사원 목록 테이블 행 모델이다.
/// </summary>
public sealed class EmployeeListItemModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmpNo { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string? DeptName { get; set; }
    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
    public bool IsActive { get; set; }
    public bool HasUserAccount { get; set; }

    // 작20260429 연차 관리 (사장님 결재): 그리드 인라인 편집·저장
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
    public decimal AnnualLeaveRemaining => AnnualLeaveTotal - AnnualLeaveUsed;
}

/// <summary>
/// 작20260429 연차 저장 요청 모델 (사장님 결재).
/// </summary>
public sealed class UpdateAnnualLeaveModel
{
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
}

/// <summary>
/// 작20260429 (사장님 결재): 대시보드 월간 연차 캘린더 응답 모델.
/// </summary>
public sealed class LeaveCalendarModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int DayCount { get; set; }
    public List<LeaveCalendarRowModel> Rows { get; set; } = new();
    public int TotalLeaveCount { get; set; }
    public string? TopUserName { get; set; }
    public decimal TopUserDays { get; set; }
}

public sealed class LeaveCalendarRowModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
    public decimal AnnualLeaveRemaining => AnnualLeaveTotal - AnnualLeaveUsed;
    public List<LeaveCalendarCellModel> Cells { get; set; } = new();
}

public sealed class LeaveCalendarCellModel
{
    public DateTime Date { get; set; }
    public string LeaveType { get; set; } = "annual";
    public decimal LeaveDays { get; set; }
    public string Status { get; set; } = "approved";
}

/// <summary>
/// 사원 입력 그리드 편집 모델이다.
/// </summary>
public sealed class EmployeeEditModel
{
    public string EmpName { get; set; } = string.Empty;
    public string? DeptId { get; set; }
    public string? DeptName { get; set; }
    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public string EmpType { get; set; } = "regular";
    public DateTime JoinDate { get; set; } = DateTime.Today;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
}

/// <summary>
/// 부서 드롭다운 모델이다.
/// </summary>
public sealed class DepartmentModel
{
    public string DeptId { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
}

/// <summary>
/// 연차 신청 목록 모델이다.
/// </summary>
public sealed class LeaveRequestModel
{
    public string RequestId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string LeaveType { get; set; } = "annual";
    public decimal LeaveDays { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = "pending";
    public string? Reason { get; set; }
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 사원 상세 패널 표시 모델이다.
/// </summary>
public sealed class EmployeeDetailModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string EmpNo { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string? DeptId { get; set; }
    public string? DeptName { get; set; }
    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public string EmpType { get; set; } = "regular";
    public DateTime JoinDate { get; set; }
    public DateTime? ResignDate { get; set; }
    public string? BirthDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
    public bool IsActive { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 연차 신청 생성 요청 모델이다.
/// </summary>
public sealed class CreateLeaveRequestModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string LeaveType { get; set; } = "annual";
    public decimal LeaveDays { get; set; } = 1.0m;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today;
    public string? Reason { get; set; }
}
