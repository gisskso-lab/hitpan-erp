using Microsoft.Extensions.Options;

namespace HitPan.Watchdog.Stages;

public class WS28E_ExternalHealthCheck
{
    private readonly ILogger<WS28E_ExternalHealthCheck> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly WatchdogOptions _options;
    private int _failStreak;

    public int FailStreak => _failStreak;

    public WS28E_ExternalHealthCheck(
        ILogger<WS28E_ExternalHealthCheck> logger,
        IHttpClientFactory httpFactory,
        IOptions<WatchdogOptions> options)
    {
        _logger = logger;
        _httpFactory = httpFactory;
        _options = options.Value;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(_options.HealthCheckTimeoutSeconds);
            var r = await http.GetAsync(_options.HealthCheckUrl, ct);
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS-28-E: external health check failure");
            return false;
        }
    }

    public int IncrementFailure() => Interlocked.Increment(ref _failStreak);
    public void ResetFailure() => Interlocked.Exchange(ref _failStreak, 0);

    public bool ShouldTriggerRecovery() => _failStreak >= _options.HealthCheckFailThreshold;
}
