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
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1451 || ex.Number == 1452)
        {
            // FK 제약 위반 — 친절한 한국어 안내로 변환
            //   1451: 부모 레코드 삭제 실패 (자식이 참조 중)
            //   1452: 자식 INSERT 실패 (부모 없음)
            _logger.LogWarning(ex, "FK constraint violation: {Message}", ex.Message);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;  // 409 Conflict
            context.Response.ContentType = "application/json";

            var userMsg = ex.Number == 1451
                ? "연결된 거래·자료가 있어 삭제할 수 없습니다. 먼저 관련 거래를 정리해주세요."
                : "참조하는 기준 정보가 존재하지 않습니다. 거래처·상품 등록 상태를 확인해주세요.";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { error = userMsg }));
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
