namespace HitPan.Application.DTOs.Sales;

public sealed class BatchSaveResult
{
    public string? DeliveryId { get; set; }
    public string? DeliveryNo { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
