using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using HitPan.Infrastructure.Configuration;
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
//   3) 본 API: 토큰 서명 검증 (백오피스 URL 호출 0건!) → local_company + local_subscription 저장
//   4) is_locked_from_landing=1 + bootstrap_at
//   5) ERP Web → 본 API POST /api/setup/create-parent (bootstrapToken 재검증 + 부모계정 생성)
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #18·#22 — 평문 사업자번호는 ERP 로컬 DB(고객사 PC)에만 저장. 본사는 해시만
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

            // local_company UPSERT — 회사정보 저장
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

            // local_subscription UPSERT — 본사 영역 캐시 저장 (토큰 클레임에서 추출)
            //
            // ⚠️ 제거 금지 가드 (2026-06-22, 사장님 결재): 아래 reseller_id·reseller_tier 는
            //   "ERP 잔재"·"고아 컬럼"·"죽은 코드"가 아니다. 백오피스→ERP 단방향 수신 캐시로,
            //   대리점 영업정보(어느 대리점이 이 고객사를 영업했는지)를 부트스트랩 토큰/웹훅으로
            //   받아 로컬에 보관한다. ERP→본사 전송이 아니므로 헌법 #18·#22 위반이 아니며(수신 방향),
            //   헌법 #35(랜딩→백오피스→ERP 유기적 연결) 정상 흐름이다.
            //   ERP 안에서 이 값을 SELECT/소비하는 코드가 현재 0건인 것은 "미사용 잔재"가 아니라
            //   "수신은 하되 소비 기능은 추후 구현" 상태(자리 선점)다. 전수조사(클로드·사람)가 이를
            //   오류·누락·죽은코드로 오판하고 제거/복구하지 말 것. 제거하려면 DDL·DTO·백오피스 송신
            //   계약까지 3시스템 동시 수술 + 사장님 결재 필요. ERP 계층은 부모/자식 2개뿐이라는 격벽과
            //   별개 개념(reseller_id 는 계정 계층이 아니라 영업 출처 메타데이터).
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

            // 봉합 (2026-06-21, 7차 전수조사 A-P0-1-REGRESSION P0, 검증팀장·설계팀장 교차검증):
            //   결재·경비는 employee_id 체계(approval_doc_lines.approver_id·approval_documents.requester_id·
            //   expenses.employee_id)로 동작하는데, 종전엔 부모계정을 users 에만 만들고 employees 행을 안 만들어
            //   로그인 토큰의 employee_id 클레임이 빈 문자열이 됐다(AuthService 가 employees WHERE user_id 로 조회).
            //   → 모든 고객사의 첫 사용자인 부모계정이 결재·경비 6개 API 에서 403(헌법 #20·#35 부모계정=ERP 진입점).
            //   부모계정 생성과 동일 흐름에서 user_id 로 연결된 employees 행을 함께 만든다(UserService.CreateAsync 패턴).
            //   emp_no='0001'(첫 사원), role='tenant_admin'(부모 권한). 단일 회사라 tenant 내 emp_no 충돌 없음.
            var employeeId = Guid.NewGuid().ToString();
            // 원자성: user 만 생기고 employee 가 누락되면 다시 A-P0-1-REGRESSION(빈 employee_id) 상태가 되고,
            //   재실행 가드(existingParent>0)에 막혀 영영 사원을 못 만든다. 두 INSERT 를 한 트랜잭션으로 원자화한다.
            await using var tx = await db.BeginTransactionAsync(ct);
            try
            {
                await db.ExecuteAsync(new CommandDefinition(@"
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
                    }, transaction: tx, cancellationToken: ct));

                await db.ExecuteAsync(new CommandDefinition(@"
                    INSERT INTO employees
                      (employee_id, tenant_id, user_id, emp_no, emp_name,
                       emp_type, join_date, is_active, created_at, updated_at, role, email)
                    VALUES
                      (@EmployeeId, @TenantId, @UserId, '0001', @Name,
                       'regular', UTC_TIMESTAMP(6), 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 'tenant_admin', @Email)",
                    new
                    {
                        EmployeeId = employeeId,
                        TenantId = tenantId,
                        UserId = userId,
                        req.Name,
                        req.Email
                    }, transaction: tx, cancellationToken: ct));

                // 봉합 (2026-06-22, 10차 P0-4):
                //   신규 가입 고객은 clean DDL 에 warehouses 시드가 0건이라 창고가 하나도 없다.
                //   → 첫 판매(SalesService '등록된 창고가 없습니다')·BOM 저장/생산(BomService '활성 창고가 없습니다')이
                //     하드 throw 로 차단되고, 매입은 실재하지 않는 'MAIN' 유령 warehouse_id 로 재고를 기록해 정합이 깨졌다.
                //   부모계정 생성과 동일 트랜잭션에서 기본 창고 1행(wh_code='MAIN')을 함께 만들어
                //   가입 직후부터 매입·판매·BOM 3흐름(헌법 #20)이 끊김 없이 돌게 한다.
                //   재실행 가드(existingParent>0)에 막혀 이 흐름은 tenant 당 1회만 실행되므로 중복 위험 없으나,
                //   기존 창고가 이미 있는 고객(마이그 등) 보호를 위해 NOT EXISTS 로 한 번 더 방어한다.
                //   wh_type 은 NOT NULL — 판매·BOM 의 기본창고 선택이 wh_code 기준이므로 'normal' 로 표준화.
                await db.ExecuteAsync(new CommandDefinition(@"
                    INSERT INTO warehouses
                      (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at)
                    SELECT @WarehouseId, @TenantId, 'MAIN', '기본창고', 'normal', 1, NOW(6), NOW(6)
                    WHERE NOT EXISTS (
                        SELECT 1 FROM warehouses WHERE tenant_id = @TenantId
                    )",
                    new
                    {
                        WarehouseId = Guid.NewGuid().ToString(),
                        TenantId = tenantId
                    }, transaction: tx, cancellationToken: ct));

                // 봉합 (2026-06-22, 12차 2단 교차검증 ACCOUNTS-SEED P0):
                //   journal_lines.fk_jl_account (tenant_id, account_code) → accounts FK 가 있는데,
                //   clean DDL 에 accounts 표준계정 시드가 0건이라 신규 가입 고객 PC 의 accounts 가 빈 테이블이다.
                //   → 매출·매입 거래명세서 확정 기표(AutoJournalHelper)와 BOM 생산/해체 기표가 참조하는
                //     표준계정(40100/25500/10800/50100/17600/23200/14600/16900)이 없어 FK 1452 → 확정 전체 롤백.
                //   "확정했는데 회계 안 잡힘/생산 안 됨"= 헌법 #20 흐름 끊김. 창고 시드(10차 P0-4)와 동일하게
                //   부모계정 생성 트랜잭션에서 AutoJournalHelper 가 쓰는 표준계정을 함께 시드해 가입 직후부터
                //   매출·매입·BOM 회계 3흐름이 끊김 없이 돈다. NOT EXISTS 로 재실행·마이그 고객 보호
                //   (마이그 SeedAccountsForMigration 은 레거시 KCODE 만 시드하므로 표준 8계정은 본 시드가 채운다.
                //    마이그 화면은 로그인 후=부모계정 생성 후라 본 시드가 항상 선행). 정의는 AutoJournalHelper 상수와 1:1.
                //   헌법 #36 주: accounts 는 tenant_id 가 PK 라 가입(부모계정 생성) 전엔 행이 존재할 수 없어
                //   clean DDL 정적 시드가 불가능하다(warehouses·employees 와 동일 — clean DDL 시드 0건).
                //   따라서 본 런타임 부트스트랩 시드가 신규설치 단일 진실원이며 별도 DDL 편입 대상이 아니다.
                var stdAccounts = new (string Code, string Name, string Type)[]
                {
                    ("10800", "외상매출금", "asset"),
                    ("17600", "부가세대급금", "asset"),
                    ("14600", "원재료", "asset"),
                    ("16900", "재공품", "asset"),
                    ("23200", "외상매입금", "liability"),
                    ("25500", "부가세예수금", "liability"),
                    ("40100", "상품매출", "revenue"),
                    ("50100", "상품매입", "expense"),
                };
                foreach (var (code, name, type) in stdAccounts)
                {
                    await db.ExecuteAsync(new CommandDefinition(@"
                        INSERT INTO accounts
                          (account_code, tenant_id, account_name, account_type, is_active, sort_order, created_at)
                        SELECT @Code, @TenantId, @Name, @Type, 1, 0, NOW(6)
                        WHERE NOT EXISTS (
                            SELECT 1 FROM accounts WHERE tenant_id = @TenantId AND account_code = @Code
                        )",
                        new { Code = code, TenantId = tenantId, Name = name, Type = type },
                        transaction: tx, cancellationToken: ct));
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            _logger.LogInformation("[CompanyBootstrap] 부모계정+사원+기본창고 생성 완료 tenant={Code} email={Email}",
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
    //   - jti 1회용은 ERP 측 별도 캐시 필요 (현재 차수 미저장 — 재사용 가능, 짧은 만료로 위험 최소화)
    private (bool ok, TokenPayload? payload, string? error) VerifyBootstrapToken(string token)
    {
        // 봉합 2026-06-17 1.2.12 — TenantConfigReader 정합
        var key = TenantConfigReader.Get("HITPAN_BOOTSTRAP_TOKEN_KEY")
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
