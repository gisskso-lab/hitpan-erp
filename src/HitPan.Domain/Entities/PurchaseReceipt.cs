using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class PurchaseReceipt : BaseEntity, ITenantEntity
{
    public string ReceiptId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ReceiptNo { get; set; } = string.Empty;
    public string? PoId { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public PurchaseReceiptStatus Status { get; set; } = PurchaseReceiptStatus.Draft;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string? Memo { get; set; }
}
