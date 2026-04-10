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
}
