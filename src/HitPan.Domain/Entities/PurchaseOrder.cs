using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class PurchaseOrder : BaseEntity, ITenantEntity
{
    // EF가 Id↔po_id 매핑 + PoId Ignore. Id alias로 통일.
    public string PoId { get => Id; set => Id = value; }
    public string TenantId { get; set; } = string.Empty;
    public string PoNo { get; set; } = string.Empty;
    public string PartnerId { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public DateTime PoDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string? Memo { get; set; }
}
