namespace HitPan.Application.DTOs.Tenant;

public sealed class TenantMeResponse
{
    public string TenantId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
