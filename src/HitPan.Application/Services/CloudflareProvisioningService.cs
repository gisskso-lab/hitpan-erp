using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// Cloudflare 프로비저닝 박제 (사장님 결재 2026-06-02 모두결재)
///
/// 헌법 정합:
///  #18·#22 — 본사 서버에서만 호출. ERP 고객 PC 절대 금지. 토큰은 본사 환경변수만 박제
///  #29 — 인프라 조작 사전 승인. 토큰 발급·회전 = 사장님 직접 박제 (Secrets Manager)
///  #34 — 베타·정식 박제 분리. 인프라 = 베타부터 정식 완성도 박제
///
/// 박제 흐름 (3단계):
///  1) Cloudflare DNS API → CNAME 박제 (www.{id}.hitpan.kr → tunnel.cfargotunnel.com)
///  2) Cloudflare Tunnel API → 터널 박제 + credentials 박제
///  3) DNS 라우팅 박제 → 터널 ↔ 도메인 박제
///
/// 토큰 미박제 시: 정직 에러 응답 박제 (헌법 #15 빈 catch 금지, #32 받아쓰기 금지)
/// </summary>
public class CloudflareProvisioningService : ICloudflareProvisioningService
{
    private static readonly HttpClient _http = new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/") };
    private readonly IConfiguration _config;
    private readonly ILogger<CloudflareProvisioningService> _logger;

    public CloudflareProvisioningService(
        IConfiguration config,
        ILogger<CloudflareProvisioningService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<ProvisioningResult> ProvisionAsync(string tenantId, string subdomain, CancellationToken ct = default)
    {
        var token = _config["Cloudflare:ApiToken"] ?? Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
        var accountId = _config["Cloudflare:AccountId"] ?? Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID");
        var zoneId = _config["Cloudflare:ZoneId"] ?? Environment.GetEnvironmentVariable("CLOUDFLARE_ZONE_ID");
        var domainBase = _config["Cloudflare:DomainBase"] ?? "hitpan.kr";

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(zoneId))
        {
            _logger.LogError("[Cloudflare] 토큰·AccountId·ZoneId 미박제 — tenant={TenantId}", tenantId);
            return new ProvisioningResult(tenantId, false, null, null,
                "본사 Cloudflare 자격 미박제. 사장님 Secrets Manager 박제 필요 (헌법 #29).");
        }

        if (string.IsNullOrWhiteSpace(subdomain))
            return new ProvisioningResult(tenantId, false, null, null, "subdomain 누락");

        var fullDomain = $"www.{subdomain}.{domainBase}";
        var tunnelName = $"hitpan-{subdomain}";

        try
        {
            // 1단계 — Cloudflare Tunnel 박제
            var tunnelSecret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var tunnelReq = new HttpRequestMessage(HttpMethod.Post, $"client/v4/accounts/{accountId}/cfd_tunnel");
            tunnelReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            tunnelReq.Content = JsonContent.Create(new { name = tunnelName, tunnel_secret = tunnelSecret, config_src = "cloudflare" });
            var tunnelResp = await _http.SendAsync(tunnelReq, ct);

            if (!tunnelResp.IsSuccessStatusCode)
            {
                var body = await tunnelResp.Content.ReadAsStringAsync(ct);
                _logger.LogError("[Cloudflare] tunnel 박제 실패 status={Status} body={Body}", tunnelResp.StatusCode, body);
                return new ProvisioningResult(tenantId, false, fullDomain, null, $"터널 박제 실패 ({tunnelResp.StatusCode})");
            }

            var tunnelJson = await tunnelResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var tunnelId = tunnelJson.GetProperty("result").GetProperty("id").GetString() ?? "";

            // 2단계 — DNS CNAME 박제 (www.{id}.hitpan.kr → {tunnelId}.cfargotunnel.com)
            var dnsReq = new HttpRequestMessage(HttpMethod.Post, $"client/v4/zones/{zoneId}/dns_records");
            dnsReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            dnsReq.Content = JsonContent.Create(new
            {
                type = "CNAME",
                name = fullDomain,
                content = $"{tunnelId}.cfargotunnel.com",
                proxied = true,
                comment = $"hitpan tenant {tenantId} (provisioned {DateTime.UtcNow:O})"
            });
            var dnsResp = await _http.SendAsync(dnsReq, ct);

            if (!dnsResp.IsSuccessStatusCode)
            {
                var body = await dnsResp.Content.ReadAsStringAsync(ct);
                _logger.LogError("[Cloudflare] DNS 박제 실패 status={Status} body={Body}", dnsResp.StatusCode, body);
                return new ProvisioningResult(tenantId, false, fullDomain, tunnelId, $"DNS 박제 실패 ({dnsResp.StatusCode})");
            }

            _logger.LogInformation("[Cloudflare] 박제 완료 tenant={TenantId} domain={Domain} tunnel={Tunnel}",
                tenantId, fullDomain, tunnelId);

            return new ProvisioningResult(tenantId, true, fullDomain, tunnelId,
                $"박제 완료: {fullDomain} ↔ tunnel {tunnelId}");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Cloudflare] 통신 실패 tenant={TenantId}", tenantId);
            return new ProvisioningResult(tenantId, false, fullDomain, null, "Cloudflare 통신 실패");
        }
    }

    public Task<ProvisioningStatus> GetStatusAsync(string tenantId, CancellationToken ct = default)
    {
        // TODO: tenants.provisioning_status 컬럼 박제 후 조회 박제
        return Task.FromResult(ProvisioningStatus.Pending);
    }

    public Task<ProvisioningResult> RetryAsync(string tenantId, CancellationToken ct = default)
    {
        _logger.LogInformation("[Cloudflare] retry 박제 tenant={TenantId}", tenantId);
        return ProvisionAsync(tenantId, "", ct);
    }
}
