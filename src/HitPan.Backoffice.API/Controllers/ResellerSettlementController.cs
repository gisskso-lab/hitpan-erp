using Dapper;
using HitPan.Backoffice.API.Filters;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 대리점 정산 (사장님 결재 2026-06-04, W9)
//
// 흐름:
//   1) POST /calculate — 월별 산출 (draft)
//   2) GET /{id} — 상세 + lines
//   3) POST /{id}/confirm — 확정 (status: draft→confirmed)
//   4) POST /{id}/paid — 송금 완료 표시 (status: confirmed→paid)
//
// 헌법 정합:
//   #3 confirmed 후 UPDATE 금지 (status·메타만)
//   #15·#18·#22·#25
[ApiController]
[Route("api/backoffice/reseller-settlements")]
[Authorize]
public class ResellerSettlementController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<ResellerSettlementController> _logger;
    private readonly IResellerSettlementCalculator _calc;
    private readonly IBoAuditService _audit;

    public ResellerSettlementController(IConfiguration config,
        ILogger<ResellerSettlementController> logger,
        IResellerSettlementCalculator calc, IBoAuditService audit)
    {
        _config = config;
        _logger = logger;
        _calc = calc;
        _audit = audit;
    }

    [HttpGet]
    [BoPermission("reseller_settlement.list")]
    public async Task<IActionResult> List([FromQuery] string? month, [FromQuery] string? resellerId,
        [FromQuery] string? status, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var where = new List<string>();
            var p = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(month)) { where.Add("s.settlement_month = @Month"); p.Add("Month", month); }
            if (!string.IsNullOrWhiteSpace(resellerId)) { where.Add("s.reseller_id = @Rid"); p.Add("Rid", resellerId); }
            if (!string.IsNullOrWhiteSpace(status) && status != "all") { where.Add("s.status = @Status"); p.Add("Status", status); }

            var sql = @"
                SELECT
                    s.settlement_id AS SettlementId,
                    CAST(s.reseller_id AS CHAR) AS ResellerId,
                    r.reseller_name AS ResellerName,
                    s.settlement_month AS SettlementMonth,
                    s.tenant_count AS TenantCount,
                    s.gross_amount AS GrossAmount,
                    s.commission_rate AS CommissionRate,
                    s.commission_amount AS CommissionAmount,
                    s.incentive_amount AS IncentiveAmount,
                    s.total_payable AS TotalPayable,
                    s.status AS Status,
                    s.confirmed_at AS ConfirmedAt,
                    s.paid_at AS PaidAt,
                    s.created_at AS CreatedAt
                FROM reseller_settlements s
                LEFT JOIN resellers r ON r.reseller_id = s.reseller_id
                " + (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "") + @"
                ORDER BY s.settlement_month DESC, s.settlement_id DESC
                LIMIT 500";
            var rows = await db.QueryAsync<SettlementRow>(sql, p);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerSettlement] 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpGet("{id:long}")]
    [BoPermission("reseller_settlement.detail")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.QueryFirstOrDefaultAsync<SettlementRow>(@"
                SELECT
                    s.settlement_id AS SettlementId,
                    CAST(s.reseller_id AS CHAR) AS ResellerId,
                    r.reseller_name AS ResellerName,
                    s.settlement_month AS SettlementMonth,
                    s.tenant_count AS TenantCount,
                    s.gross_amount AS GrossAmount,
                    s.commission_rate AS CommissionRate,
                    s.commission_amount AS CommissionAmount,
                    s.incentive_amount AS IncentiveAmount,
                    s.total_payable AS TotalPayable,
                    s.status AS Status,
                    s.confirmed_at AS ConfirmedAt,
                    s.paid_at AS PaidAt,
                    s.memo AS Memo,
                    s.created_at AS CreatedAt
                FROM reseller_settlements s
                LEFT JOIN resellers r ON r.reseller_id = s.reseller_id
                WHERE s.settlement_id = @Id", new { Id = id });
            if (row is null) return NotFound(new { success = false, message = "정산 항목을 찾을 수 없습니다." });

            var lines = await db.QueryAsync<LineRow>(@"
                SELECT
                    CAST(tenant_id AS CHAR) AS TenantId,
                    tenant_code AS TenantCode,
                    company_name AS CompanyName,
                    payment_amount AS PaymentAmount,
                    commission_amount AS CommissionAmount
                FROM reseller_settlement_lines
                WHERE settlement_id = @Id
                ORDER BY payment_amount DESC", new { Id = id });

            return Ok(new { success = true, item = row, lines });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerSettlement] 상세 조회 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "상세 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("calculate")]
    [BoPermission("reseller_settlement.calculate")]
    public async Task<IActionResult> Calculate([FromBody] CalcRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.ResellerId) || string.IsNullOrWhiteSpace(req.Month))
            return BadRequest(new { success = false, message = "resellerId·month 필수" });

        var (actorId, actorEmail, _) = GetActor();
        var (ok, settlementId, error) = await _calc.CalculateAsync(req.ResellerId, req.Month, actorId, ct);
        if (!ok)
            return BadRequest(new { success = false, message = error ?? "산출 실패", settlementId });

        await _audit.LogAsync(actorId, actorEmail, "owner",
            "reseller_settlement.calculate", "settlement", settlementId?.ToString(),
            new { req.ResellerId, req.Month }, GetIp(), GetUa(), ct);

        return Ok(new { success = true, settlementId, message = "월별 정산이 산출되었습니다 (draft)." });
    }

    [HttpPost("{id:long}/confirm")]
    [BoPermission("reseller_settlement.confirm")]
    public async Task<IActionResult> Confirm(long id, CancellationToken ct)
    {
        var (actorId, actorEmail, _) = GetActor();
        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE reseller_settlements
                SET status = 'confirmed', confirmed_by = @Actor, confirmed_at = NOW(6)
                WHERE settlement_id = @Id AND status = 'draft'",
                new { Id = id, Actor = actorId });
            if (affected == 0)
                return BadRequest(new { success = false, message = "draft 상태인 정산만 확정할 수 있습니다." });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                "reseller_settlement.confirm", "settlement", id.ToString(), null,
                GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = "정산이 확정되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerSettlement] 확정 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "확정 처리 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("{id:long}/paid")]
    [BoPermission("reseller_settlement.paid")]
    public async Task<IActionResult> MarkPaid(long id, [FromBody] PaidRequest? req, CancellationToken ct)
    {
        var (actorId, actorEmail, _) = GetActor();
        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE reseller_settlements
                SET status = 'paid', paid_at = NOW(6), memo = COALESCE(@Memo, memo)
                WHERE settlement_id = @Id AND status = 'confirmed'",
                new { Id = id, Memo = req?.Memo });
            if (affected == 0)
                return BadRequest(new { success = false, message = "confirmed 상태인 정산만 송금 완료할 수 있습니다." });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                "reseller_settlement.paid", "settlement", id.ToString(),
                new { memo = req?.Memo }, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = "송금 완료로 처리되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerSettlement] 송금 처리 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "송금 처리 중 오류가 발생했습니다." });
        }
    }

    private (string id, string email, string role) GetActor()
    {
        var id = User.FindFirst("sub")?.Value ?? "";
        var email = User.FindFirst("email")?.Value ?? "";
        var role = User.FindFirst("role")?.Value ?? "";
        return (id, email, role);
    }

    private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? GetUa()
    {
        var ua = Request.Headers["User-Agent"].ToString();
        return string.IsNullOrEmpty(ua) ? null : (ua.Length > 255 ? ua.Substring(0, 255) : ua);
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

    public record CalcRequest(string ResellerId, string Month);
    public record PaidRequest(string? Memo);

    public class SettlementRow
    {
        public long SettlementId { get; set; }
        public string ResellerId { get; set; } = "";
        public string? ResellerName { get; set; }
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
        public string? Memo { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class LineRow
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public decimal PaymentAmount { get; set; }
        public decimal CommissionAmount { get; set; }
    }
}
