using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public class PurchaseOrderItem : BaseEntity, ITenantEntity
{
    public string PoItemId { get; set; } = string.Empty;
    public string PoId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string? WarehouseId { get; set; }
    public string ItemStatus { get; set; } = "pending";
}
