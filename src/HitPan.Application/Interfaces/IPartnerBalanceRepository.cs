using HitPan.Application.DTOs.Partner;

namespace HitPan.Application.Interfaces;

public interface IPartnerBalanceRepository
{
    Task<PartnerBalanceDto?> GetBalanceAsync(string tenantId, string partnerId, CancellationToken ct = default);
}
