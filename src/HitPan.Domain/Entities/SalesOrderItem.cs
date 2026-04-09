using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public class SalesOrderItem : BaseEntity, ITenantEntity
{
    public string OrderItemId { get; set; } = string.Empty;
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
