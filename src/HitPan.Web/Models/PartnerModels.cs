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
    public DateTime? LastSupplyDate { get; set; }
    public bool IsActive { get; set; } = true;
}
