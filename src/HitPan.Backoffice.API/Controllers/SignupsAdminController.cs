using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 가입 신청 저장 관리 API (브라운킴 PM 저장 2026-06-08, 사장님 결재 저장)
//
// 흐름 저장:
//   1) GET  /api/admin/signups        — 신청 목록 (status 필터 저장)
//   2) POST /api/admin/signups/{id}/approve — 승인 저장 (status=paid → tenant=active → webhook 저장)
//   3) POST /api/admin/signups/{id}/reject  — 반려 저장 (status=rejected)
//
// 헌법 정합:
//   #18·#22 — 메타·해시만 저장, 평문 0건
//   #20 — 가입 → 승인 → ERP 저장 끊김 0
//   #35 — 랜딩 가입 → 백오피스 승인 → ERP 저장 유기적 연결
[ApiController]
[Route("api/admin/signups")]
[Authorize]
public class SignupsAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IWebhookOutboundService _webhook;
    private readonly IEmailSender _email;
    private readonly ICloudflareDomainService _cfDomain;
    private readonly ILogger<SignupsAdminController> _logger;

    public SignupsAdminController(
        IConfiguration config,
        IWebhookOutboundService webhook,
        IEmailSender email,
        ICloudflareDomainService cfDomain,
        ILogger<SignupsAdminController> logger)
    {
        _config = config;
        _webhook = webhook;
        _email = email;
        _cfDomain = cfDomain;
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
                       s.desired_domain AS DesiredDomain,
                       s.reseller_code AS ResellerCode,
                       s.status AS Status,
                       s.submitted_at AS SubmittedAt,
                       t.tenant_code AS TenantCode,
                       t.domain_alias AS DomainAlias,
                       t.status AS TenantStatus
                -- license_key_plain 폐기 (사장님 결재 2026-06-18, 보안 P0): 평문 시리얼키 영구저장 금지.
                --   목록에서 시리얼 평문 노출 제거. 분실 시 재발송 API가 신규 시리얼을 재발급(해시만 저장).
                FROM landing_signups s
                LEFT JOIN tenants t ON t.tenant_id = (
                    SELECT t2.tenant_id FROM tenants t2
                    WHERE t2.company_name = s.company_name
                      AND t2.created_at >= s.submitted_at
                    ORDER BY t2.created_at ASC
                    LIMIT 1
                )
                WHERE (@Status IS NULL OR @Status = '' OR s.status = @Status)
                ORDER BY s.submitted_at DESC
                LIMIT 200",
                new { Status = status });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SignupsAdmin] list 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 실패" });
        }
    }

    [HttpPost("{signupId:long}/approve")]
    public async Task<IActionResult> Approve(long signupId, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);

            var signup = await db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT company_name, email, status, submitted_at FROM landing_signups WHERE signup_id = @Id",
                new { Id = signupId });

            if (signup is null)
                return NotFound(new { success = false, message = "신청을 찾을 수 없습니다." });

            string status = signup.status;
            if (status == "approved" || status == "active")
                return BadRequest(new { success = false, message = "이미 승인 처리되었습니다." });

            string companyName = signup.company_name;
            DateTime submittedAt = signup.submitted_at;

            // 사고 #5 봉합 (2026-06-10): signup ↔ tenant 1:1 매칭 — submitted_at에 가장 가까운 pending tenant 1건만
            // 이전 버그: WHERE company_name AND status='pending' → 같은 회사명 여러 pending이면 모두 같은 키 저장
            var tenantId = await db.QueryFirstOrDefaultAsync<string?>(@"
                SELECT CAST(tenant_id AS CHAR)
                FROM tenants
                WHERE company_name = @CompanyName AND status = 'pending'
                ORDER BY ABS(TIMESTAMPDIFF(SECOND, created_at, @SubmittedAt))
                LIMIT 1",
                new { CompanyName = companyName, SubmittedAt = submittedAt });

            if (string.IsNullOrEmpty(tenantId))
                return NotFound(new { success = false, message = "신청에 대응하는 고객사를 찾을 수 없습니다." });

            // 1) signup 승인 처리
            await db.ExecuteAsync(
                "UPDATE landing_signups SET status = 'approved' WHERE signup_id = @Id",
                new { Id = signupId });

            // 2) 시리얼(라이선스 키) 발급 — HITP-XXXX-XXXX-XXXX-XXXX, Crockford Base32 (어벤져스 A안)
            //    헌법 #18·#22 정합 — DB에는 HMAC 해시만 저장, 평문은 응답 1회만 노출
            var licenseKey = GenerateLicenseKey();
            var licensePepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
            var licenseHash = ComputeHmacSha256(licenseKey, licensePepper);

            // 3) tenant 활성화 + license_key_hash 저장 — tenant_id 단건 정확 매칭
            //    license_key_plain 폐기 (사장님 결재 2026-06-18, 보안 P0): 평문 시리얼키 영구저장 금지.
            //      해시만 저장. 평문은 아래 응답으로 1회만 노출(관리자 전달용), DB 복원 불가.
            //      분실 시 ResendLicense가 신규 시리얼 재발급(기존 무효화). 6-08 평문 재발송 결재는 본 결재로 갱신.
            //    사고 #5 봉합 2026-06-10: WHERE tenant_id 단건 매칭으로 변경
            await db.ExecuteAsync(@"
                UPDATE tenants
                SET status = 'active',
                    license_key_hash = COALESCE(NULLIF(license_key_hash, ''), @LicenseHash),
                    updated_at = UTC_TIMESTAMP()
                WHERE tenant_id = @TenantId AND status = 'pending'",
                new { TenantId = tenantId, LicenseHash = licenseHash });

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

            // 4-2) Cloudflare DNS 자동 발급 (사고 #4 봉합 2026-06-11 - 헌법 #35 정합)
            //      도메인 별칭으로 {alias}.hitpan.kr CNAME 저장. 환경변수 미설정 시 silent skip
            //      터널 영역은 EXE 부트스트랩 시점(InstallerBootstrapController)에서 저장 (헌법 #30 정합)
            if (!string.IsNullOrEmpty(tenantId) && _cfDomain.IsConfigured)
            {
                try
                {
                    var tenantCode = await db.QueryFirstOrDefaultAsync<string>(
                        "SELECT tenant_code FROM tenants WHERE tenant_id = @Tid LIMIT 1",
                        new { Tid = tenantId }) ?? "";
                    var aliasForDns = await db.QueryFirstOrDefaultAsync<string?>(
                        "SELECT domain_alias FROM tenants WHERE tenant_id = @Tid LIMIT 1",
                        new { Tid = tenantId });
                    var dnsResult = await _cfDomain.IssueAsync(tenantId, tenantCode, aliasForDns, ct);
                    _logger.LogInformation("[SignupsAdmin] cf_dns_issued tenant={Tid} domain={Dom} record={Rid}",
                        tenantId, dnsResult.Domain, dnsResult.RecordId);
                }
                catch (Exception cex)
                {
                    _logger.LogWarning(cex, "[SignupsAdmin] CF DNS 발급 실패 tenant={Tid} (수동 발급 폴백)", tenantId);
                }
            }

            // 5) 시리얼 키 이메일 발송 — 가입자가 입력한 이메일 주소로 즉시 전송 (사장님 결재 2026-06-08)
            //    헌법 #34 정합 — 발송 채널은 환경변수(Smtp:*) 토글, 실패해도 승인 흐름은 계속 진행
            //    사장님 결재 2026-06-09 — 도메인 별칭도 메일에 저장하기
            string customerEmail = signup.email;
            // 봉합 2026-06-17 (v1.2.13 P0-D): 방금 승인한 tenant_id 단건으로 조회 (동명 회사 사고 차단)
            string? domainAlias = await db.QueryFirstOrDefaultAsync<string?>(
                "SELECT domain_alias FROM tenants WHERE tenant_id = @Tid LIMIT 1",
                new { Tid = tenantId });
            bool emailSent = false;
            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                var subject = "[히트판 ERP] 가입 승인 완료 — 시리얼 키와 ERP 주소를 안내드립니다";
                var htmlBody = BuildLicenseKeyEmailBody(companyName, licenseKey, domainAlias);
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
            _logger.LogError(ex, "[SignupsAdmin] approve 처리 실패 id={Id}", signupId);
            return StatusCode(500, new { success = false, message = "승인 처리 실패" });
        }
    }

    // 시리얼 키 재발급 (사장님 결재 2026-06-18, 보안 P0 — 6-08 평문 재발송 결재를 갱신)
    //   license_key_plain 폐기로 평문 복원 불가 → "기존 평문 재발송"을 "신규 시리얼 재발급"으로 전환.
    //   분실 시: 새 시리얼 발급 → license_key_hash 갱신(기존 시리얼 자동 무효화) → 새 평문 1회 전달.
    //   고객이 받았던 옛 시리얼은 무효화됨(사장님 결재 — 평문 보관 안 하는 대가). 헌법 #22 정합.
    [HttpPost("{signupId:long}/resend-license")]
    public async Task<IActionResult> ResendLicense(long signupId, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);

            var signup = await db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT company_name, email, status, submitted_at FROM landing_signups WHERE signup_id = @Id",
                new { Id = signupId });

            if (signup is null)
                return NotFound(new { success = false, message = "신청을 찾을 수 없습니다." });

            string status = signup.status;
            if (status != "approved" && status != "active")
                return BadRequest(new { success = false, message = "승인 완료된 신청만 재발급할 수 있습니다." });

            string companyName = signup.company_name;
            string customerEmail = signup.email;
            DateTime submittedAt = signup.submitted_at;

            // 봉합 2026-06-17 (v1.2.13 P0-D): 동명 회사 사고 차단 — submitted_at에 가장 가까운 tenant 1건만
            //   이전 사고: ORDER BY created_at DESC = 같은 회사명 신규 가입자에게 발송 사고
            var info = await db.QueryFirstOrDefaultAsync<TenantInfoRow>(@"
                SELECT CAST(tenant_id AS CHAR) AS TenantId, domain_alias AS DomainAlias
                FROM tenants
                WHERE company_name = @CompanyName
                ORDER BY ABS(TIMESTAMPDIFF(SECOND, created_at, @SubmittedAt))
                LIMIT 1",
                new { CompanyName = companyName, SubmittedAt = submittedAt });

            if (info is null || string.IsNullOrWhiteSpace(info.TenantId))
                return BadRequest(new { success = false, message = "이 고객사의 고객사 정보를 찾을 수 없습니다." });

            var domainAlias = info.DomainAlias;

            // 신규 시리얼 재발급 — 새 키 생성 → 해시 갱신(기존 시리얼 무효화). 평문은 저장하지 않음.
            var licenseKey = GenerateLicenseKey();
            var licensePepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
            var licenseHash = ComputeHmacSha256(licenseKey, licensePepper);
            await db.ExecuteAsync(@"
                UPDATE tenants
                SET license_key_hash = @LicenseHash,
                    updated_at = UTC_TIMESTAMP()
                WHERE tenant_id = @TenantId",
                new { TenantId = info.TenantId, LicenseHash = licenseHash });

            // 새 시리얼 이메일 발송
            bool emailSent = false;
            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                var subject = "[히트판 ERP] 시리얼 키 재발급 안내 (이전 시리얼은 무효화됩니다)";
                var htmlBody = BuildLicenseKeyEmailBody(companyName, licenseKey, domainAlias);
                emailSent = await _email.SendAsync(customerEmail, subject, htmlBody, ct);
            }

            _logger.LogInformation("[SignupsAdmin] license_reissued signupId={Id} tenant={Tid} email={Email} sent={Sent}",
                signupId, info.TenantId, customerEmail, emailSent);

            return Ok(new
            {
                success = true,
                message = emailSent ? "시리얼 키 재발급·이메일 발송 완료 (이전 시리얼 무효화)" : "재발급 완료 — 이메일 발송 실패, 화면에서 직접 전달 필요",
                licenseKey,  // 응답 1회만 평문 노출 (관리자 전달용, 이후 DB 복원 불가)
                emailSent
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SignupsAdmin] resend-license 실패 id={Id}", signupId);
            return StatusCode(500, new { success = false, message = "재발급 처리 실패" });
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
                return BadRequest(new { success = false, message = "반려 처리할 대상이 없습니다" });

            _logger.LogInformation("[SignupsAdmin] rejected id={Id} reason={Reason}", signupId, req?.Reason);
            return Ok(new { success = true, message = "반려 완료" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SignupsAdmin] reject 처리 실패 id={Id}", signupId);
            return StatusCode(500, new { success = false, message = "반려 처리 실패" });
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

    // 시리얼 키 메일 HTML 본문 — 사장님 결재 2026-06-08, 2026-06-09 도메인 저장
    // 헌법 정합: #22 (테넌트 코드 노출 금지, 도메인 별칭만 저장) / #25 쉽게 / #34 정식 출시 시 발신자 환경변수 교체
    private static string BuildLicenseKeyEmailBody(string companyName, string licenseKey, string? domainAlias)
    {
        var safeCompany = System.Net.WebUtility.HtmlEncode(companyName ?? "");
        var safeKey = System.Net.WebUtility.HtmlEncode(licenseKey ?? "");
        var safeDomain = System.Net.WebUtility.HtmlEncode(domainAlias ?? "");
        var domainBlock = string.IsNullOrWhiteSpace(domainAlias)
            ? ""
            : $@"
    <div style=""background:#EFF6FF;border:2px solid #2563EB;border-radius:12px;padding:24px;margin:24px 0;text-align:center;"">
      <p style=""margin:0 0 8px;color:#1E3A8A;font-size:13px;font-weight:600;"">고객사 ERP 주소</p>
      <p style=""margin:0;font-family:'Courier New',monospace;font-size:20px;font-weight:700;color:#1E3A8A;"">{safeDomain}.hitpan.kr</p>
      <p style=""margin:8px 0 0;color:#1E3A8A;font-size:12px;"">설치 후 위 주소로 접속하시면 됩니다.</p>
    </div>";
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
{domainBlock}

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

    <div style=""background:#FEF3C7;border:2px solid #F59E0B;border-radius:12px;padding:24px;margin:24px 0;text-align:center;"">
      <p style=""margin:0 0 12px;color:#92400E;font-size:14px;font-weight:700;"">📥 설치 파일 다운로드</p>
      <a href=""https://updates.hitpan.kr/packages/HitPan-ERP-Setup-1.2.26.exe""
         style=""display:inline-block;background:#0F6E56;color:#fff;padding:14px 32px;border-radius:8px;font-size:15px;font-weight:700;text-decoration:none;"">
        히트판 ERP 설치 프로그램 (280 MB)
      </a>
      <p style=""margin:12px 0 0;color:#92400E;font-size:12px;"">버전 1.2.26 · Windows 10/11 (64bit)</p>
    </div>

    <h2 style=""font-size:16px;margin:24px 0 12px;color:#0F1419;"">설치 방법 (10분 소요)</h2>
    <ol style=""margin:0 0 16px 20px;padding:0;font-size:14px;line-height:1.8;color:#374151;"">
      <li>위 [히트판 ERP 설치 프로그램] 버튼을 클릭하여 파일을 다운로드합니다.</li>
      <li>다운로드된 <code style=""background:#F3F4F6;padding:2px 6px;border-radius:4px;font-family:monospace;"">HitPan-ERP-Setup-1.2.26.exe</code> 파일을 <strong>마우스 우클릭 → 관리자 권한으로 실행</strong>합니다.</li>
      <li>설치 마법사 첫 화면에서 위 시리얼 키를 정확히 입력합니다.</li>
      <li>이후 모든 과정(데이터베이스·통신연결·자동 시작)은 자동으로 진행됩니다.</li>
      <li>설치 완료 후 브라우저가 자동으로 열리며, 본인 ERP 주소로 접속됩니다.</li>
    </ol>

    <p style=""font-size:13px;color:#6B7280;line-height:1.7;margin:0 0 16px;"">
      ※ Windows Defender SmartScreen 경고가 표시되면 [추가 정보] → [실행] 을 클릭하세요.
      베타1 기간 한정이며 정식 출시 시 자동 신뢰됩니다.
    </p>

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
        public string? DesiredDomain { get; set; }
        public string? DomainAlias { get; set; }
    }

    private class TenantInfoRow
    {
        // license_key_plain 폐기 (사장님 결재 2026-06-18): LicenseKey 평문 속성 제거.
        //   재발급은 신규 키를 생성하므로 평문 조회 불필요. tenant_id로 해시만 갱신.
        public string? TenantId { get; set; }
        public string? DomainAlias { get; set; }
    }
}
