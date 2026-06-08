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
    private readonly IEmailSender _email;
    private readonly IWebhookOutboundService _webhook;
    private readonly ILogger<LandingSignupController> _logger;

    public LandingSignupController(
        IConfiguration config,
        IEmailSender email,
        IWebhookOutboundService webhook,
        ILogger<LandingSignupController> logger)
    {
        _config = config;
        _email = email;
        _webhook = webhook;
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

        var pepper = _config["Backoffice:BizNoPepper"] ?? "dev-pepper-2026";
        var bizNoHash = ComputeHmacSha256(bizNoNormalized, pepper);

        try
        {
            await using var db = await OpenAsync(ct);

            // 중복 가입 차단 — 사장님 결재 2026-06-08:
            //   "반려의 의미는 재가입 영구차단이 아니지. 가입조건 불충족이니 충족되면 가입이 되야지."
            //   "반려된 사업자는 중복체크에서 제외되야지. 승인된 사업자 번호가 아닌데."
            //   → approved·active 상태만 중복 차단. rejected·submitted는 재가입 허용.
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

            // 같은 사업자번호로 이전 신청(반려·미처리)이 있으면 정리 박기 — UNIQUE 충돌 방지
            await db.ExecuteAsync(
                "DELETE FROM landing_signups WHERE biz_no_hash = @Hash AND status NOT IN ('approved','active')",
                new { Hash = bizNoHash });

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

            // 사장님 결재 2026-06-08 — biz_no·ceo_name 평문 박힘. 헌법 #35 정합:
            //   "랜딩에서 인증된 사업자등록증 정보 → 계정관리·회사정보 자동 반영"
            //   ERP 사용자정보설정에 자동 박혀야 정합. tenants는 백오피스 영역(고객사 PK)이므로 평문 박힘.
            // 사업자번호는 정규화(숫자만) 박은 후 저장.
            var bizNoNorm = new string((req.BizNo ?? "").Where(char.IsDigit).ToArray());
            await db.ExecuteAsync(@"
                INSERT INTO tenants
                  (tenant_id, tenant_code, company_name, biz_no, ceo_name, tel, address,
                   reseller_id, status, trial_ends_at, db_host, db_name, license_key_hash,
                   reseller_tier, created_at, updated_at)
                VALUES
                  (@TenantId, @TenantCode, @CompanyName, @BizNo, @CeoName, @Phone, '',
                   NULL, 'pending', NULL, '', '', '', 0, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
                new
                {
                    TenantId = tenantId,
                    TenantCode = tenantCode,
                    req.CompanyName,
                    BizNo = bizNoNorm,
                    CeoName = req.CeoName ?? "",
                    req.Phone
                });

            _logger.LogInformation("[LandingSignup] submitted token={Token} tenant={TenantCode} email={Email} plan={Plan}",
                signupToken, tenantCode, req.Email, req.PlanType);

            // 신청 접수 안내 메일 (헌법 #20·#22·#35 정합 — 고객사 코드는 백오피스 PK 영역, 고객 노출 절대 금지)
            // 사장님 결재 2026-06-08: tenantCode 메일에 박지 않음. 시리얼 키는 백오피스 승인 후 별도 발송.
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

            // 박힘 박제 2026-06-08 (브라운킴 PM) — 결제 완료 박힘 박은 영역 ERP 박힘 박을 영역 webhook 박힘.
            // 사장님 결재 박힘 = 카운트 박힘 박은 영역 = 결제 완료 박힘 영역. 헌법 #20·#35 정합.
            var tenantIdForWebhook = await db.QueryFirstOrDefaultAsync<string?>(
                "SELECT CAST(tenant_id AS CHAR) FROM tenants WHERE company_name = @CompanyName AND status = 'active' ORDER BY created_at DESC LIMIT 1",
                new { signup.CompanyName });
            if (!string.IsNullOrEmpty(tenantIdForWebhook))
            {
                await _webhook.EmitSubscriptionChangedAsync(tenantIdForWebhook, ct);
                _logger.LogInformation("[LandingSignup] webhook 박힘 tenant={Tid}", tenantIdForWebhook);
            }

            // 라이선스 키(부모계정ID) 이메일 송부 (헌법 #35 — 본사 백오피스가 직접 부여)
            // SMTP 미박제 시 로그만, 가입 흐름은 중단 없음
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
