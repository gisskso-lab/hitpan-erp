using HitPan.Application.DTOs.Partner;

namespace HitPan.Application.Interfaces;

public interface IPartnerService
{
    Task<PartnerBalanceDto?> GetBalanceAsync(string partnerId, CancellationToken ct = default);
}
