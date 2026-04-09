using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class SalesDelivery : BaseEntity, ITenantEntity
{
    public string DeliveryId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string DeliveryNo { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public SalesDeliveryStatus Status { get; set; } = SalesDeliveryStatus.Draft;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string? Memo { get; set; }
}
