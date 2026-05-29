using System.Diagnostics;
using System.ServiceProcess;
using System.Runtime.Versioning;

namespace HitPan.Watchdog.Stages;

[SupportedOSPlatform("windows")]
public class WS28D_ServiceReinstall
{
    private readonly ILogger<WS28D_ServiceReinstall> _logger;

    public WS28D_ServiceReinstall(ILogger<WS28D_ServiceReinstall> logger)
    {
        _logger = logger;
    }

    public bool ServiceExists(string serviceName = "cloudflared")
    {
        try
        {
            var services = ServiceController.GetServices();
            return services.Any(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS-28-D: service enumeration failure");
            return true;
        }
    }

    public async Task<bool> ReinstallAsync(CancellationToken ct = default)
    {
        try
        {
            var exePath = ResolveCloudflaredPath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                _logger.LogError("WS-28-D: cloudflared.exe not found");
                return false;
            }

            var psi = new ProcessStartInfo(exePath, "service install")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync(ct);

            using var sc = new ServiceController("cloudflared");
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            _logger.LogWarning("WS-28-D: cloudflared service reinstalled and started");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WS-28-D: reinstall failure");
            return false;
        }
    }

    private static string ResolveCloudflaredPath()
    {
        var candidates = new[]
        {
            @"C:\Program Files\HitPan\payload\cloudflared.exe",
            @"C:\Program Files (x86)\cloudflared\cloudflared.exe",
            @"C:\Program Files\cloudflared\cloudflared.exe"
        };
        return candidates.FirstOrDefault(File.Exists) ?? "";
    }
}
