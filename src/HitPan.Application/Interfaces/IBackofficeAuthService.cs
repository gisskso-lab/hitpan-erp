using HitPan.Application.DTOs.Backoffice;

namespace HitPan.Application.Interfaces;

public interface IBackofficeAuthService
{
    Task<AdminLoginResponse> AdminLoginAsync(AdminLoginRequest request, CancellationToken ct = default);
    Task<ResellerLoginResponse> ResellerLoginAsync(ResellerLoginRequest request, CancellationToken ct = default);
    Task<BackofficeRefreshResponse> RefreshAsync(BackofficeRefreshRequest request, CancellationToken ct = default);
}
