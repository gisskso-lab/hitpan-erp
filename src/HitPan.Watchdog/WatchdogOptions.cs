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

    // 봉합 (2026-06-25, ERP 자가복구 A/B): 이 엔드포인트(예: HitPan.API)가 응답 없을 때 워치독이
    //   다시 살릴 방법. 종전엔 HTTP 엔드포인트는 "감지만" 하고 재기동 경로가 없어, ERP API 가 떠 있다
    //   죽으면(예외·메모리·업데이트) 워치독이 못 살렸다(2026-06-25 demo 502 사고 = 이 구멍).
    //   ERP API 는 인스톨러가 schtasks(작업 스케줄러) 'HitPan-ERP-API-tenant-{슬롯}' 으로 ONSTART
    //   자동시작하므로, 재기동 = 그 작업을 'schtasks /Run' 으로 다시 실행하면 된다(서비스 아님).
    //   RestartTask 가 비면 종전대로 감지만 한다(하위호환·LOCAL 안전).
    public string RestartTask { get; set; } = "";
}
