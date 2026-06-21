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
            // 봉합 (2026-06-20, WD-03): 종전 fail-open(return true)은 열거 실패 시 서비스가 실제로
            //   사라졌어도 '존재함'으로 오판해 재설치를 영원히 막았다(헌법 #28 자가복구 침묵 차단).
            //   존재여부 점검은 fail-closed 가 정석 — 부재로 가정해 재설치를 시도하게 한다.
            //   재설치는 CoolDown 게이트 + 멱등(service install)로 보호되므로 false 가 안전.
            //   (동일 cloudflared 를 fail-closed 로 보는 WS-28-I 와도 정합.)
            _logger.LogWarning(ex, "WS-28-D: service enumeration failure — 부재로 가정(fail-closed)");
            return false;
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

            // 봉합 (2026-06-21, 7차 전수조사 D6-P0-02, 사장님 결재 A안 "토큰 기반 통일"):
            //   종전엔 'service install' 을 토큰 없이 호출했다. 그러나 현재 터널은 관리형(토큰 기반)이라
            //   인스톨러는 'service install {token}' 으로 설치한다(.iss 6-2). 토큰 없는 재설치는 관리형
            //   터널이 안 붙어 통신이 영구 다운(헌법 #27·#28). db.conf 의 TUNNEL_TOKEN(인스톨러가 저장)을
            //   읽어 동일 모델로 재설치한다. 토큰 부재(LOCAL 모드·구버전 설치)면 인자 없이 호출하되 경고를
            //   남긴다 — 관리형 터널이면 이 경로는 실패하고 다음 사이클이 재시도(자해 아님, 단순 무복구).
            var tunnelToken = DbConfReader.GetValue("TUNNEL_TOKEN");
            var installArgs = string.IsNullOrWhiteSpace(tunnelToken)
                ? "service install"
                : $"service install {tunnelToken}";
            if (string.IsNullOrWhiteSpace(tunnelToken))
                _logger.LogWarning("WS-28-D: db.conf 에 TUNNEL_TOKEN 부재 — 토큰 없이 재설치 시도(관리형 터널이면 미복구, 다음 사이클 재시도)");

            var psi = new ProcessStartInfo(exePath, installArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };
            using var p = Process.Start(psi);
            if (p == null) return false;

            // 봉합 (2026-06-23, 5차 전수조사 WD5-05 P2):
            //   ① 종전엔 RedirectStandardOutput/Error=true 인데 출력을 읽지 않아, 출력이 파이프 버퍼(약 4KB)를
            //      채우면 cloudflared 가 쓰기 블록 → WaitForExitAsync 영구 대기(데드락). 출력을 비동기로 동시에
            //      읽어 파이프를 비운다(ReadToEndAsync 를 WaitForExit 전에 시작).
            //   ② 종전엔 ExitCode 미검사로 service install 실패해도 sc.Start() 진행. ExitCode 검사 추가.
            var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (p.ExitCode != 0)
            {
                // 봉합 (2026-06-21, D6-P0-02-FIX, 검증팀장 P2): 토큰을 service install 인자로 넘기기 시작했으므로
                //   cloudflared 가 자기 출력에 토큰을 echo 할 이론적 노출면이 생긴다(헌법 #22·#23). 로그로 내보내기
                //   전에 토큰 문자열을 마스킹한다(통상 echo 안 하나 보수적 차단).
                var rawErr = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                var safeErr = string.IsNullOrWhiteSpace(tunnelToken)
                    ? rawErr
                    : rawErr.Replace(tunnelToken, "***REDACTED***");
                _logger.LogError("WS-28-D: 'service install' 실패 ExitCode {Code}: {Err}", p.ExitCode, safeErr);
                return false;
            }

            using var sc = new ServiceController("cloudflared");
            // 봉합 (2026-06-21, D6-P0-02-FIX, 재교차검증 설계팀장 P3): 이번 봉합으로 '서비스가 살아있는데
            //   토큰 재설치'를 강제하는 경로(관리형 다운 모드)가 열렸다. 이때 service install 이 기존 서비스를
            //   멈추지 않고 Running 으로 둔 채 끝나면, 종전의 무조건 sc.Start() 가 이미-Running 서비스에 Start 를
            //   호출해 InvalidOperationException → catch 에서 false 반환 → 실제로는 복구됐는데 '복구 실패'로
            //   오기록(MarkRecovery 누락)됐다. WS-28-C(재시작부)와 동일하게 Running 이면 Stop 후 Start 로 정리한다.
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
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
