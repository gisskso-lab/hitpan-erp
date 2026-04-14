using HitPan.Application.DTOs.Sales;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 견적서 서비스 인터페이스를 정의한다.
/// </summary>
public interface IQuotationService
{
    /// <summary>
    /// 견적서 목록을 조회한다.
    /// </summary>
    /// <param name="tenantId">테넌트 식별자다.</param>
    /// <param name="from">시작일 필터다.</param>
    /// <param name="to">종료일 필터다.</param>
    /// <param name="partnerName">거래처명 필터다.</param>
    /// <param name="status">상태 필터다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>견적서 목록이다.</returns>
    Task<List<QuotationListDto>> GetListAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? partnerName = null,
        string? status = null,
        CancellationToken ct = default);

    /// <summary>
    /// 견적서 상세를 조회한다.
    /// </summary>
    /// <param name="tenantId">테넌트 식별자다.</param>
    /// <param name="quoteId">견적서 식별자다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>견적서 상세다.</returns>
    Task<QuotationDetailDto?> GetAsync(string tenantId, string quoteId, CancellationToken ct = default);

    /// <summary>
    /// 견적서를 생성한다.
    /// </summary>
    /// <param name="tenantId">테넌트 식별자다.</param>
    /// <param name="request">생성 요청 데이터다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>생성된 견적서 식별자다.</returns>
    Task<string> CreateAsync(string tenantId, CreateQuotationRequest request, CancellationToken ct = default);

    /// <summary>
    /// 견적서를 수정한다.
    /// </summary>
    /// <param name="tenantId">테넌트 식별자다.</param>
    /// <param name="quoteId">견적서 식별자다.</param>
    /// <param name="request">수정 요청 데이터다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>비동기 작업이다.</returns>
    Task UpdateAsync(string tenantId, string quoteId, UpdateQuotationRequest request, CancellationToken ct = default);

    /// <summary>
    /// 견적서를 소프트 삭제한다.
    /// </summary>
    /// <param name="tenantId">테넌트 식별자다.</param>
    /// <param name="quoteId">견적서 식별자다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>비동기 작업이다.</returns>
    Task DeleteAsync(string tenantId, string quoteId, CancellationToken ct = default);

    /// <summary>
    /// 견적서를 수주로 전환한다.
    /// </summary>
    /// <param name="tenantId">테넌트 식별자다.</param>
    /// <param name="quoteId">견적서 식별자다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>전환된 수주서 식별자다.</returns>
    Task<string> ConvertToSalesOrderAsync(string tenantId, string quoteId, CancellationToken ct = default);
}
