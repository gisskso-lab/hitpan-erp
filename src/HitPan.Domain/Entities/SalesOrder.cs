using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class SalesOrder : BaseEntity, ITenantEntity
{
    // EF가 Id↔order_id 매핑 + OrderId Ignore. Id alias로 통일.
    public string OrderId { get => Id; set => Id = value; }
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

    /// <summary>원 견적서 quote_id (20260827작10 W2 · DB-114). 견적 없이 만든 수주는 NULL 이다.</summary>
    public string? QuotationId { get; set; }

    public bool IsAuto { get; set; } = false;
}
