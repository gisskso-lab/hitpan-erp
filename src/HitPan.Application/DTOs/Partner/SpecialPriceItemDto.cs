namespace HitPan.Application.DTOs.Partner;

public class SpecialPriceItemDto
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal SpecialPrice { get; set; }
    public decimal StdPrice { get; set; }
    // 봉합 (2026-06-23, 19차 업체특별단가 할인율): 상품 특별단가와 대칭. 고정/할인 모드 + 할인율(%).
    public string PriceType { get; set; } = "fixed";
    public decimal? DiscountRate { get; set; }
    public decimal VsRatio { get; set; }
    public DateTime? LastSupplyDate { get; set; }
    public bool IsActive { get; set; } = true;
}
