using System.Diagnostics;
using System.ServiceProcess;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog.Stages;

[SupportedOSPlatform("windows")]
public class WS28I_FourProcess
{
    private readonly ILogger<WS28I_FourProcess> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly WatchdogOptions _options;

    public WS28I_FourProcess(
        ILogger<WS28I_FourProcess> logger,
        IHttpClientFactory httpFactory,
        IOptions<WatchdogOptions> options)
    {
        _logger = logger;
        _httpFactory = httpFactory;
        _options = options.Value;
    }

    public async Task<Dictionary<string, bool>> CheckAllAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, bool>();

        foreach (var svc in _options.Processes.Services)
        {
            try
            {
                using var sc = new ServiceController(svc);
                result[svc] = sc.Status == ServiceControllerStatus.Running;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WS-28-I: service {Svc} check failure", svc);
                result[svc] = false;
            }
        }

        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(5);
        foreach (var ep in _options.Processes.HttpEndpoints)
        {
            try
            {
                var r = await http.GetAsync(ep.Url, ct);
                result[ep.Name] = r.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WS-28-I: endpoint {Name} check failure", ep.Name);
                result[ep.Name] = false;
            }
        }

        return result;
    }

    public bool TryRestartService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Running) return true;
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            _logger.LogWarning("WS-28-I: service {Svc} restarted", serviceName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WS-28-I: service {Svc} restart failure", serviceName);
            return false;
        }
    }

    /// <summary>
    /// 봉합 (2026-06-25, ERP 자가복구 A): HTTP 엔드포인트(ERP API)가 응답 없을 때 그 엔드포인트의
    /// RestartTask(인스톨러가 ONSTART 로 등록한 작업 스케줄러 작업명 'HitPan-ERP-API-tenant-{슬롯}')를
    /// 'schtasks /Run' 으로 다시 실행해 ERP 를 부활시킨다.
    ///
    /// 왜 서비스(TryRestartService)가 아니라 schtasks 인가:
    ///   ERP API 는 Windows 서비스가 아니라 작업 스케줄러로 자동시작된다(.iss 9번, 환경변수 폐기 사고
    ///   #41·#42·#39 봉합으로 db.conf 직접 읽기 모델 = 서비스화 시 또 대수술). 따라서 동일 작업을 /Run
    ///   재실행하는 것이 EXE·db.conf 무수정으로 ERP 를 살리는 정공법이다.
    ///   schtasks /Run 은 이미 실행 중이면 새 인스턴스를 띄우지 않으므로(작업당 단일 인스턴스 기본),
    ///   이 호출은 CoolDown 게이트와 함께 자해(중복 기동)를 일으키지 않는다.
    /// </summary>
    public bool TryRestartTask(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName))
        {
            _logger.LogWarning("WS-28-I: RestartTask 미설정 — ERP 재기동 경로 없음(감지만). db.conf/appsettings 확인");
            return false;
        }

        try
        {
            // schtasks /Run /TN "<작업명>" — ONSTART 작업을 즉시 1회 실행. 종료코드 0 = 성공.
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Run /TN \"{taskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                _logger.LogError("WS-28-I: schtasks 프로세스 시작 실패 (task {Task})", taskName);
                return false;
            }
            p.WaitForExit(30000);
            if (!p.HasExited)
            {
                try { p.Kill(); } catch { /* best-effort */ }
                _logger.LogError("WS-28-I: schtasks /Run 시간초과 (task {Task})", taskName);
                return false;
            }
            if (p.ExitCode == 0)
            {
                _logger.LogWarning("WS-28-I: ERP 작업 재기동 성공 (task {Task})", taskName);
                return true;
            }
            var err = p.StandardError.ReadToEnd();
            _logger.LogError("WS-28-I: schtasks /Run 실패 코드={Code} (task {Task}) {Err}", p.ExitCode, taskName, err);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WS-28-I: ERP 작업 재기동 예외 (task {Task})", taskName);
            return false;
        }
    }
}
