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
builder.Services.AddSingleton<WS28D_ServiceReinstall>();
builder.Services.AddSingleton<WS28E_ExternalHealthCheck>();
builder.Services.AddSingleton<WS28F_CoolDown>();
builder.Services.AddSingleton<WS28I_FourProcess>();
builder.Services.AddSingleton<MetaPingClient>();

// 봉합 (2026-06-29, 작1 고리1 — UpdateOrchestrator 를 워치독 Worker 에 연결):
//   기존 WS28A~I 등록 패턴 그대로 따라 버전 업데이트 연결축을 DI 에 추가한다(헌법 #1 추가만, #34 정식 완성도).
//   IUpdateClient → manifest 조회/다운로드/sha256 검증, WatchdogBackupRunner → 적용 전 안전 백업(고리3),
//   UpdateOrchestrator → 채널 분기 평가 + 백업 실패 물리적 차단(고리3).
builder.Services.AddSingleton<IUpdateClient, UpdateClient>();
builder.Services.AddSingleton<WatchdogBackupRunner>();
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
