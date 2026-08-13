using Dapper;
using HitPan.Infrastructure.Configuration;
using MySqlConnector;

namespace HitPan.API.Services;

// 회사 부트스트랩 + 부모계정 생성 공유 로직 (작업지시서 20260707작1 ②단계, 사장님 승인 2026-07-07)
//
// 목적: create-parent(웹 API) 와 seed-parent(오프라인 서브커맨드)가 동일한 DB 트랜잭션 로직을 공유한다.
//   - 복붙 금지(작업지시서 명시). 트랜잭션 부수효과(users+employees+warehouses+accounts 8계정)를 한 곳에서만 관리해
//     한쪽만 고쳐 정합이 깨지는 사고를 원천 차단(헌법 #20 3흐름).
//
// 이 서비스는 증표 "검증"은 하지 않는다(SerialProofVerifier 책임). 호출자가 검증 통과 후 payload 를 넘긴다.
public sealed class CompanyBootstrapProvisioner
{
    private readonly IConfiguration _config;
    private readonly ILogger<CompanyBootstrapProvisioner> _logger;

    public CompanyBootstrapProvisioner(IConfiguration config, ILogger<CompanyBootstrapProvisioner> logger)
    {
        _config = config;
        _logger = logger;
    }

    // 연결 문자열: 웹 호스트면 ConnectionStrings:DefaultConnection, 없으면(설치 EXE·서브커맨드) db.conf 로 조립.
    //   AuditLogMiddleware.BuildConnectionStringFromEnv 와 동일 패턴 — 설치 PC 는 db.conf 단일 진실원.
    public string ResolveConnectionString()
    {
        var cs = _config.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(cs))
            return cs;

