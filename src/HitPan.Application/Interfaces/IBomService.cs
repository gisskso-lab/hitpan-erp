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

    /// <summary>
    /// BOM 조립 직후 자재 부족·안전재고 위반 품목을 자동발주 후보로 반환.
    /// 사장님 지시 (2026-04-26): 자재가 안전재고 이하/0 이면 다이얼로그로 묻고
    /// OK 시 발주서를 즉시 생성. 판매 자동발주(SalesService)와 동일 DTO 사용.
    /// </summary>
    Task<List<DTOs.Sales.AutoOrderCandidateDto>> GetAssembleAutoOrderCandidatesAsync(
        string bomId,
        string tenantId,
        CancellationToken ct = default);
}
