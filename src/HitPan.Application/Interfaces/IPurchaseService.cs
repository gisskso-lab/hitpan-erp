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

    /// <param name="includeReturns">
    /// 🔴 20260827작1 §8-B (사장님 결재) — 「반품포함」 조회.
    /// <para>
    /// 매입목록은 <b>매입일</b>, 반품목록은 <b>반품일</b> 로 각각 날짜창을 자른다.
    /// 7월 매입을 8월에 반품하면 매입 행이 창 밖이라 「반품」 표기가 <b>나타날 자리가 없다</b>
    /// (표기 로직은 정상 — 행이 화면에 없었을 뿐이다).
    /// </para>
    /// <para>
    /// 이 값이 <c>true</c> 면 <b>기간 안에 반품이 일어난 매입</b>을 매입일이 창 밖이어도 함께 준다.
    /// 기본값 <c>false</c> — 기존 호출자는 한 줄도 안 바뀐다(헌법 #1).
    /// </para>
    /// </param>
    Task<List<PurchaseReceiptListDto>> GetReceiptsAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default,
        bool includeReturns = false);

    Task<(string ReceiptId, string ReceiptNo)> ConvertOrderToReceiptAsync(
        string poId,
        string tenantId,
        CancellationToken ct = default);

    Task<(string ReturnId, string ReturnNo)> ConvertReceiptToReturnAsync(
        string receiptId,
        string tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// 지금까지 쓰인 매입반품 사유 목록 — 자율 입력값의 재사용 (20260825작16).
    /// 사장님 지시: <i>"반품처리 반품사유도 판매쪽 반품확인서와 마찬가지로 자유입력"</i>.
    /// </summary>
    Task<List<string>> GetPurchaseReturnReasonsAsync(
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
