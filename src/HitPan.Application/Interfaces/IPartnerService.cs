using HitPan.Application.DTOs.Partner;

namespace HitPan.Application.Interfaces;

public interface IPartnerService
{
    Task<PartnerBalanceDto?> GetBalanceAsync(string partnerId, CancellationToken ct = default);
    Task<List<SpecialPriceItemDto>> GetSpecialPricesAsync(string partnerId, string tenantId, CancellationToken ct = default);
    Task UpsertSpecialPriceAsync(string partnerId, SpecialPriceUpsertDto dto, string tenantId, string userId, CancellationToken ct = default);
    Task DeleteSpecialPriceAsync(string partnerId, string itemId, string tenantId, CancellationToken ct = default);
    Task<bool> IsAssignedPartnerAsync(string? employeeId, string partnerId, string tenantId, CancellationToken ct = default);
}
