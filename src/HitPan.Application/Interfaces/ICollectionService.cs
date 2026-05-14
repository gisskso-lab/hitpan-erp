using HitPan.Application.Common;
using HitPan.Application.DTOs.Approval;

namespace HitPan.Application.Interfaces;

/// <summary>수금·지급 서비스 인터페이스</summary>
public interface ICollectionService
{
    // ── 수금 ──
    Task<List<CollectionListDto>> GetCollectionsAsync(string tenantId, DateTime? from = null, DateTime? to = null, string? partnerId = null, CancellationToken ct = default);

    /// <summary>서버 페이지네이션 버전 (2026-05-13 야간 신규).</summary>
    Task<PagedResult<CollectionListDto>> GetCollectionsPagedAsync(string tenantId, PagedRequest req, DateTime? from = null, DateTime? to = null, string? partnerId = null, CancellationToken ct = default);
    Task<string> CreateCollectionAsync(CreateCollectionRequest request, string tenantId, string userId, CancellationToken ct = default);
    Task DeleteCollectionAsync(string collectionId, string tenantId, CancellationToken ct = default);

    // ── 지급 ──
    Task<List<PaymentListDto>> GetPaymentsAsync(string tenantId, DateTime? from = null, DateTime? to = null, string? partnerId = null, CancellationToken ct = default);
    Task<string> CreatePaymentAsync(CreatePaymentRequest request, string tenantId, string userId, CancellationToken ct = default);
    Task DeletePaymentAsync(string paymentId, string tenantId, CancellationToken ct = default);

    // ── 미수/미지급 정공법 (WS-20260427-04, 사장님 헌법 §20) ──
    Task<ReceivablesResponseDto> GetReceivablesAsync(string tenantId, CancellationToken ct = default);
    Task<PayablesResponseDto> GetPayablesAsync(string tenantId, CancellationToken ct = default);
}
