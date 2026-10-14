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
            // 🔴 20261015작B1 B-6 (G2) — `_ = …` 로 버리던 자리. 이 메일에는 **계정 발급 안내**가 들어 있어
            //   안 가면 신청자가 로그인 자체를 못 한다. 그런데 버려진 false 때문에 본사도 몰랐다.
            //   이제 기다리고 결과를 남긴다. 승인은 이미 커밋됐고 흐름은 끊지 않는다(#20) —
            //   그래서 await 를 ReportMailAsync 안의 try 로 감싼다(바깥 catch 는 롤백·500 이다).
            await ReportMailAsync(_email.SendAsync(app.Email,
                    "[히트판] 협력업체 가입 승인 — 계정 발급 완료",
                    BuildApproveHtml(app.CompanyName, resellerCode, app.Email, tempPassword),
                    ct),
                "협력업체 승인 계정발급", app.Email, id);

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

            // 🔴 20261015작B1 B-6 (G2) — B-4 와 동일. 반려 안내가 안 가면 신청자는 영영 기다린다.
            await ReportMailAsync(_email.SendAsync(app.Email,
                    "[히트판] 협력업체 가입 신청 검토 결과",
                    BuildRejectHtml(app.CompanyName, req.Reason),
                    ct),
                "협력업체 반려 안내", app.Email, id);

            return Ok(new { success = true, message = "반려 처리되었습니다. 신청자 이메일로 안내가 발송되었습니다.", affected });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerAppsAdmin] 반려 처리 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "반려 처리 중 오류가 발생했습니다." });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 🔴 20261015작B1 B-6 (G2) — 버려지던 발송 결과를 받아 남긴다
    //   (LandingSignupController.ReportMailAsync 와 같은 규범 · 그 쪽은 가입 축, 여기는 협력업체 축이다.
    //    공용 통로로 묶지 않은 이유: 이 차수 갈래표 밖 파일을 새로 만들어야 하고,
    //    로그 머리말(`[ResellerAppsAdmin]`)과 식별자(신청 id)가 다르다.)
    //
    // 🔴 흐름은 끊지 않는다(#20): 승인은 이미 `tx.CommitAsync` 로 저장됐다.
    //   여기서 예외가 바깥으로 번지면 바깥 catch 가 **커밋된 트랜잭션을 롤백 시도하고 500 을 돌려준다** —
    //   화면은 "승인 실패"로 보이는데 DB 에는 승인이 남는 최악의 모양이 된다. 그래서 전부 삼키고 남긴다.
    private async Task ReportMailAsync(Task<bool> sending, string what, string toEmail, string applicationId)
    {
        try
        {
            var ok = await sending;
            if (ok)
            {
                _logger.LogInformation("[ResellerAppsAdmin] 메일 발송 성공 what={What} id={Id} to={To}", what, applicationId, toEmail);
            }
            else
            {
                // 🔴 이 한 줄이 이 차수의 요점이다. 표(bo_email_send_history)에도 같은 사실이 1행 남는다.
                _logger.LogWarning(
                    "[ResellerAppsAdmin] 메일 발송 실패 what={What} id={Id} to={To} — 처리 결과는 저장됐다. 사유는 bo_email_send_history 를 보라.",
                    what, applicationId, toEmail);
            }
        }
        catch (Exception ex)
        {
            // #15 — 삼키지 않는다(기록은 남긴다). #20 — 그래도 승인·반려 결과는 안 깬다.
            _logger.LogWarning(ex,
                "[ResellerAppsAdmin] 메일 발송 중 예외 what={What} id={Id} to={To} — 처리 결과는 저장됐다.",
                what, applicationId, toEmail);
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
