namespace HitPan.Application.DTOs.Stock;

public class StockBalanceDto
{
    public string ItemId { get; set; } = string.Empty;
    public string WarehouseId { get; set; } = string.Empty;
    public decimal BalanceQty { get; set; }
}
