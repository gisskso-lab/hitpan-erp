using HitPan.Watchdog;
using HitPan.Watchdog.Stages;
using HitPan.Watchdog.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(opts =>
{
    opts.ServiceName = "HitPanWatchdog";
});

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
