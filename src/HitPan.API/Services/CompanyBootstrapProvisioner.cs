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
        return $"Server={host};Port={port};Database={db};User={user};Password={pwd};DefaultCommandTimeout=90;";
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
                new { UserId = userId, TenantId = tenantId, Email = loginId, Hash = passwordHash, input.Name },
                transaction: tx, cancellationToken: ct));

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
