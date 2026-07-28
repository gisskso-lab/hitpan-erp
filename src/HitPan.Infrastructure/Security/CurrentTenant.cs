using HitPan.Application.Interfaces;

namespace HitPan.Infrastructure.Security;

public sealed class CurrentTenant : ICurrentTenant
{
    public string TenantId { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public string AccountType { get; private set; } = string.Empty;

    public void Set(string tenantId, string userId, string role, string accountType = "")
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        AccountType = accountType;
    }
}
