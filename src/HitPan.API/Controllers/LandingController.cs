using Dapper;
using HitPan.Application.DTOs.Landing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.API.Controllers;

/// <summary>
/// 랜딩페이지 공개 API — 베타 신청 접수.
/// AllowAnonymous: tenant_id 없음, 멀티테넌트 불필요.
/// beta_signups 테이블은 INSERT ONLY (append-only).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class LandingController : ControllerBase
{
    private readonly string _connStr;
    private readonly ILogger<LandingController> _logger;

    public LandingController(IConfiguration config, ILogger<LandingController> logger)
    {
        _connStr = config.GetConnectionString("DefaultConnection")
                   ?? throw new InvalidOperationException("DB 연결 문자열이 없습니다.");
        _logger = logger;
    }

    /// <summary>베타 신청 접수 — 랜딩페이지에서 호출. 공개 엔드포인트.</summary>
    [HttpPost("beta-signup")]
    public async Task<IActionResult> BetaSignup([FromBody] BetaSignupRequest request, CancellationToken ct)
    {
        // 필수 항목 검증
        if (string.IsNullOrWhiteSpace(request.CompanyName) ||
            string.IsNullOrWhiteSpace(request.ContactName) ||
            string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new BetaSignupResponse { Success = false, Message = "필수 항목을 모두 입력해주세요." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            await using var db = new MySqlConnection(_connStr);
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO beta_signups (signup_id, company_name, contact_name, phone, email, employee_cnt, message, ip_address) " +
                "VALUES (UUID(), @CompanyName, @ContactName, @Phone, @Email, @EmployeeCnt, @Message, @Ip)",
                new
                {
                    request.CompanyName,
                    request.ContactName,
                    request.Phone,
                    request.Email,
                    request.EmployeeCnt,
                    request.Message,
                    Ip = ip
                },
                cancellationToken: ct));

            _logger.LogInformation("베타 신청 접수: {Company} / {Email}", request.CompanyName, request.Email);

            return Ok(new BetaSignupResponse { Success = true, Message = "베타 신청이 완료되었습니다. 빠르게 연락드리겠습니다!" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "베타 신청 저장 실패: {Company} / {Email}", request.CompanyName, request.Email);
            return StatusCode(500, new BetaSignupResponse { Success = false, Message = "서버 오류가 발생했습니다. 잠시 후 다시 시도해주세요." });
        }
    }

    // ── 작9 정식 가입 흐름 스켈레톤 (W2 매니저 가도) ─────────────────────────
    // 사업자번호 검증 → 가입 임시 박제 → OTP 발송 → 결재 → 라이선스 자동 발급
    // 외부 의존: 사업자번호 검증 API · 토스 키 · SMTP (사장님 결재 영역)

    // 박제 통합 2026-06-02 (사장님 모두결재) — Signup 박제 실 구현 = LandingSignupController로 통합
    // 라우트 충돌 박제 사고 봉합 (LandingSignupController.Signup이 실 박제, 본 stub 박제 폐기)

    /// <summary>정식 가입 2단계 — 이메일 OTP 발송 (W2 매니저 가도, SMTP 7월 박제 영역)</summary>
    [HttpPost("signup/otp/send")]
    public IActionResult SendOtp([FromBody] HitPan.Application.DTOs.Landing.OtpRequestDto request, CancellationToken ct)
    {
        _logger.LogInformation("[Signup] OTP 발송 요청 박제 — Email={E}", request.Email);
        return Accepted(new { success = true, message = "OTP 발송 요청 박제 완료 — W2 매니저 가도 (SMTP)" });
    }

    /// <summary>정식 가입 3단계 — OTP 검증 + 결재 URL 발급 (W2 매니저 가도, 토스 키 사장님 결재 영역)</summary>
    [HttpPost("signup/otp/verify")]
    public IActionResult VerifyOtp([FromBody] HitPan.Application.DTOs.Landing.OtpVerifyDto request, CancellationToken ct)
    {
        _logger.LogInformation("[Signup] OTP 검증 요청 박제 — Token={T}", request.SignupToken);
        return Accepted(new HitPan.Application.DTOs.Landing.OtpVerifyResponse
        {
            Verified = false,
            Message = "OTP 검증 W2 매니저 가도 — 6/4 백엔드 매니저 발진"
        });
    }

    /// <summary>EXE 다운로드 전 라이선스 키 검증 (W2 매니저 가도)</summary>
    [HttpPost("license/claim")]
    public IActionResult ClaimLicense([FromBody] HitPan.Application.DTOs.Landing.LicenseClaimRequest request, CancellationToken ct)
    {
        _logger.LogInformation("[Signup] 라이선스 검증 요청 박제 — Key={K}", request.LicenseKey);
        return Accepted(new HitPan.Application.DTOs.Landing.LicenseClaimResponse
        {
            Valid = false,
            Message = "라이선스 검증 W2 매니저 가도 — LicenseIssueService 신규 발진 후 동작"
        });
    }
}
