using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class SalesOrder : BaseEntity, ITenantEntity
{
    public string OrderId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string PartnerId { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string? Memo { get; set; }
}
