using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

/// <summary>
/// 견적서 품목 엔티티를 정의한다.
/// </summary>
public class QuotationItem : BaseEntity
{
    /// <summary>
    /// 품목 식별자(도메인 별칭)다.
    /// </summary>
    // EF가 Id↔id 매핑 + QuotationItemId Ignore. Id alias로 통일.
    public string QuotationItemId { get => Id; set => Id = value; }

    /// <summary>
    /// 견적서 식별자다.
    /// </summary>
    public string QuoteId { get; set; } = string.Empty;

    /// <summary>
    /// 품목 식별자다.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// 규격이다.
    /// </summary>
    public string? Spec { get; set; }

    /// <summary>
    /// 단위다.
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// 수량이다.
    /// </summary>
    public decimal Qty { get; set; }

    /// <summary>
    /// 단가다.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// 할인율이다.
    /// </summary>
    public decimal DiscountRate { get; set; }

    /// <summary>
    /// 공급가액이다.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 부가세 금액이다.
    /// </summary>
    public decimal VatAmount { get; set; }

    /// <summary>
    /// 메모다.
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// 정렬 순서다.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 헤더 네비게이션이다.
    /// </summary>
    public Quotation? Quotation { get; set; }
}
