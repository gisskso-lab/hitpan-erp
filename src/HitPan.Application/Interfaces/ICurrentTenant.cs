namespace HitPan.Application.Interfaces;

public interface ICurrentTenant
{
    string TenantId { get; }
    string UserId { get; }
    string AccountType { get; }
}
