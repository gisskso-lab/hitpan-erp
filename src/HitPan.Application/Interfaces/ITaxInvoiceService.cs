using HitPan.Contracts.Sales;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 세금계산서 서비스 — 3계층 1계층 (내부 마킹).
/// 작업지시서: 20260425작2 / DESIGN_PRINCIPLES §7
/// </summary>
public interface ITaxInvoiceService
{
    /// <summary>거래명세서 → 계산서 발행 (1계층). 멱등성: 같은 delivery_id에 두 번 발행 시 두 번째 실패.</summary>
    Task<TaxInvoiceResponse> IssueAsync(
        IssueTaxInvoiceRequest request,
        string tenantId,
        string userId,
        string? idempotencyKey,
        CancellationToken ct = default);

    /// <summary>계산서 단건 조회</summary>
    Task<TaxInvoiceResponse?> GetAsync(string invoiceId, string tenantId, CancellationToken ct = default);

    /// <summary>목록 조회 (필터: 기간·거래처)</summary>
    Task<List<TaxInvoiceListItem>> ListAsync(
        string tenantId,
        DateTime? from,
        DateTime? to,
        string? partnerId,
        CancellationToken ct = default);

    /// <summary>계산서 취소. 역분개·monthly_summary 차감 별도 작업 (본 라운드 비범위 — 작2 §3).</summary>
    Task<CancelTaxInvoiceResponse> CancelAsync(
        string invoiceId,
        CancelTaxInvoiceRequest request,
        string tenantId,
        string userId,
        CancellationToken ct = default);
}
