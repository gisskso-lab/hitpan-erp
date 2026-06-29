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

    // 기기 기반 라이선싱 (선택값)
    public string? DeviceFingerprint { get; set; }
    public string? DeviceType { get; set; }
    public string? DeviceName { get; set; }
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
    public string? DeviceId { get; set; }

    // 헌법 #24: 약관 v2.0.0 미동의 시 true (Login.razor에서 /terms 강제 이동)
    public bool RequiresTermsConsent { get; set; }

    // ── 고리2(A안): 업데이트 동의 Y/N 팝업 (Login.razor에서 UpdateAvailable=true면 팝업) ──
    public string? CurrentVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public string? LatestVersion { get; set; }
    public string? UpdateChannel { get; set; }
    public string? ConsentMessage { get; set; }
}

/// <summary>고리2 업데이트 동의 요청 바디 (POST api/auth/update-consent).</summary>
public sealed class UpdateConsentRequestDto
{
    [JsonPropertyName("updateVersion")]
    public string UpdateVersion { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
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
