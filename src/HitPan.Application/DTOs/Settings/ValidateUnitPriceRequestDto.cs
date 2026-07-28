namespace HitPan.Application.DTOs.Settings;

public sealed class ValidateUnitPriceRequestDto
{
    public decimal UnitPrice { get; set; }

    public decimal ReferencePrice { get; set; }
}
