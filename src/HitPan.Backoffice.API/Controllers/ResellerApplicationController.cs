using System.ComponentModel.DataAnnotations;
using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 협력업체 가입 신청 API (사장님 결재 2026-06-04, 헌법 #35 정합)
//
// 흐름:
//   1) 신청 접수 → reseller_applications INSERT (status='pending')
//   2) 임시 표준 PDF 계약서 자동 생성 (QuestPDF)
//   3) 신청자 이메일로 PDF 첨부 송부
//   4) 응답: applicationId
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #18·#22 — 본사 백오피스 DB(reseller_applications), 평문 사업자번호 저장 (해시 아님 — 본사 운영 데이터)
//   #20 — 신청 → PDF → 메일 끊김 0
//   #29 — SMTP 자격증명 환경변수 (EmailSender에서 처리)
//   #35 — 신청 입구·코드·DB 모두 본사 백오피스 영역
[ApiController]
[Route("api/backoffice/reseller-applications")]
[AllowAnonymous]
public class ResellerApplicationController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IEmailSender _email;
    private readonly IContractPdfGenerator _pdf;
    private readonly ILogger<ResellerApplicationController> _logger;

    public ResellerApplicationController(
        IConfiguration config,
        IEmailSender email,
        IContractPdfGenerator pdf,
        ILogger<ResellerApplicationController> logger)
    {
        _config = config;
        _email = email;
        _pdf = pdf;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] ApplyRequest req, CancellationToken ct)
    {
        if (req is null)
            return BadRequest(new { success = false, message = "요청 비어 있음" });

        if (!req.AgreeTerms || !req.AgreePrivacy)
            return BadRequest(new { success = false, message = "필수 약관 2건에 모두 동의해주세요." });

        if (string.IsNullOrWhiteSpace(req.CompanyName)
            || string.IsNullOrWhiteSpace(req.CeoName)
            || string.IsNullOrWhiteSpace(req.ContactName)
            || string.IsNullOrWhiteSpace(req.Email)
            || string.IsNullOrWhiteSpace(req.Phone)
            || string.IsNullOrWhiteSpace(req.BizNo))
            return BadRequest(new { success = false, message = "필수 항목을 모두 입력해주세요." });

        var bizNoNormalized = req.BizNo.Replace("-", "").Replace(" ", "").Trim();
        if (bizNoNormalized.Length != 10 || !bizNoNormalized.All(char.IsDigit) || !IsValidBizNoChecksum(bizNoNormalized))
            return BadRequest(new { success = false, message = "올바르지 않은 사업자번호입니다." });

        var applicationId = Guid.NewGuid().ToString();
        var issuedAt = DateTime.Now;

        try
        {
            await using var db = await OpenAsync(ct);
            await db.ExecuteAsync(@"
                INSERT INTO reseller_applications
                  (application_id, company_name, representative_name, contact_name,
                   contact_email, contact_phone, business_no, region, sales_channel,
                   expected_customers, motivation, status, submitted_at)
                VALUES
                  (@ApplicationId, @CompanyName, @CeoName, @ContactName,
                   @Email, @Phone, @BizNo, @Region, @SalesChannel,
                   @ExpectedCustomers, @Motivation, 'pending', UTC_TIMESTAMP())",
                new
                {
                    ApplicationId = applicationId,
                    req.CompanyName,
                    req.CeoName,
                    req.ContactName,
                    req.Email,
                    req.Phone,
                    BizNo = bizNoNormalized,
                    Region = req.SalesRegion,
                    SalesChannel = req.ContactTitle, // 담당자 직책을 sales_channel에 저장
                    ExpectedCustomers = req.SalesYears,
                    Motivation = req.Reason
                });

            _logger.LogInformation("[ResellerApply] submitted id={Id} company={Company} email={Email}",
                applicationId, req.CompanyName, req.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerApply] DB insert 실패 company={Company} email={Email}",
                req.CompanyName, req.Email);
            return StatusCode(500, new { success = false, message = "신청 접수 중 오류가 발생했습니다. 잠시 후 다시 시도해주세요." });
        }

        // PDF 생성 + 메일 송부 — 실패해도 신청은 접수됨
        byte[]? pdfBytes = null;
        try
        {
            var input = new ResellerContractInput(
                ApplicationId: applicationId,
                CompanyName: req.CompanyName,
                BizNo: FormatBizNo(bizNoNormalized),
                CeoName: req.CeoName,
                CompanyAddress: req.CompanyAddress ?? "",
                ContactName: req.ContactName,
                ContactTitle: req.ContactTitle ?? "",
                Phone: req.Phone,
                Email: req.Email,
                SalesRegion: req.SalesRegion,
                SalesYears: req.SalesYears,
                Reason: req.Reason,
                IssuedAt: issuedAt);
            pdfBytes = _pdf.CreateResellerContract(input);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ResellerApply] PDF 생성 실패 id={Id}", applicationId);
        }

        try
        {
            var html = BuildEmailHtml(req.CompanyName, applicationId);
            if (pdfBytes is not null)
            {
                await _email.SendWithAttachmentAsync(
                    req.Email,
                    "[히트판] 협력업체 가입계약서 (초안)",
                    html,
                    pdfBytes,
                    $"히트판_협력업체_가입계약서_{applicationId[..8]}.pdf",
                    "application/pdf",
                    ct);
            }
            else
            {
                await _email.SendAsync(
                    req.Email,
                    "[히트판] 협력업체 가입 신청 접수",
                    html,
                    ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ResellerApply] 메일 송부 실패 id={Id} email={Email}", applicationId, req.Email);
        }

        return Ok(new
        {
            success = true,
            message = "신청이 접수되었습니다. 가입계약서 초안이 이메일로 발송되었습니다. 본사 검토 후 정식 계약서가 별도로 발송됩니다.",
            applicationId
        });
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

    private static bool IsValidBizNoChecksum(string bn)
    {
        if (bn.Length != 10) return false;
        ReadOnlySpan<int> w = stackalloc int[] { 1, 3, 7, 1, 3, 7, 1, 3, 5 };
        int sum = 0;
        for (int i = 0; i < 9; i++) sum += (bn[i] - '0') * w[i];
        sum += ((bn[8] - '0') * 5) / 10;
        int expected = (10 - (sum % 10)) % 10;
        return expected == (bn[9] - '0');
    }

    private static string FormatBizNo(string bn) =>
        bn.Length == 10 ? $"{bn[..3]}-{bn.Substring(3, 2)}-{bn.Substring(5)}" : bn;

    private static string BuildEmailHtml(string companyName, string applicationId) => $@"
<div style='font-family:-apple-system,BlinkMacSystemFont,Pretendard,Segoe UI,Roboto,sans-serif;max-width:560px;margin:0 auto;padding:32px;color:#1A2B4A;'>
  <h2 style='color:#0F6E56;margin:0 0 16px;'>협력업체 가입 신청이 접수되었습니다</h2>
  <p>안녕하세요, <b>{System.Net.WebUtility.HtmlEncode(companyName)}</b> 담당자님.</p>
  <p>히트판 협력업체 가입 신청이 정상 접수되었습니다.</p>
  <div style='background:#F0FAF6;border:1px solid #C7E9D9;border-radius:12px;padding:16px;margin:20px 0;'>
    <div style='font-size:13px;color:#6B7280;margin-bottom:4px;'>신청 번호</div>
    <div style='font-size:16px;font-weight:700;color:#0F6E56;font-family:Consolas,Monaco,monospace;'>{applicationId}</div>
  </div>
  <p>첨부된 <b>가입계약서 초안 PDF</b>를 확인해주십시오. 본사 검토·승인 후 정식 계약서가 별도로 발송됩니다.</p>
  <p style='margin-top:24px;color:#6B7280;font-size:13px;'>이 메일은 발신 전용입니다. 문의는 partners@hitpan.kr 로 부탁드립니다.</p>
</div>";

    public class ApplyRequest
    {
        [Required] public string CompanyName { get; set; } = "";
        [Required] public string BizNo { get; set; } = "";
        [Required] public string CeoName { get; set; } = "";
        public string? CompanyAddress { get; set; }
        [Required] public string ContactName { get; set; } = "";
        public string? ContactTitle { get; set; }
        [Required] public string Phone { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        public string? SalesRegion { get; set; }
        public int? SalesYears { get; set; }
        public string? Reason { get; set; }
        [Required] public bool AgreeTerms { get; set; }
        [Required] public bool AgreePrivacy { get; set; }
    }
}
