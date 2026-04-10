namespace HitPan.Application.DTOs.Partner;

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
