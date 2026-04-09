using System.Security.Claims;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace HitPan.Infrastructure.Security;

public sealed class HttpContextTenant : ICurrentTenant
{
    private const string TenantIdClaimType = "tenant_id";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextTenant(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string TenantId =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(TenantIdClaimType) ?? string.Empty;
}
