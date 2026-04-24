using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class PurchaseReceipt : BaseEntity, ITenantEntity
{
    // EF가 Id↔receipt_id 컬럼 매핑 + ReceiptId Ignore. DB 조회 시 빈 값 사고 방지 — Id alias로 통일.
    public string ReceiptId { get => Id; set => Id = value; }
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
