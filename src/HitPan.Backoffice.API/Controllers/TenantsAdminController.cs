using Dapper;
using HitPan.Backoffice.API.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 고객사 관리 API — 본사 어드민 (사장님 결재 2026-06-04, 헌법 #35)
//
// 권한: [BoPermission] DB 박제 정책 검사
//   - tenants.list
//   - tenants.detail
//   - tenants.suspend (owner/platform_owner 기본)
//   - tenants.activate
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #18·#22 — 본사 백오피스 DB(tenants 메타만, 평문 사업자번호는 ERP 로컬)
//   #20 — 가입 → 활성 → 정지/복구 끊김 0
[ApiController]
[Route("api/backoffice/tenants")]
[Authorize]
public class TenantsAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<TenantsAdminController> _logger;

    public TenantsAdminController(IConfiguration config, ILogger<TenantsAdminController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    [BoPermission("tenants.list")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var where = new List<string>();
            var param = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                where.Add("status = @Status");
                param.Add("Status", status);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add("(company_name LIKE @Q OR tenant_code LIKE @Q)");
                param.Add("Q", $"%{search}%");
            }
            var sql = @"
                SELECT
                    CAST(tenant_id AS CHAR) AS TenantId,
                    tenant_code AS TenantCode,
                    company_name AS CompanyName,
                    tel AS Tel,
                    status AS Status,
                    is_locked_from_landing AS IsLocked,
                    trial_ends_at AS TrialEndsAt,
                    bootstrap_at AS BootstrapAt,
                    created_at AS CreatedAt
                FROM tenants
                " + (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "") + @"
                ORDER BY created_at DESC
                LIMIT 500";

            var rows = await db.QueryAsync<TenantRow>(sql, param);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TenantsAdmin] 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpGet("{id}")]
    [BoPermission("tenants.detail")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.QueryFirstOrDefaultAsync<TenantDetailRow>(@"
                SELECT
                    CAST(t.tenant_id AS CHAR) AS TenantId,
                    t.tenant_code AS TenantCode,
                    t.company_name AS CompanyName,
                    t.tel AS Tel,
                    t.status AS Status,
                    t.is_locked_from_landing AS IsLocked,
                    t.trial_ends_at AS TrialEndsAt,
                    t.bootstrap_at AS BootstrapAt,
                    t.created_at AS CreatedAt,
                    t.updated_at AS UpdatedAt,
                    CAST(t.reseller_id AS CHAR) AS ResellerId,
                    r.reseller_name AS ResellerName,
                    r.reseller_code AS ResellerCode,
                    t.max_users AS MaxUsers,
                    LEFT(t.license_key_hash, 12) AS LicenseHashPrefix,
                    t.subscription_tier AS SubscriptionTier,
                    t.ai_mode AS AiMode,
                    t.ai_token_monthly_limit AS AiTokenMonthlyLimit
                FROM tenants t
                LEFT JOIN resellers r ON r.reseller_id = t.reseller_id
                WHERE t.tenant_id = @Id",
                new { Id = id });
            if (row is null)
                return NotFound(new { success = false, message = "고객사를 찾을 수 없습니다." });

            // 결제 이력 최근 10건 (메타만, 카드정보 0건)
            var payments = await db.QueryAsync<PaymentMetaRow>(@"
                SELECT
                    order_id AS OrderId,
                    amount AS Amount,
                    method AS Method,
                    status AS Status,
                    approved_at AS ApprovedAt,
                    created_at AS CreatedAt
                FROM tenant_payments
                WHERE signup_token IN (
                    SELECT signup_token FROM landing_signups WHERE company_name = @CompanyName
                )
                ORDER BY created_at DESC
                LIMIT 10",
                new { row.CompanyName });

            return Ok(new { success = true, item = row, payments });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TenantsAdmin] 상세 조회 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "상세 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("{id}/suspend")]
    [BoPermission("tenants.suspend")]
    public async Task<IActionResult> Suspend(string id, [FromBody] StatusChangeRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, message = "정지 사유를 입력해주세요." });

        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE tenants
                SET status = 'suspended', updated_at = UTC_TIMESTAMP()
                WHERE tenant_id = @Id AND status = 'active'",
                new { Id = id });
            if (affected == 0)
                return BadRequest(new { success = false, message = "활성 상태인 고객사만 정지할 수 있습니다." });

            _logger.LogInformation("[TenantsAdmin] suspended id={Id} reason={Reason}", id, req.Reason);
            return Ok(new { success = true, message = "고객사가 일시 정지되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TenantsAdmin] 정지 처리 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "정지 처리 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("{id}/activate")]
    [BoPermission("tenants.activate")]
    public async Task<IActionResult> Activate(string id, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE tenants
                SET status = 'active', updated_at = UTC_TIMESTAMP()
                WHERE tenant_id = @Id AND status IN ('suspended', 'pending')",
                new { Id = id });
            if (affected == 0)
                return BadRequest(new { success = false, message = "정지·대기 상태인 고객사만 복구할 수 있습니다." });

            _logger.LogInformation("[TenantsAdmin] activated id={Id}", id);
            return Ok(new { success = true, message = "고객사가 활성화 복구되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TenantsAdmin] 활성화 처리 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "활성화 처리 중 오류가 발생했습니다." });
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

    public record StatusChangeRequest(string Reason);

    public class TenantRow
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string? Tel { get; set; }
        public string Status { get; set; } = "";
        public int IsLocked { get; set; }
        public DateTime? TrialEndsAt { get; set; }
        public DateTime? BootstrapAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TenantDetailRow : TenantRow
    {
        public DateTime UpdatedAt { get; set; }
        public string? ResellerId { get; set; }
        public string? ResellerName { get; set; }
        public string? ResellerCode { get; set; }
        public int MaxUsers { get; set; }
        public string? LicenseHashPrefix { get; set; }
        public string? SubscriptionTier { get; set; }
        public string? AiMode { get; set; }
        public int AiTokenMonthlyLimit { get; set; }
    }

    public class PaymentMetaRow
    {
        public string OrderId { get; set; } = "";
        public long Amount { get; set; }
        public string? Method { get; set; }
        public string Status { get; set; } = "";
        public string? ApprovedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
