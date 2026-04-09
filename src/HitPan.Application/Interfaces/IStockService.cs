using HitPan.Application.DTOs.Stock;

namespace HitPan.Application.Interfaces;

public interface IStockService
{
    Task<IReadOnlyList<StockBalanceDto>> GetBalanceAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StockLedgerRow>> GetLedgerAsync(StockLedgerQueryRequest request, CancellationToken ct = default);
}
