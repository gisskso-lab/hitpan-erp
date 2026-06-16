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
    // 사장님 결재 2026-06-09 — 도메인 별칭 우선 발급 (DomainAliasService 결과 사용)
    Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, string? domainAlias, CancellationToken ct);
    // 사장님 결재 Plan 2026-06-09 (Day 5) — cloudflared 터널 자동 발급 + 토큰 발급
    Task<TunnelIssueResult> IssueTunnelAsync(string tenantId, string tenantCode, CancellationToken ct);

    // 봉합 2026-06-16: DNS CNAME content를 새 터널 ID로 업데이트 (1033 사고 봉합)
    Task UpdateDnsTunnelTargetAsync(string recordId, string domain, string tunnelId, CancellationToken ct);
    Task<bool> RevokeAsync(string cfZoneId, string cfRecordId, string? cfTunnelId, CancellationToken ct);
    // 사장님 결재 2026-06-11 — 도메인 별칭으로 영역 검색 + 삭제 (중복 발급 차단 영역)
    Task<bool> RevokeByDomainAsync(string subdomain, CancellationToken ct);
}

public record DomainIssueResult(string Domain, string ZoneId, string RecordId, string? TunnelId);
public record TunnelIssueResult(string TunnelId, string TunnelToken, string CnameTarget);

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

    public Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, CancellationToken ct)
        => IssueAsync(tenantId, tenantCode, null, ct);

    public async Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, string? domainAlias, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Cloudflare 환경변수 미설정 — 사장님 결재 후 CLOUDFLARE_API_TOKEN·ZONE_ID·ACCOUNT_ID 설정 후 재시도");

        // 사장님 결재 2026-06-09 — 도메인 별칭 우선, 미설정 시 테넌트 코드 폴백 (헌법 #22 정합 — tenant_code 외부 노출 0)
        var subdomain = !string.IsNullOrWhiteSpace(domainAlias)
            ? domainAlias!.ToLowerInvariant()
            : tenantCode.ToLowerInvariant().Replace("-", "");
        var domain = $"{subdomain}.{_baseDomain}";
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
            content = $"{subdomain}.cfargotunnel.com",
            ttl = 1,
            proxied = true
        };

        var dnsRes = await http.PostAsJsonAsync($"zones/{_zoneId}/dns_records", dnsPayload, ct);
        var dnsBody = await dnsRes.Content.ReadAsStringAsync(ct);
        string recordId;
        if (!dnsRes.IsSuccessStatusCode)
        {
            // 봉합 2026-06-16 (사고: test000 1033): 이미 박혀있는 DNS 영역 (code 81053) = 정합 처리
            //   기존 영역: 승인 시 DNS 박힘 → 설치 시 다시 호출 → 81053 사고 → 터널 토큰 0건 → 1033 사고
            //   봉합: 81053 사고 영역 = 기존 record ID 영역 조회·반환 (Idempotent)
            if (dnsBody.Contains("81053") || dnsBody.Contains("already exists"))
            {
                _logger.LogInformation("[CFDomain] DNS 영역 박힘 영역 — 기존 영역 조회 가도 domain={Domain}", domain);
                var lookupRes = await http.GetAsync($"zones/{_zoneId}/dns_records?type=CNAME&name={domain}", ct);
                var lookupBody = await lookupRes.Content.ReadAsStringAsync(ct);
                if (!lookupRes.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[CFDomain] 기존 영역 조회 실패 {Status} {Body}", lookupRes.StatusCode, lookupBody);
                    throw new InvalidOperationException($"DNS 기존 영역 조회 실패 ({(int)lookupRes.StatusCode}): {lookupBody}");
                }
                using var lookupJson = JsonDocument.Parse(lookupBody);
                var resultArr = lookupJson.RootElement.GetProperty("result");
                if (resultArr.GetArrayLength() == 0)
                {
                    throw new InvalidOperationException($"DNS 81053 영역 박혔는데 조회 영역 0건: {dnsBody}");
                }
                recordId = resultArr[0].GetProperty("id").GetString() ?? "";
                _logger.LogInformation("[CFDomain] 기존 영역 정합 가도 record={Rid}", recordId);
            }
            else
            {
                _logger.LogWarning("[CFDomain] DNS 생성 실패 {Status} {Body}", dnsRes.StatusCode, dnsBody);
                throw new InvalidOperationException($"DNS 레코드 생성 실패 ({(int)dnsRes.StatusCode}): {dnsBody}");
            }
        }
        else
        {
            using var dnsJson = JsonDocument.Parse(dnsBody);
            recordId = dnsJson.RootElement.GetProperty("result").GetProperty("id").GetString() ?? "";
        }

        // 2) cloudflared 터널은 본사 사전 발급 또는 별도 결재 흐름 (헌법 #29) — 본 골격에선 null
        return new DomainIssueResult(domain, _zoneId!, recordId, null);
    }

    // 봉합 2026-06-16 (사고: test000 1033 재발):
    //   DNS Idempotent 봉합 후 기존 DNS record가 잘못된 터널을 가리키는 사고.
    //   터널 새로 발급되면 DNS CNAME content도 새 tunnelId.cfargotunnel.com 로 업데이트 필요.
    //   본 메서드 영역 = InstallerBootstrap에서 터널 발급 후 호출.
    public async Task UpdateDnsTunnelTargetAsync(string recordId, string domain, string tunnelId, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Cloudflare 환경변수 미설정");

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        var patchPayload = new
        {
            type = "CNAME",
            name = domain,
            content = $"{tunnelId}.cfargotunnel.com",
            ttl = 1,
            proxied = true
        };

        var patchRes = await http.PutAsJsonAsync($"zones/{_zoneId}/dns_records/{recordId}", patchPayload, ct);
        var patchBody = await patchRes.Content.ReadAsStringAsync(ct);
        if (!patchRes.IsSuccessStatusCode)
        {
            _logger.LogWarning("[CFDomain] DNS PATCH 실패 {Status} {Body}", patchRes.StatusCode, patchBody);
            throw new InvalidOperationException($"DNS PATCH 실패 ({(int)patchRes.StatusCode}): {patchBody}");
        }
        _logger.LogInformation("[CFDomain] DNS PATCH 완료 domain={Domain} target={TunnelId}.cfargotunnel.com",
            domain, tunnelId);
    }

    // 사장님 결재 Plan 2026-06-09 (Day 5) — cloudflared 터널 자동 발급
    // 흐름:
    //   1) POST /accounts/{account}/cfd_tunnel — 새 터널 생성 (이름 = tenantCode)
    //   2) GET /accounts/{account}/cfd_tunnel/{id}/token — 터널 실행 토큰 발급
    //   3) 응답: 터널 ID + 토큰 + CNAME 대상
    // EXE는 응답 받은 토큰으로 `cloudflared service install <token>` 실행 후 자동 시작
    public async Task<TunnelIssueResult> IssueTunnelAsync(string tenantId, string tenantCode, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Cloudflare 환경변수 미설정 — 사장님 결재 후 환경변수 설정 후 재시도");

        var tunnelName = $"hitpan-{tenantCode.ToLowerInvariant().Replace("-", "")}";
        _logger.LogInformation("[CFTunnel] 터널 발급 시작 tenant={Tid} name={Name}", tenantId, tunnelName);

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        // 봉합 2026-06-16 (사고: code 1030 JSON deserialize 사고):
        //   Cloudflare API 영역 tunnel_secret 영역 string 영역 deprecated. config_src=cloudflare 영역 박혔으면
        //   tunnel_secret 영역 자체가 불필요 (CF가 자동 생성·관리).
        //   → payload 영역 tunnel_secret 영역 제거. CF Dashboard 관리형 터널 영역 정합.
        var createPayload = new
        {
            name = tunnelName,
            config_src = "cloudflare"
        };
        var createRes = await http.PostAsJsonAsync($"accounts/{_accountId}/cfd_tunnel", createPayload, ct);
        var createBody = await createRes.Content.ReadAsStringAsync(ct);
        string tunnelId;
        if (!createRes.IsSuccessStatusCode)
        {
            // 봉합 2026-06-16: 터널 영역 이미 박혀있는 경우 (이름 중복 사고) = 기존 영역 조회·재토큰 발급
            if (createBody.Contains("already exists") || createBody.Contains("duplicate") || createBody.Contains("1006"))
            {
                _logger.LogInformation("[CFTunnel] 터널 영역 박힘 영역 — 기존 영역 조회 가도 name={Name}", tunnelName);
                var listRes = await http.GetAsync($"accounts/{_accountId}/cfd_tunnel?name={tunnelName}&is_deleted=false", ct);
                var listBody = await listRes.Content.ReadAsStringAsync(ct);
                if (!listRes.IsSuccessStatusCode)
                    throw new InvalidOperationException($"터널 기존 영역 조회 실패 ({(int)listRes.StatusCode}): {listBody}");
                using var listJson = JsonDocument.Parse(listBody);
                var arr = listJson.RootElement.GetProperty("result");
                if (arr.GetArrayLength() == 0)
                    throw new InvalidOperationException($"터널 중복 영역 박혔는데 조회 0건: {createBody}");
                tunnelId = arr[0].GetProperty("id").GetString() ?? "";
                _logger.LogInformation("[CFTunnel] 기존 터널 정합 가도 id={Tid}", tunnelId);
            }
            else
            {
                _logger.LogWarning("[CFTunnel] 터널 생성 실패 {Status} {Body}", createRes.StatusCode, createBody);
                throw new InvalidOperationException($"터널 생성 실패 ({(int)createRes.StatusCode}): {createBody}");
            }
        }
        else
        {
            using var createJson = JsonDocument.Parse(createBody);
            tunnelId = createJson.RootElement.GetProperty("result").GetProperty("id").GetString() ?? "";
        }

        // 3) 터널 실행 토큰 발급
        var tokenRes = await http.GetAsync($"accounts/{_accountId}/cfd_tunnel/{tunnelId}/token", ct);
        var tokenBody = await tokenRes.Content.ReadAsStringAsync(ct);
        if (!tokenRes.IsSuccessStatusCode)
        {
            _logger.LogWarning("[CFTunnel] 토큰 발급 실패 {Status} {Body}", tokenRes.StatusCode, tokenBody);
            throw new InvalidOperationException($"터널 토큰 발급 실패 ({(int)tokenRes.StatusCode}): {tokenBody}");
        }

        using var tokenJson = JsonDocument.Parse(tokenBody);
        var tunnelToken = tokenJson.RootElement.GetProperty("result").GetString() ?? "";

        _logger.LogInformation("[CFTunnel] 발급 완료 tenant={Tid} tunnelId={Tn}", tenantId, tunnelId);
        return new TunnelIssueResult(tunnelId, tunnelToken, $"{tunnelId}.cfargotunnel.com");
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

    // 사장님 결재 2026-06-11 — 도메인 별칭으로 영역 검색 + 삭제
    //  중복 발급 사고 봉합 (81053 'CNAME already exists' 영역 차단)
    //  백오피스 가입 영역 저장할 때 옛 DNS 영역 있으면 자동 정리
    //  보존 영역: demo·back·landing·updates·api-* 영역은 저장하지 않음 (운영 영역 보호)
    public async Task<bool> RevokeByDomainAsync(string subdomain, CancellationToken ct)
    {
        if (!IsConfigured) return false;
        if (string.IsNullOrWhiteSpace(subdomain)) return false;

        // 운영 영역 영구 보호
        var reserved = new[] { "demo", "back", "landing", "updates", "www", "api-demo", "api", "api-back" };
        var sub = subdomain.ToLowerInvariant();
        if (Array.Exists(reserved, r => r == sub))
        {
            _logger.LogWarning("[CFDomain] 운영 영역 삭제 시도 차단 sub={Sub}", sub);
            return false;
        }

        var fqdn = $"{sub}.{_baseDomain}";
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        try
        {
            var listRes = await http.GetAsync($"zones/{_zoneId}/dns_records?name={fqdn}", ct);
            if (!listRes.IsSuccessStatusCode) return false;
            var listBody = await listRes.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(listBody);
            var results = doc.RootElement.GetProperty("result");
            bool anyDeleted = false;
            foreach (var r in results.EnumerateArray())
            {
                var id = r.GetProperty("id").GetString();
                if (string.IsNullOrEmpty(id)) continue;
                var delRes = await http.DeleteAsync($"zones/{_zoneId}/dns_records/{id}", ct);
                if (delRes.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[CFDomain] revoke by domain sub={Sub} id={Id}", sub, id);
                    anyDeleted = true;
                }
            }
            return anyDeleted;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CFDomain] revoke by domain 영역 사고 sub={Sub}", sub);
            return false;
        }
    }
}
