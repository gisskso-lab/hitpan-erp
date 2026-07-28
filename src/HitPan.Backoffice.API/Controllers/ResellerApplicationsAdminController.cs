using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Backoffice.API.Filters;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 협력업체 가입 신청 관리 — 본사 어드민 (사장님 결재 2026-06-04, 헌법 #35 정합)
//
// 권한: PlatformAdmin Policy (platform_admin / platform_owner)
//
// 흐름:
//   GET    /api/backoffice/reseller-applications          (목록, 상태 필터)
//   GET    /api/backoffice/reseller-applications/{id}     (상세)
//   POST   /api/backoffice/reseller-applications/{id}/approve  (승인 → reseller + reseller_accounts 생성 + 메일)
//   POST   /api/backoffice/reseller-applications/{id}/reject   (반려 → 사유 저장 + 메일)
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #18·#22 — 본사 백오피스 DB 영역 (resellers·reseller_accounts·reseller_applications)
//   #20 — 신청 → 승인 → 계정 발급 → 메일 끊김 0
//   #25 — 쉽게(목록·상세·승인 3클릭)·정확하게(트랜잭션·검증)·안전하게(BCrypt + 임시 비밀번호 1회 메일)
//   #29 — SMTP 자격증명 환경변수
[ApiController]
[Route("api/backoffice/reseller-applications")]
[Authorize]
public class ResellerApplicationsAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IEmailSender _email;
    private readonly ILogger<ResellerApplicationsAdminController> _logger;

    public ResellerApplicationsAdminController(
        IConfiguration config,
        IEmailSender email,
        ILogger<ResellerApplicationsAdminController> logger)
    {
        _config = config;
        _email = email;
        _logger = logger;
    }

    [HttpGet]
    [BoPermission("reseller-applications.list")]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var sql = @"
                SELECT
                    CAST(application_id AS CHAR) AS ApplicationId,
                    company_name      AS CompanyName,
                    representative_name AS CeoName,
                    contact_name      AS ContactName,
                    contact_email     AS Email,
                    contact_phone     AS Phone,
                    business_no       AS BizNo,
                    region            AS SalesRegion,
                    expected_customers AS SalesYears,
                    status            AS Status,
                    submitted_at      AS SubmittedAt
                FROM reseller_applications
                {0}
                ORDER BY submitted_at DESC
                LIMIT 500";
            var whereClause = "";
            object? param = null;
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                whereClause = "WHERE status = @Status";
                param = new { Status = status };
            }
            var rows = await db.QueryAsync<ApplicationRow>(string.Format(sql, whereClause), param);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerAppsAdmin] 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpGet("{id}")]
    [BoPermission("reseller-applications.detail")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.QueryFirstOrDefaultAsync<ApplicationDetailRow>(@"
                SELECT
                    CAST(application_id AS CHAR) AS ApplicationId,
                    company_name        AS CompanyName,
                    representative_name AS CeoName,
                    contact_name        AS ContactName,
                    contact_email       AS Email,
                    contact_phone       AS Phone,
                    business_no         AS BizNo,
                    region              AS SalesRegion,
                    sales_channel       AS ContactTitle,
                    expected_customers  AS SalesYears,
                    motivation          AS Reason,
                    status              AS Status,
                    submitted_at        AS SubmittedAt,
                    reviewed_at         AS ReviewedAt,
                    reviewed_by         AS ReviewedBy,
                    rejection_reason    AS RejectionReason
                FROM reseller_applications WHERE application_id = @Id",
                new { Id = id });
            if (row is null)
                return NotFound(new { success = false, message = "신청을 찾을 수 없습니다." });
            return Ok(new { success = true, item = row });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerAppsAdmin] 상세 조회 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "상세 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("{id}/approve")]
    [BoPermission("reseller-applications.approve")]
    public async Task<IActionResult> Approve(string id, CancellationToken ct)
    {
        var reviewerId = User.FindFirst("sub")?.Value ?? "system";

        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        try
        {
            var app = await db.QueryFirstOrDefaultAsync<ApplicationDetailRow>(@"
                SELECT
                    CAST(application_id AS CHAR) AS ApplicationId,
                    company_name        AS CompanyName,
                    representative_name AS CeoName,
                    contact_name        AS ContactName,
                    contact_email       AS Email,
                    contact_phone       AS Phone,
                    business_no         AS BizNo,
                    region              AS SalesRegion,
                    status              AS Status
                FROM reseller_applications WHERE application_id = @Id FOR UPDATE",
                new { Id = id }, tx);

            if (app is null)
                return NotFound(new { success = false, message = "신청을 찾을 수 없습니다." });
            if (app.Status == "approved")
                return BadRequest(new { success = false, message = "이미 승인된 신청입니다." });
            if (app.Status == "rejected")
                return BadRequest(new { success = false, message = "이미 반려된 신청입니다." });

            // 1) reseller 생성 (R-XXXX 코드)
            var resellerId = Guid.NewGuid().ToString();
            var codeSeq = await db.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) + 1 FROM resellers", transaction: tx);
            var resellerCode = $"R-{codeSeq:D4}";

            await db.ExecuteAsync(@"
                INSERT INTO resellers
                  (reseller_id, reseller_code, reseller_name, biz_no, ceo_name, tel, address,
                   contact_person, contact_phone, contact_email,
                   join_date, status, created_at, updated_at, created_by)
                VALUES
                  (@ResellerId, @ResellerCode, @CompanyName, @BizNo, @CeoName, @Phone, NULL,
                   @ContactName, @Phone, @Email,
                   CURRENT_DATE(), 'active', UTC_TIMESTAMP(), UTC_TIMESTAMP(), @ReviewerId)",
                new
                {
                    ResellerId = resellerId,
                    ResellerCode = resellerCode,
                    app.CompanyName,
                    app.BizNo,
                    app.CeoName,
                    app.Phone,
                    app.ContactName,
                    app.Email,
                    ReviewerId = reviewerId
                }, tx);

            // 2) reseller_accounts 생성 (임시 비밀번호)
            var accountId = Guid.NewGuid().ToString();
            var tempPassword = GenerateTempPassword();
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

            await db.ExecuteAsync(@"
                INSERT INTO reseller_accounts
                  (account_id, reseller_id, email, password_hash, account_name, role, phone, is_active, created_at, updated_at)
                VALUES
                  (@AccountId, @ResellerId, @Email, @Hash, @AccountName, 'reseller_admin', @Phone, 1,
                   UTC_TIMESTAMP(), UTC_TIMESTAMP())",
                new
                {
                    AccountId = accountId,
                    ResellerId = resellerId,
                    app.Email,
                    Hash = passwordHash,
                    AccountName = app.ContactName,
                    app.Phone
                }, tx);

            // 3) 신청 상태 갱신
            await db.ExecuteAsync(@"
                UPDATE reseller_applications
                SET status = 'approved', reviewed_at = UTC_TIMESTAMP(), reviewed_by = @ReviewerId
                WHERE application_id = @Id",
                new { Id = id, ReviewerId = reviewerId }, tx);

            await tx.CommitAsync(ct);

            _logger.LogInformation("[ResellerAppsAdmin] approved id={Id} reseller={Code} email={Email}",
                id, resellerCode, app.Email);

            // 4) 메일 송부 (트랜잭션 밖, 실패해도 승인은 저장됨)
            _ = _email.SendAsync(app.Email,
                "[히트판] 협력업체 가입 승인 — 계정 발급 완료",
                BuildApproveHtml(app.CompanyName, resellerCode, app.Email, tempPassword),
                ct);

            return Ok(new
            {
                success = true,
                message = "승인이 완료되었습니다. 계정 정보가 신청자 이메일로 발송되었습니다.",
                resellerId,
                resellerCode
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); }
            catch (Exception rex) { _logger.LogWarning(rex, "[ResellerAppsAdmin] rollback 실패 id={Id}", id); }
            _logger.LogError(ex, "[ResellerAppsAdmin] 승인 처리 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "승인 처리 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("{id}/reject")]
    [BoPermission("reseller-applications.reject")]
    public async Task<IActionResult> Reject(string id, [FromBody] RejectRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, message = "반려 사유를 입력해주세요." });

        var reviewerId = User.FindFirst("sub")?.Value ?? "system";

        try
        {
            await using var db = await OpenAsync(ct);
            var app = await db.QueryFirstOrDefaultAsync<ApplicationDetailRow>(@"
                SELECT company_name AS CompanyName, contact_email AS Email, status AS Status
                FROM reseller_applications WHERE application_id = @Id",
                new { Id = id });

            if (app is null)
                return NotFound(new { success = false, message = "신청을 찾을 수 없습니다." });
            if (app.Status == "approved")
                return BadRequest(new { success = false, message = "이미 승인된 신청은 반려할 수 없습니다." });
            if (app.Status == "rejected")
                return BadRequest(new { success = false, message = "이미 반려된 신청입니다." });

            var affected = await db.ExecuteAsync(@"
                UPDATE reseller_applications
                SET status = 'rejected', reviewed_at = UTC_TIMESTAMP(),
                    reviewed_by = @ReviewerId, rejection_reason = @Reason
                WHERE application_id = @Id",
                new { Id = id, ReviewerId = reviewerId, req.Reason });

            _logger.LogInformation("[ResellerAppsAdmin] rejected id={Id} reason={Reason}", id, req.Reason);

            _ = _email.SendAsync(app.Email,
                "[히트판] 협력업체 가입 신청 검토 결과",
                BuildRejectHtml(app.CompanyName, req.Reason),
                ct);

            return Ok(new { success = true, message = "반려 처리되었습니다. 신청자 이메일로 안내가 발송되었습니다.", affected });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerAppsAdmin] 반려 처리 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "반려 처리 중 오류가 발생했습니다." });
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

    // 임시 비밀번호 12자 (대소문자+숫자, I·L·O·0·1 제외 — 오인 방지)
    private static string GenerateTempPassword()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        Span<byte> buf = stackalloc byte[12];
        RandomNumberGenerator.Fill(buf);
        var sb = new StringBuilder(12);
        for (int i = 0; i < 12; i++) sb.Append(alphabet[buf[i] % alphabet.Length]);
        return sb.ToString();
    }

    private static string BuildApproveHtml(string companyName, string resellerCode, string email, string tempPassword) => $@"
