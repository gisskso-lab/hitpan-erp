using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public class Department : BaseEntity, ITenantEntity
{
    public string DeptId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? ParentDeptId { get; set; }
    public string DeptName { get; set; } = string.Empty;
    public string? DeptCode { get; set; }
    public short SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
