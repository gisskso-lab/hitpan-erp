using System.Text.Json.Serialization;

namespace HitPan.Web.Models;

public static class AuthStorageKeys
{
    public const string AccessToken = "hitpan_access_token";
    public const string RefreshToken = "hitpan_refresh_token";
    public const string UserDisplayName = "hitpan_user_name";
}

public sealed class LoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginApiResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool RedirectToWelcome { get; set; }
}

public sealed class RefreshTokenRequestDto
{
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class ApiErrorMessageDto
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class AuthLoginResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public LoginApiResponse? Data { get; init; }
}
