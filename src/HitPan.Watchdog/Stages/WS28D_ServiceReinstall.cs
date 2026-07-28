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

            // ★ 봉합 (2026-07-15, 작지서 20260714작1 W2 — Sandbox 실측: 토큰 180자 존재+서비스 부재인데
            //   7분+ 무복구): 종전엔 'service install' 1회 실패(ExitCode!=0)면 그대로 false 반환 → 다음
            //   사이클도 같은 사유로 실패를 반복해 사실상 영구 무복구였다(헌법 #27 5분 게이트 실측 실패).
            //   3단 방어로 승격: ① service install → ② 실패 시 SCM 잔재 정리(sc delete) 후 1회 재시도
            //   (설치기 W1-3 삭제-등록 경합과 동일 패턴) → ③ 그래도 실패면 sc create 직접 등록 폴백
            //   (cloudflared 자체 인스톨러 우회 — binPath 'tunnel run --token'은 관리형 터널 표준 실행형).
            var installOk = await RunProcessAsync(exePath, installArgs, tunnelToken, ct) == 0;

            if (!installOk || !ServiceExists())
            {
                _logger.LogWarning("WS-28-D: 'service install' 1차 실패 — SCM 잔재 정리 후 재시도");
                await RunProcessAsync("sc.exe", "delete cloudflared", tunnelToken, ct); // 부재·삭제지연이면 무해
                await Task.Delay(2000, ct);
                installOk = await RunProcessAsync(exePath, installArgs, tunnelToken, ct) == 0;
            }

            if ((!installOk || !ServiceExists()) && !string.IsNullOrWhiteSpace(tunnelToken))
            {
                _logger.LogWarning("WS-28-D: 'service install' 2차 실패 — sc create 직접 등록 폴백");
                var binPath = "\\\"" + exePath + "\\\" tunnel run --token " + tunnelToken;
                await RunProcessAsync("sc.exe",
                    "create cloudflared binPath= \"" + binPath + "\" start= auto DisplayName= \"Cloudflared agent\"",
                    tunnelToken, ct);
                await RunProcessAsync("sc.exe",
                    "failure cloudflared reset= 60 actions= restart/5000/restart/5000/restart/60000",
                    tunnelToken, ct);
            }

            if (!ServiceExists())
            {
                _logger.LogError("WS-28-D: 서비스 등록 실패(install·재시도·sc create 3경로 전부) — 다음 사이클 재시도");
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

    /// <summary>
    /// ★ 봉합 (2026-07-15, 작지서 20260714작1 W2): 외부 프로세스 실행 공통 헬퍼.
    ///   - 파이프 데드락 차단(WD5-05 P2 패턴: 출력 비동기 소진 후 WaitForExit)
    ///   - ExitCode!=0 이면 stderr/stdout 을 토큰 마스킹(헌법 #22·#23) 후 로그 — 침묵 금지(헌법 #15)
    ///   - 반환 = ExitCode (프로세스 시작 실패는 -1)
    /// </summary>
    private async Task<int> RunProcessAsync(string fileName, string arguments, string? secretToMask, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                _logger.LogError("WS-28-D: 프로세스 시작 실패: {File}", fileName);
                return -1;
            }
            var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (p.ExitCode != 0)
            {
                var rawErr = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                var safeErr = string.IsNullOrWhiteSpace(secretToMask)
                    ? rawErr
                    : rawErr.Replace(secretToMask, "***REDACTED***");
                var safeArgs = string.IsNullOrWhiteSpace(secretToMask)
                    ? arguments
                    : arguments.Replace(secretToMask, "***REDACTED***");
                _logger.LogError("WS-28-D: '{File} {Args}' 실패 ExitCode {Code}: {Err}",
                    Path.GetFileName(fileName), safeArgs, p.ExitCode, safeErr);
            }
            return p.ExitCode;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WS-28-D: 프로세스 실행 예외: {File}", fileName);
            return -1;
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
