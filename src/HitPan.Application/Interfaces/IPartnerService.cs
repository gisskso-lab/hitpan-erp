using HitPan.Application.Common;
using HitPan.Application.DTOs.Partner;
using HitPan.Application.DTOs.Sales;

namespace HitPan.Application.Interfaces;

public interface IPartnerService
{
    Task<PartnerBalanceDto?> GetBalanceAsync(string partnerId, CancellationToken ct = default);

    Task<List<SpecialPriceItemDto>> GetSpecialPricesAsync(string partnerId, string tenantId, CancellationToken ct = default);

    Task UpsertSpecialPriceAsync(string partnerId, SpecialPriceUpsertDto dto, string tenantId, string userId, CancellationToken ct = default);

    Task DeleteSpecialPriceAsync(string partnerId, string itemId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 단가 참고값 4종(업체특별단가·최종단가·표준단가·상품특별단가)을 한 번에 읽는다.
    /// 명세서 화면에서 <b>커서를 올렸을 때 보여주는 값</b>이다 (20260820작4 · 설계2).
    /// </summary>
    /// <param name="isPurchase">
    /// 🔴 <b>최종단가의 출처를 가른다.</b> 발주·매입·반품이면 <c>true</c>(산 값),
    /// 견적·수주·판매면 <c>false</c>(판 값). 섞이면 매입 화면에 판 가격이 뜬다.
    /// </param>
    /// <returns>업체·상품이 비면 <c>null</c>. 값이 없는 항목은 <b>0 이 아니라 <c>null</c></b> 이다.</returns>
    Task<PriceHintDto?> GetPriceHintAsync(
        string partnerId, string itemId, string tenantId, bool isPurchase, CancellationToken ct = default);

    Task<bool> IsAssignedPartnerAsync(string? employeeId, string partnerId, string tenantId, CancellationToken ct = default);

    Task<List<PartnerSearchDto>> SearchPartnersAsync(string tenantId, string keyword, CancellationToken ct = default);

    Task<List<PartnerListDto>> GetPartnerListAsync(string tenantId, string? search = null, string? type = null, CancellationToken ct = default);

    /// <summary>서버 페이지네이션 버전 (2026-05-13 야간 신규).</summary>
    Task<PagedResult<PartnerListDto>> GetPartnerListPagedAsync(string tenantId, PagedRequest req, string? type = null, CancellationToken ct = default);

    Task<PartnerDetailDto?> GetPartnerDetailAsync(string partnerId, string tenantId, CancellationToken ct = default);

    Task<string> CreatePartnerAsync(CreatePartnerDto dto, string tenantId, CancellationToken ct = default);

    Task UpdatePartnerAsync(string partnerId, UpdatePartnerDto dto, string tenantId, CancellationToken ct = default);

    Task DeletePartnerAsync(string partnerId, string tenantId, CancellationToken ct = default);

    Task<List<PartnerSpecialPriceDto>> GetPartnerSpecialPricesAsync(string partnerId, string tenantId, CancellationToken ct = default);

    Task UpsertPartnerSpecialPriceAsync(string partnerId, PartnerSpecialPriceDto dto, string tenantId, CancellationToken ct = default);

    Task DeletePartnerSpecialPriceByIdAsync(string priceId, string tenantId, CancellationToken ct = default);
}
