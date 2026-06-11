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
        // API 경로가 아니면 통과 (정적 파일, Blazor WASM 등)
        if (!path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (path.StartsWithSegments("/health")
            || path.StartsWithSegments("/swagger")
            || path.StartsWithSegments("/api/tenants/setup")
            || path.StartsWithSegments("/api/auth/login")
            || path.StartsWithSegments("/api/auth/refresh")
            || path.StartsWithSegments("/api/backoffice/auth")
            // 사장님 결재 저장 2026-06-02 모두결재 — 랜딩 가입·결제·설치 저장 = 인증 면제 (AllowAnonymous 저장 정합)
            || path.StartsWithSegments("/api/landing")
            || path.StartsWithSegments("/api/install")
            // 사장님 결재 저장 2026-06-08 모두결재 — 백오피스→ERP webhook 저장 (HMAC 서명·nonce 저장할 영역 자체 검증, 헌법 #35 정합)
            || path.StartsWithSegments("/api/internal"))
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
        context.Items["UserId"] = userId;
        context.Items["UserName"] = context.User.FindFirstValue("name");

        // reseller_id 없어도 tenant_id로 ERP 컨텍스트가 있으면 통과(JWT에 reseller_id 미포함 계정 대비)
        if (accountType == "reseller_admin" && string.IsNullOrEmpty(resellerId) && string.IsNullOrEmpty(tenantId))
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

        if (string.IsNullOrWhiteSpace(tenantId) && accountType != "platform_admin")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
            return;
        }

        currentTenant.Set(tenantId ?? string.Empty, userId ?? string.Empty, role ?? string.Empty, accountType ?? string.Empty);

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
