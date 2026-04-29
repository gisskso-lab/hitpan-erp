using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public sealed class Position : BaseEntity, ITenantEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
