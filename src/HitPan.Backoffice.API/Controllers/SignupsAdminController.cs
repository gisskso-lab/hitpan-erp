using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 가입 신청 박힘 관리 API (브라운킴 PM 박제 2026-06-08, 사장님 결재 박힘)
//
// 흐름 박힘:
//   1) GET  /api/admin/signups        — 신청 목록 (status 필터 박힘)
//   2) POST /api/admin/signups/{id}/approve — 승인 박힘 (status=paid → tenant=active → webhook 박힘)
//   3) POST /api/admin/signups/{id}/reject  — 반려 박힘 (status=rejected)
//
// 헌법 정합:
//   #18·#22 — 메타·해시만 박힘, 평문 0건
//   #20 — 가입 → 승인 → ERP 박힘 끊김 0
//   #35 — 랜딩 가입 → 백오피스 승인 → ERP 박힘 유기적 연결
[ApiController]
[Route("api/admin/signups")]
[AllowAnonymous]
public class SignupsAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IWebhookOutboundService _webhook;
    private readonly ILogger<SignupsAdminController> _logger;

    public SignupsAdminController(
        IConfiguration config,
        IWebhookOutboundService webhook,
        ILogger<SignupsAdminController> logger)
    {
        _config = config;
        _webhook = webhook;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var rows = await db.QueryAsync<SignupRow>(@"
                SELECT s.signup_id AS SignupId,
                       s.signup_token AS SignupToken,
                       s.company_name AS CompanyName,
                       s.email AS Email,
                       s.phone AS Phone,
                       s.plan_type AS PlanType,
                       s.reseller_code AS ResellerCode,
                       s.status AS Status,
                       s.submitted_at AS SubmittedAt,
                       t.tenant_code AS TenantCode,
                       t.status AS TenantStatus
                FROM landing_signups s
                LEFT JOIN tenants t ON t.company_name = s.company_name
                WHERE (@Status IS NULL OR @Status = '' OR s.status = @Status)
                ORDER BY s.submitted_at DESC
                LIMIT 200",
                new { Status = status });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SignupsAdmin] list 박힘 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 박힘 실패" });
        }
    }

    [HttpPost("{signupId:long}/approve")]
    public async Task<IActionResult> Approve(long signupId, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);

            var signup = await db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT company_name, email, status FROM landing_signups WHERE signup_id = @Id",
                new { Id = signupId });

            if (signup is null)
                return NotFound(new { success = false, message = "신청 박힘 박지 않음" });

            string status = signup.status;
            if (status == "approved" || status == "active")
                return BadRequest(new { success = false, message = "이미 승인 박힌 영역" });

            string companyName = signup.company_name;

            // 1) signup 박힘 승인 박힘
            await db.ExecuteAsync(
                "UPDATE landing_signups SET status = 'approved' WHERE signup_id = @Id",
                new { Id = signupId });

            // 2) 시리얼(라이선스 키) 발급 — HITP-XXXX-XXXX-XXXX-XXXX, Crockford Base32 (어벤져스 A안)
            //    헌법 #18·#22 정합 — DB에는 HMAC 해시만 저장, 평문은 응답 1회만 노출
            var licenseKey = GenerateLicenseKey();
            var licensePepper = _config["License:Pepper"] ?? "dev-pepper-2026";
            var licenseHash = ComputeHmacSha256(licenseKey, licensePepper);

            // 3) tenant 활성화 + license_key_hash 저장
            await db.ExecuteAsync(@"
                UPDATE tenants
                SET status = 'active',
                    license_key_hash = COALESCE(NULLIF(license_key_hash, ''), @LicenseHash),
                    updated_at = UTC_TIMESTAMP()
                WHERE company_name = @CompanyName AND status = 'pending'",
                new { CompanyName = companyName, LicenseHash = licenseHash });

            var tenantId = await db.QueryFirstOrDefaultAsync<string?>(
                "SELECT CAST(tenant_id AS CHAR) FROM tenants WHERE company_name = @CompanyName ORDER BY created_at DESC LIMIT 1",
                new { CompanyName = companyName });

            // 4) ERP webhook 발송 (헌법 #35 정합 - 백오피스→ERP 유기적 연결)
            if (!string.IsNullOrEmpty(tenantId))
            {
                await _webhook.EmitSubscriptionChangedAsync(tenantId, ct);
                _logger.LogInformation("[SignupsAdmin] approved signupId={Id} tenant={Tid} license_issued=1 webhook=ok",
                    signupId, tenantId);
            }
            else
            {
                _logger.LogWarning("[SignupsAdmin] approved 후 tenant 미발견 signupId={Id}", signupId);
            }

            return Ok(new
            {
                success = true,
                message = "승인 완료",
                tenantId,
                licenseKey  // 응답 1회만 평문 노출 (관리자가 고객에게 전달, 이후 DB에서는 복원 불가)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SignupsAdmin] approve 박힘 실패 id={Id}", signupId);
            return StatusCode(500, new { success = false, message = "승인 박힘 실패" });
        }
    }

    [HttpPost("{signupId:long}/reject")]
    public async Task<IActionResult> Reject(long signupId, [FromBody] RejectRequest? req, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE landing_signups
                SET status = 'rejected'
                WHERE signup_id = @Id AND status IN ('submitted', 'paid')",
                new { Id = signupId });
            if (affected == 0)
                return BadRequest(new { success = false, message = "반려 박힐 박을 영역 박지 않음" });

            _logger.LogInformation("[SignupsAdmin] rejected id={Id} reason={Reason}", signupId, req?.Reason);
            return Ok(new { success = true, message = "반려 박힘" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SignupsAdmin] reject 박힘 실패 id={Id}", signupId);
            return StatusCode(500, new { success = false, message = "반려 박힘 실패" });
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

    // 시리얼(라이선스 키) 발급 — HITP-XXXX-XXXX-XXXX-XXXX, Crockford Base32 변형
    // 어벤져스 4명 만장일치 A안 (2026-06-08): 사업자번호 영역 분리 + 80비트 랜덤 + I·L·O·U 제외
    private static string GenerateLicenseKey()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
        Span<byte> buf = stackalloc byte[16];
        RandomNumberGenerator.Fill(buf);
        var sb = new StringBuilder("HITP", 24);
        for (int i = 0; i < 16; i++)
        {
            if (i % 4 == 0) sb.Append('-');
            sb.Append(alphabet[buf[i] % alphabet.Length]);
        }
        return sb.ToString();
    }

    public record RejectRequest(string? Reason);

    public class SignupRow
    {
        public long SignupId { get; set; }
        public string SignupToken { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string PlanType { get; set; } = "";
        public string? ResellerCode { get; set; }
        public string Status { get; set; } = "";
        public DateTime SubmittedAt { get; set; }
        public string? TenantCode { get; set; }
        public string? TenantStatus { get; set; }
    }
}
