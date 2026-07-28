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
        // 봉합 (2026-06-23, 6차 D-P0-01): HealthCheckUrl 이 비어있으면(db.conf 미발견·LOCAL 모드) 외부 헬스체크를
        //   건너뛰고 healthy 로 본다. 종전 demo 고정값을 때리던 경로를 원천 제거 — 데모 다운 시 집단 FullRecovery
        //   (자격증명 재생성+서비스 재설치) 자해를 차단한다. 외부 도달성 확인 불가 시엔 '판정 보류'가 안전.
        if (string.IsNullOrWhiteSpace(_options.HealthCheckUrl))
        {
            _logger.LogDebug("WS-28-E: HealthCheckUrl 미설정 — 외부 헬스체크 건너뜀(헬스 판정 보류)");
            return true;
        }

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
