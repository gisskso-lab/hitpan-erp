using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public class SalesDeliveryItem : BaseEntity, ITenantEntity
{
    public string DeliveryItemId { get; set; } = string.Empty;
    public string DeliveryId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? OrderItemId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string WarehouseId { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}
