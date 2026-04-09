using HitPan.Application.DTOs.Tenant;

namespace HitPan.Application.Interfaces;

public interface ITenantService
{
    Task<CreateTenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken ct = default);
}
