using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Stock;

public class StockLedgerQueryRequest
{
    public string? ItemId { get; set; }
    public string? PartnerId { get; set; }
    public string? WarehouseId { get; set; }
    public string? EmployeeId { get; set; }

    [Required]
    public DateTime FromDate { get; set; }

    [Required]
    public DateTime ToDate { get; set; }

    [Required]
    public string LedgerType { get; set; } = string.Empty;
}
