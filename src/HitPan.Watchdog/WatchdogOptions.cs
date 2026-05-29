namespace HitPan.Watchdog;

public class WatchdogOptions
{
    public int LoopIntervalSeconds { get; set; } = 60;
    public string HealthCheckUrl { get; set; } = "https://demo.hitpan.kr/health";
    public int HealthCheckTimeoutSeconds { get; set; } = 10;
    public int HealthCheckFailThreshold { get; set; } = 3;
    public string MetaPingEndpoint { get; set; } = "https://api.hitpan.kr/watchdog/ping";
    public string MetaPingEmergencyEndpoint { get; set; } = "https://api.hitpan.kr/watchdog/emergency";
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
