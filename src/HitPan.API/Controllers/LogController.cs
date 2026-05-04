using Dapper;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace HitPan.API.Controllers;

/// <summary>
/// ERP 로그 조회 API — audit_trail(업무동작) + audit_logs(API요청).
/// 고객지원·장애 분석용. TenantAdminOnly 권한.
/// </summary>
[ApiController]
[Route("api/logs")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class LogController : HitPanControllerBase
{
    private readonly IDbConnection _db;
    private readonly ICurrentTenant _tenant;

    public LogController(IDbConnection db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>업무 동작 로그 — create/confirm/cancel 등 도메인 이벤트 기록.</summary>
    [HttpGet("audit-trail")]
    public async Task<IActionResult> GetAuditTrail(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? entityType,
        [FromQuery] string? actionType,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                a.log_id      AS LogId,
                a.user_id     AS UserId,
                a.action_type AS ActionType,
                a.entity_type AS EntityType,
                a.entity_id   AS EntityId,
                a.reason      AS Reason,
                a.before_value AS BeforeValue,
                a.after_value  AS AfterValue,
                a.created_at  AS CreatedAt
            FROM audit_trail a
            WHERE a.tenant_id = @TenantId
              AND (@From IS NULL OR a.created_at >= @From)
              AND (@To   IS NULL OR a.created_at <  @ToNext)
              AND (@EntityType IS NULL OR a.entity_type = @EntityType)
              AND (@ActionType IS NULL OR a.action_type = @ActionType)
            ORDER BY a.created_at DESC
            LIMIT @Limit
            """;

        var rows = await _db.QueryAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = _tenant.TenantId,
                From = from?.Date,
                ToNext = to?.Date.AddDays(1),
                EntityType = entityType,
                ActionType = actionType,
                Limit = Math.Min(limit, 500)
            },
            cancellationToken: ct));

        return Ok(rows);
    }

    /// <summary>API 요청 로그 — method/endpoint/status_code 기록.</summary>
    [HttpGet("api-requests")]
    public async Task<IActionResult> GetApiRequests(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? statusCode,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                a.log_id     AS LogId,
                a.user_id    AS UserId,
                a.method     AS Method,
                a.endpoint   AS Endpoint,
                a.status_code AS StatusCode,
                a.ip_address AS IpAddress,
                a.created_at AS CreatedAt
            FROM audit_logs a
            WHERE a.tenant_id = @TenantId
              AND (@From IS NULL OR a.created_at >= @From)
              AND (@To   IS NULL OR a.created_at <  @ToNext)
              AND (@StatusCode IS NULL OR a.status_code = @StatusCode)
            ORDER BY a.created_at DESC
            LIMIT @Limit
            """;

        var rows = await _db.QueryAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = _tenant.TenantId,
                From = from?.Date,
                ToNext = to?.Date.AddDays(1),
                StatusCode = statusCode,
                Limit = Math.Min(limit, 500)
            },
            cancellationToken: ct));

        return Ok(rows);
    }
}
