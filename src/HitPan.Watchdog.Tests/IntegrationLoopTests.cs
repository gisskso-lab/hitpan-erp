using HitPan.Watchdog;
using HitPan.Watchdog.AutoUpdate;
using HitPan.Watchdog.Stages;
using HitPan.Watchdog.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog.Tests;

/// <summary>
/// 통합: DI 그래프 전체 빌드 + Worker가 정상 인스턴스화되는지 검증.
/// 헌법 #15·#19 정합 — 빈 catch 0, 빌드 errors 0이라도 DI 조립 실패는 런타임 사고.
///
/// ⚠️ 이 테스트의 구조적 취약점 (2026-07-16 작1 W4-0 조사에서 드러남)
///   아래 등록 목록은 Program.cs 를 손으로 복제한 것이라, Program.cs 에 의존성이 추가되면
///   여기가 조용히 갈라진다. 실제로 2026-06-29(고리1, ae6bccf)에 Worker 가 UpdateOrchestrator 를
///   받기 시작했는데 이 목록은 2026-05-29(3958f6f) 이후 갱신되지 않아 47일간 빨간 채로 방치됐다.
///   테스트가 늘 빨가면 진짜 회귀가 나도 아무도 못 알아본다 — 고리4 작업 전에 되살려 둔다.
///   근본 해법은 Program.cs 의 등록부를 확장 메서드(예: AddWatchdogServices)로 빼서 테스트가
///   같은 코드를 호출하는 것이다. 본건(W4-0) 범위 밖이라 백로그로 남긴다.
/// </summary>
public class IntegrationLoopTests
{
    [Fact]
    public void HostBuilder_ResolvesAllStagesAndWorker()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOptions<WatchdogOptions>()
            .Configure(o =>
            {
                o.MetaPingEndpoint = "http://127.0.0.1:9";
                o.MetaPingEmergencyEndpoint = "http://127.0.0.1:9";
                o.HealthCheckUrl = "http://127.0.0.1:9";
                o.Processes = new ProcessesConfig
                {
                    Services = new List<string>(),
                    HttpEndpoints = new List<HttpEndpointConfig>()
                };
            });
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<WS28A_WindowsUpdate>();
        builder.Services.AddSingleton<WS28B_PostRebootCheck>();
        builder.Services.AddSingleton<WS28C_TunnelSecret>();
        builder.Services.AddSingleton<WS28D_ServiceReinstall>();
        builder.Services.AddSingleton<WS28E_ExternalHealthCheck>();
        builder.Services.AddSingleton<WS28F_CoolDown>();
        builder.Services.AddSingleton<WS28I_FourProcess>();
        builder.Services.AddSingleton<MetaPingClient>();

        // 정합 (2026-07-16, 작1 W4-0): Program.cs:45-58 의 자동 업데이트 연결축 등록을 반영한다.
        //   2026-06-29 고리1~2 에서 Worker 가 이들을 생성자로 받기 시작했는데 여기 목록이 안 따라와
        //   이 테스트가 47일간 실패 상태였다(Worker 조립 불가 → DI 검증이라는 이 테스트의 목적 자체가 무력화).
        builder.Services.AddSingleton<UpdateSignatureVerifier>();
        builder.Services.AddSingleton<IUpdateClient, UpdateClient>();
        builder.Services.AddSingleton<WatchdogBackupRunner>();
        builder.Services.AddSingleton<UpdateLockFile>();
        builder.Services.AddSingleton<UpdateProcessGate>();
        builder.Services.AddSingleton<UpdateOrchestrator>();
        builder.Services.AddSingleton<WatchdogConsentReader>();
        builder.Services.AddSingleton<WatchdogStatusWriter>();

        builder.Services.AddHostedService<Worker>();

        var host = builder.Build();

        Assert.NotNull(host.Services.GetRequiredService<WS28A_WindowsUpdate>());
        Assert.NotNull(host.Services.GetRequiredService<WS28F_CoolDown>());
        Assert.NotNull(host.Services.GetRequiredService<MetaPingClient>());
        Assert.NotNull(host.Services.GetRequiredService<UpdateOrchestrator>());
        var hosted = host.Services.GetServices<IHostedService>();
        Assert.Contains(hosted, h => h is Worker);
    }
}
