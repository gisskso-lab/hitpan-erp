using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog.Telemetry;

public class MetaPingClient
{
    private readonly ILogger<MetaPingClient> _logger;
    private readonly WatchdogOptions _options;
    private readonly HttpClient _http;
    private static readonly HashSet<string> ForbiddenFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "tenant_id", "tenant_name", "company_name",
        "user_email", "user_name",
        "ip_address", "mac_address", "disk_serial",
        "revenue", "sales", "purchase",
        "transaction", "invoice", "item", "customer", "employee"
    };

    public MetaPingClient(ILogger<MetaPingClient> logger, IOptions<WatchdogOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12
            }
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task SendAsync(MetaPingPayload payload, CancellationToken ct = default)
    {
        if (!ValidateMinimalism(payload))
        {
            _logger.LogError("MetaPing: forbidden field detected (헌법 #22) — send aborted");
            return;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _options.MetaPingEndpoint)
            {
                Content = JsonContent.Create(payload)
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetBearerToken(payload.TenantIdHash));

            var r = await _http.SendAsync(req, ct);
            if (!r.IsSuccessStatusCode)
                _logger.LogWarning("MetaPing: HTTP {Code}", (int)r.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MetaPing: send failure");
        }
    }

    public async Task NotifyEmergencyAsync(string reason, string? stage, CancellationToken ct = default)
    {
        try
        {
            var p = new EmergencyPayload
            {
                TenantIdHash = Sha256(GetTenantId()),
                Reason = reason,
                Stage = stage,
                Timestamp = DateTime.UtcNow
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, _options.MetaPingEmergencyEndpoint)
            {
                Content = JsonContent.Create(p)
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetBearerToken(p.TenantIdHash));
            await _http.SendAsync(req, ct);
            _logger.LogError("MetaPing: emergency sent — {Reason} / {Stage}", reason, stage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MetaPing: emergency send failure");
        }
    }

    public static string Sha256(string s)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string GetTenantId() =>
        Environment.GetEnvironmentVariable("HITPAN_TENANT_ID", EnvironmentVariableTarget.Machine)
        ?? Environment.GetEnvironmentVariable("HITPAN_TENANT_ID")
        ?? "unknown";

    private static string GetBearerToken(string tenantIdHash)
    {
        var licenseKey = Environment.GetEnvironmentVariable("HITPAN_LICENSE_KEY", EnvironmentVariableTarget.Machine)
                         ?? Environment.GetEnvironmentVariable("HITPAN_LICENSE_KEY")
                         ?? "";
        var machineGuid = Environment.MachineName;
        return Sha256(licenseKey + "|" + machineGuid + "|" + tenantIdHash);
    }

    private static bool ValidateMinimalism(MetaPingPayload p)
    {
        if (!p.TenantIdHash.StartsWith("sha256:", StringComparison.Ordinal)) return false;
        foreach (var key in p.ProcessStatus.Keys)
        {
            if (ForbiddenFields.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }
}
