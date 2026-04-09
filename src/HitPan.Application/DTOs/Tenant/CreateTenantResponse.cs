namespace HitPan.Application.DTOs.Tenant;

public class CreateTenantResponse
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
