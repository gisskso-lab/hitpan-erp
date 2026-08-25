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
            // 🔴 20260825작14 — 500 이 원인을 안 알려주는 것 자체가 결함이다.
            //
            //   [무엇을 겪고서] 반품확정 500 을 잡느라 작10·작12·작13 **세 차례**를 썼다.
            //     매번 다른 원인이었다(1054 → 1054 확대 → 1062). 그런데 화면에 뜨는 것은
            //     늘 똑같은 *"서버 오류가 발생했습니다"* 한 줄뿐이라, 사장님은 매번 같은 말을
            //     하실 수밖에 없었고 나는 매번 **추측으로 다음 후보를 골랐다.**
            //     로그에는 남지만 그 로그는 **고객 PC 안에 있다**(헌법 #30 — 본사 의존 0).
            //
            //   [고침] **추적번호**를 붙인다. 로그와 화면에 같은 번호가 찍히므로
            //     사장님이 번호 하나만 알려주시면 그 줄을 정확히 짚을 수 있다.
            //     ⚠️ 개발용어·스택·SQL 은 여전히 안 내보낸다(#23 · 고객 노출 금지).
            //       나가는 것은 **번호와 시각뿐**이고, 그것으로는 내부 구조를 알 수 없다.
            //
            //   ⚠️ 이것은 원인 규명이 아니라 **관측 가능성**이다. 원인은 여전히 로그에서 본다.
            //     다만 종전엔 "어느 로그 줄인지" 조차 못 찾아 시간을 버렸다.
            var traceId = context.TraceIdentifier;
            _logger.LogError(ex,
                "Unhandled error [{TraceId}] {Method} {Path} — {ExType}: {Message}",
                traceId, context.Request.Method, context.Request.Path, ex.GetType().Name, ex.Message);

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new
                {
                    error = $"서버 오류가 발생했습니다. 관리자에게 아래 번호를 알려주세요.\n오류번호: {traceId}",
                    traceId
                }));
        }
    }
}
