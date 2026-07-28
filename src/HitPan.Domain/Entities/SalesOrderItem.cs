using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public class SalesOrderItem : BaseEntity, ITenantEntity
{
    // EF가 Id↔order_item_id 매핑 + OrderItemId Ignore. Id alias로 통일.
    public string OrderItemId { get => Id; set => Id = value; }
    public string OrderId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public decimal DeliveredQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string ItemStatus { get; set; } = "pending";
}
