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
            var tunnelId = ReadTunnelId();
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

    // 봉합 (2026-06-21, 7차 전수조사 D6-P0-01, 사장님 결재 "db.conf 단일출처로 통합"):
    //   종전엔 Machine 환경변수 HITPAN_TUNNEL_ID 를 읽었으나, 인스톨러가 이 env var 를 더 이상 만들지 않아
    //   (2026-06-12 폐기) 신규설치 PC 에서 항상 null → 터널 자가복구가 영구 무력(헌법 #28·#30).
    //   인스톨러가 {app}\db.conf 에 쓰는 TUNNEL_ID(=백오피스 응답 domain.tunnelId, CF 터널 UUID)를 읽는다.
    //   env var 폴백을 남겨 구버전 설치(환경변수 잔존 PC)와도 하위호환.
    private static string? ReadTunnelId() =>
        DbConfReader.GetValue("TUNNEL_ID")
        ?? Environment.GetEnvironmentVariable("HITPAN_TUNNEL_ID", EnvironmentVariableTarget.Machine)
        ?? Environment.GetEnvironmentVariable("HITPAN_TUNNEL_ID");

    /// <summary>
    /// 봉합 (2026-06-21, 7차 전수조사 D6-P0-02-FIX, 교차검증 설계팀장 P1 발견):
    ///   현재 터널이 관리형(토큰 기반)인지 판정한다 — TUNNEL_TOKEN(db.conf) 존재 + 자가관리 자격증명
    ///   파일({tunnelId}.json) 부재. 관리형 터널의 자가복구는 WS-28-C(자가관리 재생성)가 아니라
    ///   WS-28-D(service install {token})가 담당하므로, Worker 가 이 신호를 보고 D 호출 게이트(!ServiceExists)를
    ///   우회해 D 를 강제해야 한다. 그러지 않으면 '서비스는 살아있고 터널 secret 만 무효화'된 관리형 다운 모드
    ///   (= 헌법 #28 이 정의한 5/15 demo 사고)에서 C 는 스킵·D 는 !ServiceExists 미충족으로 어느 경로로도
    ///   토큰 재설치가 발화하지 않아 자가복구 사각지대가 남는다.
    /// </summary>
    public bool IsManagedTunnel()
    {
        var tunnelId = ReadTunnelId();
        if (string.IsNullOrEmpty(tunnelId)) return false;
        var managedToken = DbConfReader.GetValue("TUNNEL_TOKEN");
        if (string.IsNullOrWhiteSpace(managedToken)) return false;
        var selfManagedCredFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cloudflared", $"{tunnelId}.json");
        return !File.Exists(selfManagedCredFile);
    }

    public async Task<bool> RegenerateAsync(CancellationToken ct = default)
    {
        var tunnelId = ReadTunnelId();
        if (string.IsNullOrEmpty(tunnelId))
        {
            _logger.LogError("WS-28-C: HITPAN_TUNNEL_ID env var missing");
            return false;
        }

        // 봉합 (2026-06-21, 7차 전수조사 D6-P0-02, 사장님 결재 A안 "토큰 기반 통일"):
        //   현재 터널은 관리형(토큰 기반)이라 자격증명 파일({tunnelId}.json)이 디스크에 생기지 않는다.
        //   아래 'cloudflared tunnel token --cred-file' 는 자가관리 터널 전용 — 관리형에선 credFile 이
        //   없어 무의미하거나 자해(없는 파일 백업/복원 시도)다. 따라서 관리형이면 자가관리 재생성을 건너뛴다.
        //
        //   교차검증 정정 (D6-P0-02-FIX, 설계팀장 P1): 종전 주석은 "C 직후 D 를 호출하므로 여기서 true 면
        //   D 가 복구를 이어받는다(흐름 끊김 0)"고 했으나 거짓이었다 — Worker 의 D 호출 게이트가
        //   !ServiceExists 라, 서비스가 살아있고 터널만 무효화된 관리형 대표 다운 모드에서 D 가 호출되지
        //   않았다. 이 true 의 실제 의미는 "C 는 관리형이라 할 일이 없다"일 뿐이며, D 강제 호출은 Worker 가
        //   IsManagedTunnel() 신호로 별도 보장한다(Worker.cs 정기 루프·FullRecovery).
        if (IsManagedTunnel())
        {
            _logger.LogWarning("WS-28-C: 관리형 터널(토큰 기반) 감지 — 자가관리 자격증명 재생성 스킵(토큰 재설치는 WS-28-D 소관)");
            return true;
        }

        var credDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cloudflared");
        var credFile = Path.Combine(credDir, $"{tunnelId}.json");
        var backup = $"{credFile}.bak_{DateTime.Now:yyyyMMddHHmmss}";
        var movedToBackup = false;

        // 진범 봉합 (2026-06-20, 2차 전수조사 WD-02 P1):
        //   종전엔 원본 cred 를 backup 으로 이동(복사 아님)한 뒤 토큰 재생성이 실패하면 false 만 반환하고
        //   원본을 복원하지 않아, 멀쩡하던 자격증명까지 사라져 터널이 더 확실히 죽었다(자가복구→자해).
        //   재생성에 실패하면(새 credFile 미생성) backup 을 원위치로 되돌려 0-touch 자가회복(헌법 #30)을 지킨다.
        void RestoreBackupOnFailure()
        {
            try
            {
                // 2차 봉합 (2026-06-20, 3차 전수조사 WD-02-01 P1):
                //   이 함수는 실패 3경로(프로세스 null·ExitCode!=0·예외)에서만 호출된다. 따라서 호출 시점에
                //   credFile 이 존재한다면 그건 cloudflared 가 실패하며 남긴 0바이트/부분기록된 '손상' 파일이다.
                //   종전 가드 `!File.Exists(credFile)` 는 이 손상 파일을 현역으로 고착시키고 backup 복원을 막아,
                //   봉합이 없애려던 자해 패턴을 재현했다. 실패 = 무조건 원본(backup) 우선:
                //   ① 부분 생성된 손상 credFile 을 먼저 삭제 → ② backup 을 원위치로 되돌린다.
                if (!movedToBackup || !File.Exists(backup)) return;

                if (File.Exists(credFile))
                {
                    File.Delete(credFile); // 실패가 남긴 손상 출력물 제거
                }
                File.Move(backup, credFile, overwrite: true);
                _logger.LogWarning("WS-28-C: regeneration failed — original credential restored from backup");
            }
            catch (Exception rex)
            {
                _logger.LogError(rex, "WS-28-C: backup restore failed");
            }
        }

        // 봉합 (2026-06-20, 3차 전수조사 WD-02-02 P3): 재생성 성공 시 오래된 .bak_* 자격증명 사본을
        //   최신 2개만 남기고 정리. 미정리 시 1분 주기·반복 재생성으로 ~/.cloudflared 에 평문 사본이
        //   단조 누적(디스크 위생 + 자격증명 노출면). 정리 실패는 비치명적 — 로깅 후 무시.
        void PruneOldBackups(string credPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(credPath);
                var prefix = Path.GetFileName(credPath) + ".bak_";
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                var stale = Directory.EnumerateFiles(dir, prefix + "*")
                    .OrderByDescending(f => f)   // 파일명 끝 타임스탬프(yyyyMMddHHmmss) 사전순 = 시간순
                    .Skip(2)
                    .ToList();
                foreach (var f in stale)
                {
                    File.Delete(f);
                }
                if (stale.Count > 0)
                {
                    _logger.LogInformation("WS-28-C: pruned {Count} old credential backup(s)", stale.Count);
                }
            }
            catch (Exception pex)
            {
                _logger.LogWarning(pex, "WS-28-C: backup prune failed");
            }
        }

        try
        {
            if (File.Exists(credFile))
            {
                File.Move(credFile, backup, overwrite: true);
                movedToBackup = true;
            }

            var psi = new ProcessStartInfo("cloudflared", $"tunnel token --cred-file \"{credFile}\" {tunnelId}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                _logger.LogError("WS-28-C: cloudflared process start returned null");
                RestoreBackupOnFailure();
                return false;
            }
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0)
            {
                var err = await p.StandardError.ReadToEndAsync(ct);
                _logger.LogError("WS-28-C: cloudflared exit {Code}: {Err}", p.ExitCode, err);
                RestoreBackupOnFailure();
                return false;
            }

            // 봉합 (2026-06-23, 5차 전수조사 WD5-01 P1):
            //   여기 도달 = cloudflared 가 ExitCode 0 으로 새 자격증명(credFile)을 정상 생성한 상태.
            //   종전엔 이 아래 서비스 재시작이 30초 내 안 되면 WaitForStatus 가 TimeoutException →
            //   바깥 catch 의 RestoreBackupOnFailure() 가 '방금 만든 정상 자격증명'을 지우고 깨진 백업을
            //   복원하는 자해가 났다(3차에 봉합한 자해 패턴이 다른 경로로 재현).
            //   해법: 자격증명 생성이 성공한 시점에 복원 트리거(movedToBackup)를 끈다 — 이후 무슨 일이
            //   있어도 새 자격증명은 보존. 서비스 재시작 실패는 별도 처리하고 다음 워치독 사이클(WS-28-D/I)이
            //   서비스만 다시 살리면 된다. 자격증명은 이미 정상이므로 백업 복원은 틀린 행동.
            movedToBackup = false;
            PruneOldBackups(credFile);

            try
            {
                using var sc = new ServiceController("cloudflared");
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                _logger.LogWarning("WS-28-C: TunnelSecret regenerated, cloudflared restarted");
            }
            catch (Exception svcEx)
            {
                // 자격증명은 정상 생성됨 — 서비스 재시작만 실패. 자격증명은 건드리지 않고,
                // 서비스 복구는 다음 사이클/WS-28-D 에 위임. 재생성 자체는 성공으로 본다.
                _logger.LogError(svcEx, "WS-28-C: 자격증명 재생성 성공, 그러나 서비스 재시작 실패 — 다음 사이클이 서비스 복구. 자격증명 보존.");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WS-28-C: regeneration failure");
            RestoreBackupOnFailure();
            return false;
        }
    }
}
