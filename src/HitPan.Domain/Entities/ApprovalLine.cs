using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public sealed class ApprovalLine : BaseEntity, ITenantEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ApprovalLineStep : ITenantEntity
{
    public string StepId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string ApprovalLineId { get; set; } = string.Empty;
    public int StepOrder { get; set; }
    public string PositionId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
