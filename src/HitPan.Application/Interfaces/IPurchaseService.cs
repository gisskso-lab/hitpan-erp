using HitPan.Application.DTOs.Purchase;

namespace HitPan.Application.Interfaces;

public interface IPurchaseService
{
    Task<string> CreateOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default);
    Task<string> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken ct = default);
    Task ConfirmReceiptAsync(string receiptId, ConfirmReceiptRequest request, CancellationToken ct = default);

    Task<List<PurchaseOrderListDto>> GetOrdersAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default);

    Task<List<PurchaseReceiptListDto>> GetReceiptsAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default);

    Task<(string ReceiptId, string ReceiptNo)> ConvertOrderToReceiptAsync(
        string poId,
        string tenantId,
        CancellationToken ct = default);

    Task<(string ReturnId, string ReturnNo)> ConvertReceiptToReturnAsync(
        string receiptId,
        string tenantId,
        CancellationToken ct = default);

    Task<List<PurchaseReturnListDto>> GetReturnsAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);

    Task ConfirmPurchaseReturnAsync(
        string returnId,
        string tenantId,
        string? employeeId,
        CancellationToken ct = default);

    // 매입반품 취소 — confirmed → canceled + 재고 IN(확정 OUT 되돌림) + 매입 복원기표(단일 트랜잭션).
    //   15차 적대검증 15-P1 봉합 (매출반품 취소 대칭).
    Task CancelPurchaseReturnAsync(
        string returnId,
        string tenantId,
        string? employeeId,
        CancellationToken ct = default);

    Task DeletePurchaseReturnAsync(
        string returnId,
        string tenantId,
        CancellationToken ct = default);

    Task<PurchaseReceiptDetailDto?> GetReceiptDetailAsync(
        string receiptId,
        string tenantId,
        CancellationToken ct = default);

    Task DeletePurchaseReceiptAsync(
        string receiptId,
        string tenantId,
        CancellationToken ct = default);

    Task<PurchaseOrderDetailDto?> GetOrderDetailAsync(
        string poId,
        string tenantId,
        CancellationToken ct = default);

    Task DeletePurchaseOrderAsync(
        string poId,
        string tenantId,
        CancellationToken ct = default);

    Task<PurchaseReturnDetailDto?> GetReturnDetailAsync(
        string returnId,
        string tenantId,
        CancellationToken ct = default);

    // P0 #1 — 매입반품 신규 작성 (헌법 #20 흐름 끊김 봉합).
    Task<(string ReturnId, string ReturnNo)> CreatePurchaseReturnAsync(
        CreatePurchaseReturnRequest request,
        string tenantId,
        CancellationToken ct = default);

    // P0 #1 — draft 상태 매입반품 수정.
    Task UpdatePurchaseReturnAsync(
        string returnId,
        UpdatePurchaseReturnRequest request,
        string tenantId,
        CancellationToken ct = default);
}
