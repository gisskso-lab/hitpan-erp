using Dapper;
using HitPan.Backoffice.API.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// Owner 영역 webhook outbox 조회/재발송 (사장님 결재 2026-06-04, W10)
//
// 권한: webhook.admin (owner/platform_owner 기본)
[ApiController]
[Route("api/backoffice/webhook")]
[Authorize]
public class WebhookAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<WebhookAdminController> _logger;

    public WebhookAdminController(IConfiguration config, ILogger<WebhookAdminController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("outbox")]
    [BoPermission("webhook.admin")]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var where = "";
            object? param = null;
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                where = "WHERE status = @Status";
                param = new { Status = status };
            }
            var rows = await db.QueryAsync<OutboxItem>($@"
                SELECT
                    outbox_id AS OutboxId,
                    CAST(tenant_id AS CHAR) AS TenantId,
                    event_type AS EventType,
                    target_url AS TargetUrl,
                    status AS Status,
                    retry_count AS RetryCount,
                    last_error AS LastError,
                    next_retry_at AS NextRetryAt,
                    created_at AS CreatedAt,
                    sent_at AS SentAt
                FROM webhook_outbox
                {where}
                ORDER BY outbox_id DESC
                LIMIT 200", param);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebhookAdmin] 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("outbox/{id:long}/retry")]
    [BoPermission("webhook.admin")]
    public async Task<IActionResult> Retry(long id, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE webhook_outbox
                SET status = 'pending', retry_count = 0, next_retry_at = NOW(6), last_error = NULL
                WHERE outbox_id = @Id AND status IN ('failed', 'dead')",
                new { Id = id });
            if (affected == 0)
                return BadRequest(new { success = false, message = "실패·dead 상태인 항목만 재발송할 수 있습니다." });

            _logger.LogInformation("[WebhookAdmin] 재발송 큐 복귀 outbox_id={Id}", id);
            return Ok(new { success = true, message = "재발송 큐에 다시 박았습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebhookAdmin] 재발송 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "재발송 처리 중 오류가 발생했습니다." });
        }
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? _config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }

    public class OutboxItem
    {
        public long OutboxId { get; set; }
        public string TenantId { get; set; } = "";
        public string EventType { get; set; } = "";
        public string TargetUrl { get; set; } = "";
        public string Status { get; set; } = "";
        public int RetryCount { get; set; }
        public string? LastError { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }
}
