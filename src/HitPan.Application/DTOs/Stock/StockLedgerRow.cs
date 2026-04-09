namespace HitPan.Application.DTOs.Stock;

public class StockLedgerRow
{
    public string? ItemId { get; set; }
    public string? PartnerId { get; set; }
    public string? WarehouseId { get; set; }
    public string? EmployeeId { get; set; }
    public DateTime? LedgerDate { get; set; }
    public string? Ym { get; set; }
    public string? SourceType { get; set; }
    public string? CreatedBy { get; set; }
    public decimal QtyIn { get; set; }
    public decimal QtyOut { get; set; }
    public decimal BalanceQty { get; set; }
}
