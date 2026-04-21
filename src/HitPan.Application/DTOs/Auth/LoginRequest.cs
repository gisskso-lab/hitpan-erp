using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Auth;

public class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    // ── 기기 기반 라이선싱용 (선택값; 미지원 클라이언트는 그대로 null) ──
    /// <summary>브라우저 지문 (SHA-256 또는 간이 해시).</summary>
    public string? DeviceFingerprint { get; set; }

    /// <summary>pc / mobile / tablet</summary>
    public string? DeviceType { get; set; }

    /// <summary>사용자가 붙인 기기 이름(선택).</summary>
    public string? DeviceName { get; set; }
}
