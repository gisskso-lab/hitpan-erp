using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

/// <summary>
/// 견적서 헤더 엔티티를 정의한다.
/// </summary>
public class Quotation : BaseEntity, ITenantEntity
{
    /// <summary>
    /// 견적서 식별자(도메인 별칭)다.
    /// </summary>
    public string QuoteId { get; set; } = string.Empty;

    /// <summary>
    /// 테넌트 식별자다.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// 견적서 번호다.
    /// </summary>
    public string QuoteNo { get; set; } = string.Empty;

    /// <summary>
    /// 거래처 식별자다.
    /// </summary>
    public string PartnerId { get; set; } = string.Empty;

    /// <summary>
    /// 담당자 식별자다.
    /// </summary>
    public string? EmployeeId { get; set; }

    /// <summary>
    /// 견적일자다.
    /// </summary>
    public DateTime QuoteDate { get; set; }

    /// <summary>
    /// 유효기한이다.
    /// </summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>
    /// 견적서 상태다.
    /// </summary>
    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;

    /// <summary>
    /// 공급가액 합계다.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// 부가세 합계다.
    /// </summary>
    public decimal VatAmount { get; set; }

    /// <summary>
    /// 메모다.
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// 전환된 수주서 식별자다.
    /// </summary>
    public string? ConvertedOrderId { get; set; }

    /// <summary>
    /// 소프트 삭제 여부다.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// 삭제 시각이다.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 견적서 품목 목록이다.
    /// </summary>
    public List<QuotationItem> Items { get; set; } = new();
}
