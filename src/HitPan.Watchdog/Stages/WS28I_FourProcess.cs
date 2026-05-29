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
}
