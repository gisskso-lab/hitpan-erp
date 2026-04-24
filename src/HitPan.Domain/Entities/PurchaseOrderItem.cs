using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public class PurchaseOrderItem : BaseEntity, ITenantEntity
{
    // EF가 Id↔po_item_id 매핑 + PoItemId Ignore. Id alias로 통일.
    public string PoItemId { get => Id; set => Id = value; }
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
