namespace HitPan.Watchdog.Stages;

public class WS28B_PostRebootCheck
{
    private readonly ILogger<WS28B_PostRebootCheck> _logger;
    private readonly string _flagPath;

    public WS28B_PostRebootCheck(ILogger<WS28B_PostRebootCheck> logger)
    {
        _logger = logger;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HitPan", "Watchdog");
        Directory.CreateDirectory(dir);
        _flagPath = Path.Combine(dir, "post_reboot_check.flag");
    }

    public void MarkPostRebootCheck()
    {
        try
        {
            File.WriteAllText(_flagPath, DateTime.UtcNow.ToString("O"));
            _logger.LogInformation("WS-28-B: post-reboot check flag set");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS-28-B: flag write failure");
        }
    }

    public bool ShouldRunPostRebootCheck()
    {
        if (!File.Exists(_flagPath)) return false;
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return uptime.TotalMinutes < 5;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void ClearFlag()
    {
        try { if (File.Exists(_flagPath)) File.Delete(_flagPath); }
        catch (Exception ex) { _logger.LogWarning(ex, "WS-28-B: flag delete failure"); }
    }
}
