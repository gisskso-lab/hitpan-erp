namespace HitPan.Web.Models;

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
