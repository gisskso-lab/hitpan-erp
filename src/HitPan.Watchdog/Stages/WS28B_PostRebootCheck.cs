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
            // 봉합 (2026-06-23, 6차 전수조사 D-P2-03): 종전엔 uptime<5분 일 때만 true 라, 워치독이 지연 시작
            //   (Defender 스캔·디스크 부하로 부팅 후 5분 넘어 첫 틱)되면 재부팅 복구를 영구 스킵했다(헌법 #28).
            //   또 false 분기에서 ClearFlag 를 안 해 만료 플래그가 잔존했다.
            //   해법: ① 창을 30분으로 넓혀 지연 시작 흡수 ② '이번 부팅 세션에서 찍힌 플래그인지'를 부팅 시각과
            //   비교해 판정(uptime 으로 부팅 시각 역산, 플래그 기록 시각이 그 이후면 이번 부팅 건) ③ 24시간 넘은
            //   오래된 플래그는 만료로 보고 정리해 잔존 차단.
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var bootTimeUtc = DateTime.UtcNow - uptime;

            if (DateTime.TryParse(File.ReadAllText(_flagPath).Trim(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var flagUtc))
            {
                // 24시간 넘은 플래그 = 만료(처리 못 한 채 방치). 정리하고 스킵 — 정기 루프가 점검을 이어간다.
                if (DateTime.UtcNow - flagUtc > TimeSpan.FromHours(24))
                {
                    ClearFlag();
                    return false;
                }
                // 부팅 시각 이후 ~ 부팅 30분 이내에 찍힌 플래그면 이번 부팅의 미처리 건.
                return flagUtc >= bootTimeUtc.AddMinutes(-5) && uptime.TotalMinutes < 30;
            }

            // 플래그 시각 파싱 실패(구버전 형식 등) — uptime 창만으로 보수적 판정(30분).
            return uptime.TotalMinutes < 30;
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
