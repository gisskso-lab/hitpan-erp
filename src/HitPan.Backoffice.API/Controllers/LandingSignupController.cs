using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 랜딩 가입 API — 헌법 #35 정합 이식 (사장님 결재 2026-06-04)
//
// 이식 배경:
//   - 기존 HitPan.API(ERP)에 저장돼 있던 /api/landing/* 일체를 백오피스 API로 이식
//   - 헌법 #35: 랜딩(가입) → 백오피스(워크스페이스) 흐름. ERP는 고객사 업무 전용
//   - 옵션 B(Dapper 직접) — Application/Infrastructure 의존성 0
//
// 헌법 정합:
//   #18·#22 — 평문 사업자번호 0건 DB 저장, HMAC-SHA256 해시만
//   #15 — 빈 catch 금지, ILogger 저장
//   #20 — 가입 → 백오피스 워크플로우 끊김 0건 (tenants 즉시 INSERT)
//   #25 — 쉽게·정확하게·안전하게
[ApiController]
[Route("api/landing")]
public class LandingSignupController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IEmailSender _email;
    private readonly IWebhookOutboundService _webhook;
    private readonly IDomainAliasService _domainAlias;
    private readonly ICloudflareDomainService _cfDomain;
    private readonly ILogger<LandingSignupController> _logger;

    public LandingSignupController(
        IConfiguration config,
        IEmailSender email,
        IWebhookOutboundService webhook,
        IDomainAliasService domainAlias,
        ICloudflareDomainService cfDomain,
        ILogger<LandingSignupController> logger)
    {
        _config = config;
        _email = email;
        _webhook = webhook;
        _domainAlias = domainAlias;
        _cfDomain = cfDomain;
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

        // 숫자만 추출 (하이픈·공백·점 등 모두 제거)
        var bizNoNormalized = new string((req.BizNo ?? "").Where(char.IsDigit).ToArray());
        if (bizNoNormalized.Length != 10)
            return BadRequest(new { success = false, message = "사업자번호는 숫자 10자리여야 합니다." });

        // 사장님 결재 2026-06-09 — 도메인 별칭 가입 시점 재검증 (race condition 차단)
        var desiredDomain = (req.DesiredDomain ?? "").Trim().ToLowerInvariant();
        var domainCheck = await _domainAlias.ValidateAsync(desiredDomain, ct);
        if (!domainCheck.Available)
        {
            return BadRequest(new
            {
                success = false,
                code = domainCheck.Code,
                message = domainCheck.Message,
                suggestions = domainCheck.Suggestions
            });
        }

        // 국세청 진위확인 (헌법 #25 정합 — 정확하게)
        // 환경변수 우선 (운영), 그 다음 appsettings (개발) — appsettings 빈 문자열 차단
        var ntsKey = Environment.GetEnvironmentVariable("NTS_API_KEY");
        if (string.IsNullOrWhiteSpace(ntsKey))
        {
            var cfgKey = _config["BizVerify:NtsApiKey"];
            if (!string.IsNullOrWhiteSpace(cfgKey)) ntsKey = cfgKey;
        }
        if (!string.IsNullOrWhiteSpace(ntsKey))
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var msg = new HttpRequestMessage(HttpMethod.Post,
                    $"https://api.odcloud.kr/api/nts-businessman/v1/status?serviceKey={Uri.EscapeDataString(ntsKey)}");
                msg.Content = new StringContent($"{{\"b_no\":[\"{bizNoNormalized}\"]}}",
                    Encoding.UTF8, "application/json");
                using var res = await http.SendAsync(msg, ct);
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[LandingSignup] nts api fail status={Status}", (int)res.StatusCode);
                    return BadRequest(new { success = false, message = "국세청 서비스 일시 장애입니다. 잠시 후 다시 시도해주세요." });
                }
                var body = await res.Content.ReadAsStringAsync(ct);
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.GetArrayLength() == 0)
                    return BadRequest(new { success = false, message = "국세청 응답 확인 실패. 잠시 후 다시 시도해주세요." });
                var item = arr[0];
                var bSttCd = item.TryGetProperty("b_stt_cd", out var e) ? e.GetString() ?? "" : "";
                if (bSttCd != "01")
                {
                    var msg2 = bSttCd switch
                    {
                        "02" => "휴업 상태 사업자입니다. 정상 사업자만 가입 가능합니다.",
                        "03" => "폐업 상태 사업자입니다. 정상 사업자만 가입 가능합니다.",
                        _ => "국세청에 등록되지 않은 사업자번호입니다."
                    };
                    return BadRequest(new { success = false, message = msg2 });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LandingSignup] nts api 호출 실패");
                return BadRequest(new { success = false, message = "국세청 서비스 일시 장애입니다. 잠시 후 다시 시도해주세요." });
            }
        }

        var pepper = _config["Backoffice:BizNoPepper"] ?? throw new InvalidOperationException("Backoffice:BizNoPepper 미설정");
        var bizNoHash = ComputeHmacSha256(bizNoNormalized, pepper);

        try
        {
            await using var db = await OpenAsync(ct);

            // 중복 가입 차단 — 사장님 결재 2026-06-08:
            //   "반려의 의미는 재가입 영구차단이 아니지. 가입조건 불충족이니 충족되면 가입이 되야지."
            //   "반려된 사업자는 중복체크에서 제외되야지. 승인된 사업자 번호가 아닌데."
            //   → approved·active 상태만 중복 차단. rejected는 재가입 허용.
            var existing = await db.QueryFirstOrDefaultAsync<long?>(
                "SELECT COUNT(*) FROM landing_signups WHERE biz_no_hash = @Hash AND status IN ('approved','active')",
                new { Hash = bizNoHash });

            if (existing.HasValue && existing.Value > 0)
            {
                _logger.LogInformation("[LandingSignup] duplicate active biz_no_hash email={Email}", req.Email);
                return BadRequest(new
                {
                    success = false,
                    message = "이미 활성 상태로 가입된 사업자번호입니다. 계정 분실은 '계정 분실/문의' 메뉴로 이동해주세요."
                });
            }

            // 봉합 v1.2.4 (2026-06-11): 5분 안 중복 저장 차단
            //   사장님 의중: "백오피스에 시리얼 요청 1건인데 왜 여러 건?"
            //   진범: 가입 폼 영역 중복 클릭 또는 race condition 영역
            //   봉합: 같은 사업자번호 + 5분 안 submitted 영역 있으면 차단
            var recentSubmitted = await db.QueryFirstOrDefaultAsync<long?>(@"
                SELECT COUNT(*) FROM landing_signups
                WHERE biz_no_hash = @Hash
                  AND status = 'submitted'
                  AND submitted_at > DATE_SUB(UTC_TIMESTAMP(), INTERVAL 5 MINUTE)",
                new { Hash = bizNoHash });
            if (recentSubmitted.HasValue && recentSubmitted.Value > 0)
            {
                _logger.LogWarning("[LandingSignup] duplicate within 5min biz_no_hash email={Email}", req.Email);
                return BadRequest(new
                {
                    success = false,
                    message = "이미 가입 신청이 진행 중입니다. 잠시 후 다시 시도해주세요."
                });
            }

            // 같은 사업자번호로 이전 신청(반려·미처리)이 있으면 정리 저장하기 — UNIQUE 충돌 방지
            await db.ExecuteAsync(
                "DELETE FROM landing_signups WHERE biz_no_hash = @Hash AND status NOT IN ('approved','active')",
                new { Hash = bizNoHash });

            // 봉합 v1.2.5 (2026-06-11): 옛 영역 도메인 별칭 영역 Cloudflare DNS 정리
            //  사장님 의중: "삭제된 테넌트 코드에 서브도매인 주소와 DNS가 정리되지 않으면 안되.
            //               추후 중복되서 오류날수 있으니"
            //  진범: 옛 가입 영역에 저장된 DNS 영역 있으면 신규 가입 영역 저장할 때 81053 사고
            //  봉합: 가입 영역 저장된 도메인 별칭 영역 + DB tenants 영역 0건이면 옛 DNS 영역 자동 정리
            if (!string.IsNullOrWhiteSpace(desiredDomain) && _cfDomain.IsConfigured)
            {
                var existingTenant = await db.QueryFirstOrDefaultAsync<long?>(
                    "SELECT COUNT(*) FROM tenants WHERE domain_alias = @Alias",
                    new { Alias = desiredDomain });
                if (!existingTenant.HasValue || existingTenant.Value == 0)
                {
                    try
                    {
                        var revoked = await _cfDomain.RevokeByDomainAsync(desiredDomain, ct);
                        if (revoked)
                            _logger.LogInformation("[LandingSignup] orphan DNS 정리 sub={Sub}", desiredDomain);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[LandingSignup] orphan DNS 정리 사고 sub={Sub}", desiredDomain);
                    }
                }
            }

            var signupToken = $"sgn-{Guid.NewGuid():N}";

            await db.ExecuteAsync(@"
                INSERT INTO landing_signups
                  (signup_token, biz_no_hash, company_name, email, phone, plan_type, desired_domain, reseller_code,
                   agree_terms, agree_privacy, status, submitted_at)
                VALUES
                  (@SignupToken, @BizNoHash, @CompanyName, @Email, @Phone, @PlanType, @DesiredDomain, @ResellerCode,
                   1, 1, 'submitted', UTC_TIMESTAMP())",
                new
                {
                    SignupToken = signupToken,
                    BizNoHash = bizNoHash,
                    req.CompanyName,
                    req.Email,
                    req.Phone,
                    req.PlanType,
                    DesiredDomain = desiredDomain,
                    req.ResellerCode
                });

            // 헌법 #20·#22 정합 — 가입 즉시 tenants에 메타 저장 (업무 데이터 0, status=pending)
            var tenantId = Guid.NewGuid().ToString();

            // 🔴 P0 봉합 (2026-08-02, DB 매니저 적발 — 삭제 기능의 선행 조건):
            //   종전: SELECT COUNT(*) + 1 FROM tenants  → T-{seq:D3}
            //   이건 시퀀스가 아니라 '현재 행 수'다. uq_tenant_code(00_backoffice_core.sql:136)는 UNIQUE 다.
            //
            //   왜 지금 고치나 — 삭제 기능과 공존이 불가능하기 때문이다:
            //     T-001~T-005 가 있는 상태에서 T-003 을 지우면 COUNT=4 → 다음 가입이 T-005 를 시도
            //     → 이미 존재 → uq_tenant_code 위반 → INSERT 실패 → catch → HTTP 500.
            //     그런데 landing_signups INSERT(:206)는 이 위에서 '이미 끝난' 뒤이고 트랜잭션도 없다.
            //     ⇒ 회사(tenants)는 안 만들어졌는데 신청서만 남는다. 그리고 그 다음 가입도 계속 실패한다.
            //        가입 흐름이 영구히 막힌다(헌법 #20 — 워크플로우는 절대 안 끊긴다).
            //
            //   봉합: 실제 발급된 최대 번호 + 1. 행을 지워도 번호가 되돌아가지 않는다(단조증가).
            //     SUBSTRING(tenant_code, 3) = 'T-' 접두사 제거. 형식이 다른 값은 CAST 가 0 이 되어 무해.
            //     COALESCE — 테이블이 비면 MAX 가 NULL 이므로 0 으로 떨어뜨려 첫 코드가 T-001 이 되게 한다.
            var maxSeq = await db.QueryFirstOrDefaultAsync<int>(
                @"SELECT COALESCE(MAX(CAST(SUBSTRING(tenant_code, 3) AS UNSIGNED)), 0)
                  FROM tenants
                  WHERE tenant_code LIKE 'T-%'");
            var tenantCode = $"T-{maxSeq + 1:D3}";

            // 데이터 흐름도 정정 (사장님 결재 2026-06-18 "길 B — 백오피스 평문 0건"):
            //   [이전] biz_no·ceo_name 평문을 tenants에 저장(2026-06-08 결재) → 헌법 #22 위반으로 회수.
            //   [현재] 백오피스는 사업자번호 평문을 1바이트도 보유·전달하지 않는다.
            //     - 사업자번호 검증은 landing_signups.biz_no_hash(HMAC) 매칭으로만 수행 (위 INSERT).
            //     - 사업자등록증 정보(사업자번호·대표자명)는 ERP 설치 시 고객사 로컬 local_company에만 저장.
            //       (ERP /setup/license Step1에서 사용자가 입력한 biz_no를 그대로 ERP가 보관 → 길 B)
            //   백오피스 tenants 보유 = 계정·연락처·구독 메타만 (헌법 #22 본사 데이터 최소주의 정합).
            //   개인정보보호법 리스크 차단: 본사가 사업자번호·대표자명을 안 가지면 본사가 털릴 일 없다.
            await db.ExecuteAsync(@"
                INSERT INTO tenants
                  (tenant_id, tenant_code, domain_alias, company_name, tel,
                   reseller_id, status, trial_ends_at, db_host, db_name, license_key_hash,
                   reseller_tier, created_at, updated_at)
                VALUES
                  (@TenantId, @TenantCode, @DomainAlias, @CompanyName, @Phone,
                   NULL, 'pending', NULL, '', '', '', 0, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
                new
                {
                    TenantId = tenantId,
                    TenantCode = tenantCode,
                    DomainAlias = desiredDomain,
                    req.CompanyName,
                    req.Phone
                });

            _logger.LogInformation("[LandingSignup] submitted token={Token} tenant={TenantCode} email={Email} plan={Plan}",
                signupToken, tenantCode, req.Email, req.PlanType);

            // 신청 접수 안내 메일 (헌법 #20·#22·#35 정합 — 고객사 코드는 백오피스 PK 영역, 고객 노출 절대 금지)
            // 사장님 결재 2026-06-08: tenantCode 메일에 저장하지 않음. 시리얼 키는 백오피스 승인 후 별도 발송.
            _ = _email.SendAsync(req.Email,
                "[히트판] 가입 신청 접수 완료",
                BuildSignupReceivedHtml(req.CompanyName),
                ct);

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
                @"SELECT company_name AS CompanyName, status AS Status, email AS Email
                  FROM landing_signups WHERE signup_token = @Token",
                new { Token = req.SignupToken });

            if (signup is null)
                return BadRequest(new { success = false, message = "유효하지 않은 signup_token" });

            await db.ExecuteAsync(
                "UPDATE landing_signups SET status = 'paid' WHERE signup_token = @Token",
                new { Token = req.SignupToken });

            // 라이선스 키 저장 (HITP-XXXX-XXXX-XXXX-XXXX, Crockford Base32)
            // 헌법 #22 — tenants에는 HMAC 해시만, 평문은 응답 1회 (헌법 정합)
            var licenseKey = GenerateLicenseKey();
            var licensePepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
            var licenseHash = ComputeHmacSha256(licenseKey, licensePepper);

            var affected = await db.ExecuteAsync(
                @"UPDATE tenants
                  SET status = 'active', license_key_hash = @Hash, updated_at = UTC_TIMESTAMP()
                  WHERE company_name = @CompanyName AND status = 'pending'",
                new { Hash = licenseHash, signup.CompanyName });

            _logger.LogInformation("[LandingSignup] payment confirmed token={Token} tenants_updated={Cnt} license_issued=1",
                req.SignupToken, affected);

            // 저장 완료 2026-06-08 (브라운킴 PM) — 결제 완료 저장 저장한 영역 ERP 저장할 영역 webhook 저장.
            // 사장님 결재 저장 = 카운트 저장 저장한 영역 = 결제 완료 저장 영역. 헌법 #20·#35 정합.
            var tenantIdForWebhook = await db.QueryFirstOrDefaultAsync<string?>(
                "SELECT CAST(tenant_id AS CHAR) FROM tenants WHERE company_name = @CompanyName AND status = 'active' ORDER BY created_at DESC LIMIT 1",
                new { signup.CompanyName });
            if (!string.IsNullOrEmpty(tenantIdForWebhook))
            {
                await _webhook.EmitSubscriptionChangedAsync(tenantIdForWebhook, ct);
                _logger.LogInformation("[LandingSignup] webhook 저장 tenant={Tid}", tenantIdForWebhook);
            }

            // 라이선스 키(부모계정ID) 이메일 송부 (헌법 #35 — 본사 백오피스가 직접 부여)
            // SMTP 미저장 시 로그만, 가입 흐름은 중단 없음
            if (!string.IsNullOrWhiteSpace(signup.Email))
            {
                _ = _email.SendAsync(signup.Email,
                    "[히트판] 가입 완료 — 부모 계정ID(라이선스 키)",
                    BuildLicenseHtml(signup.CompanyName, licenseKey),
                    ct);
            }

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
        // 사용자 편의: 하이픈·공백·점 등 구분자 허용. 컨트롤러에서 숫자 10자리로 정규화 후 길이 검증.
        [Required] public string BizNo { get; set; } = "";
        [Required] public string CeoName { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Phone { get; set; } = "";
        [Required] public string PlanType { get; set; } = "basic";

        // 사장님 결재 2026-06-09 — 고객 입력 ERP 주소 별칭. 헌법 #22 정합 (테넌트 코드 노출 금지).
        // 형식·중복·예약어 검증은 DomainAliasController와 동일 로직, 가입 시 재검증.
        [Required] public string DesiredDomain { get; set; } = "";

        public string? ResellerCode { get; set; }
        [Required] public bool AgreeTerms { get; set; }
        [Required] public bool AgreePrivacy { get; set; }
    }

    public record ConfirmPaymentRequest(string SignupToken);

    private class SignupRow
    {
        public string CompanyName { get; set; } = "";
        public string Status { get; set; } = "";
        public string Email { get; set; } = "";
    }

    // 사장님 결재 2026-06-08 — 고객사 코드(tenantCode)는 백오피스 PK 영역, 고객 메일 노출 절대 금지.
    // 베타1 정합 — 결제 안내 제거. 본사 검토 후 시리얼 키 이메일 자동 발송.
    private static string BuildSignupReceivedHtml(string companyName) => $@"
<div style='font-family:-apple-system,BlinkMacSystemFont,Pretendard,Segoe UI,Roboto,sans-serif;max-width:560px;margin:0 auto;padding:32px;color:#1A2B4A;'>
  <h2 style='color:#0F6E56;margin:0 0 16px;'>가입 신청이 접수되었습니다</h2>
  <p>안녕하세요, <b>{System.Net.WebUtility.HtmlEncode(companyName)}</b> 담당자님.</p>
  <p>히트판 ERP 가입 신청이 정상 접수되었습니다.</p>
  <div style='background:#F0FAF6;border:1px solid #C7E9D9;border-radius:12px;padding:16px;margin:20px 0;'>
    <p style='margin:0;color:#0F6E56;font-size:14px;line-height:1.7;'>
      본사에서 사업자등록증과 입력 정보를 검토한 뒤, 본 이메일 주소로 <b>시리얼 키</b>를 즉시 발송해 드립니다.<br/>
      보통 영업시간 내 1시간 이내 처리됩니다.
    </p>
  </div>
  <p style='margin-top:24px;color:#6B7280;font-size:13px;'>이 메일은 발신 전용입니다. 문의는 support@hitpan.kr 로 부탁드립니다.</p>
</div>";

    private static string BuildLicenseHtml(string companyName, string licenseKey) => $@"
<div style='font-family:-apple-system,BlinkMacSystemFont,Pretendard,Segoe UI,Roboto,sans-serif;max-width:560px;margin:0 auto;padding:32px;color:#1A2B4A;'>
  <h2 style='color:#0F6E56;margin:0 0 16px;'>가입이 완료되었습니다</h2>
  <p>안녕하세요, <b>{System.Net.WebUtility.HtmlEncode(companyName)}</b> 담당자님.</p>
  <p>결제가 확인되어 히트판 ERP 부모 계정ID(라이선스 키)가 발급되었습니다.</p>
  <div style='background:#0F6E56;color:#fff;border-radius:12px;padding:20px;margin:20px 0;text-align:center;'>
    <div style='font-size:13px;opacity:0.85;margin-bottom:6px;'>부모 계정ID (라이선스 키)</div>
    <div style='font-size:22px;font-weight:700;letter-spacing:2px;font-family:Consolas,Monaco,monospace;'>{licenseKey}</div>
  </div>
  <p><b>중요:</b> 이 키는 <u>한 번만 발급</u>되며, 분실 시 본인확인 후 재발급됩니다. 안전한 곳에 보관해주세요.</p>
  <p>다음 단계에서 히트판 ERP 설치 프로그램을 다운로드하시고, 설치 중 이 키를 입력하시면 자동으로 회사 정보가 반영됩니다.</p>
  <p style='margin-top:24px;color:#6B7280;font-size:13px;'>이 메일은 발신 전용입니다. 문의는 support@hitpan.kr 로 부탁드립니다.</p>
</div>";
}
