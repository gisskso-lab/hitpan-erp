namespace HitPan.Web.Models;

/// <summary>
/// PUT api/settings/company 요청 본문에 사용하는 사업장(tenants) 정보 모델이다.
/// </summary>
public sealed class TenantCompanyModel
{
    /// <summary>상호(사용업체명).</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>대표자명.</summary>
    public string CeoName { get; set; } = string.Empty;

    /// <summary>사업자등록번호.</summary>
    public string BizNo { get; set; } = string.Empty;

    /// <summary>업태.</summary>
    public string? BizType { get; set; }

    /// <summary>업종.</summary>
    public string? BizItem { get; set; }

    /// <summary>대표 전화.</summary>
    public string? Tel { get; set; }

    /// <summary>팩스.</summary>
    public string? Fax { get; set; }

    /// <summary>이메일.</summary>
    public string? Email { get; set; }

    /// <summary>홈페이지.</summary>
    public string? Homepage { get; set; }

    /// <summary>우편번호.</summary>
    public string? ZipCode { get; set; }

    /// <summary>기본 주소.</summary>
    public string? Address { get; set; }

    /// <summary>상세주소.</summary>
    public string? AddressDetail { get; set; }

    /// <summary>법인등록번호.</summary>
    public string? CorpNo { get; set; }

    /// <summary>종사업장번호.</summary>
    public string? SubsidiaryNo { get; set; }
}

public class TenantSettingsModel
{
    public string StockEvalMethod { get; set; } = "moving_avg";

    public bool UseMultiWarehouse { get; set; }

    public bool StockShortageAlert { get; set; } = true;

    public bool AllowMinusStock { get; set; }

    public string PriceInputType { get; set; } = "net";

    public bool AutoVatAdjust { get; set; } = true;

    public string VatRoundType { get; set; } = "round";

    public decimal PriceARate { get; set; } = 1.00m;

    public decimal PriceBRate { get; set; } = 1.10m;

    public decimal PriceCRate { get; set; } = 1.20m;

    public decimal PriceDRate { get; set; } = 1.30m;

    public decimal PriceERate { get; set; } = 1.50m;

    public bool UseCreditLimit { get; set; } = true;

    public decimal CreditLimitAmount { get; set; } = 1000000;

    public bool ShowPurchasePrice { get; set; }

    public bool UseSalesByEmployee { get; set; } = true;

    public bool AllowForcePriceInput { get; set; } = true;

    public bool AllowForceVatInput { get; set; }

    public bool AllowZeroPrice { get; set; }

    public bool AllowPastEdit { get; set; }

    public bool AllowForceStockAdjust { get; set; } = true;

    public bool AllowCreditOverride { get; set; }

    public int PriceDeviationLimit { get; set; } = 50;

    public bool ForceEditRequirePassword { get; set; } = true;

    public bool UsePersonalInfoProtect { get; set; } = true;

    public string IndustryType { get; set; } = "retail";
}
