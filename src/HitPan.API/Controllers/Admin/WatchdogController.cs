using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

/// <summary>
/// 본사 워치독 수신 엔드포인트 (고객 PC HitPan.Watchdog → 본사 push).
/// 헌법 #18 v3·#22 본사 데이터 최소주의 정합: 메타 + 카운터만 수신.
/// 인증: Bearer 토큰 (SHA-256(license_key|machine|tenant_id_hash)).
/// </summary>
[ApiController]
[Route("watchdog")]
[AllowAnonymous]
public sealed class WatchdogController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly ILogger<WatchdogController> _logger;

    private static readonly HashSet<string> ForbiddenKeyFragments = new(StringComparer.OrdinalIgnoreCase)
    {
        "tenant_id", "tenant_name", "company_name",
        "user_email", "user_name",
        "ip_address", "mac_address", "disk_serial",
        "revenue", "sales", "purchase",
        "transaction", "invoice", "item", "customer", "employee"
    };

    public WatchdogController(IDbConnection db, ILogger<WatchdogController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public sealed record MetaPingDto(
        string tenant_id_hash,
        DateTime timestamp,
        string status,
        int recent_recovery_count,
        string watchdog_version,
        Dictionary<string, bool>? process_status,
        LastRecoveryDto? last_recovery);

    public sealed record LastRecoveryDto(string? stage, DateTime? timestamp);

    public sealed record EmergencyDto(
        string tenant_id_hash,
        string reason,
        string? stage,
        DateTime timestamp);

    [HttpPost("ping")]
    public async Task<IActionResult> Ping([FromBody] MetaPingDto p, CancellationToken ct)
    {
        if (p is null) return BadRequest(new { error = "payload required" });
        if (!p.tenant_id_hash.StartsWith("sha256:", StringComparison.Ordinal))
            return BadRequest(new { error = "tenant_id_hash must be sha256-prefixed (헌법 #22)" });
        if (p.status is not ("healthy" or "recovering" or "down"))
            return BadRequest(new { error = "invalid status" });

        if (p.process_status is not null && p.process_status.Keys.Any(IsForbiddenKey))
        {
            _logger.LogError("[Watchdog] 금지 필드 탐지 — tenant_hash={Hash}", p.tenant_id_hash);
            return BadRequest(new { error = "forbidden field detected (헌법 #22)" });
        }

        try
        {
            await EnsureOpenAsync(ct);
            const string sql = @"
                INSERT INTO watchdog_pings
                    (tenant_id_hash, received_at, status, recent_recovery_count,
                     watchdog_version, process_status_json, last_recovery_stage, last_recovery_at)
                VALUES
                    (@TenantIdHash, UTC_TIMESTAMP(3), @Status, @RecentRecoveryCount,
                     @WatchdogVersion, @ProcessStatusJson, @LastRecoveryStage, @LastRecoveryAt);";

            await _db.ExecuteAsync(sql, new
            {
                TenantIdHash = p.tenant_id_hash,
                p.status,
                RecentRecoveryCount = p.recent_recovery_count,
                WatchdogVersion = p.watchdog_version,
                ProcessStatusJson = JsonSerializer.Serialize(p.process_status ?? new()),
                LastRecoveryStage = p.last_recovery?.stage,
                LastRecoveryAt = p.last_recovery?.timestamp
            });

            return Ok(new
            {
                received = true,
                next_ping_seconds = 300,
                instructions = Array.Empty<string>()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Watchdog] ping 저장 실패 — tenant_hash={Hash}", p.tenant_id_hash);
            return StatusCode(500, new { error = "internal error" });
        }
    }

    [HttpPost("emergency")]
    public async Task<IActionResult> Emergency([FromBody] EmergencyDto p, CancellationToken ct)
    {
        if (p is null) return BadRequest(new { error = "payload required" });
        if (!p.tenant_id_hash.StartsWith("sha256:", StringComparison.Ordinal))
            return BadRequest(new { error = "tenant_id_hash must be sha256-prefixed" });
        if (string.IsNullOrWhiteSpace(p.reason))
            return BadRequest(new { error = "reason required" });

        try
        {
            await EnsureOpenAsync(ct);
            const string sql = @"
                INSERT INTO watchdog_emergencies
                    (tenant_id_hash, received_at, reason, stage)
                VALUES
                    (@TenantIdHash, UTC_TIMESTAMP(3), @Reason, @Stage);";

            await _db.ExecuteAsync(sql, new
            {
                TenantIdHash = p.tenant_id_hash,
                p.reason,
                p.stage
            });

            _logger.LogError("[Watchdog] EMERGENCY received — tenant_hash={Hash} reason={Reason} stage={Stage}",
                p.tenant_id_hash, p.reason, p.stage);

            return Ok(new { received = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Watchdog] emergency 저장 실패");
            return StatusCode(500, new { error = "internal error" });
        }
    }

    private static bool IsForbiddenKey(string key) =>
        ForbiddenKeyFragments.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase));

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is System.Data.Common.DbConnection c) await c.OpenAsync(ct);
        else _db.Open();
    }
}
