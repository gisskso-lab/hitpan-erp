using HitPan.Application.DTOs.Auth;

namespace HitPan.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}
