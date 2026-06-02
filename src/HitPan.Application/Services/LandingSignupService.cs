using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Application.DTOs.Landing;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

// 랜딩 가입 처리 (사장님 결재 2026-06-01)
//
// 8명제 정합:
//  #1 — 랜딩 zero-DB passthrough: 평문 사업자정보 본사 백오피스 저장 0건, 메타만 박제
//  #2 — 본사 계정 발급 (signup_token 부여, 이메일·SMS 2채널 별도 발송)
//  #3 — biz_no_hash CHAR(64) HMAC-SHA256 + pepper (HSM 결재 후 환경변수에서만)
//
// 헌법 정합:
//  #4 — 금액은 decimal (이 서비스는 금액 미보유)
//  #15 — 빈 catch 금지, ILogger.LogError + 메시지 반환
//  #18·#22 — 평문 사업자정보 0건, 해시·시리얼·signup_token만
//  #25 — 쉽게·정확하게·안전하게
public class LandingSignupService : ILandingSignupService
{
    private readonly IUnitOfWork _uow;
    private readonly IConfiguration _config;
    private readonly ILogger<LandingSignupService> _logger;

    public LandingSignupService(IUnitOfWork uow, IConfiguration config, ILogger<LandingSignupService> logger)
    {
        _uow = uow;
        _config = config;
        _logger = logger;
    }

    public async Task<SignupResponse> SubmitAsync(SignupRequest request, CancellationToken ct = default)
    {
        if (request is null)
            return new SignupResponse { Success = false, Message = "요청 비어 있음" };

        if (!request.AgreeTerms || !request.AgreePrivacy)
            return new SignupResponse { Success = false, Message = "필수 약관 동의가 필요합니다" };

        if (string.IsNullOrWhiteSpace(request.CompanyName) || string.IsNullOrWhiteSpace(request.Email))
            return new SignupResponse { Success = false, Message = "회사명·이메일 필수" };

        var bizNoNormalized = (request.BizNo ?? "").Replace("-", "").Trim();
        if (bizNoNormalized.Length != 10)
            return new SignupResponse { Success = false, Message = "사업자번호 형식 오류" };

        // HMAC-SHA256 + pepper 해시 (8명제 #3·헌법 #22 정합)
        // pepper 미설정 시 = 개발 환경 fallback (운영 결재 시 HSM 결재 후 환경변수)
        var pepper = _config["Backoffice:BizNoPepper"] ?? "dev-pepper-2026";
        var bizNoHash = ComputeHmacSha256(bizNoNormalized, pepper);

        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        try
        {
            // 중복 가입 체크 (biz_no_hash UNIQUE)
            var existing = await db.QueryFirstOrDefaultAsync<long?>(
                "SELECT COUNT(*) FROM landing_signups WHERE biz_no_hash = @Hash",
                new { Hash = bizNoHash });

            if (existing.HasValue && existing.Value > 0)
            {
                _logger.LogInformation("[LandingSignup] duplicate biz_no_hash detected (email={Email})", request.Email);
                return new SignupResponse
                {
                    Success = false,
                    Message = "이미 가입된 사업자번호입니다. 계정 분실은 '계정 분실/문의' 메뉴로 이동해주세요."
                };
            }

            var signupToken = $"sgn-{Guid.NewGuid():N}";

            // 8명제 #1 — 평문 사업자정보 0건, 메타만 박제
            // signup_token = OTP·결제 단계로 이어지는 단발성 토큰 (30분 만료, 호출 측에서 관리)
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
                    request.CompanyName,
                    request.Email,
                    request.Phone,
                    request.PlanType,
                    request.ResellerCode
                });

