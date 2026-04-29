namespace HitPan.Application.DTOs.Employee;

/// <summary>
/// 사원 목록 조회 응답 DTO이다.
/// </summary>
public sealed class EmployeeListDto
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

    // 작20260429 연차 관리 (사장님 결재)
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
    public decimal AnnualLeaveRemaining => AnnualLeaveTotal - AnnualLeaveUsed;
}

/// <summary>
/// 사원 상세 조회 응답 DTO이다.
/// </summary>
public sealed class EmployeeDetailDto
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
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
    public decimal AnnualLeaveRemaining => AnnualLeaveTotal - AnnualLeaveUsed;
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 작20260429 연차 저장 요청 (사장님 결재).
/// 사원관리 그리드에서 부여·사용 일수만 단독 저장한다.
/// </summary>
public sealed class UpdateAnnualLeaveRequest
{
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
}

/// <summary>
/// 사원 생성 요청 DTO이다.
/// </summary>
public sealed class CreateEmployeeRequest
{
    public string EmpName { get; set; } = string.Empty;
    public string? DeptId { get; set; }
    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public string EmpType { get; set; } = "regular";
    public DateTime JoinDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
}

/// <summary>
/// 사원 수정 요청 DTO이다.
/// </summary>
public sealed class UpdateEmployeeRequest
{
    public string EmpName { get; set; } = string.Empty;
    public string? DeptId { get; set; }
    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public string EmpType { get; set; } = "regular";
    public DateTime JoinDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
}
