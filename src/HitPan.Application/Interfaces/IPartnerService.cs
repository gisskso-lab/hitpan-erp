using HitPan.Application.DTOs.Partner;
using HitPan.Application.DTOs.Sales;

namespace HitPan.Application.Interfaces;

public interface IPartnerService
{
    Task<PartnerBalanceDto?> GetBalanceAsync(string partnerId, CancellationToken ct = default);

    Task<List<SpecialPriceItemDto>> GetSpecialPricesAsync(string partnerId, string tenantId, CancellationToken ct = default);

    Task UpsertSpecialPriceAsync(string partnerId, SpecialPriceUpsertDto dto, string tenantId, string userId, CancellationToken ct = default);

    Task DeleteSpecialPriceAsync(string partnerId, string itemId, string tenantId, CancellationToken ct = default);

    Task<bool> IsAssignedPartnerAsync(string? employeeId, string partnerId, string tenantId, CancellationToken ct = default);

    Task<List<PartnerSearchDto>> SearchPartnersAsync(string tenantId, string keyword, CancellationToken ct = default);

    Task<List<PartnerListDto>> GetPartnerListAsync(string tenantId, string? search = null, string? type = null, CancellationToken ct = default);

    Task<PartnerDetailDto?> GetPartnerDetailAsync(string partnerId, string tenantId, CancellationToken ct = default);

    Task<string> CreatePartnerAsync(CreatePartnerDto dto, string tenantId, CancellationToken ct = default);

    Task UpdatePartnerAsync(string partnerId, UpdatePartnerDto dto, string tenantId, CancellationToken ct = default);

    Task DeletePartnerAsync(string partnerId, string tenantId, CancellationToken ct = default);

    Task<List<PartnerSpecialPriceDto>> GetPartnerSpecialPricesAsync(string partnerId, string tenantId, CancellationToken ct = default);

    Task UpsertPartnerSpecialPriceAsync(string partnerId, PartnerSpecialPriceDto dto, string tenantId, CancellationToken ct = default);

    Task DeletePartnerSpecialPriceByIdAsync(string priceId, string tenantId, CancellationToken ct = default);
}