            // 사장님 결재 박제 2026-06-02 (의중 B 모두결재) — 가입 즉시 백오피스 AdminTenants 노출
            // 헌법 #22 정합: biz_no=NULL (해시는 landing_signups에만), 결제 박제 완료 시 status='active' 박제
            // 헌법 #20 정합: 가입→백오피스 워크플로우 끊김 0건
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
                    request.CompanyName,
                    request.Phone
                });

            _logger.LogInformation("[LandingSignup] submitted token={Token} tenant={TenantCode} email={Email} plan={Plan}",
                signupToken, tenantCode, request.Email, request.PlanType);

            return new SignupResponse
            {
                Success = true,
                Message = "가입 신청이 접수되었습니다. 인증 단계로 이동합니다.",
                SignupToken = signupToken
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LandingSignup] submit failed email={Email}", request.Email);
            return new SignupResponse
            {
                Success = false,
                Message = "가입 처리 중 오류가 발생했습니다. 잠시 후 다시 시도해주세요."
            };
        }
    }

    // 사장님 결재 박제 2026-06-02 (의중 B 모두결재) — 결제 완료 시 tenants.status='pending' → 'active'
    // 헌법 #20 정합: 가입→백오피스→결제 워크플로우 끊김 0건
    // 헌법 #15 정합: 빈 catch 금지, ILogger 박제
    public async Task<SignupResponse> ConfirmPaymentAsync(string signupToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(signupToken))
            return new SignupResponse { Success = false, Message = "signup_token 누락" };

        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        try
        {
            var signup = await db.QueryFirstOrDefaultAsync<(string CompanyName, string Status)?>(
                @"SELECT company_name AS CompanyName, status AS Status
                  FROM landing_signups WHERE signup_token = @Token",
                new { Token = signupToken });

            if (signup is null)
                return new SignupResponse { Success = false, Message = "유효하지 않은 signup_token" };

            await db.ExecuteAsync(
                "UPDATE landing_signups SET status = 'paid' WHERE signup_token = @Token",
                new { Token = signupToken });

            // 사장님 결재 박제 2026-06-02 (의중 B 모두결재) — 결제 박제 시 라이선스 키 박제
            // 키 형식: HITP-XXXX-XXXX-XXXX-XXXX (16자 박제, Base32 박제)
            // 헌법 #22 정합: tenants에는 SHA256 해시만 박제, 평문은 응답으로만 1회 박제
            var licenseKey = GenerateLicenseKey();
            var pepper = _config["License:Pepper"] ?? "dev-pepper-2026";
            var licenseHash = ComputeHmacSha256(licenseKey, pepper);

            var affected = await db.ExecuteAsync(
                @"UPDATE tenants
                  SET status = 'active', license_key_hash = @Hash, updated_at = UTC_TIMESTAMP()
                  WHERE company_name = @CompanyName AND status = 'pending'",
                new { Hash = licenseHash, signup.Value.CompanyName });

            _logger.LogInformation("[LandingSignup] payment confirmed token={Token} tenants_updated={Cnt} license_issued=1",
                signupToken, affected);

            return new SignupResponse
            {
                Success = true,
                Message = "결제가 확인되었습니다.",
                SignupToken = signupToken,
                LicenseKey = licenseKey
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LandingSignup] confirm payment failed token={Token}", signupToken);
            return new SignupResponse
            {
                Success = false,
                Message = "결제 확인 중 오류가 발생했습니다."
            };
        }
    }

    private static string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // 사장님 결재 박제 2026-06-02 — 라이선스 키 박제 (HITP-XXXX-XXXX-XXXX-XXXX, Crockford Base32)
    // 헌법 #25 정합: 쉽게(외울 수 있는 형식) + 정확(HMAC 해시 검증) + 안전(평문 1회 응답만)
    private static string GenerateLicenseKey()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789"; // I·L·O·U·0·1 제외 (오인 박제 방지)
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

    private static async Task EnsureOpenAsync(IDbConnection db, CancellationToken ct)
    {
        if (db.State == ConnectionState.Open) return;
        if (db is DbConnection c)
            await c.OpenAsync(ct).ConfigureAwait(false);
        else
            db.Open();
    }
}
