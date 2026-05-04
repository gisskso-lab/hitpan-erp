using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Backoffice;

// ── 본사 관리자 로그인 ──────────────────────────────────────────
public class AdminLoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class AdminLoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Role { get; set; } = string.Empty;
    public string AdminName { get; set; } = string.Empty;
    public string AdminId { get; set; } = string.Empty;
    public string AccountType { get; set; } = "platform_admin";
}

// ── 대리점 계정 로그인 ─────────────────────────────────────────
public class ResellerLoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class ResellerLoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Role { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string ResellerId { get; set; } = string.Empty;
    public string ResellerName { get; set; } = string.Empty;
    public string AccountType { get; set; } = "reseller_admin";
}

// ── 공통 토큰 갱신 ────────────────────────────────────────────
public class BackofficeRefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public class BackofficeRefreshResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
