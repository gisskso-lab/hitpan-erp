namespace HitPan.Application.DTOs.Settings;

public sealed class UpdateTenantSettingsDto
{
    public bool AllowForcePriceInput { get; set; } = true;

    public bool AllowForceVatInput { get; set; }

    public bool AllowZeroPrice { get; set; }

    public bool AllowPastEdit { get; set; }

    public bool AllowForceStockAdjust { get; set; } = true;

    public bool AllowCreditOverride { get; set; }

    public int PriceDeviationLimit { get; set; } = 50;

    public bool ForceEditRequirePassword { get; set; } = true;

    /// <summary>비워 두면 기존 해시 유지, 빈 문자열이면 해시 제거, 값이 있으면 새로 해시 저장.</summary>
    public string? PastEditPassword { get; set; }
}
