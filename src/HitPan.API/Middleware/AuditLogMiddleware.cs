using Dapper;
using MySqlConnector;

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
        catch
        {
            // 로깅 실패해도 요청은 이미 완료됨
        }
    }

    private static string BuildConnectionStringFromEnv()
    {
        var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
        var db = Environment.GetEnvironmentVariable("DB_NAME")
            ?? throw new InvalidOperationException("DB_NAME 환경변수 없음");
        var user = Environment.GetEnvironmentVariable("DB_USER")
            ?? throw new InvalidOperationException("DB_USER 환경변수 없음");
        var pwd = Environment.GetEnvironmentVariable("DB_PASSWORD")
            ?? throw new InvalidOperationException("DB_PASSWORD 환경변수 없음");
        return $"Server={host};Port={port};Database={db};User={user};Password={pwd};";
    }
}
