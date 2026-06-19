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
        // P2 봉합(2026-06-20): 정식 인스톨러(HitPan-Universal.iss:85)는 cloudflared.exe 를
        // {app}=C:\Program Files\HitPan\ 에 직접 설치한다. 종전 후보엔 옛 payload\ 구조만 있어
        // 현재 설치 구조에서 재설치 자동복구가 exe 를 못 찾았다. 현재 경로를 1순위로 추가한다.
        var appDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(appDir, "HitPan", "cloudflared.exe"),         // 현재 정식 인스톨러 구조 ({app})
            Path.Combine(appDir, "HitPan", "payload", "cloudflared.exe"), // 옛 빌드 구조(하위호환)
            @"C:\Program Files\HitPan\cloudflared.exe",
            @"C:\Program Files\HitPan\payload\cloudflared.exe",
            @"C:\Program Files (x86)\cloudflared\cloudflared.exe",
            @"C:\Program Files\cloudflared\cloudflared.exe"
        };
        return candidates.FirstOrDefault(File.Exists) ?? "";
    }
}
