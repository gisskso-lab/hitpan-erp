using System.Collections.Concurrent;

namespace HitPan.API.Middleware;

public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> LoginAttempts = new();
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> ApiRequests = new();

    public RateLimitMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        if (path.Contains("/api/auth/login", StringComparison.Ordinal))
        {
            if (!CheckRateLimit(LoginAttempts, ip, maxCount: 10, windowSeconds: 300))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "너무 많은 로그인 시도입니다. 5분 후 다시 시도해주세요."
                }).ConfigureAwait(false);
                return;
            }
        }

        if (path.StartsWith("/api/", StringComparison.Ordinal))
        {
            if (!CheckRateLimit(ApiRequests, ip, maxCount: 300, windowSeconds: 60))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "요청이 너무 많습니다. 잠시 후 다시 시도해주세요."
                }).ConfigureAwait(false);
                return;
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool CheckRateLimit(
        ConcurrentDictionary<string, (int Count, DateTime Window)> store,
        string key,
        int maxCount,
        int windowSeconds)
    {
        var now = DateTime.UtcNow;
        var entry = store.GetOrAdd(key, _ => (0, now));

        if ((now - entry.Window).TotalSeconds > windowSeconds)
        {
            store[key] = (1, now);
            return true;
        }

        if (entry.Count >= maxCount)
        {
            return false;
        }

        store[key] = (entry.Count + 1, entry.Window);
        return true;
    }
}
