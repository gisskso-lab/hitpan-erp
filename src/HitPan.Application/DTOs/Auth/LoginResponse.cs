namespace HitPan.Application.DTOs.Auth;

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>로그인 시점에 last_login_at 이 비어 있었으면 true (온보딩 라우팅용).</summary>
    public bool RedirectToWelcome { get; set; }
}
