namespace HitPan.Application.DTOs.Partner;

public class PartnerBalanceDto
{
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public decimal TotalSales { get; set; }
    public decimal TotalReceived { get; set; }
    public decimal ReceivableBalance { get; set; }
    public decimal TotalPurchase { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal PayableBalance { get; set; }
    public DateTime CalculatedAt { get; set; }
}
