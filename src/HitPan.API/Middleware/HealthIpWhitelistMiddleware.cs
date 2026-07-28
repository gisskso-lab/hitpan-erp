using System.Net;

namespace HitPan.API.Middleware;

/// <summary>
/// /health 엔드포인트를 허용된 IP만 접근하도록 제한 (RED-3 보강).
/// appsettings.json HealthCheck:AllowedIps 미설정 시 루프백(127.0.0.1, ::1)만 허용.
/// </summary>
public sealed class HealthIpWhitelistMiddleware(RequestDelegate next, IConfiguration config, ILogger<HealthIpWhitelistMiddleware> logger)
{
    private readonly HashSet<IPAddress> _allowed = BuildAllowed(config);

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            var remote = ctx.Connection.RemoteIpAddress;
            if (remote is null || !IsAllowed(remote))
            {
                logger.LogWarning("Health check blocked — remote IP {IP}", remote);
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("Forbidden");
                return;
            }
        }
        await next(ctx);
    }

    private bool IsAllowed(IPAddress remote)
    {
        // IPv4-mapped IPv6 주소(::ffff:x.x.x.x) 정규화
        var addr = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
        return _allowed.Contains(addr);
    }

    private static HashSet<IPAddress> BuildAllowed(IConfiguration config)
    {
        var set = new HashSet<IPAddress>
        {
            IPAddress.Loopback,    // 127.0.0.1
            IPAddress.IPv6Loopback // ::1
        };
        var ips = config.GetSection("HealthCheck:AllowedIps").Get<string[]>();
        if (ips is not null)
        {
            foreach (var ip in ips)
            {
                if (IPAddress.TryParse(ip.Trim(), out var addr))
                    set.Add(addr);
            }
        }
        return set;
    }
}
