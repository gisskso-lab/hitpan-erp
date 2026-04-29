using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class Employee : BaseEntity, ITenantEntity
{
    public string EmployeeId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string EmpNo { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string? DeptId { get; set; }
    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public EmployeeType EmpType { get; set; }
    public DateTime JoinDate { get; set; }
    public DateTime? ResignDate { get; set; }
    public string? BirthDate { get; set; }
    public string? IdNoHash { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? BaseSalary { get; set; }
    public string Role { get; set; } = "sales_user";
    public bool IsActive { get; set; } = true;

    // 작20260429 연차 관리 (사장님 결재): employees 테이블에 컬럼 2개만 추가.
    // 잔여는 (Total - Used) 계산값 — DB 저장 X, 화면 표시만.
    // 별도 이력 테이블은 베타 후 정식 운영 시 마이그레이션 검토.
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
}
