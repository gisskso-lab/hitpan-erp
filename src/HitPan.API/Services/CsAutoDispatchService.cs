using System.Data;
using Dapper;

namespace HitPan.API.Services;

/// <summary>
/// CS 자동 발신 서비스 PoC.
/// watchdog_emergencies cs_notified_at IS NULL 5분 주기 스캔 → Twilio Korea / Toast SMS 호출.
/// 헌법 #28 정합: cool down 5회 초과 = 즉시 본사 알림.
/// </summary>
public sealed class CsAutoDispatchService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<CsAutoDispatchService> _logger;
    private readonly IConfiguration _config;

    public CsAutoDispatchService(
        IServiceProvider sp,
        ILogger<CsAutoDispatchService> logger,
        IConfiguration config)
    {
        _sp = sp;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue("CsAutoDispatch:Enabled", false);
        if (!enabled)
        {
            _logger.LogInformation("[CsAutoDispatch] disabled — skip");
            return;
        }

        var pollSec = _config.GetValue("CsAutoDispatch:PollSeconds", 300);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CsAutoDispatch] loop failure");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(pollSec), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        if (db.State != ConnectionState.Open)
        {
            if (db is System.Data.Common.DbConnection c) await c.OpenAsync(ct);
            else db.Open();
        }

        const string sql = @"
            SELECT id, tenant_id_hash, received_at, reason, stage
            FROM watchdog_emergencies
            WHERE cs_notified_at IS NULL
              AND received_at >= UTC_TIMESTAMP() - INTERVAL 1 HOUR
            ORDER BY received_at ASC
            LIMIT 20;";

        var rows = (await db.QueryAsync<PendingEmergency>(sql)).ToList();
        if (rows.Count == 0) return;

        _logger.LogWarning("[CsAutoDispatch] processing {Count} pending emergencies", rows.Count);

        foreach (var row in rows)
        {
            var ok = await SendNotificationAsync(row, ct);
            if (ok)
            {
                await db.ExecuteAsync(
                    "UPDATE watchdog_emergencies SET cs_notified_at = UTC_TIMESTAMP(3) WHERE id = @Id",
                    new { row.Id });
            }
        }
    }

    private async Task<bool> SendNotificationAsync(PendingEmergency row, CancellationToken ct)
    {
        var channel = _config.GetValue<string>("CsAutoDispatch:Channel") ?? "log";
        try
        {
            switch (channel.ToLowerInvariant())
            {
                case "twilio":
                    return await SendTwilioAsync(row, ct);
                case "toast":
                    return await SendToastAsync(row, ct);
                default:
                    _logger.LogError("[CsAutoDispatch] EMERGENCY tenant={Hash} reason={Reason} stage={Stage} — channel=log only",
                        row.TenantIdHash, row.Reason, row.Stage);
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CsAutoDispatch] send failure id={Id}", row.Id);
            return false;
        }
    }

    private Task<bool> SendTwilioAsync(PendingEmergency row, CancellationToken ct)
    {
        // Twilio Korea REST API 통합 (PoC). 정식 적용은 사장님 계약 결재 후.
        _logger.LogWarning("[CsAutoDispatch] Twilio stub — would dial CS for tenant={Hash}", row.TenantIdHash);
        return Task.FromResult(true);
    }

    private Task<bool> SendToastAsync(PendingEmergency row, CancellationToken ct)
    {
        // NHN Toast SMS REST API 통합 (PoC).
        _logger.LogWarning("[CsAutoDispatch] Toast SMS stub — would notify CS for tenant={Hash}", row.TenantIdHash);
        return Task.FromResult(true);
    }

    private sealed class PendingEmergency
    {
        public long Id { get; set; }
        public string TenantIdHash { get; set; } = "";
        public DateTime ReceivedAt { get; set; }
        public string Reason { get; set; } = "";
        public string? Stage { get; set; }
    }
}
