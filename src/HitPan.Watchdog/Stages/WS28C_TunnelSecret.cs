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
        // P0-2 봉합(2026-06-20): 로그 파일이 없으면(초기 설치 직후·로그 삭제) 종전엔 무조건 "정상"으로
        // 판정해 실제 터널 무효화를 놓쳤다(5/15 demo 6시간 다운 패턴). 로그 부재 시 cloudflared
        // 서비스 상태를 직접 조회해, 자격증명 파일은 있는데 서비스가 죽어있으면 무효화로 간주한다.
        if (!File.Exists(_logPath))
            return DetectViaServiceFallback();

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
            _logger.LogWarning(ex, "WS-28-C: log read failure — 서비스 상태 폴백으로 전환");
            return DetectViaServiceFallback();
        }
    }

    /// <summary>
    /// 로그 부재/읽기 실패 시 폴백. 터널 자격증명 파일은 존재하는데 cloudflared 서비스가
    /// 멈춰/없으면 무효화 의심으로 true. 자격증명 자체가 없으면 WS-28-D(재설치) 영역이라 false.
    /// </summary>
    private bool DetectViaServiceFallback()
    {
        try
        {
            var tunnelId = Environment.GetEnvironmentVariable("HITPAN_TUNNEL_ID");
            if (string.IsNullOrEmpty(tunnelId)) return false;

            var credFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cloudflared", $"{tunnelId}.json");
            if (!File.Exists(credFile)) return false; // 자격증명 자체 부재 → WS-28-D 소관

            using var sc = new ServiceController("cloudflared");
            // 검증팀장 P0-2 반려 반영(2026-06-20): 정지 상태를 나열하면 StartPending(재부팅 직후
            // 정상 시작 중)을 "무효화"로 오판해, 멀쩡히 켜지는 서비스를 재생성하는 자해가 난다
            // (5/15 demo "정상을 이상으로 판정" 함정). 정상 상태를 화이트리스트로 명시하고,
            // 그 외(Stopped/StopPending/Paused)만 정지로 본다. StartPending·ContinuePending은 정상.
            var running = sc.Status is ServiceControllerStatus.Running
                or ServiceControllerStatus.StartPending
                or ServiceControllerStatus.ContinuePending;
            if (!running)
                _logger.LogWarning("WS-28-C: 로그 부재 + cloudflared 서비스 정지({Status}) — 무효화 의심", sc.Status);
            return !running;
        }
        catch (InvalidOperationException)
        {
            // ServiceController 가 'cloudflared' 서비스를 못 찾음 → 서비스 미존재. WS-28-D 소관이라 false.
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS-28-C: 서비스 상태 폴백 조회 실패");
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
