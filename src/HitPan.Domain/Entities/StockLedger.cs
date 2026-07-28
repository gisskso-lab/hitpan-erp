using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class StockLedger : BaseEntity, ITenantEntity
{
    public long LedgerId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string WarehouseId { get; set; } = string.Empty;
    public string? PartnerId { get; set; }
    public string? EmployeeId { get; set; }
    public DateTime LedgerDate { get; set; }
    public string Ym { get; set; } = string.Empty;
    public StockMoveType MoveType { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string? DocNo { get; set; }
    public decimal QtyIn { get; set; }
    public decimal QtyOut { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? SupplyAmount { get; set; }
    public string? Memo { get; set; }
}
