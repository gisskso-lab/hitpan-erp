using HitPan.Application.DTOs.Bom;

namespace HitPan.Application.Interfaces;

public interface IBomService
{
    Task<List<BomListDto>> GetListAsync(string tenantId, CancellationToken ct = default);
    Task<BomDetailDto?> GetAsync(string bomId, string tenantId, CancellationToken ct = default);
    Task<string> CreateAsync(CreateBomDto dto, string tenantId, CancellationToken ct = default);
    Task UpdateAsync(string bomId, CreateBomDto dto, string tenantId, CancellationToken ct = default);
    Task DeleteAsync(string bomId, string tenantId, CancellationToken ct = default);
    Task<string> RegisterBomAsItemAsync(string bomId, string itemType, string tenantId, CancellationToken ct = default);
    Task<BomAssembleCheckDto> CheckAssembleAsync(string bomId, decimal produceQty, string tenantId, CancellationToken ct = default);
    Task AssembleAsync(BomAssembleDto dto, string tenantId, string userId, CancellationToken ct = default);
    Task<List<StockAlertDto>> GetAlertsAsync(string tenantId, CancellationToken ct = default);
    Task DismissAlertAsync(string alertId, string tenantId, CancellationToken ct = default);
    Task OrderAlertAsync(string alertId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 상품마스터의 item_id 로 매핑된 BOM 헤더의 bom_id 를 찾는다.
    /// 해당 item_id 가 BOM 등록된 완제품/반제품일 때만 결과 반환.
    /// </summary>
    Task<string?> GetBomIdByItemAsync(string itemId, string tenantId, CancellationToken ct = default);
}
