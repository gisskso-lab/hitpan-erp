namespace HitPan.Application.DTOs.Stock;

/// <summary>재고현황 DTO — item_stock + items 조인</summary>
public class StockBalanceDto
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public string? ItemGroup { get; set; }
    public string WarehouseId { get; set; } = string.Empty;
    public string? WarehouseName { get; set; }
    public decimal CurrentQty { get; set; }
    public decimal AvgCost { get; set; }
    public decimal SafetyStock { get; set; }
    public decimal BalanceQty { get; set; }
}
