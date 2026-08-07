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

    // 20260806작4 (사장님 오더 ③) — 업데이트 결과 보고 경로.
    //   ⚠️ 위 MetaPing 2개와 **다른 경로**다. /watchdog/* 수신부는 백오피스에 아직 0건이라
    //     지금도 400 이 난다(실측 2026-08-06). 그 복구는 별건이고, 이 경로는 새로 만든 문이다.
    public string UpdateHistoryEndpoint { get; set; } = "https://back.hitpan.kr/api/telemetry/update-history";
    public int MetaPingIntervalMinutes { get; set; } = 5;
    public int CoolDownMaxPerHour { get; set; } = 5;

    // ★ 20260807작2 N-10 (4안 혼합 · 사장님 결재-1) — 새 버전 확인 주기(분).
    //   ■ 무엇을 겪고서 생겼나
    //     종전은 "하루 1회"였다. 2026-08-07 사장님 백지환경 실측에서 게시한 1.2.55 를 워치독이
    //     스스로 발견하지 못했고, `sc stop`/`sc start` 를 직접 치신 뒤에야 잡혔다.
    //     고객 PC 에는 재시작할 사람이 없다 — 최대 24시간 못 받으면서 화면은 정상으로 보인다.
    //   ■ 기본 60분인 이유
    //     사장님 3단계(코드수정 → 업데이트 → 실측)가 최대 1시간 / 평균 30분에 성립한다.
    //     부하: 고객사 1,000곳 기준 24,000 req/일 ≈ 0.28 req/s. manifest 는 1KB 미만이고
    //     nginx 정적 서빙 기준으로 부하라 부르기 어렵다(설계서 §3-1).
    //   ⚠️ manifest.json 은 no-cache 라 Cloudflare 가 흡수하지 않는다. 폴링 1건 = 오리진 1건이다.
    //     "CDN 뒤라 괜찮다"는 직관은 이 파일에 대해서만은 틀렸다(설계서 §1-4).
    public int UpdateCheckIntervalMinutes { get; set; } = 60;

    // 🔴 상·하한을 코드에 두는 이유 (결재-1 · 설계서 A-8) — 설정으로 우회 가능하면 그 자체가 사고다.
    //   ■ 상한 60분: N > 1h 이면 야간 창을 통째로 건너뛸 수 있다.
    //     IsNightWindow 는 새벽 3시대 **1시간뿐**이다(UpdateOrchestrator — Hour >= 3 && Hour < 4).
    //     N ≤ 60분이라야 3시대에 반드시 1회 이상 평가가 걸린다. 이것이 상한의 진짜 근거이며,
    //     야간 창 정책 자체는 이번 범위에서 건드리지 않는다(작2 §5-5 — N ≤ 1h 로 회피).
    //   ■ 하한 5분: 고객사가 설정을 15초로 낮추면 본사 오리진 자해다(설계서 §6-1 마이클 소견).
    //     5분이면 1,000사 기준 288,000 req/일 — 이미 상당하다. 그 아래로는 열지 않는다.
    //     MetaPingIntervalMinutes(5분)와 같은 자릿수로 맞춰, 워치독이 본사를 두드리는 최소 간격을 통일한다.
    //   ■ 개발과 고객이 같은 값을 쓴다(설계서 §10-2). 분기하는 순간 "개발 PC 에선 됐는데" 를 구조로 만든다.
    public const int UpdateCheckIntervalMinMinutes = 5;
    public const int UpdateCheckIntervalMaxMinutes = 60;

    /// <summary>
    /// 상·하한을 강제한 실제 확인 주기. 설정값이 범위를 벗어나면 조용히 잘라낸다(설정으로 우회 불가).
    /// 0 이하 같은 무의미한 값도 하한으로 수렴하므로 "설정 실수 = 폴링 폭주" 가 성립하지 않는다.
    /// </summary>
    public TimeSpan ResolvedUpdateCheckInterval => TimeSpan.FromMinutes(
        Math.Clamp(UpdateCheckIntervalMinutes, UpdateCheckIntervalMinMinutes, UpdateCheckIntervalMaxMinutes));

    /// <summary>설정값이 상·하한에 걸려 잘렸는가. 걸렸으면 기동 시 1회 로그로 알린다(헌법 #15 침묵 금지).</summary>
    public bool IsUpdateCheckIntervalClamped =>
        UpdateCheckIntervalMinutes != (int)ResolvedUpdateCheckInterval.TotalMinutes;

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
