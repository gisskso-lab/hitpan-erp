using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HitPan.Backoffice.API.Services;

// Cloudflare 도메인 자동 발급 골격 (사장님 결재 2026-06-05, 헌법 #29 정합)
//
// 환경변수 (모두 설정해야 실제 호출):
//   CLOUDFLARE_API_TOKEN          — Zone DNS 권한, Tunnels 권한
//   CLOUDFLARE_ZONE_ID            — hitpan.kr Zone ID
//   CLOUDFLARE_ACCOUNT_ID         — Cloudflare Account ID
//   CLOUDFLARE_TUNNEL_TEMPLATE    — (선택) 터널 템플릿 ID
//
// 헌법 #29 가드:
//   - 토큰 미설정 시 모든 메서드 InvalidOperationException → 503
//   - 사장님 결재 환경변수 설정 후에만 실제 발급 가능
//   - 본 서비스는 골격 — 실제 API 호출 흐름 정의, 호출 자체는 사장님 결재 영역
public interface ICloudflareDomainService
{
    bool IsConfigured { get; }
    Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, CancellationToken ct);
    Task<bool> RevokeAsync(string cfZoneId, string cfRecordId, string? cfTunnelId, CancellationToken ct);
}

public record DomainIssueResult(string Domain, string ZoneId, string RecordId, string? TunnelId);

public class CloudflareDomainService : ICloudflareDomainService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CloudflareDomainService> _logger;
    private readonly string? _token;
    private readonly string? _zoneId;
    private readonly string? _accountId;
    private readonly string _baseDomain;

    public CloudflareDomainService(IConfiguration config, IHttpClientFactory httpFactory, ILogger<CloudflareDomainService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _token = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
        _zoneId = Environment.GetEnvironmentVariable("CLOUDFLARE_ZONE_ID");
        _accountId = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID");
        _baseDomain = config["Cloudflare:BaseDomain"] ?? "hitpan.kr";
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_token)
        && !string.IsNullOrWhiteSpace(_zoneId)
        && !string.IsNullOrWhiteSpace(_accountId);

    public async Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Cloudflare 환경변수 미설정 — 사장님 결재 후 CLOUDFLARE_API_TOKEN·ZONE_ID·ACCOUNT_ID 설정 후 재시도");

        var domain = $"www.{tenantCode.ToLowerInvariant()}.{_baseDomain}";
        _logger.LogInformation("[CFDomain] 발급 시작 tenant={Tid} domain={Domain}", tenantId, domain);

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        // 1) DNS CNAME 레코드 생성 (target = 본사 cloudflared 터널 도메인)
        //    실제 호출은 사장님 결재 환경변수 설정 후에만
        var dnsPayload = new
        {
            type = "CNAME",
            name = domain,
            content = $"{tenantCode.ToLowerInvariant()}.cfargotunnel.com",
            ttl = 1,
            proxied = true
        };

        var dnsRes = await http.PostAsJsonAsync($"zones/{_zoneId}/dns_records", dnsPayload, ct);
        var dnsBody = await dnsRes.Content.ReadAsStringAsync(ct);
        if (!dnsRes.IsSuccessStatusCode)
        {
            _logger.LogWarning("[CFDomain] DNS 생성 실패 {Status} {Body}", dnsRes.StatusCode, dnsBody);
            throw new InvalidOperationException($"DNS 레코드 생성 실패 ({(int)dnsRes.StatusCode}): {dnsBody}");
        }

        using var dnsJson = JsonDocument.Parse(dnsBody);
        var recordId = dnsJson.RootElement.GetProperty("result").GetProperty("id").GetString() ?? "";

        // 2) cloudflared 터널은 본사 사전 발급 또는 별도 결재 흐름 (헌법 #29) — 본 골격에선 null
        return new DomainIssueResult(domain, _zoneId!, recordId, null);
    }

    public async Task<bool> RevokeAsync(string cfZoneId, string cfRecordId, string? cfTunnelId, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Cloudflare 환경변수 미설정");

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        var res = await http.DeleteAsync($"zones/{cfZoneId}/dns_records/{cfRecordId}", ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[CFDomain] 회수 실패 {Status} {Body}", res.StatusCode, body);
            return false;
        }
        return true;
    }
}
