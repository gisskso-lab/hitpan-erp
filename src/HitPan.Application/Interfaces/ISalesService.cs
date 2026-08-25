using HitPan.Application.DTOs.Sales;

namespace HitPan.Application.Interfaces;

public interface ISalesService
{
    Task<string> CreateOrderAsync(CreateSalesOrderRequest request, CancellationToken ct = default);
    /// <summary>
    /// 거래명세서를 생성한다. 수주 없이 들어오면 정합성을 위해 수주서를 자동 생성한다(헌법 #20).
    /// </summary>
    /// <returns>
    /// <c>AutoCreatedOrderNo</c> 는 <b>자동 생성했을 때만</b> 수주번호가 담긴다.
    /// 수주에서 전환된 정상 흐름이면 <c>null</c> — 호출자는 이 값으로 안내 문구를 가른다 (20260825작5).
    /// </returns>
    Task<(string Id, string DocumentNumber, string? AutoCreatedOrderNo)> CreateDeliveryAsync(CreateDeliveryRequest request, CancellationToken ct = default);
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

    // 봉합 (2026-06-22, 11차전 수주재편집): 수주(draft) 헤더/라인 재편집.
    Task UpdateOrderAsync(
        string orderId,
        UpdateSalesOrderRequest request,
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

    // ─────────────────────────────────────────────────────────────────────
    // 매출반품 — 13차 후순위 봉합(2026-06-22, A 매입반품 대칭 풀스택).
    // 고객이 판매분을 돌려보냄 → 확정 시 재고 IN(증가) + 매출 역분개.
    // 매입반품(IPurchaseService) 5메서드의 정확한 거울.
    // ─────────────────────────────────────────────────────────────────────

    // 매출반품 목록 (status 필터 포함 — 13차 메모리 "매출반품 status 필터" 핵심).
    Task<List<SalesReturnListDto>> GetSalesReturnsAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default);

    Task<SalesReturnDetailDto?> GetSalesReturnDetailAsync(
        string returnId,
        string tenantId,
        CancellationToken ct = default);

    Task<(string ReturnId, string ReturnNo)> CreateSalesReturnAsync(
        CreateSalesReturnRequest request,
        string tenantId,
        CancellationToken ct = default);

    Task UpdateSalesReturnAsync(
        string returnId,
        UpdateSalesReturnRequest request,
        string tenantId,
        CancellationToken ct = default);

    // 매출반품 확정 — draft → confirmed + 재고 IN + 매출 역분개(단일 트랜잭션).
    Task ConfirmSalesReturnAsync(
        string returnId,
        string tenantId,
        string? employeeId,
        CancellationToken ct = default);

    // 매출반품 취소 — confirmed → canceled + 재고 OUT(확정 IN 되돌림) + 매출 복원기표(단일 트랜잭션).
    //   15차 적대검증 15-P1 봉합: 잘못 확정한 반품을 원장 무결성 유지하며 되돌리는 경로.
    Task CancelSalesReturnAsync(
        string returnId,
        string tenantId,
        string? employeeId,
        CancellationToken ct = default);

    Task DeleteSalesReturnAsync(
        string returnId,
        string tenantId,
        CancellationToken ct = default);
}
