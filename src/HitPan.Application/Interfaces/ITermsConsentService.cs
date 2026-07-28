using HitPan.Application.DTOs.Auth;

namespace HitPan.Application.Interfaces;

public interface ITermsConsentService
{
    Task<TermsConsentResponse> ConsentAsync(string tenantId, string userId, string clientIp, string? userAgent, TermsConsentRequest request, CancellationToken ct = default);
    Task<TermsConsentStatus> GetStatusAsync(string tenantId, string userId, string requiredVersion, CancellationToken ct = default);
    Task<bool> HasAgreedAsync(string tenantId, string userId, string requiredVersion, CancellationToken ct = default);
}
