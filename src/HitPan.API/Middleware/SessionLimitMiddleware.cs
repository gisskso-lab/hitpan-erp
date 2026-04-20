using System.Data;
using Dapper;
using Microsoft.Extensions.Caching.Memory;

namespace HitPan.API.Middleware;

/// <summary>동시 세션 제한 미들웨어 — 티어별 최대 접속 수 제한</summary>
public sealed class SessionLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionLimitMiddleware> _logger;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan TierCacheTtl = TimeSpan.FromMinutes(5);
    // 티어별 동시 세션 제한 (기본값)
    private static readonly Dictionary<string, int> TierLimits = new()
    {
        ["basic"] = 8,       // PC5 + 모바일3
        ["pro"] = 20,        // PC10 + 모바일10
        ["premium"] = 100,   // PC50 + 모바일무제한
        ["trial"] = 50,      // 체험판
        [""] = 50            // 기본값 (개발 모드)
    };

    public SessionLimitMiddleware(
        RequestDelegate next,
        ILogger<SessionLimitMiddleware> logger,
        IMemoryCache cache)
    {
        _next = next;
        _logger = logger;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext context, IDbConnection db)
    {
        // API 경로만 체크
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api")) { await _next(context); return; }

        // 인증 안 된 요청은 통과 (로그인/리프레시 등)
        if (context.User?.Identity?.IsAuthenticated != true) { await _next(context); return; }

        var tenantId = context.Items["TenantId"]?.ToString();
        var userId = context.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) { await _next(context); return; }

        try
        {
            // 현재 활성 세션 수 확인 (최근 30분 내 활동)
            if (db.State != ConnectionState.Open)
            {
                if (db is System.Data.Common.DbConnection c) await c.OpenAsync();
                else db.Open();
            }

            var activeCount = await db.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(DISTINCT user_id) FROM user_sessions WHERE tenant_id = @TenantId AND expires_at > NOW()",
                new { TenantId = tenantId });

            // 테넌트 티어 확인 — 5분 캐싱 (과금 주기 내 거의 불변)
            var tier = await _cache.GetOrCreateAsync($"tenant-tier:{tenantId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TierCacheTtl;
                return await db.QueryFirstOrDefaultAsync<string>(
                    "SELECT COALESCE(subscription_tier, '') FROM tenants WHERE tenant_id = @TenantId",
                    new { TenantId = tenantId }) ?? "";
            }) ?? "";

            var limit = TierLimits.GetValueOrDefault(tier.ToLower(), 50);

            if (activeCount > limit)
            {
                _logger.LogWarning(
                    "Session limit exceeded. Tenant={TenantId} User={UserId} Active={Active} Limit={Limit} Tier={Tier}",
                    tenantId, userId, activeCount, limit, tier);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    $"{{\"error\":\"동시 접속 제한 초과 ({activeCount}/{limit}). 다른 기기에서 로그아웃해주세요.\"}}");
                return;
            }
        }
        catch (Exception ex)
        {
            // 세션 체크 실패해도 요청은 통과 (가용성 우선) — 단, 반드시 로그 남김
            _logger.LogError(ex,
                "Session limit check failed. Tenant={TenantId} User={UserId}. Request passed through (availability priority).",
                tenantId, userId);
        }

        await _next(context);
    }
}
