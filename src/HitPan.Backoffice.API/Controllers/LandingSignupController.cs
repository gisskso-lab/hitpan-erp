using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 랜딩 가입 API — 헌법 #35 정합 이식 (사장님 결재 2026-06-04)
//
// 이식 배경:
//   - 기존 HitPan.API(ERP)에 박제돼 있던 /api/landing/* 일체를 백오피스 API로 이식
//   - 헌법 #35: 랜딩(가입) → 백오피스(워크스페이스) 흐름. ERP는 고객사 업무 전용
//   - 옵션 B(Dapper 직접) — Application/Infrastructure 의존성 0
//
// 헌법 정합:
//   #18·#22 — 평문 사업자번호 0건 DB 박제, HMAC-SHA256 해시만
//   #15 — 빈 catch 금지, ILogger 박제
//   #20 — 가입 → 백오피스 워크플로우 끊김 0건 (tenants 즉시 INSERT)
//   #25 — 쉽게·정확하게·안전하게
[ApiController]
[Route("api/landing")]
public class LandingSignupController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<LandingSignupController> _logger;

    public LandingSignupController(IConfiguration config, ILogger<LandingSignupController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost("signup")]
    [AllowAnonymous]
    public async Task<IActionResult> Signup([FromBody] SignupRequest req, CancellationToken ct)
    {
        if (req is null)
            return BadRequest(new { success = false, message = "요청 비어 있음" });
        if (!req.AgreeTerms || !req.AgreePrivacy)
            return BadRequest(new { success = false, message = "필수 약관 동의가 필요합니다" });
        if (string.IsNullOrWhiteSpace(req.CompanyName) || string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { success = false, message = "회사명·이메일 필수" });

        var bizNoNormalized = (req.BizNo ?? "").Replace("-", "").Trim();
        if (bizNoNormalized.Length != 10)
            return BadRequest(new { success = false, message = "사업자번호 형식 오류 (10자리)" });

        var pepper = _config["Backoffice:BizNoPepper"] ?? "dev-pepper-2026";
        var bizNoHash = ComputeHmacSha256(bizNoNormalized, pepper);

        try
        {
            await using var db = await OpenAsync(ct);

            // 중복 가입 차단 (biz_no_hash UNIQUE)
            var existing = await db.QueryFirstOrDefaultAsync<long?>(
                "SELECT COUNT(*) FROM landing_signups WHERE biz_no_hash = @Hash",
                new { Hash = bizNoHash });

            if (existing.HasValue && existing.Value > 0)
            {
                _logger.LogInformation("[LandingSignup] duplicate biz_no_hash email={Email}", req.Email);
                return BadRequest(new
                {
                    success = false,
                    message = "이미 가입된 사업자번호입니다. 계정 분실은 '계정 분실/문의' 메뉴로 이동해주세요."
                });
            }

            var signupToken = $"sgn-{Guid.NewGuid():N}";

            await db.ExecuteAsync(@"
                INSERT INTO landing_signups
                  (signup_token, biz_no_hash, company_name, email, phone, plan_type, reseller_code,
                   agree_terms, agree_privacy, status, submitted_at)
                VALUES
                  (@SignupToken, @BizNoHash, @CompanyName, @Email, @Phone, @PlanType, @ResellerCode,
                   1, 1, 'submitted', UTC_TIMESTAMP())",
                new
                {
                    SignupToken = signupToken,
                    BizNoHash = bizNoHash,
                    req.CompanyName,
                    req.Email,
                    req.Phone,
                    req.PlanType,
                    req.ResellerCode
                });

            // 헌법 #20·#22 정합 — 가입 즉시 tenants에 메타 박제 (업무 데이터 0, status=pending)
            var tenantId = Guid.NewGuid().ToString();
            var codeSeq = await db.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) + 1 FROM tenants");
            var tenantCode = $"T-{codeSeq:D3}";

            await db.ExecuteAsync(@"
                INSERT INTO tenants
                  (tenant_id, tenant_code, company_name, biz_no, ceo_name, tel, address,
                   reseller_id, status, trial_ends_at, db_host, db_name, license_key_hash,
                   reseller_tier, created_at, updated_at)
                VALUES
                  (@TenantId, @TenantCode, @CompanyName, NULL, NULL, @Phone, NULL,
                   NULL, 'pending', NULL, '', '', '', 0, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
                new
                {
                    TenantId = tenantId,
                    TenantCode = tenantCode,
                    req.CompanyName,
                    req.Phone
                });

            _logger.LogInformation("[LandingSignup] submitted token={Token} tenant={TenantCode} email={Email} plan={Plan}",
                signupToken, tenantCode, req.Email, req.PlanType);

            return Ok(new
            {
                success = true,
                message = "가입 신청이 접수되었습니다. 인증 단계로 이동합니다.",
                signupToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LandingSignup] submit failed email={Email}", req.Email);
            return StatusCode(500, new
            {
                success = false,
                message = "가입 처리 중 오류가 발생했습니다. 잠시 후 다시 시도해주세요."
            });
        }
    }

    [HttpPost("confirm-payment")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmPayment([FromBody] ConfirmPaymentRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.SignupToken))
            return BadRequest(new { success = false, message = "signup_token 누락" });

        try
        {
            await using var db = await OpenAsync(ct);

            var signup = await db.QueryFirstOrDefaultAsync<SignupRow>(
                @"SELECT company_name AS CompanyName, status AS Status
                  FROM landing_signups WHERE signup_token = @Token",
                new { Token = req.SignupToken });

            if (signup is null)
                return BadRequest(new { success = false, message = "유효하지 않은 signup_token" });

            await db.ExecuteAsync(
                "UPDATE landing_signups SET status = 'paid' WHERE signup_token = @Token",
                new { Token = req.SignupToken });

            // 라이선스 키 박제 (HITP-XXXX-XXXX-XXXX-XXXX, Crockford Base32)
            // 헌법 #22 — tenants에는 HMAC 해시만, 평문은 응답 1회 (헌법 정합)
            var licenseKey = GenerateLicenseKey();
            var licensePepper = _config["License:Pepper"] ?? "dev-pepper-2026";
            var licenseHash = ComputeHmacSha256(licenseKey, licensePepper);

            var affected = await db.ExecuteAsync(
                @"UPDATE tenants
                  SET status = 'active', license_key_hash = @Hash, updated_at = UTC_TIMESTAMP()
                  WHERE company_name = @CompanyName AND status = 'pending'",
                new { Hash = licenseHash, signup.CompanyName });

            _logger.LogInformation("[LandingSignup] payment confirmed token={Token} tenants_updated={Cnt} license_issued=1",
                req.SignupToken, affected);

            return Ok(new
            {
                success = true,
                message = "결제가 확인되었습니다.",
                licenseKey
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LandingSignup] confirm payment failed token={Token}", req.SignupToken);
            return StatusCode(500, new { success = false, message = "결제 확인 중 오류가 발생했습니다." });
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

    private static string GenerateLicenseKey()
    {
        // I·L·O·U·0·1 제외 (오인 방지) — Crockford Base32 변형
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

    public class SignupRequest
    {
        [Required] public string CompanyName { get; set; } = "";
        [Required, RegularExpression(@"^\d{3}-?\d{2}-?\d{5}$")] public string BizNo { get; set; } = "";
        [Required] public string CeoName { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Phone { get; set; } = "";
        [Required] public string PlanType { get; set; } = "basic";
        public string? ResellerCode { get; set; }
        [Required] public bool AgreeTerms { get; set; }
        [Required] public bool AgreePrivacy { get; set; }
    }

    public record ConfirmPaymentRequest(string SignupToken);

    private class SignupRow
    {
        public string CompanyName { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
