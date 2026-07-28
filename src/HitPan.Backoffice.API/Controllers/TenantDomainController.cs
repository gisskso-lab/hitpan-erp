using Dapper;
using HitPan.Backoffice.API.Filters;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// Cloudflare 도메인 자동 발급 컨트롤러 (W14 골격, 사장님 결재 2026-06-05)
//
// 흐름:
//   1) GET   /  — 발급 이력 조회
//   2) POST /tenants/{id}/issue — DNS CNAME 생성 + tenant_domains INSERT
//   3) POST /tenants/{id}/revoke — DNS 회수 + status='revoked'
//
// 헌법 정합:
//   #15·#18·#22·#25·#29 — 토큰은 환경변수만, 미설정 시 503
//   #34 정식 완성도 — www.{tenant_code}.hitpan.kr 자동 발급
[ApiController]
[Route("api/backoffice/tenant-domains")]
[Authorize]
public class TenantDomainController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<TenantDomainController> _logger;
    private readonly ICloudflareDomainService _cf;
    private readonly IBoAuditService _audit;

    public TenantDomainController(IConfiguration config, ILogger<TenantDomainController> logger,
        ICloudflareDomainService cf, IBoAuditService audit)
    {
        _config = config;
        _logger = logger;
        _cf = cf;
        _audit = audit;
    }

    [HttpGet]
    [BoPermission("tenant_domain.list")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var rows = await db.QueryAsync<DomainRow>(@"
                SELECT
                    domain_id AS DomainId,
                    CAST(tenant_id AS CHAR) AS TenantId,
                    domain AS Domain,
                    cf_zone_id AS CfZoneId,
                    cf_record_id AS CfRecordId,
                    cf_tunnel_id AS CfTunnelId,
                    status AS Status,
                    issued_at AS IssuedAt,
                    last_error AS LastError,
                    created_at AS CreatedAt
                FROM tenant_domains
                ORDER BY domain_id DESC
                LIMIT 200");
            return Ok(new { success = true, configured = _cf.IsConfigured, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TenantDomain] 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("tenants/{tenantId}/issue")]
    [BoPermission("tenant_domain.issue")]
    public async Task<IActionResult> Issue(string tenantId, CancellationToken ct)
    {
        if (!_cf.IsConfigured)
            return StatusCode(503, new
            {
                success = false,
                message = "Cloudflare 환경변수 미설정 — 사장님 결재 후 CLOUDFLARE_API_TOKEN·ZONE_ID·ACCOUNT_ID 설정해야 발급 가능합니다 (헌법 #29)."
            });

        var (actorId, actorEmail, _) = GetActor();

        try
        {
            await using var db = await OpenAsync(ct);

            var tenant = await db.QueryFirstOrDefaultAsync<TenantMeta>(
                "SELECT CAST(tenant_id AS CHAR) AS TenantId, tenant_code AS TenantCode FROM tenants WHERE tenant_id = @Id",
                new { Id = tenantId });
            if (tenant is null)
                return NotFound(new { success = false, message = "고객사를 찾을 수 없습니다." });

            var dup = await db.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM tenant_domains WHERE tenant_id = @Id AND status IN ('issued', 'pending')",
                new { Id = tenantId });
            if (dup > 0)
                return BadRequest(new { success = false, message = "이미 발급된(또는 대기 중) 도메인이 있습니다." });

            DomainIssueResult result;
            try
            {
                result = await _cf.IssueAsync(tenantId, tenant.TenantCode, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TenantDomain] Cloudflare 발급 실패 tenant={Tid}", tenantId);
                await db.ExecuteAsync(@"
                    INSERT INTO tenant_domains (tenant_id, domain, status, last_error)
                    VALUES (@Tid, @Domain, 'failed', @Err)",
                    new
                    {
                        Tid = tenantId,
                        Domain = $"www.{tenant.TenantCode.ToLowerInvariant()}.hitpan.kr",
                        Err = ex.Message.Length > 500 ? ex.Message.Substring(0, 500) : ex.Message
                    });
                return StatusCode(500, new { success = false, message = $"Cloudflare 발급 실패: {ex.Message}" });
            }

            await db.ExecuteAsync(@"
                INSERT INTO tenant_domains
                    (tenant_id, domain, cf_zone_id, cf_record_id, cf_tunnel_id, status, issued_at)
                VALUES
                    (@Tid, @Domain, @ZoneId, @RecordId, @TunnelId, 'issued', NOW(6))",
                new
                {
                    Tid = tenantId,
                    result.Domain,
                    ZoneId = result.ZoneId,
                    RecordId = result.RecordId,
                    TunnelId = result.TunnelId
                });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                "tenant_domain.issue", "tenant", tenantId,
                new { result.Domain }, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = "도메인이 발급되었습니다.", domain = result.Domain });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TenantDomain] 발급 실패 tenant={Tid}", tenantId);
            return StatusCode(500, new { success = false, message = "발급 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("{domainId:long}/revoke")]
    [BoPermission("tenant_domain.revoke")]
    public async Task<IActionResult> Revoke(long domainId, CancellationToken ct)
    {
        if (!_cf.IsConfigured)
            return StatusCode(503, new { success = false, message = "Cloudflare 환경변수 미설정 (헌법 #29)" });

        var (actorId, actorEmail, _) = GetActor();

        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.QueryFirstOrDefaultAsync<DomainRow>(@"
                SELECT
                    domain_id AS DomainId,
                    CAST(tenant_id AS CHAR) AS TenantId,
                    domain AS Domain,
                    cf_zone_id AS CfZoneId,
                    cf_record_id AS CfRecordId,
                    cf_tunnel_id AS CfTunnelId,
                    status AS Status
                FROM tenant_domains WHERE domain_id = @Id",
                new { Id = domainId });
            if (row is null) return NotFound(new { success = false, message = "도메인 발급 이력을 찾을 수 없습니다." });
            if (row.Status != "issued")
                return BadRequest(new { success = false, message = $"issued 상태만 회수 가능 (현재: {row.Status})" });

            var ok = await _cf.RevokeAsync(row.CfZoneId ?? "", row.CfRecordId ?? "", row.CfTunnelId, ct);
            if (!ok)
                return StatusCode(500, new { success = false, message = "Cloudflare 회수 실패" });

            await db.ExecuteAsync(
                "UPDATE tenant_domains SET status = 'revoked', updated_at = NOW(6) WHERE domain_id = @Id",
                new { Id = domainId });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                "tenant_domain.revoke", "tenant_domain", domainId.ToString(),
                new { row.Domain }, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = "도메인이 회수되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TenantDomain] 회수 실패 id={Id}", domainId);
            return StatusCode(500, new { success = false, message = "회수 중 오류가 발생했습니다." });
        }
    }

    private (string id, string email, string role) GetActor()
    {
        var id = User.FindFirst("sub")?.Value ?? "";
        var email = User.FindFirst("email")?.Value ?? "";
        var role = User.FindFirst("role")?.Value ?? "";
        return (id, email, role);
    }

    private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? GetUa()
    {
        var ua = Request.Headers["User-Agent"].ToString();
        return string.IsNullOrEmpty(ua) ? null : (ua.Length > 255 ? ua.Substring(0, 255) : ua);
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

    private class TenantMeta
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
    }

    private class DomainRow
    {
        public long DomainId { get; set; }
        public string TenantId { get; set; } = "";
        public string Domain { get; set; } = "";
        public string? CfZoneId { get; set; }
        public string? CfRecordId { get; set; }
        public string? CfTunnelId { get; set; }
        public string Status { get; set; } = "";
        public DateTime? IssuedAt { get; set; }
        public string? LastError { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
