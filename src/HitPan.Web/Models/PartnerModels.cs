namespace HitPan.Web.Models;

public class SpecialPriceItem
{
    private decimal _vsRatio;

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
    // 봉합 (2026-06-23, 19차 업체특별단가 할인율): 상품 특별단가와 대칭. 백엔드 PartnerSpecialPriceDto 와
    //   필드명(PriceType·DiscountRate) 일치 — 종전엔 이 둘이 없어 할인율 모드가 화면에서 통째 유실됐다.
    public string PriceType { get; set; } = "fixed";
    public decimal? DiscountRate { get; set; }
    public decimal VsRatio
    {
        get => StdPrice > 0 ? Math.Round(SpecialPrice / StdPrice * 100, 1) : _vsRatio;
        set => _vsRatio = value;
    }
    public DateTime? LastSupplyDate { get; set; }
    public bool IsActive { get; set; } = true;

    // UI only
    public bool IsEditing { get; set; }
    public string VsRatioClass =>
        VsRatio < 100 ? "ratio-down" :
        VsRatio > 100 ? "ratio-up" :
        "ratio-same";

    public string VsRatioLabel =>
        VsRatio == 0 ? "-" : $"{VsRatio:N1}%";
}

public class SpecialPriceUpsertDto
{
    public string PartnerId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal SpecialPrice { get; set; }
    public decimal StdPrice { get; set; }
    // 봉합 (2026-06-23, 19차 업체특별단가 할인율): 고정/할인 모드 + 할인율(%). 백엔드 UpsertPartnerSpecialPriceAsync 가 소비.
    public string PriceType { get; set; } = "fixed";
    public decimal? DiscountRate { get; set; }
    public DateTime? LastSupplyDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PartnerListRow
{
    public string PartnerId { get; set; } = "";

    public string PartnerCode { get; set; } = "";

    public string PartnerName { get; set; } = "";

    public string PartnerType { get; set; } = "";

    public string? BizNo { get; set; }

    public string? Tel { get; set; }

    public string PriceGrade { get; set; } = "A";

    public decimal CreditLimit { get; set; }

    public decimal Balance { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class PartnerDetailModel : PartnerListRow
{
    public string? CeoName { get; set; }
    public string? BizType { get; set; }
    public string? BizItem { get; set; }
    public string? Fax { get; set; }
    public string? ZipCode { get; set; }
    public string? Address { get; set; }
    public string? AddressDetail { get; set; }
    public string? Email { get; set; }
    public string? Homepage { get; set; }
    public string? ManagerName { get; set; }
    public string? ManagerTel { get; set; }
    public string TaxType { get; set; } = "taxable";
    public int PaymentTerms { get; set; } = 30;
    public string? Memo { get; set; }
    public int RowVersion { get; set; }
}
