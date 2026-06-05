using Dapper;
using HitPan.Backoffice.API.Filters;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// W17 프로모션 백엔드 (사장님 결재 2026-06-05)
//
// 흐름:
//   1) GET /              — 프로모션 목록 (status·기간 필터)
//   2) POST /             — 생성 (promo_code UNIQUE, 기간 검증)
//   3) POST /{id}/toggle  — 활성/비활성 토글
//   4) GET  /{id}/usages  — 사용 이력 조회 (INSERT ONLY)
//   5) POST /redeem       — 가입 흐름에서 코드 검증·차감 (랜딩 → BO 직호출)
//
// 헌법 정합:
//   #3 promotion_usages INSERT ONLY
//   #4 decimal
//   #15 빈 catch 0
//   #18·#22 본사 메타만, 평문 0
//   #20 워크플로우 끊김 0
//   #25 안전하게 (UNIQUE 차단 + use_count 검증)
[ApiController]
[Route("api/backoffice/promotions")]
public class PromotionController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<PromotionController> _logger;
    private readonly IBoAuditService _audit;

    public PromotionController(IConfiguration config, ILogger<PromotionController> logger, IBoAuditService audit)
    {
        _config = config;
        _logger = logger;
        _audit = audit;
    }

    [HttpGet]
    [Authorize]
    [BoPermission("promotion.list")]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var where = "";
            object? param = null;
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                where = "WHERE status = @Status";
                param = new { Status = status };
            }
            var rows = await db.QueryAsync<PromotionRow>($@"
                SELECT
                    promotion_id AS PromotionId,
                    promo_code AS PromoCode,
                    title AS Title,
                    discount_type AS DiscountType,
                    discount_value AS DiscountValue,
                    starts_at AS StartsAt,
                    ends_at AS EndsAt,
                    max_uses AS MaxUses,
                    use_count AS UseCount,
                    target_plan AS TargetPlan,
                    status AS Status,
                    created_at AS CreatedAt
                FROM promotions
                {where}
                ORDER BY promotion_id DESC
                LIMIT 500", param);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Promotion] 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpPost]
    [Authorize]
    [BoPermission("promotion.create")]
    public async Task<IActionResult> Create([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (req is null
            || string.IsNullOrWhiteSpace(req.PromoCode)
            || string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { success = false, message = "프로모션 코드·제목 필수" });
        if (req.DiscountType != "percent" && req.DiscountType != "fixed")
            return BadRequest(new { success = false, message = "discount_type은 percent 또는 fixed" });
        if (req.DiscountValue <= 0)
            return BadRequest(new { success = false, message = "할인 값은 0보다 커야 합니다." });
        if (req.DiscountType == "percent" && req.DiscountValue > 100)
            return BadRequest(new { success = false, message = "percent 할인은 100 이하" });
        if (req.EndsAt <= req.StartsAt)
            return BadRequest(new { success = false, message = "종료일은 시작일 이후" });

        var (actorId, actorEmail, _) = GetActor();

        try
        {
            await using var db = await OpenAsync(ct);
            var dup = await db.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM promotions WHERE promo_code = @Code",
                new { Code = req.PromoCode });
            if (dup > 0)
                return BadRequest(new { success = false, message = "이미 사용 중인 프로모션 코드입니다." });

            var promotionId = await db.ExecuteScalarAsync<long>(@"
                INSERT INTO promotions
                    (promo_code, title, discount_type, discount_value,
                     starts_at, ends_at, max_uses, target_plan, status, created_by)
                VALUES
                    (@PromoCode, @Title, @DiscountType, @DiscountValue,
                     @StartsAt, @EndsAt, @MaxUses, @TargetPlan, 'active', @ActorId);
                SELECT LAST_INSERT_ID();",
                new
                {
                    req.PromoCode,
                    req.Title,
                    req.DiscountType,
                    req.DiscountValue,
                    req.StartsAt,
                    req.EndsAt,
                    MaxUses = req.MaxUses < 0 ? 0 : req.MaxUses,
                    req.TargetPlan,
                    ActorId = actorId
                });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                "promotion.create", "promotion", promotionId.ToString(),
                new { req.PromoCode, req.Title }, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = "프로모션이 생성되었습니다.", promotionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Promotion] 생성 실패");
            return StatusCode(500, new { success = false, message = "생성 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("{id:long}/toggle")]
    [Authorize]
    [BoPermission("promotion.toggle")]
    public async Task<IActionResult> Toggle(long id, [FromBody] ToggleRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Status))
            return BadRequest(new { success = false, message = "status 필수" });
        if (req.Status != "active" && req.Status != "inactive" && req.Status != "expired")
            return BadRequest(new { success = false, message = "status는 active/inactive/expired" });

        var (actorId, actorEmail, _) = GetActor();
        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(
                "UPDATE promotions SET status = @Status WHERE promotion_id = @Id",
                new { Status = req.Status, Id = id });
            if (affected == 0)
                return NotFound(new { success = false, message = "프로모션을 찾을 수 없습니다." });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                $"promotion.toggle:{req.Status}", "promotion", id.ToString(),
                null, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = $"상태가 {req.Status}로 변경됐습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Promotion] 토글 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "처리 중 오류가 발생했습니다." });
        }
    }

    [HttpGet("{id:long}/usages")]
    [Authorize]
    [BoPermission("promotion.usage")]
    public async Task<IActionResult> Usages(long id, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var rows = await db.QueryAsync<UsageRow>(@"
                SELECT
                    usage_id AS UsageId,
                    promotion_id AS PromotionId,
                    promo_code AS PromoCode,
                    CAST(tenant_id AS CHAR) AS TenantId,
                    signup_token AS SignupToken,
                    applied_amount AS AppliedAmount,
                    applied_at AS AppliedAt,
                    source AS Source
                FROM promotion_usages
                WHERE promotion_id = @Id
                ORDER BY usage_id DESC
                LIMIT 500",
                new { Id = id });
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Promotion] 사용 이력 조회 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "조회 중 오류가 발생했습니다." });
        }
    }

    // 가입 흐름에서 호출 — 인증 불필요 (랜딩 → BO 직호출)
    [HttpPost("redeem")]
    [AllowAnonymous]
    public async Task<IActionResult> Redeem([FromBody] RedeemRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.PromoCode))
            return BadRequest(new { valid = false, message = "프로모션 코드 필수" });

        try
        {
            await using var db = await OpenAsync(ct);
            var promo = await db.QueryFirstOrDefaultAsync<RedeemRow>(@"
                SELECT
                    promotion_id AS PromotionId,
                    promo_code AS PromoCode,
                    discount_type AS DiscountType,
                    discount_value AS DiscountValue,
                    starts_at AS StartsAt,
                    ends_at AS EndsAt,
                    max_uses AS MaxUses,
                    use_count AS UseCount,
                    target_plan AS TargetPlan,
                    status AS Status
                FROM promotions
                WHERE promo_code = @Code",
                new { Code = req.PromoCode });

            if (promo is null)
                return Ok(new { valid = false, message = "유효하지 않은 코드입니다." });
            if (promo.Status != "active")
                return Ok(new { valid = false, message = "비활성 상태의 코드입니다." });
            var now = DateTime.UtcNow;
            if (promo.StartsAt > now || promo.EndsAt < now)
                return Ok(new { valid = false, message = "유효 기간이 아닙니다." });
            if (promo.MaxUses > 0 && promo.UseCount >= promo.MaxUses)
                return Ok(new { valid = false, message = "사용 한도 초과" });
            if (!string.IsNullOrWhiteSpace(promo.TargetPlan)
                && !string.IsNullOrWhiteSpace(req.PlanType)
                && promo.TargetPlan != req.PlanType)
                return Ok(new { valid = false, message = $"본 코드는 {promo.TargetPlan} 플랜 전용" });

            // 사용 이력 INSERT (헌법 #3 INSERT ONLY)
            await db.ExecuteAsync(@"
                INSERT INTO promotion_usages
                    (promotion_id, promo_code, signup_token, applied_amount, source)
                VALUES
                    (@PromotionId, @PromoCode, @SignupToken, @AppliedAmount, 'landing_signup');
                UPDATE promotions SET use_count = use_count + 1 WHERE promotion_id = @PromotionId;",
                new
                {
                    promo.PromotionId,
                    promo.PromoCode,
                    req.SignupToken,
                    AppliedAmount = req.BaseAmount * (promo.DiscountType == "percent"
                        ? promo.DiscountValue / 100m
                        : 1m) * (promo.DiscountType == "fixed" ? 1m : 1m)
                });

            // discount_value 적용
            decimal appliedDiscount = promo.DiscountType == "percent"
                ? req.BaseAmount * (promo.DiscountValue / 100m)
                : promo.DiscountValue;

            return Ok(new
            {
                valid = true,
                discountType = promo.DiscountType,
                discountValue = promo.DiscountValue,
                appliedDiscount,
                message = "프로모션이 적용됐습니다."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Promotion] redeem 실패 code={Code}", req.PromoCode);
            return StatusCode(500, new { valid = false, message = "처리 중 오류가 발생했습니다." });
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

    public record CreateRequest(string PromoCode, string Title, string DiscountType, decimal DiscountValue,
        DateTime StartsAt, DateTime EndsAt, int MaxUses, string? TargetPlan);

    public record ToggleRequest(string Status);

    public record RedeemRequest(string PromoCode, string? SignupToken, string? PlanType, decimal BaseAmount);

    private class PromotionRow
    {
        public long PromotionId { get; set; }
        public string PromoCode { get; set; } = "";
        public string Title { get; set; } = "";
        public string DiscountType { get; set; } = "";
        public decimal DiscountValue { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public int MaxUses { get; set; }
        public int UseCount { get; set; }
        public string? TargetPlan { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    private class UsageRow
    {
        public long UsageId { get; set; }
        public long PromotionId { get; set; }
        public string PromoCode { get; set; } = "";
        public string? TenantId { get; set; }
        public string? SignupToken { get; set; }
        public decimal AppliedAmount { get; set; }
        public DateTime AppliedAt { get; set; }
        public string Source { get; set; } = "";
    }

    private class RedeemRow
    {
        public long PromotionId { get; set; }
        public string PromoCode { get; set; } = "";
        public string DiscountType { get; set; } = "";
        public decimal DiscountValue { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public int MaxUses { get; set; }
        public int UseCount { get; set; }
        public string? TargetPlan { get; set; }
        public string Status { get; set; } = "";
    }
}
