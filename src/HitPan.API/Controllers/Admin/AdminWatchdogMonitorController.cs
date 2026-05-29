using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

/// <summary>
/// 본사 백오피스 워치독 모니터링 (관리자 전용).
/// 헌법 #22 정합: 집계·메타만 반환 (업무 데이터 0).
/// </summary>
[ApiController]
[Route("api/admin/watchdog")]
[Authorize(Policy = "PlatformOnly")]
public sealed class AdminWatchdogMonitorController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly ILogger<AdminWatchdogMonitorController> _logger;

    public AdminWatchdogMonitorController(IDbConnection db, ILogger<AdminWatchdogMonitorController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        try
        {
            await EnsureOpenAsync(ct);

            const string sqlStatus = @"
                SELECT status, COUNT(DISTINCT tenant_id_hash) AS cnt
                FROM watchdog_pings
                WHERE received_at >= UTC_TIMESTAMP() - INTERVAL 5 MINUTE
                GROUP BY status;";
            var rows = (await _db.QueryAsync<(string status, int cnt)>(sqlStatus)).ToList();

            int healthy = rows.FirstOrDefault(r => r.status == "healthy").cnt;
            int recovering = rows.FirstOrDefault(r => r.status == "recovering").cnt;
            int down = rows.FirstOrDefault(r => r.status == "down").cnt;

            const string sqlEmerg = @"
                SELECT COUNT(*) FROM watchdog_emergencies
                WHERE cs_resolved_at IS NULL
                  AND received_at >= UTC_TIMESTAMP() - INTERVAL 24 HOUR;";
            var unresolved = await _db.ExecuteScalarAsync<int>(sqlEmerg);

            const string sqlSla = @"
                SELECT
                    COUNT(*) AS total,
                    SUM(CASE WHEN status = 'healthy' THEN 1 ELSE 0 END) AS healthy
                FROM watchdog_pings
                WHERE received_at >= DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-01');";
            var sla = await _db.QuerySingleOrDefaultAsync<(long total, long healthy)>(sqlSla);
            double slaPct = sla.total > 0 ? (double)sla.healthy / sla.total * 100.0 : 100.0;

            const string sqlRefund = @"
                SELECT COUNT(DISTINCT tenant_id_hash) FROM watchdog_pings
                WHERE received_at >= DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-01')
                  AND status <> 'healthy';";
            var refund = await _db.ExecuteScalarAsync<int>(sqlRefund);

            return Ok(new
            {
                healthy,
                recovering,
                down,
                unresolvedEmergencies = unresolved,
                slaMonth = DateTime.UtcNow.ToString("yyyy-MM"),
                slaActual = Math.Round(slaPct, 4),
                refundCount = refund
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Watchdog] summary 조회 실패");
            return StatusCode(500, new { error = "internal error" });
        }
    }

    [HttpGet("emergencies")]
    public async Task<IActionResult> GetEmergencies([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        if (hours <= 0 || hours > 720) hours = 24;
        try
        {
            await EnsureOpenAsync(ct);
            const string sql = @"
                SELECT tenant_id_hash, received_at, reason, stage, cs_notified_at, cs_resolved_at
                FROM watchdog_emergencies
                WHERE received_at >= UTC_TIMESTAMP() - INTERVAL @Hours HOUR
                ORDER BY received_at DESC
                LIMIT 100;";
            var rows = await _db.QueryAsync(sql, new { Hours = hours });
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Watchdog] emergencies 조회 실패");
            return StatusCode(500, new { error = "internal error" });
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is System.Data.Common.DbConnection c) await c.OpenAsync(ct);
        else _db.Open();
    }
}
