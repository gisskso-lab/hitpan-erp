using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
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
    public async Task<IActionResult> ClaimLicense([FromBody] LicenseClaimRequest req, CancellationToken ct)
    {
        // 헌법 #35 + #18/#22 정합 옵션 C (사장님 결재 2026-06-04):
        //   - 본사 평문 사업자정보 보관 0건
        //   - ERP 첫 부팅 시 라이선스 키 + 사업자번호 입력 → 둘 다 해시 매칭만 검증
        //   - 통과 시 ERP에 메타(회사명·고객사 코드)만 반환, 사업자번호는 ERP가 입력 그대로 박제
        if (req is null || string.IsNullOrWhiteSpace(req.LicenseKey) || string.IsNullOrWhiteSpace(req.BizNo))
            return BadRequest(new LicenseClaimResponse { Valid = false, Message = "라이선스 키와 사업자번호가 필요합니다." });

        var bizNoNormalized = req.BizNo.Replace("-", "").Replace(" ", "").Trim();
        if (bizNoNormalized.Length != 10)
            return BadRequest(new LicenseClaimResponse { Valid = false, Message = "사업자번호 형식 오류 (10자리)" });

        var licensePepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
        var bizPepper = _config["Backoffice:BizNoPepper"] ?? throw new InvalidOperationException("Backoffice:BizNoPepper 미설정");
        var licenseHash = ComputeHmacSha256(req.LicenseKey.Trim(), licensePepper);
        var bizHash = ComputeHmacSha256(bizNoNormalized, bizPepper);

        try
        {
            await using var db = await OpenAsync(ct);
            // 라이선스 해시 매칭 + status=active 검증
            var tenant = await db.QueryFirstOrDefaultAsync<TenantRow>(@"
                SELECT CAST(tenant_id AS CHAR) AS TenantId,
                       tenant_code AS TenantCode,
                       company_name AS CompanyName,
                       status AS Status
                FROM tenants
                WHERE license_key_hash = @Hash AND status = 'active'
                LIMIT 1",
                new { Hash = licenseHash });

            if (tenant is null)
            {
                _logger.LogInformation("[LicenseClaim] license hash 매칭 실패 또는 비활성 status");
                return Ok(new LicenseClaimResponse
                {
                    Valid = false,
                    Message = "라이선스 키가 유효하지 않거나 활성 상태가 아닙니다. 결제가 완료되었는지 확인해주세요."
                });
            }

            // 사업자번호 해시 매칭 (landing_signups에 박제된 해시와 비교)
            // 같은 회사명이 여러 가입에 박제될 수 있어 tenant.CompanyName과 함께 좁힘
            var bizMatch = await db.QueryFirstOrDefaultAsync<int>(@"
                SELECT COUNT(*) FROM landing_signups
                WHERE biz_no_hash = @BizHash AND company_name = @CompanyName",
                new { BizHash = bizHash, tenant.CompanyName });

            if (bizMatch == 0)
            {
                _logger.LogInformation("[LicenseClaim] biz_no hash 매칭 실패 tenant={Code}", tenant.TenantCode);
                return Ok(new LicenseClaimResponse
                {
                    Valid = false,
                    Message = "사업자번호가 가입 시 입력한 정보와 일치하지 않습니다."
                });
            }

            // 헌법 #35 (사장님 결재 2026-06-04) W2 — 객체 완전 분리 서명 토큰
            //   - 백오피스가 HMAC-SHA256 토큰 발급, ERP는 HTTP 호출 없이 서명 검증으로 신뢰
            //   - 클레임: tenantId · tenantCode · companyName · subscription_tier · status · 본사 영역 캐시
            //   - 만료: 10분, jti 1회용 (재사용은 ERP가 캐시 박제)
            var bootstrapKey = Environment.GetEnvironmentVariable("HITPAN_BOOTSTRAP_TOKEN_KEY")
                              ?? _config["Bootstrap:TokenKey"]
                              ?? throw new InvalidOperationException("Bootstrap:TokenKey 또는 HITPAN_BOOTSTRAP_TOKEN_KEY 환경변수 미설정");

            // 본사 영역 캐시 데이터 (ERP local_subscription 박제용)
            var sub = await db.QueryFirstOrDefaultAsync<SubscriptionRow>(@"
                SELECT
                    COALESCE(subscription_tier, 'basic') AS SubscriptionTier,
                    COALESCE(status, 'active')           AS Status,
                    COALESCE(ai_mode, 'hitpan_pool')     AS AiMode,
                    COALESCE(ai_token_monthly_limit, 100000) AS AiTokenMonthlyLimit,
                    COALESCE(ai_token_extra, 0)          AS AiTokenExtra,
                    anthropic_api_key_last4              AS AnthropicKeyLast4,
                    COALESCE(anthropic_key_status, 'none') AS AnthropicKeyStatus,
                    COALESCE(max_users, 3)               AS MaxUsers,
                    COALESCE(extra_device_slots, 0)      AS ExtraDeviceSlots,
                    CAST(reseller_id AS CHAR)            AS ResellerId,
                    COALESCE(reseller_tier, 0)           AS ResellerTier,
                    trial_ends_at                        AS TrialEndsAt
                FROM tenants
                WHERE tenant_id = @TenantId",
                new { tenant.TenantId });

            var tokenPayload = new
            {
                jti = Guid.NewGuid().ToString("N"),
                iss = "hitpan-backoffice",
                aud = "erp-bootstrap",
                sub = tenant.TenantId,
                tenant_code = tenant.TenantCode,
                company_name = tenant.CompanyName,
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
                subscription = sub
            };
            var bootstrapToken = SignToken(tokenPayload, bootstrapKey);

            _logger.LogInformation("[LicenseClaim] 검증 통과 tenant={Code}", tenant.TenantCode);
            return Ok(new LicenseClaimResponse
            {
                Valid = true,
                CompanyName = tenant.CompanyName,
                Message = "라이선스 검증 완료. ERP 자동 반영을 진행합니다.",
                TenantCode = tenant.TenantCode,
                BootstrapToken = bootstrapToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LicenseClaim] 검증 중 오류");
            return StatusCode(500, new LicenseClaimResponse { Valid = false, Message = "검증 중 서버 오류가 발생했습니다." });
        }
    }

    private static string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // 헌법 #35 W2 부트스트랩 토큰 — JSON 페이로드 base64url + HMAC-SHA256 서명 base64url
    //   형식: <payloadB64>.<signatureB64>
    //   ERP는 동일 키로 서명 검증 후 페이로드 신뢰
    private static string SignToken(object payload, string key)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var payloadB64 = Base64UrlEncode(payloadBytes);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        var sigB64 = Base64UrlEncode(signature);
        return $"{payloadB64}.{sigB64}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private class TenantRow
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class SubscriptionRow
    {
        public string SubscriptionTier { get; set; } = "basic";
        public string Status { get; set; } = "active";
        public string AiMode { get; set; } = "hitpan_pool";
        public int AiTokenMonthlyLimit { get; set; } = 100000;
        public int AiTokenExtra { get; set; }
        public string? AnthropicKeyLast4 { get; set; }
        public string AnthropicKeyStatus { get; set; } = "none";
        public int MaxUsers { get; set; } = 3;
        public int ExtraDeviceSlots { get; set; }
        public string? ResellerId { get; set; }
        public int ResellerTier { get; set; }
        public DateTime? TrialEndsAt { get; set; }
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
        // 옵션 C (사장님 결재 2026-06-04) — 본사 평문 보관 0건, ERP가 입력한 사업자번호로 해시 매칭 검증
        [Required] public string BizNo { get; set; } = "";
    }

    public class LicenseClaimResponse
    {
        public bool Valid { get; set; }
        public string? DownloadUrl { get; set; }
        public string? CompanyName { get; set; }
        public string? Message { get; set; }
        public string? TenantCode { get; set; }
        // 헌법 #35 W2 (사장님 결재 2026-06-04): 객체 완전 분리 서명 토큰
        //   ERP가 검증 후 local_company / local_subscription 박제 시 사용
        public string? BootstrapToken { get; set; }
    }
}
