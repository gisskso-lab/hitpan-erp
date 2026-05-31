using System.Data;
using Dapper;
using Microsoft.AspNetCore.Http;

namespace HitPan.API.Middleware;

// 첫 로그인 약관 4건 강제 동의 검증 미들웨어 (헌법 #24)
// TenantMiddleware 이후 가동 — JWT 인증 + tenant_id 박제 완료 가정.
// 미동의 시 /api/* 차단 (403 forbidden_terms_consent).
// bypass: /api/auth/terms/* + /api/auth/login + /api/auth/refresh + /api/auth/logout + Swagger·health·terms 자체.
public sealed class TermsConsentMiddleware
{
    private const string CurrentTermsVersion = "v2.0.0";

    private static readonly string[] BypassPrefixes = new[]
    {
        "/api/auth/terms",
        "/api/auth/login",
        "/api/auth/logout",
        "/api/auth/refresh",
        "/api/auth/me",
        "/api/backoffice/auth",
        "/api/tenants/setup",
        "/health",
        "/swagger"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<TermsConsentMiddleware> _logger;

    public TermsConsentMiddleware(RequestDelegate next, ILogger<TermsConsentMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IDbConnection db)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // API 외 (정적·Blazor WASM) 통과
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // bypass
        foreach (var prefix in BypassPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
        }

        // 인증 안 됐으면 TenantMiddleware가 이미 401 봉합. 여기서는 인증 가정.
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var tenantId = context.Items["TenantId"]?.ToString();
        var userId = context.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId))
        {
            await _next(context);
            return;
        }

        // 백오피스·플랫폼 계정은 약관 동의 면제 (본사 직원 = 별도 가도)
        var accountType = context.Items["AccountType"]?.ToString();
        if (!string.Equals(accountType, "tenant", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(accountType))
        {
            await _next(context);
            return;
        }

        try
        {
            if (db.State != ConnectionState.Open) db.Open();

            var hasAgreed = await db.ExecuteScalarAsync<int>(new CommandDefinition(
                @"SELECT COUNT(*) FROM user_terms_consent
                  WHERE tenant_id=@Tid AND user_id=@Uid
                    AND terms_version=@Ver
                    AND agree_service=1 AND agree_privacy=1
                    AND agree_subscription=1 AND agree_data_ownership=1
                  LIMIT 1",
                new { Tid = tenantId, Uid = userId, Ver = CurrentTermsVersion },
                cancellationToken: context.RequestAborted)) > 0;

            if (!hasAgreed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    "{\"error\":\"forbidden_terms_consent\",\"required_version\":\"" + CurrentTermsVersion + "\",\"redirect\":\"/terms\"}",
                    context.RequestAborted);
                return;
            }
        }
        catch (Exception ex)
        {
            // 테이블 부재 등 인프라 사고 = 동의 강제 보류 (베타 1주차 정합, 헌법 #15 silent swallow 금지)
            _logger.LogWarning(ex, "Terms consent check skipped (infra fault): tenant={Tenant} user={User}", tenantId, userId);
        }

        await _next(context);
    }
}
