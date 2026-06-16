using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 설치마법사 EXE 부트스트랩 API (브라운킴 PM 2026-06-09, 사장님 결재 Plan 정합)
//
// 흐름:
//   1) 고객이 EXE 다운로드 → 더블클릭 실행
//   2) 마법사 화면에서 시리얼 키 입력
//   3) EXE가 POST /api/installer/bootstrap 호출
//   4) 백오피스 검증 → 회사정보 + 도메인 + 터널 토큰 응답
//   5) EXE가 응답대로 cloudflared 등록 + DB 셋업 + 자동 시작
//
// 헌법 정합:
//   #18·#22 — 시리얼 = 백오피스↔ERP 포링키. 평문 IO 1회만 (응답 직후 EXE 파기 의무)
//   #28·#30 — 고객 손 0번. 시리얼 1개만 입력하면 끝
//   #29 — 인프라 토큰은 백오피스가 발급 (EXE에 사전 저장하지 않음)
//   #33 — 모든 응답에 source 명시 ("installer-bootstrap")
//   #35 — 시리얼 = 포링키. 백오피스가 평문 관리, EXE는 평문 받아 사용 후 폐기
[ApiController]
[Route("api/installer")]
[AllowAnonymous]
public class InstallerBootstrapController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ICloudflareDomainService _cfDomain;
    private readonly ILogger<InstallerBootstrapController> _logger;

    public InstallerBootstrapController(
        IConfiguration config,
        ICloudflareDomainService cfDomain,
        ILogger<InstallerBootstrapController> logger)
    {
        _config = config;
        _cfDomain = cfDomain;
        _logger = logger;
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.LicenseKey))
            return BadRequest(new { success = false, message = "시리얼 키를 입력해주세요." });
        if (string.IsNullOrWhiteSpace(req.MachineFingerprint))
            return BadRequest(new { success = false, message = "PC 정보가 누락되었습니다." });

        try
        {
            await using var db = await OpenAsync(ct);

            var pepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
            var normalizedKey = req.LicenseKey.Trim().ToUpperInvariant().Replace(" ", "");
            var licHash = ComputeHmacSha256(normalizedKey, pepper);

            // 시리얼 = 포링키. tenants에서 평문 비교 가능하지만 HMAC 비교가 보안 정합.
            var tenant = await db.QueryFirstOrDefaultAsync<TenantRow>(@"
                SELECT CAST(t.tenant_id AS CHAR) AS TenantId,
                       t.tenant_code AS TenantCode,
                       t.domain_alias AS DomainAlias,
                       t.company_name AS CompanyName,
                       t.biz_no AS BizNo,
                       t.ceo_name AS CeoName,
                       t.tel AS Tel,
                       t.status AS Status,
                       ls.email AS Email,
                       ls.plan_type AS PlanType
                FROM tenants t
                LEFT JOIN landing_signups ls ON ls.company_name = t.company_name
                WHERE t.license_key_hash = @Hash AND t.status = 'active'
                LIMIT 1",
                new { Hash = licHash });

            if (tenant is null)
            {
                _logger.LogWarning("[InstallerBootstrap] invalid serial machine={Fp}", req.MachineFingerprint);
                return Unauthorized(new
                {
                    success = false,
                    message = "올바르지 않은 시리얼이거나 승인되지 않은 계정입니다."
                });
            }

            // 사장님 결재 2026-06-09 — 도메인 별칭(domain_alias) 저장된 영역 우선 저장하기.
            // 저장하지 못한 영역(레거시 가입자)이면 텐넌트 코드 폴백, 단 외부 메일/UI 표시는 절대 저장하지 않음 (헌법 #22 정합).
            var subdomain = !string.IsNullOrWhiteSpace(tenant.DomainAlias)
                ? tenant.DomainAlias!
                : tenant.TenantCode.ToLowerInvariant().Replace("-", "");
            var primaryDomain = $"{subdomain}.hitpan.kr";
            var apiDomain = $"api-{subdomain}.hitpan.kr";

            // Cloudflare 환경변수 설정되어 있으면 DNS + 터널 자동 발급 (사장님 결재 2026-06-09 Day 5)
            string? tunnelToken = null;
            string? tunnelDomain = null;
            string? tunnelId = null;
            if (_cfDomain.IsConfigured)
            {
                try
                {
                    var domainResult = await _cfDomain.IssueAsync(tenant.TenantId, tenant.TenantCode, tenant.DomainAlias, ct);
                    tunnelDomain = domainResult.Domain;

                    // cloudflared 터널 자동 발급 (실패해도 DNS만으로 동작 가능 — 부트스트랩은 성공으로 처리)
                    try
                    {
                        var tunnelResult = await _cfDomain.IssueTunnelAsync(tenant.TenantId, tenant.TenantCode, ct);
                        tunnelToken = tunnelResult.TunnelToken;
                        tunnelId = tunnelResult.TunnelId;

                        // 봉합 2026-06-16 (사고: test000 1033 재발):
                        //   DNS Idempotent 봉합 후 기존 DNS record가 잘못된 터널 가리키는 사고.
                        //   터널 새로 발급 → DNS CNAME content를 새 tunnelId.cfargotunnel.com 로 PATCH.
                        try
                        {
                            await _cfDomain.UpdateDnsTunnelTargetAsync(domainResult.RecordId, domainResult.Domain, tunnelId, ct);
                        }
                        catch (Exception pex)
                        {
                            _logger.LogWarning(pex, "[InstallerBootstrap] DNS PATCH 실패 tenant={Tid} (1033 사고 가능성)", tenant.TenantId);
                        }
                    }
                    catch (Exception tex)
                    {
                        _logger.LogWarning(tex, "[InstallerBootstrap] 터널 발급 실패 (DNS만 발급, 수동 터널 등록 폴백) tenant={Tid}", tenant.TenantId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[InstallerBootstrap] CF 발급 실패 (수동 발급 폴백) tenant={Tid}", tenant.TenantId);
                }
            }

            // 부트스트랩 토큰 — EXE가 향후 자동 업데이트·재인증에 사용 (24시간 유효)
            var bootstrapToken = GenerateBootstrapToken();
            var bootstrapTokenHash = ComputeHmacSha256(bootstrapToken, pepper);

            // 기기 부트스트랩 로그 (감사 추적)
            // 봉합 v1.2.5 (2026-06-11): 컬럼 영역 정정 license_key_hash -> submitted_hash
            await db.ExecuteAsync(@"
                INSERT INTO serial_verify_attempts
                    (tenant_id, submitted_hash, client_fingerprint, client_ip, result, attempted_at)
                VALUES
                    (@TenantId, @LicHash, @Fp, @Ip, 'installer-bootstrap', UTC_TIMESTAMP(6))",
                new
                {
                    TenantId = tenant.TenantId,
                    LicHash = licHash,
                    Fp = ComputeHmacSha256(req.MachineFingerprint, pepper),
                    Ip = HttpContext.Connection.RemoteIpAddress?.ToString()
                });

            _logger.LogInformation("[InstallerBootstrap] success tenant={Tid} code={Code} domain={Dom} cf={Cf}",
                tenant.TenantId, tenant.TenantCode, primaryDomain, _cfDomain.IsConfigured);

            return Ok(new
            {
                success = true,
                source = "installer-bootstrap",
                tenant = new
                {
                    tenantCode = tenant.TenantCode,
                    companyName = tenant.CompanyName,
                    bizNo = tenant.BizNo,
                    ceoName = tenant.CeoName,
                    tel = tenant.Tel,
                    email = tenant.Email,
                    planType = tenant.PlanType
                },
                domain = new
                {
                    primary = tunnelDomain ?? primaryDomain,
                    api = apiDomain,
                    tunnelTokenIssued = !string.IsNullOrEmpty(tunnelToken),
                    tunnelToken = tunnelToken,
                    tunnelId = tunnelId
                },
                bootstrap = new
                {
                    token = bootstrapToken,
                    expiresInSec = 86400,
                    backofficeUrl = "https://back.hitpan.kr"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[InstallerBootstrap] 처리 실패");
            return StatusCode(500, new { success = false, message = "부트스트랩 처리 중 오류가 발생했습니다." });
        }
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? _config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }

    private static string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GenerateBootstrapToken()
    {
        Span<byte> buf = stackalloc byte[32];
        RandomNumberGenerator.Fill(buf);
        return "bst_" + Convert.ToBase64String(buf).Replace("/", "_").Replace("+", "-").TrimEnd('=');
    }

    public class BootstrapRequest
    {
        public string LicenseKey { get; set; } = "";
        public string MachineFingerprint { get; set; } = "";
        public string? Hostname { get; set; }
        public string? OsVersion { get; set; }
        public string? InstallerVersion { get; set; }
    }

    private class TenantRow
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string? DomainAlias { get; set; }
        public string CompanyName { get; set; } = "";
        public string BizNo { get; set; } = "";
        public string CeoName { get; set; } = "";
        public string Tel { get; set; } = "";
        public string Status { get; set; } = "";
        public string? Email { get; set; }
        public string? PlanType { get; set; }
    }
}
