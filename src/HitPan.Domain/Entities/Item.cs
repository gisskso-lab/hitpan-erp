using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class Item : BaseEntity, ITenantEntity
{
    public string ItemId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public ItemType ItemType { get; set; }
    public string? CategoryId { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal? StdPrice { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal? SafeStock { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Memo { get; set; }
}
