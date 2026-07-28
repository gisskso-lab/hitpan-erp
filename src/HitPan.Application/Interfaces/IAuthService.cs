using HitPan.Application.DTOs.Auth;

namespace HitPan.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default);

    /// <summary>
    /// Step-up 인증: 사용자 ID + 비밀번호로 본인 검증 (BCrypt).
    /// 민감 작업(사용자 정보 수정/비밀번호 초기화/비활성화/권한 변경) 전 재인증용.
    /// WO-20260430-9.
    /// </summary>
    Task<bool> VerifyOwnPasswordAsync(string userId, string password, CancellationToken ct = default);
}
