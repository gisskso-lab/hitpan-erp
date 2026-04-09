using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Tenant;

public class CreateTenantRequest
{
    [Required]
    [MaxLength(100)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    [StringLength(12, MinimumLength = 12)]
    public string BizNo { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string CeoName { get; set; } = string.Empty;

    public string? Tel { get; set; }

    public string? Address { get; set; }

    [Required]
    [EmailAddress]
    public string AdminEmail { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string AdminPassword { get; set; } = string.Empty;
}
