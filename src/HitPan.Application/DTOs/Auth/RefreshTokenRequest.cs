using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Auth;

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
