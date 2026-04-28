using System.Collections.Concurrent;
using System.Security.Claims;

namespace HitPan.API.Middleware;

public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> LoginAttempts = new();
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> ApiRequests = new();
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> WriteRequests = new();
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> ExportRequests = new();

    public RateLimitMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 작20260428이7 #4: 터널 환경(cloudflared/Cloudflare) 호환 키 식별.
        // 우선순위: 인증된 user_id → CF-Connecting-IP(Cloudflare) → X-Forwarded-For 첫 번째 → RemoteIpAddress.
        // 인증 사용자 단위로 카운트하면 IP 충돌(여러 사용자가 같은 터널/엣지 IP 공유)로 인한 오차단 제거.
        var key = ResolveRateLimitKey(context);
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        if (path.Contains("/api/auth/login", StringComparison.Ordinal))
        {
            // 로그인은 항상 IP 기반 (인증 전이라 user_id 없음).
            var ipKey = ResolveClientIp(context);
            // 베타 환경: 비번 헷갈려 여러 번 시도해도 OK + 동일 IP 다수 사용자(터널) 고려.
            if (!CheckRateLimit(LoginAttempts, ipKey, maxCount: 100, windowSeconds: 300))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "너무 많은 로그인 시도입니다. 5분 후 다시 시도해주세요."
                }).ConfigureAwait(false);
                return;
            }
        }

        // 쓰기 요청 (POST/PUT/DELETE) — 사용자당 분당 600회 (단일 사용자 화면 작업 충분).
        if (path.StartsWith("/api/", StringComparison.Ordinal) &&
            context.Request.Method is "POST" or "PUT" or "DELETE")
        {
            if (!CheckRateLimit(WriteRequests, key, maxCount: 600, windowSeconds: 60))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "쓰기 요청이 너무 많습니다. 잠시 후 다시 시도해주세요."
                }).ConfigureAwait(false);
                return;
            }
        }

        // 내보내기/리포트 — 사용자당 분당 30회 (PDF/엑셀 생성은 무거움).
        if (path.Contains("/export", StringComparison.Ordinal) ||
            path.Contains("/pdf", StringComparison.Ordinal) ||
            path.Contains("/excel", StringComparison.Ordinal))
        {
            if (!CheckRateLimit(ExportRequests, key, maxCount: 30, windowSeconds: 60))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "내보내기 요청이 너무 많습니다. 잠시 후 다시 시도해주세요."
                }).ConfigureAwait(false);
                return;
            }
        }

        // 전체 API — 사용자당 분당 3000회 (Blazor WASM은 한 화면에 다수 호출).
        if (path.StartsWith("/api/", StringComparison.Ordinal))
        {
            if (!CheckRateLimit(ApiRequests, key, maxCount: 3000, windowSeconds: 60))
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

    private static string ResolveRateLimitKey(HttpContext context)
    {
        // 인증된 사용자: user_id 기반 (멀티테넌트 안전: 같은 user_id는 같은 테넌트).
        var userId = context.User?.FindFirst("user_id")?.Value
                     ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return "u:" + userId;
        }
        return "ip:" + ResolveClientIp(context);
    }

    private static string ResolveClientIp(HttpContext context)
    {
        // Cloudflare 터널: CF-Connecting-IP가 진짜 클라이언트 IP.
        var cfIp = context.Request.Headers["CF-Connecting-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(cfIp))
        {
            return cfIp.Trim();
        }
        // 일반 리버스 프록시: X-Forwarded-For 첫 번째 IP.
        var xff = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(xff))
        {
            var first = xff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (first.Length > 0)
            {
                return first[0];
            }
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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
