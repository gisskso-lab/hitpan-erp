using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// W18 대리점 본인 조회 (사장님 결재 2026-06-05)
//
// 행 수준 권한 (헌법 #7):
//   - 본인 토큰의 reseller_id 클레임만 조회
//   - 다른 대리점 reseller_id 파라미터로 받지 않음 (테넌트 격리)
//
// 엔드포인트:
//   1) GET /summary       — 본인 월별 매출·정산·고객사 카운터
//   2) GET /settlements   — 본인 정산 목록 (W9 reseller_settlements 행 수준 필터)
//   3) GET /customers     — 본인 고객사 목록 (tenants.reseller_id 매칭, 평문 0)
//
// 헌법 정합:
//   #15 빈 catch 0
//   #18·#22 평문 0 (사업자번호·CEO명 0건)
//   #20 워크플로우 끊김 0
//   #35 객체 분리 + 행 수준 격리
[ApiController]
[Route("api/reseller-portal")]
[Authorize]
public class ResellerPortalController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<ResellerPortalController> _logger;

    public ResellerPortalController(IConfiguration config, ILogger<ResellerPortalController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var resellerId = GetResellerId();
        if (string.IsNullOrWhiteSpace(resellerId))
            return Forbid();

        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.QueryFirstOrDefaultAsync<SummaryRow>(@"
                SELECT
                    (SELECT COUNT(*) FROM tenants WHERE reseller_id = @Rid AND status = 'active') AS ActiveTenants,
                    (SELECT COUNT(*) FROM tenants WHERE reseller_id = @Rid AND status = 'pending') AS PendingTenants,
                    (SELECT COALESCE(SUM(total_payable), 0) FROM reseller_settlements
                        WHERE reseller_id = @Rid AND status IN ('confirmed', 'paid')) AS TotalConfirmedPayable,
                    (SELECT COALESCE(SUM(total_payable), 0) FROM reseller_settlements
                        WHERE reseller_id = @Rid AND status = 'paid') AS TotalPaid,
                    (SELECT COUNT(*) FROM reseller_settlements
                        WHERE reseller_id = @Rid AND status = 'draft') AS DraftSettlements",
                new { Rid = resellerId });
            return Ok(new { success = true, summary = row });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerPortal] summary 실패 reseller={Rid}", resellerId);
            return StatusCode(500, new { success = false, message = "요약 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpGet("settlements")]
    public async Task<IActionResult> Settlements([FromQuery] string? status, CancellationToken ct)
    {
        var resellerId = GetResellerId();
        if (string.IsNullOrWhiteSpace(resellerId))
            return Forbid();

        try
        {
            await using var db = await OpenAsync(ct);
            var where = "WHERE reseller_id = @Rid";
            var p = new DynamicParameters();
            p.Add("Rid", resellerId);
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                where += " AND status = @Status";
                p.Add("Status", status);
            }
            var rows = await db.QueryAsync<SettlementRow>($@"
                SELECT
                    settlement_id AS SettlementId,
                    settlement_month AS SettlementMonth,
                    tenant_count AS TenantCount,
                    gross_amount AS GrossAmount,
                    commission_rate AS CommissionRate,
                    commission_amount AS CommissionAmount,
                    incentive_amount AS IncentiveAmount,
                    total_payable AS TotalPayable,
                    status AS Status,
                    confirmed_at AS ConfirmedAt,
                    paid_at AS PaidAt,
                    created_at AS CreatedAt
                FROM reseller_settlements
                {where}
                ORDER BY settlement_month DESC, settlement_id DESC
                LIMIT 200", p);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerPortal] settlements 실패 reseller={Rid}", resellerId);
            return StatusCode(500, new { success = false, message = "정산 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpGet("customers")]
    public async Task<IActionResult> Customers([FromQuery] string? status, CancellationToken ct)
    {
        var resellerId = GetResellerId();
        if (string.IsNullOrWhiteSpace(resellerId))
            return Forbid();

        try
        {
            await using var db = await OpenAsync(ct);
            var where = "WHERE reseller_id = @Rid";
            var p = new DynamicParameters();
            p.Add("Rid", resellerId);
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                where += " AND status = @Status";
                p.Add("Status", status);
            }

            // 헌법 #18·#22 — 평문 0 (사업자번호·CEO명·주소·전화 0건)
            //   대리점 본인용은 company_name·tenant_code·subscription_tier·status만 노출
            var rows = await db.QueryAsync<CustomerRow>($@"
                SELECT
                    CAST(tenant_id AS CHAR) AS TenantId,
                    tenant_code AS TenantCode,
                    company_name AS CompanyName,
                    subscription_tier AS SubscriptionTier,
                    status AS Status,
                    trial_ends_at AS TrialEndsAt,
                    created_at AS CreatedAt
                FROM tenants
                {where}
                ORDER BY created_at DESC
                LIMIT 500", p);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerPortal] customers 실패 reseller={Rid}", resellerId);
            return StatusCode(500, new { success = false, message = "고객사 조회 중 오류가 발생했습니다." });
        }
    }

    private string? GetResellerId()
    {
        // 클레임 우선순위: reseller_id > sub (대리점 토큰)
        var rid = User.FindFirst("reseller_id")?.Value;
        if (!string.IsNullOrWhiteSpace(rid)) return rid;
        // account_type=reseller인 경우 sub가 reseller_id
        var accountType = User.FindFirst("account_type")?.Value;
        if (accountType == "reseller")
            return User.FindFirst("sub")?.Value;
        return null;
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

    private class SummaryRow
    {
        public int ActiveTenants { get; set; }
        public int PendingTenants { get; set; }
        public decimal TotalConfirmedPayable { get; set; }
        public decimal TotalPaid { get; set; }
        public int DraftSettlements { get; set; }
    }

    private class SettlementRow
    {
        public long SettlementId { get; set; }
        public string SettlementMonth { get; set; } = "";
        public int TenantCount { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal CommissionRate { get; set; }
        public decimal CommissionAmount { get; set; }
        public decimal IncentiveAmount { get; set; }
        public decimal TotalPayable { get; set; }
        public string Status { get; set; } = "";
        public DateTime? ConfirmedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private class CustomerRow
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string SubscriptionTier { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? TrialEndsAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
