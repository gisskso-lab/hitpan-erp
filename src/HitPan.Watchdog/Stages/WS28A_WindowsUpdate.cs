using System.Diagnostics;

namespace HitPan.Watchdog.Stages;

public class WS28A_WindowsUpdate
{
    private readonly ILogger<WS28A_WindowsUpdate> _logger;

    public WS28A_WindowsUpdate(ILogger<WS28A_WindowsUpdate> logger)
    {
        _logger = logger;
    }

    public bool DetectImminentReboot()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var log = new EventLog("System");
            var cutoff = DateTime.Now.AddMinutes(-10);
            for (int i = log.Entries.Count - 1; i >= 0 && i >= log.Entries.Count - 200; i--)
            {
                var entry = log.Entries[i];
                if (entry.TimeGenerated < cutoff) break;
                if (entry.InstanceId == 1074 && entry.Message.Contains("TrustedInstaller"))
                {
                    _logger.LogWarning("WS-28-A: TrustedInstaller 1074 detected at {Time}", entry.TimeGenerated);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS-28-A: EventLog read failure");
        }
        return false;
    }
}
