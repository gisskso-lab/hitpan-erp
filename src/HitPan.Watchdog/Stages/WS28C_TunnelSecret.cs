using System.Diagnostics;
using System.ServiceProcess;
using System.Runtime.Versioning;

namespace HitPan.Watchdog.Stages;

[SupportedOSPlatform("windows")]
public class WS28C_TunnelSecret
{
    private readonly ILogger<WS28C_TunnelSecret> _logger;
    private readonly string _logPath;

    public WS28C_TunnelSecret(ILogger<WS28C_TunnelSecret> logger)
    {
        _logger = logger;
        _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cloudflared", "cloudflared.log");
    }

    public bool DetectInvalidSecret()
    {
        if (!File.Exists(_logPath)) return false;
        try
        {
            using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var lines = new Queue<string>(100);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (lines.Count >= 100) lines.Dequeue();
                lines.Enqueue(line);
            }
            return lines.Any(l => l.Contains("Invalid tunnel secret", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS-28-C: log read failure");
            return false;
        }
    }

    public async Task<bool> RegenerateAsync(CancellationToken ct = default)
    {
        var tunnelId = Environment.GetEnvironmentVariable("HITPAN_TUNNEL_ID");
        if (string.IsNullOrEmpty(tunnelId))
        {
            _logger.LogError("WS-28-C: HITPAN_TUNNEL_ID env var missing");
            return false;
        }

        var credDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cloudflared");
        var credFile = Path.Combine(credDir, $"{tunnelId}.json");
        var backup = $"{credFile}.bak_{DateTime.Now:yyyyMMddHHmmss}";

        try
        {
            if (File.Exists(credFile)) File.Move(credFile, backup, overwrite: true);

            var psi = new ProcessStartInfo("cloudflared", $"tunnel token --cred-file \"{credFile}\" {tunnelId}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0)
            {
                var err = await p.StandardError.ReadToEndAsync(ct);
                _logger.LogError("WS-28-C: cloudflared exit {Code}: {Err}", p.ExitCode, err);
                return false;
            }

            using var sc = new ServiceController("cloudflared");
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

            _logger.LogWarning("WS-28-C: TunnelSecret regenerated, cloudflared restarted");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WS-28-C: regeneration failure");
            return false;
        }
    }
}
