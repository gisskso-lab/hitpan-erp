using HitPan.Application.DTOs.Stock;

namespace HitPan.Application.Interfaces;

public interface IStockService
{
    Task<IReadOnlyList<StockBalanceDto>> GetBalanceAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StockLedgerRow>> GetLedgerAsync(StockLedgerQueryRequest request, CancellationToken ct = default);

    // 재고 실사·조정
    Task<StockAdjustResultDto> AdjustStockAsync(string tenantId, string userId, StockAdjustRequest req, CancellationToken ct = default);
    Task<List<StockAdjustResultDto>> GetAdjustHistoryAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default);

    // 재고 이송
    Task TransferStockAsync(string tenantId, string userId, StockTransferRequest req, CancellationToken ct = default);
    Task<List<StockTransferDto>> GetTransferHistoryAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default);

    // 창고 분리 현황
    Task<List<WarehouseSplitDto>> GetWarehouseSplitAsync(string tenantId, CancellationToken ct = default);
}
