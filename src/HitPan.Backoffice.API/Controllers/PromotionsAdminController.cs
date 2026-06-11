using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 프로모션·할인·이벤트 관리 (브라운킴 PM 2026-06-08)
[ApiController]
[Route("api/admin/promotions-v2")]
[Authorize(Policy = "PlatformAdmin")]  // 본사 마스터 계정만 (2026-06-11 P0 봉합)
public class PromotionsAdminV2Controller : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<PromotionsAdminV2Controller> _logger;

    public PromotionsAdminV2Controller(IConfiguration config, ILogger<PromotionsAdminV2Controller> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? activeOnly, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var sql = @"
                SELECT promotion_id AS PromotionId, promotion_code AS PromotionCode,
                       promotion_name AS PromotionName, description AS Description,
                       discount_type AS DiscountType, discount_value AS DiscountValue,
                       target_plan_id AS TargetPlanId, target_scope AS TargetScope,
                       starts_at AS StartsAt, ends_at AS EndsAt,
                       max_uses AS MaxUses, used_count AS UsedCount,
                       is_active AS IsActive, created_by AS CreatedBy, created_at AS CreatedAt
                FROM promotions
                WHERE (@ActiveOnly IS NULL OR @ActiveOnly = 0 OR (is_active = 1 AND starts_at <= NOW() AND ends_at >= NOW()))
                ORDER BY created_at DESC LIMIT 200";
            var rows = await db.QueryAsync(sql, new { ActiveOnly = activeOnly == true ? 1 : (int?)null });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Promotions] list 실패");
            return StatusCode(500, new { success = false, message = "조회 실패" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePromotionRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.PromotionCode))
            return BadRequest(new { success = false, message = "프로모션 코드 필수" });
        if (req.DiscountValue < 0)
            return BadRequest(new { success = false, message = "할인 값은 0 이상이어야 합니다." });
        if (req.DiscountType == "percent" && req.DiscountValue > 100)
            return BadRequest(new { success = false, message = "할인율은 100% 이하여야 합니다." });
        if (req.StartsAt >= req.EndsAt)
            return BadRequest(new { success = false, message = "시작일이 종료일보다 빨라야 합니다." });

        try
        {
            await using var db = await OpenAsync(ct);
            var promotionId = Guid.NewGuid().ToString();

            await db.ExecuteAsync(@"
                INSERT INTO promotions (promotion_id, promotion_code, promotion_name, description,
                                        discount_type, discount_value, target_plan_id, target_scope,
                                        target_filter, starts_at, ends_at, max_uses,
                                        is_active, created_by)
                VALUES (@PromotionId, @PromotionCode, @PromotionName, @Description,
                        @DiscountType, @DiscountValue, @TargetPlanId, @TargetScope,
                        @TargetFilter, @StartsAt, @EndsAt, @MaxUses,
                        1, @CreatedBy)",
                new
                {
                    PromotionId = promotionId,
                    req.PromotionCode,
                    req.PromotionName,
                    req.Description,
                    req.DiscountType,
                    req.DiscountValue,
                    req.TargetPlanId,
                    req.TargetScope,
                    req.TargetFilter,
                    req.StartsAt,
                    req.EndsAt,
                    req.MaxUses,
                    CreatedBy = req.CreatedBy ?? "system"
                });

            _logger.LogInformation("[Promotions] created code={Code} discount={Type}/{Val} by={By}",
                req.PromotionCode, req.DiscountType, req.DiscountValue, req.CreatedBy);

            return Ok(new { success = true, message = "프로모션 생성 완료", promotionId });
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return BadRequest(new { success = false, message = "이미 사용 중인 프로모션 코드입니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Promotions] create 실패");
            return StatusCode(500, new { success = false, message = "생성 실패" });
        }
    }

    [HttpPost("{promotionId}/toggle-active")]
    public async Task<IActionResult> ToggleActive(string promotionId, [FromBody] ToggleRequest req, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(
                "UPDATE promotions SET is_active = @S, updated_at = CURRENT_TIMESTAMP(6) WHERE promotion_id = @Id",
                new { S = req.IsActive ? 1 : 0, Id = promotionId });
            if (affected == 0) return NotFound(new { success = false, message = "프로모션 없음" });
            return Ok(new { success = true, message = req.IsActive ? "활성화" : "비활성화" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Promotions] toggle 실패");
            return StatusCode(500, new { success = false, message = "처리 실패" });
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

    public class CreatePromotionRequest
    {
        public string PromotionCode { get; set; } = "";
        public string PromotionName { get; set; } = "";
        public string? Description { get; set; }
        public string DiscountType { get; set; } = "percent";  // percent, fixed, free_months
        public decimal DiscountValue { get; set; }
        public string? TargetPlanId { get; set; }
        public string TargetScope { get; set; } = "all";  // all, reseller, new_only, specific
        public string? TargetFilter { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public int? MaxUses { get; set; }
        public string? CreatedBy { get; set; }
    }

    public class ToggleRequest
    {
        public bool IsActive { get; set; }
        public string? Reason { get; set; }
    }
}

// 리워드 지급 (개별 고객사 크레딧·기간 연장·할인)
[ApiController]
[Route("api/admin/rewards")]
[Authorize(Policy = "PlatformAdmin")]  // 본사 마스터 계정만 (2026-06-11 P0 봉합)
public class RewardsAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<RewardsAdminController> _logger;

    public RewardsAdminController(IConfiguration config, ILogger<RewardsAdminController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("tenant/{tenantId}")]
    public async Task<IActionResult> ListByTenant(string tenantId, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var rows = await db.QueryAsync(@"
                SELECT reward_id AS RewardId, reward_type AS RewardType,
                       reward_value AS RewardValue, reason AS Reason,
                       expires_at AS ExpiresAt, is_consumed AS IsConsumed,
                       consumed_at AS ConsumedAt, granted_by AS GrantedBy, granted_at AS GrantedAt
                FROM tenant_rewards WHERE tenant_id = @TenantId ORDER BY reward_id DESC LIMIT 100",
                new { TenantId = tenantId });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Rewards] list 실패");
            return StatusCode(500, new { success = false, message = "조회 실패" });
        }
    }

    [HttpPost("grant")]
    public async Task<IActionResult> Grant([FromBody] GrantRewardRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.TenantId))
            return BadRequest(new { success = false, message = "tenant_id 필수" });
        if (req.RewardValue <= 0)
            return BadRequest(new { success = false, message = "리워드 값은 0 초과여야 합니다." });

        try
        {
            await using var db = await OpenAsync(ct);
            var id = await db.ExecuteScalarAsync<long>(@"
                INSERT INTO tenant_rewards (tenant_id, reward_type, reward_value, reason, expires_at, granted_by)
                VALUES (@TenantId, @RewardType, @RewardValue, @Reason, @ExpiresAt, @GrantedBy);
                SELECT LAST_INSERT_ID();",
                new
                {
                    req.TenantId,
                    req.RewardType,
                    req.RewardValue,
                    req.Reason,
                    req.ExpiresAt,
                    GrantedBy = req.GrantedBy ?? "system"
                });

            _logger.LogInformation("[Rewards] granted tenant={Tid} type={T} value={V} by={By}",
                req.TenantId, req.RewardType, req.RewardValue, req.GrantedBy);
            return Ok(new { success = true, message = "리워드 지급 완료", rewardId = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Rewards] grant 실패");
            return StatusCode(500, new { success = false, message = "지급 실패" });
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

    public class GrantRewardRequest
    {
        public string TenantId { get; set; } = "";
        public string RewardType { get; set; } = "credit";  // credit, extend_days, discount, plan_upgrade
        public decimal RewardValue { get; set; }
        public string? Reason { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? GrantedBy { get; set; }
    }
}

// 가격 공개 조회 (랜딩 페이지용 — 실시간 가격, 인증 불필요)
[ApiController]
[Route("api/landing/pricing")]
[AllowAnonymous]
public class PricingPublicController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<PricingPublicController> _logger;

    public PricingPublicController(IConfiguration config, ILogger<PricingPublicController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("plans")]
    public async Task<IActionResult> GetPublicPlans(CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var plans = await db.QueryAsync(@"
                SELECT plan_id AS PlanId, plan_name AS PlanName, description AS Description,
                       monthly_price AS MonthlyPrice, yearly_price AS YearlyPrice,
                       price_display AS PriceDisplay,
                       max_users AS MaxUsers, max_devices AS MaxDevices,
                       max_pc_devices AS MaxPcDevices, max_mobile_devices AS MaxMobileDevices,
                       ai_token_monthly AS AiTokenMonthly, features_json AS FeaturesJson
                FROM pricing_plans
                WHERE is_active = 1 AND is_visible = 1
                ORDER BY display_order, plan_id");
            return Ok(new { success = true, data = plans });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PricingPublic] 조회 실패");
            return StatusCode(500, new { success = false, message = "조회 실패" });
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
}
