using System.Security.Claims;
using HitPan.Infrastructure.Security;

namespace HitPan.API.Middleware;

public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CurrentTenant currentTenant)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/health")
            || path.StartsWithSegments("/swagger")
            || path.StartsWithSegments("/api/tenants/setup")
            || path.StartsWithSegments("/api/auth/login")
            || path.StartsWithSegments("/api/auth/refresh"))
        {
            await _next(context);
            return;
        }

        // Excel/PDF: opened in a new window with ?token=…; controller validates token and tenant.
        if (IsDocumentDownload(path))
        {
            await _next(context);
            return;
        }

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var accountType = context.User.FindFirstValue("account_type");
        var tenantId = context.User.FindFirstValue("tenant_id");
        var resellerId = context.User.FindFirstValue("reseller_id");
        var platformId = context.User.FindFirstValue("platform_id");
        var userId = context.User.FindFirstValue("user_id");
        var role = context.User.FindFirstValue("role");

        context.Items["AccountType"] = accountType;
        context.Items["TenantId"] = tenantId;
        context.Items["ResellerId"] = resellerId;
        context.Items["PlatformId"] = platformId;

        if (accountType == "reseller_admin" && string.IsNullOrEmpty(resellerId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
            return;
        }

        if ((accountType == "tenant_user" || accountType == "tenant_admin") && string.IsNullOrEmpty(tenantId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
            return;
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
            return;
        }

        currentTenant.Set(tenantId, userId ?? string.Empty, role ?? string.Empty);

        await _next(context);
    }

    private static bool IsDocumentDownload(PathString path)
    {
        var p = path.Value ?? string.Empty;
        if (!p.StartsWith("/api/documents/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return p.EndsWith("/excel", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("/pdf", StringComparison.OrdinalIgnoreCase);
    }
}
