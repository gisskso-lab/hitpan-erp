using HitPan.Web.Models;

namespace HitPan.Web.Services;

public interface IAuthService
{
    Task<AuthLoginResult> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<bool> RefreshAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
}
