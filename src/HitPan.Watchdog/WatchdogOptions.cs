namespace HitPan.Watchdog;

public class WatchdogOptions
{
    public int LoopIntervalSeconds { get; set; } = 60;
    // 봉합 (2026-06-23, 6차 전수조사 D-P0-01): 기본값을 demo 고정에서 빈 문자열로 변경.
    //   실제 URL 은 DbConfReader 가 {app}\db.conf 의 PRIMARY_DOMAIN 으로 동적 구성한다. db.conf 미발견·
    //   LOCAL 모드면 빈 문자열로 남아 외부 헬스체크가 비활성된다(demo 서버를 절대 타격하지 않음).
    public string HealthCheckUrl { get; set; } = string.Empty;
    public int HealthCheckTimeoutSeconds { get; set; } = 10;
    public int HealthCheckFailThreshold { get; set; } = 3;
    public string MetaPingEndpoint { get; set; } = "https://back.hitpan.kr/watchdog/ping";
    public string MetaPingEmergencyEndpoint { get; set; } = "https://back.hitpan.kr/watchdog/emergency";
    public int MetaPingIntervalMinutes { get; set; } = 5;
    public int CoolDownMaxPerHour { get; set; } = 5;
    public ProcessesConfig Processes { get; set; } = new();
}

public class ProcessesConfig
{
    public List<string> Services { get; set; } = new();
    public List<HttpEndpointConfig> HttpEndpoints { get; set; } = new();
}

public class HttpEndpointConfig
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}
