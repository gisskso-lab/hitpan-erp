using Dapper;
using MySqlConnector;
using HitPan.Infrastructure.Configuration;

namespace HitPan.API.Middleware;

public sealed class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _connStr;

    public AuditLogMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _connStr = config.GetConnectionString("DefaultConnection")
            ?? BuildConnectionStringFromEnv();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context).ConfigureAwait(false);

        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            return;
        }

        if (path.Contains("swagger", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var userId = context.User?.FindFirst("user_id")?.Value;
            var tenantId = context.Items["TenantId"]?.ToString();
            var accountType = context.User?.FindFirst("account_type")?.Value;
            var ip = context.Connection.RemoteIpAddress?.ToString();
            var userAgent = context.Request.Headers.UserAgent.FirstOrDefault();

            await using var conn = new MySqlConnection(_connStr);
            await conn.OpenAsync(context.RequestAborted).ConfigureAwait(false);
            await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO audit_logs (
                      log_id, tenant_id, user_id,
                      account_type, ip_address,
                      method, endpoint,
                      status_code, user_agent,
                      created_at)
                    VALUES (
                      UUID(), @TenantId, @UserId,
                      @AccountType, @Ip,
                      @Method, @Endpoint,
                      @StatusCode, @UserAgent,
                      NOW(6))
                    """,
                    new
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        AccountType = accountType,
                        Ip = ip,
                        Method = context.Request.Method,
                        Endpoint = path,
                        StatusCode = context.Response.StatusCode,
                        UserAgent = userAgent
                    },
                    cancellationToken: context.RequestAborted)).ConfigureAwait(false);
        }
        catch (Exception logEx)
        {
            // 로깅 실패해도 요청은 이미 완료됨 (헌법 #15 정합 로그)
            Console.Error.WriteLine($"[AuditLogMiddleware] audit log insert failed: {logEx.Message}");
        }
    }

    // 봉합 2026-06-16 (사고: Sandbox 1.2.10 API 가도 실패):
    //   AuditLogMiddleware 영역이 환경변수 직접 읽음 → 1.2.6 봉합 (TenantConfigReader 도입) 누락.
    //   다른 4개 (Integrity·Idempotency·Auth·Infrastructure) 영역만 봉합 박혔는데 본 영역 누락 = PM 사고.
    //   봉합: db.conf 영역 직접 읽음 (TenantConfigReader 영역).
    private static string BuildConnectionStringFromEnv()
    {
        var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
        var port = TenantConfigReader.Get("DB_PORT") ?? "3306";
        var db = TenantConfigReader.GetRequired("DB_NAME");
        var user = TenantConfigReader.GetRequired("DB_USER");
        var pwd = TenantConfigReader.GetRequired("DB_PASSWORD");
        // GuidFormat=None — char(36) 을 Guid 로 돌려주면 string DTO 매핑이 터진다 (봉합 2026-08-12, PI-07).
        return $"Server={host};Port={port};Database={db};User={user};Password={pwd};GuidFormat=None;";
    }
}
