using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public class PurchaseReceiptItem : BaseEntity, ITenantEntity
{
    public string ReceiptItemId { get; set; } = string.Empty;
    public string ReceiptId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? PoItemId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string WarehouseId { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}
