using System.Diagnostics;
using System.Runtime.Versioning;
using HitPan.Watchdog;
using HitPan.Watchdog.AutoUpdate;
using HitPan.Watchdog.Stages;
using HitPan.Watchdog.Telemetry;

if (args.Length > 0 && (args[0] == "--health" || args[0] == "-h" || args[0] == "/health"))
{
    return await HealthProbe.RunAsync(args);
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(opts =>
{
    opts.ServiceName = "HitPanWatchdog";
});

// 고정 (2026-07-16, 작1 W4-1 — 검증팀 R-1 적발): Worker(ExecuteAsync)가 예외로 죽으면 호스트를 내린다.
//   왜 명시하나: 이 값은 .NET 8 기본값이지만, W4-1 의 ③ 자가 점검(기동 시 keepalive 복원)이
//   이 동작에 통째로 기대고 있다. 만약 Ignore 로 바뀌면 —
//     ExecuteAsync 만 죽고 프로세스는 살아남는다 → sc failure(5초)가 안 뛴다(프로세스가 살아있으니까)
//     → 워치독이 재기동되지 않는다 → ③ 이 영원히 안 돈다
//     → 업데이트 중 죽어 keepalive 가 꺼진 상태면 ERP 가 영영 안 뜬다.
//   즉 "기본값에 우연히 기댄 안전망"이라 명시로 고정한다. 이 줄을 지우지 말 것.
builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

EnsureEventSource("HitPanWatchdog");

builder.Logging.AddEventLog(opts => { opts.SourceName = "HitPanWatchdog"; });

builder.Services.AddOptions<WatchdogOptions>()
    .Bind(builder.Configuration.GetSection("Watchdog"))
    // 봉합 (2026-06-23, 6차 전수조사 D-P0-01·D-P1-02): bind 후 {app}\db.conf 단일출처로 HealthCheckUrl·
    //   로컬 API 포트를 고객 환경에 맞게 덮어쓴다. demo 고정·5234 포트 오류 제거(자가복구 오판·집단 자해 차단).
    .PostConfigure(opts => DbConfReader.ApplyToOptions(opts, msg => Console.Error.WriteLine(msg)));

builder.Services.AddHttpClient();

builder.Services.AddSingleton<WS28A_WindowsUpdate>();
builder.Services.AddSingleton<WS28B_PostRebootCheck>();
builder.Services.AddSingleton<WS28C_TunnelSecret>();
// 봉합 20260730작8 P0-4 (사장님 결재): 터널 토큰 유실·무효 시 본사 자력 재발급.
//   WS28D 가 선택적으로 주입받는다 — 등록해두면 토큰이 없을 때 스스로 받아와 복구한다.
//   미등록이어도 WS28D 는 종전 동작을 유지하므로(nullable) 안전하다.
builder.Services.AddSingleton<ITunnelTokenRecovery, TunnelTokenRecovery>();
builder.Services.AddSingleton<WS28D_ServiceReinstall>();
builder.Services.AddSingleton<WS28E_ExternalHealthCheck>();
builder.Services.AddSingleton<WS28F_CoolDown>();
builder.Services.AddSingleton<WS28I_FourProcess>();
builder.Services.AddSingleton<MetaPingClient>();
// 20260806작4 (사장님 오더 ③): 업데이트 결과를 본사 백오피스에 보고 — MetaPing 과 별개 경로.
builder.Services.AddSingleton<UpdateHistoryClient>();

// 봉합 (2026-06-29, 작1 고리1 — UpdateOrchestrator 를 워치독 Worker 에 연결):
//   기존 WS28A~I 등록 패턴 그대로 따라 버전 업데이트 연결축을 DI 에 추가한다(헌법 #1 추가만, #34 정식 완성도).
//   IUpdateClient → manifest 조회/다운로드/sha256 검증, WatchdogBackupRunner → 적용 전 안전 백업(고리3),
//   UpdateOrchestrator → 채널 분기 평가 + 백업 실패 물리적 차단(고리3).
// 봉합 (2026-07-16, 작1 서명 — 사장님 결재): manifest 위조 차단.
//   feed 주소는 환경변수로 바뀔 수 있고 sha256 은 manifest 안에 있어 자기참조라, 서명이 없으면
//   실제 파일 교체(W4-2)가 붙는 순간 "환경변수 하나 → SYSTEM 권한 임의 코드 실행"이 성립한다.
//   개인키는 NCP(600 root)에만 있고 EXE 는 공개키만 갖는다.
builder.Services.AddSingleton<UpdateSignatureVerifier>();

builder.Services.AddSingleton<IUpdateClient, UpdateClient>();
builder.Services.AddSingleton<WatchdogBackupRunner>();

// 봉합 (2026-07-16, 작1 W4-1 — 사장님 결재): 교체 구간 정지·복원 게이트 + 진행 표식.
//   keepalive(1분 주기)가 교체 중 ERP 를 되살려 파일을 잠그는 것을 막고, 워치독이 도중에 죽어도
//   ERP 가 영영 안 뜨는 사고를 3중으로 방어한다(finally 복원 / 부팅 안전망 / 기동 시 자가 점검).
builder.Services.AddSingleton<UpdateLockFile>();
builder.Services.AddSingleton<UpdateProcessGate>();

// ★ 20260807작2 N-10 (4안 혼합 · 사장님 결재 7건 2026-08-07): 마지막 새 버전 확인 시각을 {app} 에 기록.
//   2026-08-07 백지환경 실측에서 게시본을 워치독이 스스로 못 찾고 `sc stop`/`sc start` 로만 잡혔다.
//   확인 주기를 N시간(기본 60분)으로 줄이면서, 그 기억이 재시작으로 날아가 크래시 루프 PC 가 기동마다
//   본사를 두드리는 것을 막는 상한 장치다. UpdateLockFile 과 같은 {app} 관례·같은 fail-open 정책.
builder.Services.AddSingleton<UpdateCheckStampFile>();

builder.Services.AddSingleton<UpdateOrchestrator>();

// 봉합 (2026-06-29, 작1 고리2 워치독 측 — 로컬 동의 리더):
//   A안(헌법 #30): ERP 가 local_update_consents(DB-82)에 INSERT 한 Major 업데이트 동의를 워치독이
//   로컬에서 SELECT 해 적용 가부를 판단한다. WatchdogBackupRunner 와 동일하게 db.conf 자격증명 +
//   MariaDB 클라이언트 CLI 자족 실행(드라이버 패키지·API 의존 0). 헌법 #1 추가만.
builder.Services.AddSingleton<WatchdogConsentReader>();

// 봉합 (2026-06-29, 작1 고리2 마지막 빈 칸 — 로컬 새버전 상태 라이터):
//   A안(헌법 #30): 워치독이 발견한 Major 새버전을 local_update_status(DB-83)에 적재한다.
//   ERP 가 로그인 시 그 행을 읽어 UpdateAvailable 을 채워 Y/N 동의 팝업을 노출한다(본사 의존 0). 헌법 #1 추가만.
builder.Services.AddSingleton<WatchdogStatusWriter>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
return 0;

[SupportedOSPlatform("windows")]
static void EnsureEventSource(string sourceName)
{
    try
    {
        if (!EventLog.SourceExists(sourceName))
            EventLog.CreateEventSource(sourceName, "Application");
    }
    catch (Exception ex)
    {
        // 헌법 #15: 빈 catch 금지. 관리자 권한 부재 시 EventLog 소스 생성 실패는 정상 폴백이나 흔적은 남긴다.
        Console.Error.WriteLine($"[Watchdog] EventSource 생성 실패(관리자 권한 부재 가능) — 콘솔 로그로 폴백: {ex.Message}");
    }
}
