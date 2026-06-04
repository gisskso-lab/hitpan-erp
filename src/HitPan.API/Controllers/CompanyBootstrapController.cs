using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.API.Controllers;

// ERP 첫 설치 자동 반영 — 헌법 #35 객체 완전 분리 (사장님 결재 2026-06-04, W2)
//
// 흐름:
//   1) ERP Web /setup/license Step 1 → 백오피스 API /api/landing/license/claim
//      → 응답에 bootstrapToken (HMAC-SHA256 서명, 10분 만료, subscription 클레임 포함)
//   2) ERP Web → 본 API POST /api/setup/bootstrap (bootstrapToken + 회사정보)
//   3) 본 API: 토큰 서명 검증 (백오피스 URL 호출 0건!) → local_company + local_subscription 박제
//   4) is_locked_from_landing=1 + bootstrap_at
//   5) ERP Web → 본 API POST /api/setup/create-parent (bootstrapToken 재검증 + 부모계정 생성)
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #18·#22 — 평문 사업자번호는 ERP 로컬 DB(고객사 PC)에만 박제. 본사는 해시만
//   #20 — 가입 → 결제 → 라이선스 → 설치 → 자동 반영 끊김 0
//   #29 — Bootstrap Token Key는 환경변수만, 응답·로그 0건
//   #35 — 객체 완전 분리. ERP는 백오피스 URL·존재 0건 의존. 공유 키만 보유.
[ApiController]
[Route("api/setup")]
[AllowAnonymous]
public class CompanyBootstrapController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<CompanyBootstrapController> _logger;

    public CompanyBootstrapController(IConfiguration config, ILogger<CompanyBootstrapController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.BootstrapToken) || string.IsNullOrWhiteSpace(req.BizNo))
            return BadRequest(new { success = false, message = "부트스트랩 토큰과 사업자번호가 필요합니다." });

        var bizNoNormalized = req.BizNo.Replace("-", "").Replace(" ", "").Trim();
        if (bizNoNormalized.Length != 10 || !bizNoNormalized.All(char.IsDigit))
            return BadRequest(new { success = false, message = "사업자번호 형식 오류 (10자리 숫자)" });

        var (ok, payload, error) = VerifyBootstrapToken(req.BootstrapToken);
        if (!ok || payload is null)
            return Unauthorized(new { success = false, message = error ?? "유효하지 않은 부트스트랩 토큰입니다." });

        var tenantId = payload.Sub;
        var tenantCode = payload.TenantCode;
        var companyName = payload.CompanyName;

        try
        {
            var cs = _config.GetConnectionString("DefaultConnection")
                     ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection 미설정");
            await using var db = new MySqlConnection(cs);
            await db.OpenAsync(ct);

            // 잠금 검사 — local_company.is_locked_from_landing=1 시 이미 설치 완료
            var lockedRaw = await db.QueryFirstOrDefaultAsync<int?>(
                "SELECT is_locked_from_landing FROM local_company WHERE tenant_id = @TenantId",
                new { TenantId = tenantId });
            if (lockedRaw == 1)
                return BadRequest(new { success = false, message = "이미 설치가 완료된 라이선스입니다. 회사정보 변경은 랜딩에서 사업자등록증 재등록이 필요합니다." });

            // local_company UPSERT — 회사정보 박제
            await db.ExecuteAsync(@"
                INSERT INTO local_company
                    (tenant_id, tenant_code, company_name, biz_no, ceo_name, tel, address, email,
                     biz_type, biz_item, zip_code, corp_no,
                     is_locked_from_landing, bootstrap_at, created_at, updated_at)
                VALUES
                    (@TenantId, @TenantCode, @CompanyName, @BizNo, @CeoName, @Tel, @Address, @Email,
                     @BizType, @BizItem, @ZipCode, @CorpNo,
                     1, NOW(6), NOW(6), NOW(6))
                ON DUPLICATE KEY UPDATE
                    tenant_code = @TenantCode,
                    company_name = @CompanyName,
                    biz_no = @BizNo,
                    ceo_name = @CeoName,
                    tel = @Tel,
                    address = @Address,
                    email = @Email,
                    biz_type = @BizType,
                    biz_item = @BizItem,
                    zip_code = @ZipCode,
                    corp_no = @CorpNo,
                    is_locked_from_landing = 1,
                    bootstrap_at = NOW(6),
                    updated_at = NOW(6)",
                new
                {
                    TenantId = tenantId,
                    TenantCode = tenantCode,
                    CompanyName = companyName,
                    BizNo = bizNoNormalized,
                    CeoName = req.CeoName ?? "",
                    Tel = req.Tel,
                    Address = req.Address,
                    Email = req.Email,
                    BizType = req.BizType,
                    BizItem = req.BizItem,
                    ZipCode = req.ZipCode,
                    CorpNo = req.CorpNo
                });

            // local_subscription UPSERT — 본사 영역 캐시 박제 (토큰 클레임에서 추출)
            var sub = payload.Subscription;
            if (sub is not null)
            {
                await db.ExecuteAsync(@"
                    INSERT INTO local_subscription
                        (tenant_id, subscription_tier, status, trial_ends_at,
                         ai_mode, ai_token_monthly_limit, ai_token_extra,
                         anthropic_api_key_last4, anthropic_key_status,
                         max_users, extra_device_slots,
                         reseller_id, reseller_tier,
                         last_sync_at, sync_source, created_at, updated_at)
                    VALUES
                        (@TenantId, @SubscriptionTier, @Status, @TrialEndsAt,
                         @AiMode, @AiTokenMonthlyLimit, @AiTokenExtra,
                         @AnthropicKeyLast4, @AnthropicKeyStatus,
                         @MaxUsers, @ExtraDeviceSlots,
                         @ResellerId, @ResellerTier,
                         NOW(6), 'bootstrap', NOW(6), NOW(6))
                    ON DUPLICATE KEY UPDATE
                        subscription_tier = @SubscriptionTier,
                        status = @Status,
                        trial_ends_at = @TrialEndsAt,
                        ai_mode = @AiMode,
                        ai_token_monthly_limit = @AiTokenMonthlyLimit,
                        ai_token_extra = @AiTokenExtra,
                        anthropic_api_key_last4 = @AnthropicKeyLast4,
                        anthropic_key_status = @AnthropicKeyStatus,
                        max_users = @MaxUsers,
                        extra_device_slots = @ExtraDeviceSlots,
                        reseller_id = @ResellerId,
                        reseller_tier = @ResellerTier,
                        last_sync_at = NOW(6),
                        sync_source = 'bootstrap',
                        updated_at = NOW(6)",
                    new
                    {
                        TenantId = tenantId,
                        sub.SubscriptionTier,
                        sub.Status,
                        sub.TrialEndsAt,
                        sub.AiMode,
                        sub.AiTokenMonthlyLimit,
                        sub.AiTokenExtra,
                        sub.AnthropicKeyLast4,
                        sub.AnthropicKeyStatus,
                        sub.MaxUsers,
                        sub.ExtraDeviceSlots,
                        sub.ResellerId,
                        sub.ResellerTier
                    });
            }

            _logger.LogInformation("[CompanyBootstrap] 자동 반영 완료 tenant={Code} biz_no={Mask}",
                tenantCode, MaskBizNo(bizNoNormalized));

            return Ok(new
            {
                success = true,
                message = "회사 정보가 ERP에 자동 반영되었습니다.",
                tenantCode,
                companyName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CompanyBootstrap] 자동 반영 실패");
            return StatusCode(500, new { success = false, message = "자동 반영 중 서버 오류가 발생했습니다." });
        }
    }

    // 부모계정 자동 생성 — W2 토큰 재검증
    [HttpPost("create-parent")]
    public async Task<IActionResult> CreateParent([FromBody] CreateParentRequest req, CancellationToken ct)
    {
        if (req is null
            || string.IsNullOrWhiteSpace(req.BootstrapToken)
            || string.IsNullOrWhiteSpace(req.Email)
            || string.IsNullOrWhiteSpace(req.Password)
            || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { success = false, message = "부트스트랩 토큰·이메일·비밀번호·이름 필수" });

        if (req.Password.Length < 8)
            return BadRequest(new { success = false, message = "비밀번호는 8자 이상이어야 합니다." });

        var (ok, payload, error) = VerifyBootstrapToken(req.BootstrapToken);
        if (!ok || payload is null)
            return Unauthorized(new { success = false, message = error ?? "유효하지 않은 부트스트랩 토큰입니다." });

        var tenantId = payload.Sub;
        var tenantCode = payload.TenantCode;

        try
        {
            var cs = _config.GetConnectionString("DefaultConnection")
                     ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection 미설정");
            await using var db = new MySqlConnection(cs);
            await db.OpenAsync(ct);

            // bootstrap 선행 검증
            var localCompany = await db.QueryFirstOrDefaultAsync<int?>(
                "SELECT is_locked_from_landing FROM local_company WHERE tenant_id = @TenantId",
                new { TenantId = tenantId });
            if (localCompany != 1)
                return BadRequest(new { success = false, message = "회사 정보 자동 반영(bootstrap)을 먼저 완료해주세요." });

            // tenant당 부모계정 1명만
            var existingParent = await db.QueryFirstOrDefaultAsync<int>(@"
                SELECT COUNT(*) FROM users
                WHERE tenant_id = @TenantId AND is_parent = 1 AND is_deleted = 0",
                new { TenantId = tenantId });
            if (existingParent > 0)
                return BadRequest(new { success = false, message = "이미 부모계정이 생성된 라이선스입니다." });

            // 이메일 중복 차단
            var dupEmail = await db.QueryFirstOrDefaultAsync<int>(@"
                SELECT COUNT(*) FROM users WHERE email = @Email AND is_deleted = 0",
                new { req.Email });
            if (dupEmail > 0)
                return BadRequest(new { success = false, message = "이미 사용 중인 이메일입니다." });

            var userId = Guid.NewGuid().ToString();
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);

            await db.ExecuteAsync(@"
                INSERT INTO users
                  (user_id, tenant_id, email, password_hash, user_name,
                   role, account_type, is_parent,
                   is_active, failed_login_count,
                   created_at, updated_at, is_deleted, emp_name)
                VALUES
                  (@UserId, @TenantId, @Email, @Hash, @Name,
                   'tenant_admin', 'tenant_admin', 1,
                   1, 0,
                   UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 0, @Name)",
                new
                {
                    UserId = userId,
                    TenantId = tenantId,
                    req.Email,
                    Hash = passwordHash,
                    req.Name
                });

            _logger.LogInformation("[CompanyBootstrap] 부모계정 생성 완료 tenant={Code} email={Email}",
                tenantCode, req.Email);

            return Ok(new
            {
                success = true,
                message = "부모 계정이 생성되었습니다. 로그인 화면으로 이동해주세요.",
                userId,
                email = req.Email,
                tenantCode
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CompanyBootstrap] 부모계정 생성 실패");
            return StatusCode(500, new { success = false, message = "부모계정 생성 중 서버 오류가 발생했습니다." });
        }
    }

    // 헌법 #35 W2 — HMAC-SHA256 서명 검증. 백오피스 URL·존재 의존 0건.
    //   - 동일 키(HITPAN_BOOTSTRAP_TOKEN_KEY)로 서명 검증
    //   - exp 만료 검사, aud 일치 검사
    //   - jti 1회용은 ERP 측 별도 캐시 필요 (현재 차수 미박제 — 재사용 가능, 짧은 만료로 위험 최소화)
    private (bool ok, TokenPayload? payload, string? error) VerifyBootstrapToken(string token)
    {
        var key = Environment.GetEnvironmentVariable("HITPAN_BOOTSTRAP_TOKEN_KEY")
                 ?? _config["Bootstrap:TokenKey"]
                 ?? "DEV-bootstrap-token-key-change-in-production-32+chars";

        var parts = token.Split('.');
        if (parts.Length != 2)
            return (false, null, "토큰 형식 오류");

        try
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]));
            var actual = Base64UrlDecode(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                return (false, null, "서명 불일치");

            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            var payload = JsonSerializer.Deserialize<TokenPayload>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null)
                return (false, null, "페이로드 파싱 실패");

            if (payload.Aud != "erp-bootstrap")
                return (false, null, "audience 불일치");

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (payload.Exp < now)
                return (false, null, "토큰 만료 (라이선스 검증을 다시 진행해주세요)");

            if (string.IsNullOrEmpty(payload.Sub))
                return (false, null, "tenant 식별 불가");

            return (true, payload, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CompanyBootstrap] 토큰 검증 예외");
            return (false, null, "토큰 검증 오류");
        }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var pad = s.Length % 4;
        if (pad > 0) s = s.PadRight(s.Length + (4 - pad), '=');
        return Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/'));
    }

    private static string MaskBizNo(string bn) =>
        bn.Length >= 6 ? bn.Substring(0, 3) + "**" + bn.Substring(5) : "***";

    public class BootstrapRequest
    {
        public string BootstrapToken { get; set; } = "";
        public string BizNo { get; set; } = "";
        public string? CeoName { get; set; }
        public string? Tel { get; set; }
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? BizType { get; set; }
        public string? BizItem { get; set; }
        public string? ZipCode { get; set; }
        public string? CorpNo { get; set; }
    }

    public class CreateParentRequest
    {
        public string BootstrapToken { get; set; } = "";
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class TokenPayload
    {
        public string Jti { get; set; } = "";
        public string Iss { get; set; } = "";
        public string Aud { get; set; } = "";
        public string Sub { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public long Iat { get; set; }
        public long Exp { get; set; }
        public SubscriptionClaim? Subscription { get; set; }
    }

    public class SubscriptionClaim
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
}
