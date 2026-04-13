using HitPan.Application.DTOs.Bom;

namespace HitPan.Application.Interfaces;

public interface IBomService
{
    Task<List<BomListDto>> GetListAsync(string tenantId, CancellationToken ct = default);
    Task<BomDetailDto?> GetAsync(string bomId, string tenantId, CancellationToken ct = default);
    Task<string> CreateAsync(CreateBomDto dto, string tenantId, CancellationToken ct = default);
    Task UpdateAsync(string bomId, CreateBomDto dto, string tenantId, CancellationToken ct = default);
    Task DeleteAsync(string bomId, string tenantId, CancellationToken ct = default);
    Task<BomAssembleCheckDto> CheckAssembleAsync(string bomId, decimal produceQty, string tenantId, CancellationToken ct = default);
    Task AssembleAsync(BomAssembleDto dto, string tenantId, string userId, CancellationToken ct = default);
    Task<List<StockAlertDto>> GetAlertsAsync(string tenantId, CancellationToken ct = default);
    Task DismissAlertAsync(string alertId, string tenantId, CancellationToken ct = default);
    Task OrderAlertAsync(string alertId, string tenantId, CancellationToken ct = default);
}
