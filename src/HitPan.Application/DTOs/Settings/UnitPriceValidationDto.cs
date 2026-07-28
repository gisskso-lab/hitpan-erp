namespace HitPan.Application.DTOs.Settings;

public sealed class UnitPriceValidationDto
{
    public bool Ok { get; set; }

    public string? Message { get; set; }

    public decimal? DeviationPercent { get; set; }

    public int AppliedDeviationLimit { get; set; }
}
