using Dapper;
using HitPan.Backoffice.API.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 협력업체(resellers) 관리 API — 본사 어드민 (사장님 결재 2026-06-04, 헌법 #35)
//
// 권한: [BoPermission] DB 박제
//   - resellers.list
//   - resellers.detail
//
// 신청 검토/승인은 ResellerApplicationsAdminController(별도). 본 컨트롤러는 활성 협력업체 관리만.
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #18·#22 — 본사 백오피스 DB(resellers·reseller_accounts 메타)
[ApiController]
[Route("api/backoffice/resellers")]
[Authorize]
public class ResellersAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<ResellersAdminController> _logger;

    public ResellersAdminController(IConfiguration config, ILogger<ResellersAdminController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    [BoPermission("resellers.list")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var where = new List<string>();
            var param = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                where.Add("r.status = @Status");
                param.Add("Status", status);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add("(r.reseller_name LIKE @Q OR r.reseller_code LIKE @Q)");
                param.Add("Q", $"%{search}%");
            }
            var sql = @"
                SELECT
                    CAST(r.reseller_id AS CHAR) AS ResellerId,
                    r.reseller_code AS ResellerCode,
                    r.reseller_name AS ResellerName,
                    r.biz_no AS BizNo,
                    r.ceo_name AS CeoName,
                    r.contact_person AS ContactPerson,
                    r.contact_email AS ContactEmail,
                    r.contact_phone AS ContactPhone,
                    r.status AS Status,
                    r.join_date AS JoinDate,
                    r.created_at AS CreatedAt,
                    (SELECT COUNT(*) FROM tenants t WHERE t.reseller_id = r.reseller_id) AS TenantCount
                FROM resellers r
                " + (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "") + @"
                ORDER BY r.created_at DESC
                LIMIT 500";

            var rows = await db.QueryAsync<ResellerRow>(sql, param);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellersAdmin] 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpGet("{id}")]
    [BoPermission("resellers.detail")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.QueryFirstOrDefaultAsync<ResellerDetailRow>(@"
                SELECT
                    CAST(reseller_id AS CHAR) AS ResellerId,
                    reseller_code AS ResellerCode,
                    reseller_name AS ResellerName,
                    biz_no AS BizNo,
                    ceo_name AS CeoName,
                    tel AS Tel,
                    address AS Address,
                    bank_name AS BankName,
                    account_holder AS AccountHolder,
                    contact_person AS ContactPerson,
                    contact_phone AS ContactPhone,
                    contact_email AS ContactEmail,
                    status AS Status,
                    join_date AS JoinDate,
                    created_at AS CreatedAt,
                    updated_at AS UpdatedAt
                FROM resellers
                WHERE reseller_id = @Id",
                new { Id = id });
            if (row is null)
                return NotFound(new { success = false, message = "협력업체를 찾을 수 없습니다." });

            var tenantCount = await db.QueryFirstOrDefaultAsync<int>(@"
                SELECT COUNT(*) FROM tenants WHERE reseller_id = @Id",
                new { Id = id });
            row.TenantCount = tenantCount;

            // 영업 고객사 최근 20건 (메타만)
            var tenants = await db.QueryAsync<ResellerTenantBrief>(@"
                SELECT
                    CAST(tenant_id AS CHAR) AS TenantId,
                    tenant_code AS TenantCode,
                    company_name AS CompanyName,
                    status AS Status,
                    subscription_tier AS SubscriptionTier,
                    created_at AS CreatedAt
                FROM tenants
                WHERE reseller_id = @Id
                ORDER BY created_at DESC
                LIMIT 20",
                new { Id = id });

            return Ok(new { success = true, item = row, tenants });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellersAdmin] 상세 조회 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "상세 조회 중 오류가 발생했습니다." });
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

    public class ResellerRow
    {
        public string ResellerId { get; set; } = "";
        public string ResellerCode { get; set; } = "";
        public string ResellerName { get; set; } = "";
        public string? BizNo { get; set; }
        public string? CeoName { get; set; }
        public string? ContactPerson { get; set; }
        public string? ContactEmail { get; set; }
        public string? ContactPhone { get; set; }
        public string Status { get; set; } = "";
        public DateTime? JoinDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TenantCount { get; set; }
    }

    public class ResellerDetailRow : ResellerRow
    {
        public string? Tel { get; set; }
        public string? Address { get; set; }
        public string? BankName { get; set; }
        public string? AccountHolder { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ResellerTenantBrief
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Status { get; set; } = "";
        public string? SubscriptionTier { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
