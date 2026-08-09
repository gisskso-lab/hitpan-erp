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
    //   ■ 🔴 정정 (2026-08-09, 사장님 지시) — 60분은 틀렸다. 실시간이어야 한다.
    //     사장님: "워치독은 업데이트 된 파일을 실시간으로 인식하며 돌아야 되는거 아니야??"
    //             "배포 전 수정, 테스트 과정에선 더더욱 실시간으로 업데이트 파일을 빌드하고
    //              확인하며 테스트 하는게 중요해. 바로 바꿔"
    //
    //     종전 60분의 근거는 "오리진 부하"였는데, 그 계산을 보수적으로 읽은 것이 오판이었다:
    //       manifest.json 은 **1KB 미만 정적 파일**이다. 1분 주기라도 1,000사 기준
    //       1,440,000 req/일 = **16.7 req/s = 16.7KB/s**. nginx 정적 서빙에 부하가 아니다.
    //       (종전 주석은 288,000 req/일을 "이미 상당하다"고 적었으나 3.3 req/s 였다.)
    //
    //     그리고 무엇보다 — **60분은 고객이 겪는 시간**이다. 새 버전이 올라와도 최대 1시간
    //     동안 화면은 정상으로 보이고 아무 일도 안 일어난다. 개발·테스트 중에는 그 1시간이
    //     사이클 전체를 잡아먹는다(사장님 3단계: 코드수정 → 업데이트 → 실측).
    //
    //   ⚠️ manifest.json 은 no-cache 라 Cloudflare 가 흡수하지 않는다. 폴링 1건 = 오리진 1건이다.
    //     "CDN 뒤라 괜찮다"는 직관은 이 파일에 대해서만은 틀렸다(설계서 §1-4).
    //     그래도 1KB × 16.7/s 는 감당 범위다.
    //
    //   📌 근본 해법은 폴링이 아니라 **게시 신호(2안)** 다 — 본사가 게시하면 워치독에 알린다.
    //     작2 결재-7 에서 `/watchdog/*` 수신부 부재로 보류됐고, 그 부재는 병렬이슈 07(2026-08-09)
    //     에서 실측 확인됐다(MetaPing HTTP 400). 그것이 서면 주기 폴링 자체가 보조 수단이 된다.
    public int UpdateCheckIntervalSeconds { get; set; } = 60;

    // 🔴 상·하한을 코드에 두는 이유 (결재-1 · 설계서 A-8) — 설정으로 우회 가능하면 그 자체가 사고다.
    //   ■ 상한 3600초(60분): N > 1h 이면 야간 창을 통째로 건너뛸 수 있다.
    //     IsNightWindow 는 새벽 3시대 **1시간뿐**이다(UpdateOrchestrator — Hour >= 3 && Hour < 4).
    //     N ≤ 60분이라야 3시대에 반드시 1회 이상 평가가 걸린다. 이 보장은 그대로 유지된다.
    //   ■ 하한 30초: 워치독 메인 루프가 60초 주기(LoopIntervalSeconds)이므로 그보다 짧게 잡아도
    //     실제 조회는 루프 주기에 묶인다. 30초는 "루프마다 매번 확인"과 사실상 같은 값이며,
    //     그 아래로 열어도 얻는 것이 없고 설정 실수만 유발한다.
    //   ■ 개발과 고객이 같은 값을 쓴다(설계서 §10-2). 분기하는 순간 "개발 PC 에선 됐는데" 를 구조로 만든다.
    public const int UpdateCheckIntervalMinSeconds = 30;
    public const int UpdateCheckIntervalMaxSeconds = 3600;

    /// <summary>
    /// 상·하한을 강제한 실제 확인 주기. 설정값이 범위를 벗어나면 조용히 잘라낸다(설정으로 우회 불가).
    /// 0 이하 같은 무의미한 값도 하한으로 수렴하므로 "설정 실수 = 폴링 폭주" 가 성립하지 않는다.
    /// </summary>
    public TimeSpan ResolvedUpdateCheckInterval => TimeSpan.FromSeconds(
        Math.Clamp(UpdateCheckIntervalSeconds, UpdateCheckIntervalMinSeconds, UpdateCheckIntervalMaxSeconds));

    /// <summary>설정값이 상·하한에 걸려 잘렸는가. 걸렸으면 기동 시 1회 로그로 알린다(헌법 #15 침묵 금지).</summary>
    public bool IsUpdateCheckIntervalClamped =>
        UpdateCheckIntervalSeconds != (int)ResolvedUpdateCheckInterval.TotalSeconds;

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
