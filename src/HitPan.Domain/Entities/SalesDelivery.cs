using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class SalesDelivery : BaseEntity, ITenantEntity
{
    // EF Configuration이 PK Id↔delivery_id 컬럼으로 매핑하고 DeliveryId 프로퍼티는 Ignore한다.
    // DB 조회(GetByIdAsync)로 받은 엔티티는 DeliveryId가 기본값(빈 문자열)이어서
    // 후속 MonthlySummaryGuard 검증에서 빈 값으로 터졌다(ArgumentException). → Id alias 로 통일.
    public string DeliveryId { get => Id; set => Id = value; }
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
