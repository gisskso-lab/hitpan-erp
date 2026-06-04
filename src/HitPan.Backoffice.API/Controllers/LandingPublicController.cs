using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 랜딩 공개 API — 헌법 #35 정합 이식 (사장님 결재 2026-06-04)
//
// 이식 범위:
//   - POST /api/landing/beta-signup         (베타 신청)
//   - POST /api/landing/signup/otp/send     (OTP 발송 stub — W2 매니저 가도)
//   - POST /api/landing/signup/otp/verify   (OTP 검증 stub — W2 매니저 가도)
//   - POST /api/landing/license/claim       (라이선스 검증 stub — W2 매니저 가도)
//
// 가입 본 흐름(POST /api/landing/signup + /confirm-payment)은 LandingSignupController 박제.
//
// 헌법 정합:
//   #18·#22 — 본사 DB만 박제, 평문 사업자정보 0건
//   #15 — 빈 catch 금지, ILogger 박제
[ApiController]
[Route("api/landing")]
[AllowAnonymous]
public class LandingPublicController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<LandingPublicController> _logger;

    public LandingPublicController(IConfiguration config, ILogger<LandingPublicController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost("beta-signup")]
    public async Task<IActionResult> BetaSignup([FromBody] BetaSignupRequest req, CancellationToken ct)
    {
        if (req is null
            || string.IsNullOrWhiteSpace(req.CompanyName)
            || string.IsNullOrWhiteSpace(req.ContactName)
            || string.IsNullOrWhiteSpace(req.Phone)
            || string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new BetaSignupResponse { Success = false, Message = "필수 항목을 모두 입력해주세요." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            await using var db = await OpenAsync(ct);
            await db.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO beta_signups (signup_id, company_name, contact_name, phone, email, employee_cnt, message, ip_address)
                  VALUES (UUID(), @CompanyName, @ContactName, @Phone, @Email, @EmployeeCnt, @Message, @Ip)",
                new
                {
                    req.CompanyName,
                    req.ContactName,
                    req.Phone,
                    req.Email,
                    req.EmployeeCnt,
                    req.Message,
                    Ip = ip
                },
                cancellationToken: ct));

            _logger.LogInformation("[BetaSignup] {Company} / {Email}", req.CompanyName, req.Email);
            return Ok(new BetaSignupResponse { Success = true, Message = "베타 신청이 완료되었습니다. 빠르게 연락드리겠습니다!" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BetaSignup] 저장 실패 {Company} / {Email}", req.CompanyName, req.Email);
            return StatusCode(500, new BetaSignupResponse { Success = false, Message = "서버 오류가 발생했습니다. 잠시 후 다시 시도해주세요." });
        }
    }

    [HttpPost("signup/otp/send")]
    public IActionResult SendOtp([FromBody] OtpRequestDto req, CancellationToken ct)
    {
        _logger.LogInformation("[Signup] OTP 발송 요청 Email={E}", req.Email);
        return Accepted(new { success = true, message = "OTP 발송 요청 접수 — SMTP 가도 W2 매니저" });
    }

    [HttpPost("signup/otp/verify")]
    public IActionResult VerifyOtp([FromBody] OtpVerifyDto req, CancellationToken ct)
    {
        _logger.LogInformation("[Signup] OTP 검증 요청 Token={T}", req.SignupToken);
        return Accepted(new OtpVerifyResponse
        {
            Verified = false,
            Message = "OTP 검증 가도 — W2 매니저"
        });
    }

    [HttpPost("license/claim")]
    public IActionResult ClaimLicense([FromBody] LicenseClaimRequest req, CancellationToken ct)
    {
        _logger.LogInformation("[Signup] 라이선스 검증 요청 Key={K}", req.LicenseKey);
        return Accepted(new LicenseClaimResponse
        {
            Valid = false,
            Message = "라이선스 검증 가도 — LicenseIssueService 신규 후 동작"
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

    public class BetaSignupRequest
    {
        public string CompanyName { get; set; } = "";
        public string ContactName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string? EmployeeCnt { get; set; }
        public string? Message { get; set; }
    }

    public class BetaSignupResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    public class OtpRequestDto
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string SignupToken { get; set; } = "";
    }

    public class OtpVerifyDto
    {
        [Required] public string SignupToken { get; set; } = "";
        [Required, StringLength(6, MinimumLength = 6)] public string Code { get; set; } = "";
    }

    public class OtpVerifyResponse
    {
        public bool Verified { get; set; }
        public string? PaymentUrl { get; set; }
        public string? Message { get; set; }
    }

    public class LicenseClaimRequest
    {
        [Required] public string LicenseKey { get; set; } = "";
    }

    public class LicenseClaimResponse
    {
        public bool Valid { get; set; }
        public string? DownloadUrl { get; set; }
        public string? CompanyName { get; set; }
        public string? Message { get; set; }
    }
}
