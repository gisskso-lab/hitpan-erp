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
                // 봉합 (2026-06-23, 6차 전수조사 D-P2-04): 종전엔 Message.Contains("TrustedInstaller") 로 걸렀는데,
                //   한국어 Windows 의 1074 이벤트 메시지는 영문 리터럴 "TrustedInstaller" 를 포함하지 않을 수 있어
                //   (메시지 현지화) 재부팅 임박을 놓쳤다(헌법 #28 자가복구 미작동). 이벤트 ID 1074(예정된 재시작/종료)는
                //   그 자체로 재부팅 신호이므로, Message 영문 의존을 제거하고 Source(TrustedInstaller/Winlogon=업데이트·
                //   시스템 재부팅)로 좁힌다. Source 도 못 읽으면 1074 만으로 보수적 감지(post-reboot 점검은 재부팅이면
                //   도는 게 안전 — 헌법 #28).
                if (entry.InstanceId == 1074)
                {
                    var src = entry.Source ?? string.Empty;
                    var isSystemReboot = src.Length == 0
                        || src.Contains("TrustedInstaller", StringComparison.OrdinalIgnoreCase)
                        || src.Contains("Winlogon", StringComparison.OrdinalIgnoreCase)
                        || src.Contains("USER32", StringComparison.OrdinalIgnoreCase);
                    if (isSystemReboot)
                    {
                        _logger.LogWarning("WS-28-A: 재시작 이벤트 1074 감지 (Source={Source}) at {Time}", src, entry.TimeGenerated);
                        return true;
                    }
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