<div style='font-family:-apple-system,BlinkMacSystemFont,Pretendard,Segoe UI,Roboto,sans-serif;max-width:560px;margin:0 auto;padding:32px;color:#1A2B4A;'>
  <h2 style='color:#0F6E56;margin:0 0 16px;'>협력업체 가입이 승인되었습니다</h2>
  <p>안녕하세요, <b>{System.Net.WebUtility.HtmlEncode(companyName)}</b> 담당자님.</p>
  <p>히트판 협력업체 가입 신청이 본사 검토를 통과하여 승인되었습니다.</p>

  <div style='background:#0F6E56;color:#fff;border-radius:12px;padding:20px;margin:20px 0;'>
    <div style='font-size:13px;opacity:0.85;margin-bottom:6px;'>대리점 코드</div>
    <div style='font-size:22px;font-weight:700;letter-spacing:2px;font-family:Consolas,Monaco,monospace;'>{resellerCode}</div>
  </div>

  <div style='background:#FEF3C7;border:1px solid #FDE68A;border-radius:10px;padding:14px;margin:16px 0;'>
    <div style='font-size:13px;font-weight:700;color:#92400E;margin-bottom:8px;'>로그인 정보 (1회 발송 — 안전한 곳에 보관)</div>
    <div style='font-family:Consolas,Monaco,monospace;font-size:14px;color:#1A2B4A;'>
      <div>로그인 ID: <b>{System.Net.WebUtility.HtmlEncode(email)}</b></div>
      <div>임시 비밀번호: <b>{tempPassword}</b></div>
    </div>
  </div>

  <p><b>중요:</b> 백오피스 첫 로그인 후 즉시 비밀번호를 변경해주세요.</p>
  <p>로그인 주소: <a href='https://back.hitpan.kr/backoffice/login' style='color:#0F6E56;'>https://back.hitpan.kr/backoffice/login</a></p>

  <p style='margin-top:24px;color:#6B7280;font-size:13px;'>이 메일은 발신 전용입니다. 문의는 partners@hitpan.kr 로 부탁드립니다.</p>
