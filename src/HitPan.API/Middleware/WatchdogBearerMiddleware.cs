using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace HitPan.API.Middleware;

/// <summary>
/// 워치독 endpoint(/watchdog/*) 전용 Bearer 토큰 검증.
/// 토큰 = sha256(license_key + "|" + machine_guid + "|" + tenant_id_hash)
/// licenses 테이블에서 (license_key_hash, machine_guid_hash) 매칭 확인.
/// 헌법 #22 정합: tenant_id 원본 비교 금지, hash만 사용.
/// </summary>
public sealed class WatchdogBearerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WatchdogBearerMiddleware> _logger;

    public WatchdogBearerMiddleware(RequestDelegate next, ILogger<WatchdogBearerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx, IDbConnection db)
    {
        if (!ctx.Request.Path.StartsWithSegments("/watchdog", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var auth = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("missing bearer");
            return;
        }

        var token = auth.Substring("Bearer ".Length).Trim();
        if (token.StartsWith("sha256:", StringComparison.Ordinal))
            token = token.Substring(7);

        if (token.Length != 64)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("invalid token format");
            return;
        }

        try
        {
            if (db.State != ConnectionState.Open)
            {
                if (db is System.Data.Common.DbConnection c) await c.OpenAsync(ctx.RequestAborted);
                else db.Open();
            }

            const string sql = @"
                SELECT COUNT(*) FROM licenses
                WHERE token_hash = @TokenHash
                  AND status = 'active'
                  AND (expires_at IS NULL OR expires_at > UTC_TIMESTAMP())
                LIMIT 1;";

            var ok = await db.ExecuteScalarAsync<int>(sql, new { TokenHash = token });
            if (ok == 0)
            {
                _logger.LogWarning("[WatchdogBearer] invalid token from {IP}", ctx.Connection.RemoteIpAddress);
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("unauthorized");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WatchdogBearer] DB lookup failure");
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsync("service unavailable");
            return;
        }

        await _next(ctx);
    }

    /// <summary>고객 PC 워치독이 발신할 토큰 생성 (테스트용 헬퍼).</summary>
    public static string GenerateToken(string licenseKey, string machineGuid, string tenantIdHash)
    {
        var input = $"{licenseKey}|{machineGuid}|{tenantIdHash}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
