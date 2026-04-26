using HitPan.Application.DTOs.Sales;

namespace HitPan.Application.Interfaces;

public interface ISalesService
{
    Task<string> CreateOrderAsync(CreateSalesOrderRequest request, CancellationToken ct = default);
    Task<(string Id, string DocumentNumber)> CreateDeliveryAsync(CreateDeliveryRequest request, CancellationToken ct = default);
    Task ConfirmDeliveryAsync(string deliveryId, ConfirmDeliveryRequest request, CancellationToken ct = default);

    Task<DeliveryDetailDto?> GetDeliveryAsync(string deliveryId, string tenantId, CancellationToken ct = default);

    Task<List<DeliveryListDto>> GetDeliveriesAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? partnerName = null,
        string? status = null,
        CancellationToken ct = default);

    Task UpdateDeliveryAsync(
        string deliveryId,
        UpdateDeliveryDto dto,
        string tenantId,
        string userId,
        CancellationToken ct = default);

    Task DeleteDeliveryAsync(string deliveryId, string tenantId, CancellationToken ct = default);

    Task CancelConfirmedDeliveryAsync(string deliveryId, string tenantId, string? employeeId, CancellationToken ct = default);

    Task<List<SalesOrderListDto>> GetOrdersAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default);

    Task<(string DeliveryId, string DocumentNumber)> ConvertOrderToDeliveryAsync(
        string orderId,
        string tenantId,
        CancellationToken ct = default);

    Task<List<PartnerSearchDto>> SearchPartnersAsync(string tenantId, string keyword, CancellationToken ct = default);

    Task<SalesOrderDetailDto?> GetOrderDetailAsync(
        string orderId,
        string tenantId,
        CancellationToken ct = default);

    Task DeleteSalesOrderAsync(
        string orderId,
        string tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// 거래명세서 확정 직후 안전재고 위반·재고0 품목을 자동발주 후보로 반환.
    /// auto_order_enabled=1 이고 (current_qty &lt;= safety_stock OR current_qty &lt;= 0) 인 라인만.
    /// </summary>
    Task<List<AutoOrderCandidateDto>> GetAutoOrderCandidatesAsync(
        string deliveryId,
        string tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// 자동발주 — 사용자가 다이얼로그에서 OK한 품목을 발주서(status=draft) 한 건으로 즉시 생성.
    /// 공급처별로 발주서를 묶어 생성한다(공급처 미설정 품목은 스킵 + 사유 반환).
    /// autoReceive=true 이면 발주 생성 직후 매입전환 + 매입 확정까지 원클릭으로 완료.
    /// 사장님 지시 (2026-04-26): "자동발주 → 매입처리까지 원클릭".
    /// </summary>
    Task<List<AutoOrderResultDto>> CreateAutoOrdersAsync(
        IReadOnlyList<AutoOrderCandidateDto> candidates,
        string tenantId,
        bool autoReceive = false,
        CancellationToken ct = default);
}
