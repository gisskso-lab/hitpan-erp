namespace HitPan.Application.DTOs.Settings;

public sealed class TenantSettingsDto
{
    public string TenantId { get; set; } = string.Empty;

    public bool AllowForcePriceInput { get; set; } = true;

    public bool AllowForceVatInput { get; set; }

    public bool AllowZeroPrice { get; set; }

    public bool AllowPastEdit { get; set; }

    public bool HasPastEditPassword { get; set; }

    public bool AllowForceStockAdjust { get; set; } = true;

    public bool AllowCreditOverride { get; set; }

    public int PriceDeviationLimit { get; set; } = 50;

    public bool ForceEditRequirePassword { get; set; } = true;
}
