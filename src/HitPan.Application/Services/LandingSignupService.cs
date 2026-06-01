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

            _logger.LogInformation("[LandingSignup] submitted token={Token} email={Email} plan={Plan}",
                signupToken, request.Email, request.PlanType);

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

    private static string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