</div>";

    private static string BuildRejectHtml(string companyName, string reason) => $@"
<div style='font-family:-apple-system,BlinkMacSystemFont,Pretendard,Segoe UI,Roboto,sans-serif;max-width:560px;margin:0 auto;padding:32px;color:#1A2B4A;'>
  <h2 style='color:#B45309;margin:0 0 16px;'>협력업체 가입 신청 검토 결과</h2>
  <p>안녕하세요, <b>{System.Net.WebUtility.HtmlEncode(companyName)}</b> 담당자님.</p>
  <p>히트판 협력업체 가입 신청을 검토한 결과, 아래 사유로 이번 신청은 승인되지 않았습니다.</p>

  <div style='background:#FEF2F2;border:1px solid #FECACA;border-radius:10px;padding:14px;margin:16px 0;color:#7F1D1D;'>
    <div style='font-size:13px;font-weight:700;margin-bottom:6px;'>검토 사유</div>
    <div style='font-size:14px;line-height:1.6;'>{System.Net.WebUtility.HtmlEncode(reason)}</div>
  </div>

  <p>추가 문의나 재신청이 필요하시면 partners@hitpan.kr 로 연락 주시기 바랍니다.</p>

  <p style='margin-top:24px;color:#6B7280;font-size:13px;'>이 메일은 발신 전용입니다.</p>
</div>";

    public record RejectRequest(string Reason);

    public class ApplicationRow
    {
        public string ApplicationId { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string CeoName { get; set; } = "";
        public string ContactName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string? BizNo { get; set; }
        public string? SalesRegion { get; set; }
        public int? SalesYears { get; set; }
        public string Status { get; set; } = "";
        public DateTime SubmittedAt { get; set; }
    }

    public class ApplicationDetailRow : ApplicationRow
    {
        public string? ContactTitle { get; set; }
        public string? Reason { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewedBy { get; set; }
        public string? RejectionReason { get; set; }
    }
}
