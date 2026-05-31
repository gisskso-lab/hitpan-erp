using System.Diagnostics;
using System.Runtime.Versioning;
using HitPan.Watchdog;
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
    .Bind(builder.Configuration.GetSection("Watchdog"));

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
    catch (Exception)
    {
        // 관리자 권한 부재 시 무시 — appsettings 기반 콘솔 로그로 fallback
    }
}