        var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
        var port = TenantConfigReader.Get("DB_PORT") ?? "3306";
        var db = TenantConfigReader.GetRequired("DB_NAME");
        var user = TenantConfigReader.GetRequired("DB_USER");
        var pwd = TenantConfigReader.GetRequired("DB_PASSWORD");
        // GuidFormat=None — char(36) 을 Guid 로 돌려주면 string DTO 매핑이 터진다 (봉합 2026-08-12, PI-07).
        return $"Server={host};Port={port};Database={db};User={user};Password={pwd};DefaultCommandTimeout=90;GuidFormat=None;";
    }

    public enum BootstrapOutcome { Ok, AlreadyLocked, Error }

    // local_company + local_subscription UPSERT — is_locked_from_landing=1.
    public async Task<(BootstrapOutcome outcome, string? message)> BootstrapCompanyAsync(
        ProofPayload payload, CompanyBootstrapInput input, CancellationToken ct)
    {
        var tenantId = payload.Sub;
        var tenantCode = payload.TenantCode;
        var companyName = payload.CompanyName;

        var bizNoNormalized = (input.BizNo ?? "").Replace("-", "").Replace(" ", "").Trim();
        if (bizNoNormalized.Length != 10 || !bizNoNormalized.All(char.IsDigit))
            return (BootstrapOutcome.Error, "사업자번호 형식 오류 (10자리 숫자)");

        await using var db = new MySqlConnection(ResolveConnectionString());
        await db.OpenAsync(ct);

        var lockedRaw = await db.QueryFirstOrDefaultAsync<int?>(
            "SELECT is_locked_from_landing FROM local_company WHERE tenant_id = @TenantId",
            new { TenantId = tenantId });
        if (lockedRaw == 1)
            return (BootstrapOutcome.AlreadyLocked,
                "이미 설치가 완료된 라이선스입니다. 회사정보 변경은 랜딩에서 사업자등록증 재등록이 필요합니다.");

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
                CeoName = input.CeoName ?? "",
                input.Tel,
                input.Address,
                input.Email,
                input.BizType,
                input.BizItem,
                input.ZipCode,
                input.CorpNo
            });

        // local_subscription UPSERT — 본사 영역 수신 캐시(reseller_id·reseller_tier 는 덩어리2, 헌법 #37 보존 대상).
        //   제거 금지: 백오피스→ERP 단방향 수신 캐시(영업 출처 메타데이터). ERP→본사 전송 아님(수신 방향).
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
                     NOW(6), @SyncSource, NOW(6), NOW(6))
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
                    sync_source = @SyncSource,
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
                    sub.ResellerTier,
                    SyncSource = input.SyncSource
                });
        }

        _logger.LogInformation("[CompanyBootstrap] 자동 반영 완료 tenant={Code} source={Source}",
            tenantCode, input.SyncSource);
        return (BootstrapOutcome.Ok, null);
    }

    public enum CreateParentOutcome { Ok, BootstrapMissing, ParentExists, DuplicateLogin, Error }

    // 부모계정(+사원+기본창고+표준계정) 로컬 트랜잭션 생성.
    //   ⚠️ replay 방어: local_company.is_locked_from_landing=1 이 곧 replay 방어다(한 번 잠기면 재부트스트랩 차단).
    //     추가로 existingParent>0 가드로 tenant 당 부모계정 1명만 — 캡처된 유효 증표 재사용도 여기서 막힌다.
    //     오프라인 공개키 검증은 위조는 막아도 replay 는 못 막으므로(서버 nonce 소각 불가) 이 두 게이트가 방어선.
    public async Task<(CreateParentOutcome outcome, string? message, string? userId)> CreateParentAsync(
        ProofPayload payload, CreateParentInput input, CancellationToken ct)
    {
        var tenantId = payload.Sub;
        var tenantCode = payload.TenantCode;

        var loginId = (input.LoginId ?? "").Trim();
        if (loginId.Length < 4 || loginId.Contains(' '))
            return (CreateParentOutcome.Error, "아이디는 공백 없이 4자 이상이어야 합니다.", null);
        if (string.IsNullOrWhiteSpace(input.Password) || input.Password.Length < 8)
            return (CreateParentOutcome.Error, "비밀번호는 8자 이상이어야 합니다.", null);
        if (string.IsNullOrWhiteSpace(input.Name))
            return (CreateParentOutcome.Error, "이름은 필수입니다.", null);

        await using var db = new MySqlConnection(ResolveConnectionString());
        await db.OpenAsync(ct);

        // bootstrap 선행 검증 (is_locked_from_landing=1 = replay 방어 게이트)
        var localCompany = await db.QueryFirstOrDefaultAsync<int?>(
            "SELECT is_locked_from_landing FROM local_company WHERE tenant_id = @TenantId",
            new { TenantId = tenantId });
        if (localCompany != 1)
            return (CreateParentOutcome.BootstrapMissing, "회사 정보 자동 반영(bootstrap)을 먼저 완료해주세요.", null);

        // tenant당 부모계정 1명만 (replay·중복 방어)
        var existingParent = await db.QueryFirstOrDefaultAsync<int>(@"
            SELECT COUNT(*) FROM users
            WHERE tenant_id = @TenantId AND is_parent = 1 AND is_deleted = 0",
            new { TenantId = tenantId });
        if (existingParent > 0)
            return (CreateParentOutcome.ParentExists, "이미 부모계정이 생성된 라이선스입니다.", null);

        var dupEmail = await db.QueryFirstOrDefaultAsync<int>(@"
            SELECT COUNT(*) FROM users WHERE email = @Email AND is_deleted = 0",
            new { Email = loginId });
        if (dupEmail > 0)
            return (CreateParentOutcome.DuplicateLogin, "이미 사용 중인 아이디입니다.", null);

        var userId = Guid.NewGuid().ToString();
        var employeeId = Guid.NewGuid().ToString();
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(input.Password);

        // 원자성: users+employees+warehouses+accounts 를 한 트랜잭션으로 (A-P0-1-REGRESSION·10차·12차 봉합 정합).
        await using var tx = await db.BeginTransactionAsync(ct);
        try
        {
            // W1-2 (작업지시서 20260707작2) — 저장 어휘 단일화:
            //   users.role = 'TenantAdmin' (UserRole enum 멤버명, UserService.cs:112 role.ToString()과 동일 사전).
            //     종전 'tenant_admin'(언더스코어)은 UserConfiguration HasConversion<string>() 역변환 폭발
            //     = 로그인 500 진범. (읽기측 관용 컨버터 W1-1과 한 세트 — 쓰기도 정본 어휘로 통일.)
            //   users.account_type = 'tenant_admin' 그대로 유지 — JWT claim·Authorization 정책(TenantOnly 등)이
            //     snake_case를 사용하므로 여기는 손대면 안 된다(다른 사전).
            await db.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO users
                  (user_id, tenant_id, email, password_hash, user_name,
                   role, account_type, is_parent,
                   is_active, failed_login_count,
                   created_at, updated_at, is_deleted, emp_name)
                VALUES
                  (@UserId, @TenantId, @Email, @Hash, @Name,
                   'TenantAdmin', 'tenant_admin', 1,
                   1, 0,
                   UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 0, @Name)",
                new { UserId = userId, TenantId = tenantId, Email = loginId, Hash = passwordHash, input.Name },
                transaction: tx, cancellationToken: ct));

            // employees.role = 'tenant_admin' 유지 (W1-2 구분 사유): employees.role은 EF enum 매핑이 아니라
            //   문자열 컬럼이며, 로그인 시 employeeRole claim(ClaimTypes.Role)으로 그대로 실려 Authorization
            //   정책(snake_case 어휘)과 짝을 이룬다 — users.role(enum 사전)과 다른 사전이므로 바꾸면 안 된다.
            await db.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO employees
                  (employee_id, tenant_id, user_id, emp_no, emp_name,
                   emp_type, join_date, is_active, created_at, updated_at, role, email)
                VALUES
                  (@EmployeeId, @TenantId, @UserId, '0001', @Name,
                   'regular', UTC_TIMESTAMP(6), 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 'tenant_admin', @Email)",
                new { EmployeeId = employeeId, TenantId = tenantId, UserId = userId, input.Name, Email = loginId },
                transaction: tx, cancellationToken: ct));

            // 기본 창고 1행 (10차 P0-4) — NOT EXISTS 로 마이그 고객 보호.
            await db.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO warehouses
                  (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at)
                SELECT @WarehouseId, @TenantId, 'MAIN', '기본창고', 'normal', 1, NOW(6), NOW(6)
                WHERE NOT EXISTS (
                    SELECT 1 FROM warehouses WHERE tenant_id = @TenantId
                )",
                new { WarehouseId = Guid.NewGuid().ToString(), TenantId = tenantId },
                transaction: tx, cancellationToken: ct));

            // 기본 직급 6개 시드 (작 2026-08-13, 그룹웨어 단계4 토대) — 창고와 같은 패턴.
            //
            // 🔴 왜 여기인가: DB-22 가 직급을 시드했으나 `tenant_id='tenant-001'` 하드코딩이라
            //    실제 고객에게 안 갔고(실측: positions 0행), 그 주석이 말한
            //    "가입 프로비저닝에서 동일 시드" 는 구현되지 않았다. 그래서
            //    ①사원 등록의 직급이 자유 텍스트로 남았고 ②12명 중 8명이 직급 없음이 됐다.
            //    DB-93 이 기존 고객을 메우지만, 신규 설치는 사원이 생기기 전에 마이그가 돌아
            //    거기서 안 걸린다 — 신규 고객사는 여기서 깔아야 한다.
            //
            // ⚠️ 출발점이지 정답이 아니다. 회사마다 직급 체계가 다르므로 관리자가
            //    설정 → 직급 관리에서 고치고 지운다(헌법 #11 — 우리가 템플릿을 주지 않는다).
            var stdPositions = new (string Code, string Name, int Sort)[]
            {
                ("CEO", "대표이사", 100),
                ("DIRECTOR", "부장", 80),
                ("DEPUTY", "차장", 70),
                ("MANAGER", "과장", 60),
                ("ASSISTANT_MANAGER", "대리", 50),
                ("STAFF", "사원", 10),
            };
            foreach (var (code, name, sort) in stdPositions)
            {
                await db.ExecuteAsync(new CommandDefinition(@"
                    INSERT INTO positions
                      (position_id, tenant_id, code, name, sort_order, is_active)
                    SELECT @PositionId, @TenantId, @Code, @Name, @Sort, 1
                    WHERE NOT EXISTS (
                        SELECT 1 FROM positions WHERE tenant_id = @TenantId AND code = @Code
                    )",
                    new
                    {
                        PositionId = Guid.NewGuid().ToString(),
                        TenantId = tenantId,
                        Code = code,
                        Name = name,
                        Sort = sort
                    },
                    transaction: tx, cancellationToken: ct));
            }

            // ─────────────────────────────────────────────────────────────
            // 노무 기준값 시드 (작 2026-08-13, 단계6 실측 P0)
            // ─────────────────────────────────────────────────────────────
            //
            // 🔴 실측으로 잡은 결함이다. DB-96·DB-98 의 시드는
            //      SELECT DISTINCT tenant_id FROM employees
            //    로 회사를 고른다. 그런데 **신규 설치는 이 시점에 직원이 0명**이라
            //    어느 회사에도 안 깔린다. 직급(DB-93)이 같은 이유로 안 깔렸던 것과 같은 자리다.
            //
            //    그대로 뒀으면 신규 고객사에서:
            //      · 연차 계산이 15일이 아니라 **0일** 로 나오고(기준값이 없으면 0 — 폴백을 안 두므로)
            //      · 휴직은 "기준이 정해져 있지 않습니다" 만 뜬다
            //    둘 다 화면은 열리는데 값이 안 나오는, 고객이 열어봐야 아는 종류다.
            //
            // ⚠️ 여기 숫자는 **2026-08 시점의 법정 최소**다. 법이 바뀌면 관리자가
            //    설정에서 **새 시행일로 행을 추가**한다(기존 행을 고치지 않는다 — 과거분이 틀어진다).
            //    마이그 파일과 이 목록이 갈라지면 안 된다(게이트: AbsenceGuardTests).
            var stdPolicies = new (string Key, decimal Value, string Unit, string From,
                string Label, bool Statutory)[]
            {
                // 연차 (DB-96)
                ("annual_leave_base_days", 15.0m, "day", "2018-05-29", "기본 연차 일수", true),
                ("annual_leave_extra_per_years", 1.0m, "day", "2018-05-29", "가산 연차 일수", true),
                ("annual_leave_extra_cycle_years", 2.0m, "count", "2018-05-29", "가산 주기(년)", true),
                ("annual_leave_extra_start_years", 3.0m, "count", "2018-05-29", "가산 시작 근속(년)", true),
                ("annual_leave_max_days", 25.0m, "day", "2018-05-29", "연차 상한(일)", true),
                ("monthly_leave_days_under_1y", 1.0m, "day", "2018-05-29", "1년 미만 월차(일)", true),
                ("small_business_threshold", 5.0m, "count", "2018-05-29", "소규모 사업장 기준(명)", true),
                ("short_time_weekly_hours", 15.0m, "hour", "2018-05-29", "단시간 근로 기준(주 시간)", true),

                // 휴직 (DB-98) — 육아휴직은 2025-02-23 에 12→18개월로 바뀌었다.
                ("childcare_leave_max_months", 18.0m, "count", "2025-02-23", "육아휴직 최대 기간(개월)", true),
                ("childcare_leave_split_count", 3.0m, "count", "2025-02-23", "육아휴직 분할 횟수", true),
                ("maternity_leave_days", 90.0m, "day", "2018-05-29", "출산전후휴가(일)", true),
                ("maternity_leave_days_multiple", 120.0m, "day", "2018-05-29", "출산전후휴가 다태아(일)", true),
                ("family_care_leave_max_days", 90.0m, "day", "2020-01-01", "가족돌봄휴직 최대(일)", true),
                ("family_care_leave_min_split_days", 30.0m, "day", "2020-01-01", "가족돌봄휴직 분할 최소(일)", true),
                ("sick_leave_max_months", 6.0m, "count", "2018-05-29", "질병휴직 최대(개월)", false),
                ("personal_leave_max_months", 12.0m, "count", "2018-05-29", "개인사정 휴직 최대(개월)", false),
            };
            foreach (var (key, value, unit, from, label, statutory) in stdPolicies)
            {
                await db.ExecuteAsync(new CommandDefinition(@"
                    INSERT INTO labor_policy_settings
                      (policy_id, tenant_id, policy_key, policy_value, value_unit,
                       effective_from, label, is_statutory)
                    SELECT @PolicyId, @TenantId, @Key, @Value, @Unit, @From, @Label, @Statutory
                    WHERE NOT EXISTS (
                        SELECT 1 FROM labor_policy_settings
                        WHERE tenant_id = @TenantId AND policy_key = @Key AND effective_from = @From
                    )",
                    new
                    {
                        PolicyId = Guid.NewGuid().ToString(),
                        TenantId = tenantId,
                        Key = key,
                        Value = value,
                        Unit = unit,
                        From = from,
                        Label = label,
                        Statutory = statutory ? 1 : 0
                    },
                    transaction: tx, cancellationToken: ct));
            }

            // 표준 8계정 시드 (12차 ACCOUNTS-SEED P0) — AutoJournalHelper 상수와 1:1. NOT EXISTS 로 재실행·마이그 보호.
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

        _logger.LogInformation("[CompanyBootstrap] 부모계정+사원+기본창고 생성 완료 tenant={Code} loginId={LoginId}",
            tenantCode, loginId);
        return (CreateParentOutcome.Ok, null, userId);
    }
}

// bootstrap 입력(회사정보). 웹/서브커맨드 공통.
public sealed class CompanyBootstrapInput
{
    public string BizNo { get; set; } = "";
    public string? CeoName { get; set; }
    public string? Tel { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? BizType { get; set; }
    public string? BizItem { get; set; }
    public string? ZipCode { get; set; }
    public string? CorpNo { get; set; }
    // sync_source: 웹 API='bootstrap', 오프라인 서브커맨드='seed-parent'.
    public string SyncSource { get; set; } = "bootstrap";
}

// 부모계정 입력. LoginId 는 users.email 컬럼을 아이디로 재사용(이메일 형식 강제 안 함 — 헌법 #40).
public sealed class CreateParentInput
{
    public string LoginId { get; set; } = "";
    public string Password { get; set; } = "";
    public string Name { get; set; } = "";
}
