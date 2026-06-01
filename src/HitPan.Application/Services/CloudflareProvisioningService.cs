using HitPan.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 프로비저닝 스켈레톤 (사장님 결재 2026-06-01)
///
/// 본사 서버 환경변수에서 DPAPI 암호화된 Cloudflare API 토큰을 복호화하여 사용.
/// 헌법 #18: 본사에서만 호출. ERP 고객 PC 절대 금지.
/// 헌법 #29: 토큰 변경 시 사장님 사전 결재.
///
/// 현재 단계: 스켈레톤. 실제 HTTP 호출은 후속 작지 박제.
/// </summary>
public class CloudflareProvisioningService : ICloudflareProvisioningService
{
    private readonly IConfiguration _config;
    private readonly ILogger<CloudflareProvisioningService> _logger;

    public CloudflareProvisioningService(IConfiguration config, ILogger<CloudflareProvisioningService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<ProvisioningResult> ProvisionAsync(string tenantId, string subdomain, CancellationToken ct = default)
    {
        // 환경변수 또는 user-secrets에서 토큰 읽기
        var tokenEnc = _config["CLOUDFLARE_API_TOKEN_ENC"]
            ?? Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN_ENC");
        var accountId = _config["CLOUDFLARE_ACCOUNT_ID"];
        var zoneId = _config["CLOUDFLARE_ZONE_ID"];
        var domainBase = _config["CLOUDFLARE_TUNNEL_DOMAIN_BASE"] ?? "hitpan.kr";

        if (string.IsNullOrEmpty(tokenEnc))
        {
            _logger.LogError("Cloudflare API token not configured. Provisioning skipped for {TenantId}", tenantId);
            return Task.FromResult(new ProvisioningResult(tenantId, false, null, null,
                "본사 환경변수 미설정 (CLOUDFLARE_API_TOKEN_ENC). 사장님 결재 후 박제 필요"));
        }

        // TODO 후속 작지:
        //  1. DPAPI 복호화 (Windows Data Protection API)
        //  2. Cloudflare DNS API 호출 — CNAME 생성
        //  3. Cloudflare Tunnel API 호출 — 터널 + credentials
        //  4. provisioning 상태 DB UPDATE
        //  5. credentials 암호화 저장
        //  6. 이메일 발송 트리거

        var fullDomain = $"{subdomain}.{domainBase}";
        _logger.LogInformation("Provisioning skeleton called for {TenantId} → {Domain}", tenantId, fullDomain);

        return Task.FromResult(new ProvisioningResult(
            tenantId, false, fullDomain, null,
            "스켈레톤 단계 — 실제 Cloudflare API 호출 후속 작지에서 박제"));
    }

    public Task<ProvisioningStatus> GetStatusAsync(string tenantId, CancellationToken ct = default)
    {
        // TODO: licenses 테이블 또는 별도 provisioning_jobs 테이블 조회
        return Task.FromResult(ProvisioningStatus.Pending);
    }

    public Task<ProvisioningResult> RetryAsync(string tenantId, CancellationToken ct = default)
    {
        _logger.LogInformation("Provisioning retry requested for {TenantId}", tenantId);
        return ProvisionAsync(tenantId, "", ct);
    }
}
