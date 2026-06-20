using System.Text.Json;
using HitPan.Watchdog.Stages;
using HitPan.Watchdog.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog;

/// <summary>
/// CLI 진단 모드 (--health). 워치독 Service 등록 안 하고 즉시 현재 상태 JSON 출력.
/// 사장님·CS·검증팀이 봉합 시 빠른 진단용.
/// 사용: HitPan.Watchdog.exe --health
/// </summary>
public static class HealthProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Services.AddOptions<WatchdogOptions>()
            .Bind(builder.Configuration.GetSection("Watchdog"))
            // 봉합 (2026-06-23, 6차 D-P0-01): 진단 모드도 db.conf 단일출처로 정정해 실제 고객 도메인을 검사.
            .PostConfigure(opts => DbConfReader.ApplyToOptions(opts));
        builder.Services.AddHttpClient();

        var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<WatchdogOptions>>();
        var httpFactory = host.Services.GetRequiredService<IHttpClientFactory>();

        var report = new HealthReport
        {
            Timestamp = DateTime.UtcNow,
            WatchdogVersion = "1.0.0",
            TenantIdHash = SafeSha256(MetaPingClient.GetTenantId()),
            Environment = new EnvSnapshot
            {
                HasTunnelId = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HITPAN_TUNNEL_ID", EnvironmentVariableTarget.Machine)),
                HasLicenseKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HITPAN_LICENSE_KEY", EnvironmentVariableTarget.Machine)),
                HasTenantId = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HITPAN_TENANT_ID", EnvironmentVariableTarget.Machine)),
                Subdomain = Environment.GetEnvironmentVariable("HITPAN_SUBDOMAIN", EnvironmentVariableTarget.Machine) ?? "(미설정)",
                MachineName = System.Environment.MachineName,
                OsVersion = System.Environment.OSVersion.VersionString,
                Is64BitOs = System.Environment.Is64BitOperatingSystem,
                IsElevated = IsElevated()
            }
        };

        if (OperatingSystem.IsWindows())
        {
            var i = new WS28I_FourProcess(NullLogger<WS28I_FourProcess>.Instance, httpFactory, options);
            report.ProcessStatus = await i.CheckAllAsync();

            var c = new WS28C_TunnelSecret(NullLogger<WS28C_TunnelSecret>.Instance);
            report.TunnelSecretInvalid = c.DetectInvalidSecret();

            var d = new WS28D_ServiceReinstall(NullLogger<WS28D_ServiceReinstall>.Instance);
            report.CloudflaredServiceExists = d.ServiceExists("cloudflared");
            report.WatchdogServiceExists = d.ServiceExists("HitPanWatchdog");

            var a = new WS28A_WindowsUpdate(NullLogger<WS28A_WindowsUpdate>.Instance);
            report.RebootImminent = a.DetectImminentReboot();
        }

        var e = new WS28E_ExternalHealthCheck(NullLogger<WS28E_ExternalHealthCheck>.Instance, httpFactory, options);
        report.ExternalHealthOk = await e.PingAsync();

        report.OverallStatus = ComputeOverall(report);

        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));

        return report.OverallStatus == "healthy" ? 0 : 1;
    }

    private static string ComputeOverall(HealthReport r)
    {
        if (!r.ExternalHealthOk) return "down";
        if (r.TunnelSecretInvalid) return "recovering";
        if (r.ProcessStatus.Values.Any(v => !v)) return "recovering";
        if (!r.CloudflaredServiceExists) return "recovering";
        return "healthy";
    }

    private static string SafeSha256(string s)
    {
        try { return MetaPingClient.Sha256(s); }
        catch (Exception ex)
        {
            // §절대원칙 #15: 빈 catch 금지. 자가진단 해싱 실패는 CS 추적용으로 표준오류에 남긴다.
            Console.Error.WriteLine($"[HealthProbe] SafeSha256 failed: {ex.Message}");
            return "sha256:unknown";
        }
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            // §절대원칙 #15: 권한 조회 실패는 "비관리자"가 아니라 "오류"다. 침묵하지 않고 남긴다.
            Console.Error.WriteLine($"[HealthProbe] IsElevated check failed: {ex.Message}");
            return false;
        }
    }

    public class HealthReport
    {
        public DateTime Timestamp { get; set; }
        public string WatchdogVersion { get; set; } = "";
        public string TenantIdHash { get; set; } = "";
        public string OverallStatus { get; set; } = "unknown";
        public bool ExternalHealthOk { get; set; }
        public bool TunnelSecretInvalid { get; set; }
        public bool CloudflaredServiceExists { get; set; }
        public bool WatchdogServiceExists { get; set; }
        public bool RebootImminent { get; set; }
        public Dictionary<string, bool> ProcessStatus { get; set; } = new();
        public EnvSnapshot Environment { get; set; } = new();
    }

    public class EnvSnapshot
    {
        public bool HasTunnelId { get; set; }
        public bool HasLicenseKey { get; set; }
        public bool HasTenantId { get; set; }
        public string Subdomain { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public bool Is64BitOs { get; set; }
        public bool IsElevated { get; set; }
    }
}
