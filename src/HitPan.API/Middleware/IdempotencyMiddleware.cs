using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Contracts.Idempotency;

namespace HitPan.API.Middleware;

// Idempotency-Key 헤더 처리 미들웨어 (DESIGN_PRINCIPLES §5.3, 작업지시서 20260425작4)
//
// 동작:
//   1. [IdempotencyKey] 데코레이터가 붙은 액션에만 적용 (옵트인)
//   2. 헤더 없으면 400 BadRequest
//   3. 헤더 + 캐시 적중 + request_hash 일치 → 캐시된 응답 반환 (실제 핸들러 미호출)
//   4. 헤더 + 캐시 적중 + request_hash 불일치 → 409 Conflict (같은 키 다른 본문)
//   5. 헤더 + 캐시 미스 → 정상 처리 → 응답 캐싱 (TTL 24h)
//
// 데코레이터 미부착 액션은 본 미들웨어가 그대로 통과시킨다 (성능 영향 0).
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IDbConnection db)
    {
        var endpoint = context.GetEndpoint();
        var attr = endpoint?.Metadata.GetMetadata<IdempotencyKeyAttribute>();

        // 데코레이터 미부착 → 통과
        if (attr is null)
        {
            await _next(context);
            return;
        }

        var key = context.Request.Headers[IdempotencyConstants.HeaderName].ToString();

        // 헤더 누락
        if (string.IsNullOrWhiteSpace(key))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "idempotency_key_required",
                message = $"'{IdempotencyConstants.HeaderName}' 헤더가 필요합니다."
            });
            return;
        }

        if (key.Length is < IdempotencyConstants.MinKeyLength or > IdempotencyConstants.MaxKeyLength)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "idempotency_key_invalid_length",
                message = $"멱등키 길이는 {IdempotencyConstants.MinKeyLength}~{IdempotencyConstants.MaxKeyLength} 사이여야 합니다."
            });
            return;
        }

        // 테넌트 식별 (TenantMiddleware가 먼저 실행되어 Items["TenantId"] 채움)
        var tenantId = context.Items["TenantId"] as string ?? string.Empty;
        if (string.IsNullOrEmpty(tenantId))
        {
            // 인증 통과한 액션에서 tenantId가 비면 자체 모순 — TenantMiddleware가 잡았어야 함
            _logger.LogWarning("Idempotency: tenantId 비어있음. 라우트={Route}", context.Request.Path);
            await _next(context);
            return;
        }

        // 요청 본문 해시 (같은 키 + 다른 본문 차단용)
        context.Request.EnableBuffering();
        var requestHash = await ComputeBodyHashAsync(context.Request.Body);
        context.Request.Body.Position = 0;

        // 기존 캐시 조회
        var cached = await db.QueryFirstOrDefaultAsync<CachedRow>(
            """
            SELECT request_hash AS RequestHash,
                   status_code  AS StatusCode,
                   response_body AS ResponseBody,
                   expires_at   AS ExpiresAt
              FROM idempotency_keys
             WHERE tenant_id = @TenantId AND idempotency_key = @Key AND expires_at > NOW(6)
             LIMIT 1
            """,
            new { TenantId = tenantId, Key = key });

        if (cached is not null)
        {
            // 같은 키 + 다른 본문 → 충돌
            if (!string.Equals(cached.RequestHash, requestHash, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(IdempotencyConflictResponse.SameKeyDifferentBody());
                _logger.LogInformation(
                    "Idempotency 충돌: tenant={Tenant} key={Key} 라우트={Route}",
                    tenantId, key, context.Request.Path);
                return;
            }

            // 같은 키 + 같은 본문 → 캐시된 응답 반환
            context.Response.StatusCode = cached.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(cached.ResponseBody);
            _logger.LogInformation(
                "Idempotency 캐시 적중: tenant={Tenant} key={Key} 라우트={Route} status={Status}",
                tenantId, key, context.Request.Path, cached.StatusCode);
            return;
        }

        // 캐시 미스 → 응답을 가로채 캐싱
        var originalBody = context.Response.Body;
        await using var capture = new MemoryStream();
        context.Response.Body = capture;

        try
        {
            await _next(context);
        }
        catch
        {
            context.Response.Body = originalBody;
            throw;
        }

        // 캐싱 대상 상태 (2xx만 캐시)
        var shouldCache = context.Response.StatusCode is >= 200 and < 300;
        capture.Position = 0;
        var responseBody = await new StreamReader(capture, Encoding.UTF8).ReadToEndAsync();

        // 원본 응답 스트림에 그대로 복사
        capture.Position = 0;
        await capture.CopyToAsync(originalBody);
        context.Response.Body = originalBody;

        if (!shouldCache) return;

        var ttlSeconds = attr.OverrideTtlSeconds ?? (int)IdempotencyConstants.DefaultTtl.TotalSeconds;
        var expiresAt = DateTime.UtcNow.AddSeconds(ttlSeconds);

        // SkipCacheBody면 응답 본문은 비우고 메타만 저장 (재요청 시 본문 재생성 필요)
        var bodyToStore = attr.SkipCacheBody ? string.Empty : responseBody;

        try
        {
            await db.ExecuteAsync(
                """
                INSERT INTO idempotency_keys
                  (tenant_id, idempotency_key, endpoint, request_hash, status_code, response_body, expires_at)
                VALUES
                  (@TenantId, @Key, @Endpoint, @Hash, @Status, @Body, @ExpiresAt)
                ON DUPLICATE KEY UPDATE
                  expires_at = VALUES(expires_at)
                """,
                new
                {
                    TenantId = tenantId,
                    Key = key,
                    Endpoint = $"{context.Request.Method} {context.Request.Path}",
                    Hash = requestHash,
                    Status = context.Response.StatusCode,
                    Body = bodyToStore,
                    ExpiresAt = expiresAt
                });
        }
        catch (Exception ex)
        {
            // fail-open 정책: 캐시 저장 실패해도 사용자 응답은 이미 보냄
            _logger.LogWarning(ex,
                "Idempotency 캐시 저장 실패 (fail-open): tenant={Tenant} key={Key}",
                tenantId, key);
        }
    }

    private static async Task<string> ComputeBodyHashAsync(Stream body)
    {
        using var sha = SHA256.Create();
        var bytes = await sha.ComputeHashAsync(body);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record CachedRow(string RequestHash, int StatusCode, string ResponseBody, DateTime ExpiresAt);
}
