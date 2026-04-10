using HitPan.Application.DTOs.Partner;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

public class PartnerService : IPartnerService
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IPartnerBalanceRepository _partnerBalanceRepository;

    public PartnerService(ICurrentTenant currentTenant, IPartnerBalanceRepository partnerBalanceRepository)
    {
        _currentTenant = currentTenant;
        _partnerBalanceRepository = partnerBalanceRepository;
    }

    public Task<PartnerBalanceDto?> GetBalanceAsync(string partnerId, CancellationToken ct = default)
    {
        return _partnerBalanceRepository.GetBalanceAsync(_currentTenant.TenantId, partnerId, ct);
    }
}
