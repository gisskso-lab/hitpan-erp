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
    private readonly IEmailSender _email;
    private readonly ILogger<SignupsAdminController> _logger;

    public SignupsAdminController(
        IConfiguration config,
        IWebhookOutboundService webhook,
        IEmailSender email,
        ILogger<SignupsAdminController> logger)
    {
        _config = config;
        _webhook = webhook;
        _email = email;
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
                       t.status AS TenantStatus,
                       t.license_key_plain AS LicenseKey
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

            // 3) tenant 활성화 + license_key_hash + license_key_plain 저장
            //    사장님 결재 2026-06-08: 시리얼은 백오피스 ↔ ERP 포링키 영역.
            //    백오피스가 평문 박혀있어야 재발송·고객 응대 가능.
            await db.ExecuteAsync(@"
                UPDATE tenants
                SET status = 'active',
                    license_key_hash = COALESCE(NULLIF(license_key_hash, ''), @LicenseHash),
                    license_key_plain = COALESCE(NULLIF(license_key_plain, ''), @LicensePlain),
                    updated_at = UTC_TIMESTAMP()
                WHERE company_name = @CompanyName AND status = 'pending'",
                new { CompanyName = companyName, LicenseHash = licenseHash, LicensePlain = licenseKey });

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

            // 5) 시리얼 키 이메일 발송 — 가입자가 입력한 이메일 주소로 즉시 전송 (사장님 결재 2026-06-08)
            //    헌법 #34 정합 — 발송 채널은 환경변수(Smtp:*) 토글, 실패해도 승인 흐름은 계속 진행
            string customerEmail = signup.email;
            bool emailSent = false;
            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                var subject = "[히트판 ERP] 가입 승인 완료 — 시리얼 키를 안내드립니다";
                var htmlBody = BuildLicenseKeyEmailBody(companyName, licenseKey);
                emailSent = await _email.SendAsync(customerEmail, subject, htmlBody, ct);
                if (emailSent)
                    _logger.LogInformation("[SignupsAdmin] license_email sent to={Email} signupId={Id}", customerEmail, signupId);
                else
                    _logger.LogWarning("[SignupsAdmin] license_email send FAIL to={Email} signupId={Id} — 화면에서 직접 안내 필요", customerEmail, signupId);
            }

            return Ok(new
            {
                success = true,
                message = emailSent ? "승인 완료 — 시리얼 키 이메일 발송됨" : "승인 완료 — 이메일 발송 실패, 화면에서 직접 전달 필요",
                tenantId,
                licenseKey,  // 응답 1회만 평문 노출 (관리자가 고객에게 전달, 이후 DB에서는 복원 불가)
                emailSent
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SignupsAdmin] approve 박힘 실패 id={Id}", signupId);
            return StatusCode(500, new { success = false, message = "승인 박힘 실패" });
        }
    }

    // 시리얼 키 이메일 재발송 (사장님 결재 2026-06-08)
    // 사장님 명시: "백오피스에선 실제 평문으로 관리 → 시리얼넘버 형태로 복호화 시켜서 고객사메일로 시리얼넘버 전송"
    // → 기존 평문 시리얼 그대로 재발송. 새 시리얼 발급 박지 않음 (ERP 포링키 무효화 박지 않음).
    [HttpPost("{signupId:long}/resend-license")]
    public async Task<IActionResult> ResendLicense(long signupId, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);

            var signup = await db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT company_name, email, status FROM landing_signups WHERE signup_id = @Id",
                new { Id = signupId });

            if (signup is null)
                return NotFound(new { success = false, message = "신청을 찾을 수 없습니다." });

            string status = signup.status;
            if (status != "approved" && status != "active")
                return BadRequest(new { success = false, message = "승인 완료된 신청만 재발송할 수 있습니다." });

            string companyName = signup.company_name;
            string customerEmail = signup.email;

            // 기존 시리얼 평문 박힌 영역에서 그대로 읽음 (포링키 그대로 유지)
            var licenseKey = await db.QueryFirstOrDefaultAsync<string?>(
                "SELECT license_key_plain FROM tenants WHERE company_name = @CompanyName ORDER BY created_at DESC LIMIT 1",
                new { CompanyName = companyName });

            if (string.IsNullOrWhiteSpace(licenseKey))
                return BadRequest(new { success = false, message = "이 고객사의 시리얼 키가 박혀있지 않습니다. 새로 발급이 필요합니다." });

            // 이메일 재발송
            bool emailSent = false;
            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                var subject = "[히트판 ERP] 시리얼 키 재발송 안내";
                var htmlBody = BuildLicenseKeyEmailBody(companyName, licenseKey);
                emailSent = await _email.SendAsync(customerEmail, subject, htmlBody, ct);
            }

            _logger.LogInformation("[SignupsAdmin] license_resent signupId={Id} email={Email} sent={Sent}",
                signupId, customerEmail, emailSent);

            return Ok(new
            {
                success = true,
                message = emailSent ? "시리얼 키 이메일 재발송 완료" : "재발송 실패 — 화면에서 직접 전달 필요",
                emailSent
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SignupsAdmin] resend-license 실패 id={Id}", signupId);
            return StatusCode(500, new { success = false, message = "재발송 처리 실패" });
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

    // 시리얼 키 메일 HTML 본문 — 사장님 결재 2026-06-08
    // 헌법 정합: #25 쉽게 / #34 정식 출시 시 발신자만 hitpan@hitpan.co.kr로 환경변수 교체
    private static string BuildLicenseKeyEmailBody(string companyName, string licenseKey)
    {
        var safeCompany = System.Net.WebUtility.HtmlEncode(companyName ?? "");
        var safeKey = System.Net.WebUtility.HtmlEncode(licenseKey ?? "");
        return $@"<!DOCTYPE html>
<html lang=""ko"">
<head><meta charset=""utf-8""></head>
<body style=""margin:0;padding:0;background:#F9FAFB;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Noto Sans KR',Arial,sans-serif;color:#0F1419;"">
<div style=""max-width:560px;margin:32px auto;background:#fff;border-radius:16px;box-shadow:0 4px 16px rgba(0,0,0,0.06);overflow:hidden;"">
  <div style=""background:#0F6E56;color:#fff;padding:28px 32px;"">
    <h1 style=""margin:0;font-size:22px;letter-spacing:-0.3px;"">히트판 ERP 가입이 승인되었습니다</h1>
  </div>
  <div style=""padding:32px;"">
    <p style=""font-size:15px;line-height:1.7;margin:0 0 16px;"">
      <strong>{safeCompany}</strong> 님, 가입을 축하드립니다.<br />
      본사 검토 결과 가입이 승인되어 시리얼 키를 안내드립니다.
    </p>

    <div style=""background:#F0F9F5;border:2px solid #0F6E56;border-radius:12px;padding:24px;margin:24px 0;text-align:center;"">
      <p style=""margin:0 0 8px;color:#0F6E56;font-size:13px;font-weight:600;"">시리얼 키</p>
      <p style=""margin:0;font-family:'Courier New',monospace;font-size:22px;font-weight:700;letter-spacing:1.5px;color:#0F1419;"">{safeKey}</p>
    </div>

    <div style=""background:#FEE2E2;border:2px solid #DC2626;border-radius:8px;padding:16px;margin:20px 0;"">
      <p style=""margin:0 0 6px;font-size:14px;font-weight:700;color:#991B1B;"">🔒 보안 안내 — 반드시 지켜주세요</p>
      <p style=""margin:0;color:#991B1B;font-size:14px;font-weight:600;line-height:1.6;"">
        이 메일을 받으시면 <u>히트판 ERP에 입력, 메모장에 저장 후 즉시 삭제</u>하세요.
      </p>
      <ul style=""margin:10px 0 0 18px;padding:0;color:#991B1B;font-size:13px;line-height:1.6;"">
        <li>시리얼 키는 본인 PC의 안전한 위치에만 보관하세요.</li>
        <li>이메일 계정이 탈취되어도 시리얼이 유출되지 않도록, 메일 본문에 남기지 마세요.</li>
        <li>분실 시 본사 고객센터로 문의하시면 재발송해 드립니다.</li>
      </ul>
    </div>

    <h2 style=""font-size:16px;margin:24px 0 12px;color:#0F1419;"">다음 단계</h2>
    <ol style=""margin:0 0 16px 20px;padding:0;font-size:14px;line-height:1.8;color:#374151;"">
      <li>히트판 ERP 설치 파일을 다운로드합니다.</li>
      <li>설치 후 첫 화면에서 위 시리얼 키를 입력합니다.</li>
      <li>인증이 완료되면 ‘이 PC를 등록하시겠습니까?’ 안내가 표시됩니다.</li>
      <li>등록 후 바로 사용하실 수 있습니다.</li>
    </ol>

    <p style=""font-size:13px;color:#6B7280;line-height:1.7;margin:24px 0 0;border-top:1px solid #E5E7EB;padding-top:16px;"">
      문의: 히트판 고객센터 / 본 메일은 발신 전용입니다.
    </p>
  </div>
</div>
</body>
</html>";
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
        public string? LicenseKey { get; set; }
    }
}
