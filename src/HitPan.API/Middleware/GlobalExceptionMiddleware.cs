using System.Net;
using System.Text.Json;

namespace HitPan.API.Middleware;

/// <summary>전역 예외 처리 — 스택트레이스 노출 방지 + 일관된 에러 응답</summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (InvalidOperationException ex)
        {
            // 비즈니스 로직 예외 (월마감, 결재 권한, 재고 부족 등)
            _logger.LogWarning(ex, "Business error: {Message}", ex.Message);
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { error = ex.Message }));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Auth error: {Message}", ex.Message);
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { error = "인증이 필요합니다." }));
        }
        catch (Exception ex)
        {
            // 예상치 못한 서버 에러 — 스택트레이스 숨김
            _logger.LogError(ex, "Unhandled error: {Message}", ex.Message);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { error = "서버 오류가 발생했습니다. 관리자에게 문의해주세요." }));
        }
    }
}
