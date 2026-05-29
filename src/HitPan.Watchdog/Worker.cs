using HitPan.Watchdog.Stages;
using HitPan.Watchdog.Telemetry;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly WatchdogOptions _options;
    private readonly WS28A_WindowsUpdate _a;
    private readonly WS28B_PostRebootCheck _b;
    private readonly WS28C_TunnelSecret _c;
    private readonly WS28D_ServiceReinstall _d;
    private readonly WS28E_ExternalHealthCheck _e;
    private readonly WS28F_CoolDown _f;
    private readonly WS28I_FourProcess _i;
    private readonly MetaPingClient _meta;

    private string? _lastRecoveryStage;
    private DateTime? _lastRecoveryAt;

    public Worker(
        ILogger<Worker> logger,
        IOptions<WatchdogOptions> options,
        WS28A_WindowsUpdate a,
        WS28B_PostRebootCheck b,
        WS28C_TunnelSecret c,
        WS28D_ServiceReinstall d,
        WS28E_ExternalHealthCheck e,
        WS28F_CoolDown f,
        WS28I_FourProcess i,
        MetaPingClient meta)
    {
        _logger = logger;
        _options = options.Value;
        _a = a; _b = b; _c = c; _d = d; _e = e; _f = f; _i = i;
        _meta = meta;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HitPan Watchdog started v1.0.0");

        if (OperatingSystem.IsWindows() && _b.ShouldRunPostRebootCheck())
        {
            _logger.LogWarning("WS-28-B: post-reboot check active");
            _b.ClearFlag();
        }

        var tickCount = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOneLoopAsync(stoppingToken);
                tickCount++;
                if (tickCount % _options.MetaPingIntervalMinutes == 0)
                    await SendMetaPingAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchdog loop failure");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_options.LoopIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOneLoopAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogDebug("Non-Windows platform — skipping Windows-specific stages");
            await _e.PingAsync(ct);
            return;
        }

        var procStatus = await _i.CheckAllAsync(ct);
        foreach (var (name, ok) in procStatus)
        {
            if (!ok && _options.Processes.Services.Contains(name))
            {
                if (_f.AllowRecovery($"svc:{name}"))
                {
                    _i.TryRestartService(name);
                    MarkRecovery($"WS-28-I/{name}");
                }
            }
        }

        if (_c.DetectInvalidSecret())
        {
            if (_f.AllowRecovery("TunnelSecret"))
            {
                if (await _c.RegenerateAsync(ct))
                    MarkRecovery("WS-28-C");
            }
            else
            {
                await _meta.NotifyEmergencyAsync("cooldown_exceeded", "WS-28-C", ct);
            }
        }

        if (!_d.ServiceExists("cloudflared"))
        {
            if (_f.AllowRecovery("ServiceReinstall"))
            {
                if (await _d.ReinstallAsync(ct))
                    MarkRecovery("WS-28-D");
            }
        }

        var healthy = await _e.PingAsync(ct);
        if (!healthy)
        {
            var streak = _e.IncrementFailure();
            _logger.LogWarning("WS-28-E: health check fail streak {Streak}", streak);
            if (_e.ShouldTriggerRecovery() && _f.AllowRecovery("FullRecovery"))
            {
                await _meta.NotifyEmergencyAsync("external_health_fail", "WS-28-E", ct);
                MarkRecovery("WS-28-E");
                _e.ResetFailure();
            }
        }
        else
        {
            _e.ResetFailure();
        }

        if (_a.DetectImminentReboot())
        {
            _b.MarkPostRebootCheck();
            _logger.LogWarning("WS-28-A: reboot imminent, post-check flagged");
        }
    }

    private async Task SendMetaPingAsync(CancellationToken ct)
    {
        var procStatus = OperatingSystem.IsWindows() ? await _i.CheckAllAsync(ct) : new();
        var recoveryCount = _f.RecentRecoveryCount;
        var status = procStatus.All(kv => kv.Value)
            ? "healthy"
            : recoveryCount > 0 ? "recovering" : "down";

        var payload = new MetaPingPayload
        {
            TenantIdHash = MetaPingClient.Sha256(MetaPingClient.GetTenantId()),
            Timestamp = DateTime.UtcNow,
            Status = status,
            RecentRecoveryCount = recoveryCount,
            WatchdogVersion = "1.0.0",
            ProcessStatus = procStatus,
            LastRecovery = new LastRecoveryInfo
            {
                Stage = _lastRecoveryStage,
                Timestamp = _lastRecoveryAt
            }
        };
        await _meta.SendAsync(payload, ct);
    }

    private void MarkRecovery(string stage)
    {
        _lastRecoveryStage = stage;
        _lastRecoveryAt = DateTime.UtcNow;
    }
}
