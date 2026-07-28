using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class Warehouse : BaseEntity, ITenantEntity
{
    public string WarehouseId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string WhCode { get; set; } = string.Empty;
    public string WhName { get; set; } = string.Empty;
    public WarehouseType WhType { get; set; }
    public string? Location { get; set; }
    public bool IsActive { get; set; } = true;
}
