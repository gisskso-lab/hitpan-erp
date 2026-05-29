using HitPan.Watchdog;
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
        builder.Services.AddHostedService<Worker>();

        var host = builder.Build();

        Assert.NotNull(host.Services.GetRequiredService<WS28A_WindowsUpdate>());
        Assert.NotNull(host.Services.GetRequiredService<WS28F_CoolDown>());
        Assert.NotNull(host.Services.GetRequiredService<MetaPingClient>());
        var hosted = host.Services.GetServices<IHostedService>();
        Assert.Contains(hosted, h => h is Worker);
    }
}
