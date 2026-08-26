using Dapper;
using HitPan.Infrastructure.Configuration;
using MySqlConnector;
using static HitPan.Domain.Common.OrgDefaults;
// 🔴 20260816작1 R-1 봉합 — 기기 슬롯 기준값의 **단일 정의**. 숫자를 여기 또 적지 않는다.
using HitPan.Application.Services;

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
        {
            // 🔴 CR2-1 봉합 — 돌아가기 **전에** 기준값을 깔아 준다.
            //
            //   여기로 오는 회사가 정확히 **R-1 이 덮으려던 집단**이다(이미 설치가 끝난 회사).
            //   R-1 1차 봉합은 시드를 CreateParentAsync 맨 끝에 뒀는데, 그 함수는
            //   ParentExists 로 **시드에 닿기 전에** 돌아간다 — 기존 회사는 영원히 못 받았다.
            //   ⇒ 조기 return 앞에 둔다. 멱등이므로(테넌트 단위 무접촉) 몇 번 와도 안전하다.
            await SeedDeviceSlotPolicyAsync(db, tenantId, tenantCode, ct);

            return (BootstrapOutcome.AlreadyLocked,
                "이미 설치가 완료된 라이선스입니다. 회사정보 변경은 랜딩에서 사업자등록증 재등록이 필요합니다.");
        }

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

        // 🔴 CR2-1 봉합 — 회사 행이 생긴 **이 시점**이 기준값을 깔 수 있는 가장 이른 자리다.
        //   여기서 깔면 뒤이은 부모계정 생성이 실패해도(아이디 중복 등) 기준값은 이미 있다.
        //   CreateParentAsync 에도 같은 호출이 남아 있다 — 멱등이라 두 번 불려도 행이 안 늘어난다(G-21).
        //   두 곳 다 두는 이유: 두 경로(웹 API · 오프라인 seed-parent)의 어느 쪽이 먼저 오든 덮인다.
        await SeedDeviceSlotPolicyAsync(db, tenantId, tenantCode, ct);

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
            // 🔴 작(2026-08-14) 사장님 지시: "부모계정 = 직급은 자동으로 대표.등록"
            //    종전엔 position 을 안 넣어 부모계정 직급이 NULL 이었다(실측 확인).
            //    ⇒ 사원관리·직원현황에서 대표의 직급이 빈칸이고, 직급으로 짜는
            //      결재선에서도 대표를 고를 수 없었다.
            //    ⚠️ 이름은 직급 마스터에 이미 있는 "대표이사"(CEO, sort_order 100)를 그대로 쓴다.
            //      새 이름("대표")을 만들면 "대표"와 "대표이사"가 둘 다 생겨 목록이 헷갈린다
            //      (사장님 결재 2026-08-14). 마스터 시드는 아래 stdPositions 에 있다.
            await db.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO employees
                  (employee_id, tenant_id, user_id, emp_no, emp_name,
                   position, emp_type, join_date, is_active, created_at, updated_at, role, email)
                VALUES
                  (@EmployeeId, @TenantId, @UserId, '0001', @Name,
                   @Position, 'regular', UTC_TIMESTAMP(6), 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 'tenant_admin', @Email)",
                new { EmployeeId = employeeId, TenantId = tenantId, UserId = userId, input.Name,
                      Position = OwnerPositionName, Email = loginId },
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

            // 표준 계정 시드 (12차 ACCOUNTS-SEED P0) — AutoJournalHelper 상수와 1:1. NOT EXISTS 로 재실행·마이그 보호.
            //
            // 🔴 20260827작4 (사장님 오더 "모든 돈의 흐름을 회계장부 하나로") — 8개 → 27개로 확장.
            //   수금·지급·경비·급여를 기표하려면 그 상대계정이 accounts 에 **먼저 있어야** 한다.
            //   journal_lines → accounts FK(fk_jl_account) 때문에, 없는 계정에 기표하면 FK 1452 로 죽는다.
            //
            //   ⚠️ 이 목록은 DB-111_chart_of_accounts_expand.sql 과 **반드시 같아야 한다.**
            //   두 경로(신규 프로비저닝 / 기존 테넌트 마이그)가 갈리면, 한쪽 경로로 만들어진
            //   고객만 특정 기표에서 FK 1452 로 죽는다 — 실제로 그 사고가 잠복해 있었다:
            //   종전 프로비저너는 8개인데 DB-32 는 6개(14600·16900 없음)라, 마이그 경로 테넌트는
            //   BOM 생산 확정이 죽는 상태였다. DB-111 이 그 둘도 같이 심어 일치시켰다.
            //
            // 🔴 현금(10100)은 **수기 입력** — 사장님 지시("현금은 수기로"). 자동 시재계산 없음.
            //   복식부기 차·대 짝을 맞추기 위한 그릇으로만 존재한다.
            var stdAccounts = new (string Code, string Name, string Type)[]
            {
                // 자산
                ("10100", "현금", "asset"),
                ("10300", "보통예금", "asset"),
                ("10800", "외상매출금", "asset"),
                ("14600", "원재료", "asset"),
                ("16900", "재공품", "asset"),
                ("17600", "부가세대급금", "asset"),
                // 부채
                ("23200", "외상매입금", "liability"),
                ("25300", "미지급금", "liability"),
                ("25400", "예수금", "liability"),
                ("25500", "부가세예수금", "liability"),
                // 수익
                ("40100", "상품매출", "revenue"),
                // 비용 — 매출원가
                ("50100", "상품매입", "expense"),
                // 비용 — 판매비와관리비
                ("80100", "급여", "expense"),
                ("81100", "복리후생비", "expense"),
                ("81200", "여비교통비", "expense"),
                ("81300", "접대비", "expense"),
                ("81400", "통신비", "expense"),
                ("81500", "수도광열비", "expense"),
                ("81700", "세금과공과", "expense"),
                ("81900", "감가상각비", "expense"),
                ("82100", "보험료", "expense"),
                ("82200", "차량유지비", "expense"),
                ("82500", "소모품비", "expense"),
                ("82600", "지급수수료", "expense"),
                ("82700", "광고선전비", "expense"),
                ("83100", "지급임차료", "expense"),
                ("84100", "잡비", "expense"),
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

        // 🔴 기기 슬롯 기준값 시드 (20260816작1 R-1 봉합) — 트랜잭션 **밖**이다. 아래 사유 참조.
        await SeedDeviceSlotPolicyAsync(db, tenantId, tenantCode, ct);

        _logger.LogInformation("[CompanyBootstrap] 부모계정+사원+기본창고 생성 완료 tenant={Code} loginId={LoginId}",
            tenantCode, loginId);
        return (CreateParentOutcome.Ok, null, userId);
    }

    /// <summary>
    /// 기기 슬롯 기준값(DB-104)을 이 회사에 깔아 준다 — <b>신규 설치 R-1 봉합</b> (20260816작1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>무엇이 고장나 있었나</b> — 검증팀이 격리 DB 에 출하 DDL 을 그대로 넣어 실측한 결과다.
    /// DB-104 시드는 <c>SELECT DISTINCT tenant_id FROM local_subscription</c> 을 기준으로 도는데
    /// <b>신규 설치 시점엔 그 표가 0행</b>이다(회사가 아직 없다). 그리고 출하 DDL 이
    /// <c>('DB-104','clean-ddl',1)</c> 로 <b>이미 성공 표시</b>를 해서, 나중에 회사가 생겨도
    /// <c>MigrationRunner</c> 가 <c>success=1</c> 이면 건너뛴다.
    /// ⇒ <b>신규 고객은 정책 표가 영원히 비어</b> 요금 한도가 언제나 코드 상수(안전망)로 결정됐다.
    /// 요금 한도를 설정으로 꺼낸다는 작업의 목적이 <b>신규 고객에게만 무력</b>이었다.
    /// </para>
    /// <para>
    /// ⚠️ <b>회귀가 아니라 미달성이다</b> — 안전망 숫자가 시드와 같으므로 <b>요금이 틀리지는 않았다.</b>
    /// 다만 대표가 화면에서 한도를 고쳐도 <b>반영될 표가 없었다.</b>
    /// </para>
    /// <para>
    /// 🔴 <b>왜 여기인가</b> — 바로 위 <c>labor_policy_settings</c>(DB-96) 시드가
    /// <b>똑같은 문제를 이미 이렇게 풀었다.</b> 새 구조를 만들지 않고 <b>선례를 그대로 따른다.</b>
    /// 회사가 만들어지는 이 시점에는 테넌트가 존재하므로 <c>CROSS JOIN</c> 문제가 없다.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>왜 트랜잭션 밖인가 — 시드가 회사 생성을 막으면 안 된다</b>
    /// </para>
    /// <para>
    /// 위 트랜잭션 안에 넣으면 이 표 하나가 잘못됐을 때
    /// <b>부모계정·사원·창고까지 통째로 되돌아가 설치 자체가 실패</b>한다.
    /// 그런데 이 표는 <b>없어도 회사가 돈다</b> — 안전망(<c>FallbackLimits</c>)이 같은 숫자를 준다.
    /// 있으면 좋은 것 때문에 <b>반드시 되어야 하는 것</b>을 무너뜨리는 것이 가장 나쁘다(작지서 §7).
    /// ⇒ 커밋 뒤에 따로, <b>실패해도 회사는 만들어지게</b>. 단 <b>기록은 반드시 남긴다</b>(헌법 #15).
    /// </para>
    /// <para>
    /// ⚠️ <b>이미 값을 손댄 회사는 건드리지 않는다</b>(헌법 #1 덮어쓰기 금지 · #11).
    /// <c>WHERE NOT EXISTS</c> 의 기준이 <b>열쇠가 아니라 테넌트</b>인 것이 핵심이다 —
    /// DB-104 의 의도를 그대로 옮겼다. 대표가 열쇠 하나만 고치고 나머지를 지웠더라도
    /// 우리가 <b>지운 것을 되살리지 않는다.</b> 그것도 대표의 설정이기 때문이다.
    /// 그래서 <b>두 번 돌려도 행이 안 늘어난다</b>(멱등 · G-21).
    /// </para>
    /// <para>
    /// 🔴 <b>숫자를 여기 적지 않는다</b> — <see cref="SlotPolicyDefaults"/> 가 유일한 정의다.
    /// 마이그 DB-104 와 <b>한 글자도 다르지 않아야</b> 하고, 그 일치는 게이트가 실제로 대조한다
    /// (<c>DeviceSlotGuardTests</c> G-20). 여기 숫자를 또 적으면 <b>나중에 반드시 갈라진다.</b>
    /// </para>
    /// </remarks>
    private async Task SeedDeviceSlotPolicyAsync(
        MySqlConnection db, string tenantId, string tenantCode, CancellationToken ct)
    {
        try
        {
            // 🔴 "손댔는가" 는 **맨 앞에서 한 번만** 묻는다.
            //
            //   ⚠️ 이 자리를 처음엔 틀렸고 **G-19 가 잡았다**(15줄을 기대했는데 1줄만 들어왔다).
            //     줄마다 `WHERE NOT EXISTS (… p.tenant_id = @TenantId)` 를 걸었더니,
            //     **첫 줄이 들어간 순간 그 줄이 스스로 조건을 깨뜨려** 2~15번째가 전부 건너뛰었다.
            //     조건의 기준이 **열쇠가 아니라 테넌트**라서 생기는 일이다.
            //
            //   ⇒ 판단을 루프 밖으로 뺀다. 이러면 두 요구가 동시에 선다:
            //     · 손 안 댄 회사     → 15줄이 **전부** 깔린다(G-19)
            //     · 이미 손댄 회사   → **한 줄도 안 건드린다**(G-22 · DB-104 의 의도 그대로)
            //     · 두 번 돌려도     → 두 번째엔 이미 있으므로 안 늘어난다(G-21 멱등)
            var alreadyHas = await db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM device_slot_policy_settings WHERE tenant_id = @TenantId",
                new { TenantId = tenantId }, cancellationToken: ct));

            var inserted = 0;

            if (alreadyHas == 0)
            {
                foreach (var row in SlotPolicyDefaults.Rows)
                {
                    // ⚠️ 줄 단위 NOT EXISTS 는 **열쇠 기준**이다 — 위 테넌트 판단과 역할이 다르다.
                    //   같은 열쇠가 두 벌이 되는 것(UNIQUE 충돌)만 막는 안전장치다.
                    inserted += await db.ExecuteAsync(new CommandDefinition(@"
                        INSERT INTO device_slot_policy_settings
                          (policy_id, tenant_id, policy_key, policy_value, value_unit, label, description)
                        SELECT @PolicyId, @TenantId, @Key, @Value, @Unit, @Label, @Description
                        WHERE NOT EXISTS (
                            SELECT 1 FROM device_slot_policy_settings p
                            WHERE p.tenant_id = @TenantId AND p.policy_key = @Key
                        )",
                        new
                        {
                            PolicyId = Guid.NewGuid().ToString(),
                            TenantId = tenantId,
                            row.Key,
                            row.Value,
                            Unit = row.Unit,
                            row.Label,
                            row.Description
                        },
                        cancellationToken: ct));
                }
            }

            if (inserted > 0)
            {
                _logger.LogInformation(
                    "[CompanyBootstrap] 기기 슬롯 기준값 {Count}건 시드 완료 tenant={Code}",
                    inserted, tenantCode);
            }
            else
            {
                // 이미 값이 있는 회사다 — 정상이다. 덮어쓰지 않은 것이 옳다.
                _logger.LogInformation(
                    "[CompanyBootstrap] 기기 슬롯 기준값이 이미 있어 건너뛴다(덮어쓰지 않는다) tenant={Code}",
                    tenantCode);
            }
        }
        catch (Exception ex)
        {
            // 🔴 헌법 #15 — 빈 catch 금지. 그리고 이 실패로 회사 생성을 막지 않는다(작지서 §7).
            //   표가 없어도 안전망이 같은 숫자를 주므로 **업무는 정상으로 돈다.**
            //   다만 "대표가 화면에서 한도를 고쳐도 반영될 표가 없는" 상태이므로
            //   조용히 지나가면 안 된다 — R-1 이 두 달 안 보였던 이유가 정확히 그것이다.
            _logger.LogError(ex,
                "[CompanyBootstrap] 🔴 기기 슬롯 기준값 시드 실패 — 회사 생성은 계속한다. "
                + "이 회사는 요금 한도가 설정표가 아니라 안전망(코드 기본값)으로 돈다. "
                + "숫자는 같으므로 요금이 틀리지는 않으나, 설정 화면에서 고쳐도 반영될 표가 없다. "
                + "tenant={Code}", tenantCode);
        }
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
