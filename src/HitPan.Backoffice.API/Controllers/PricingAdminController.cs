using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 가격 관리 API (브라운킴 PM 저장 2026-06-08, 사장님 결재)
//
// 사장님 헌법:
//   "가격은 저장해서 못 바꾸게 하지 말고 본사 마스터 계정이 쉽게 변경 할 수 있도록"
//   "가격상승이나 프로모션, 리워드지급, 행사, 할인 등등 본사 재량으로 가격설정"
//   "구독결재 청구서도 변경된 가격으로 자동반영"
//
// 영역:
//   GET    /api/admin/pricing/plans                — 요금제 목록
//   GET    /api/admin/pricing/plans/{id}           — 단일 조회
//   PUT    /api/admin/pricing/plans/{id}           — 가격·기능 수정 (history 자동 박음)
//   POST   /api/admin/pricing/plans/{id}/activate  — 활성/비활성 토글
//   GET    /api/admin/pricing/plans/{id}/history   — 변경 이력
//
// 헌법 정합:
//   #3 INSERT ONLY — pricing_history는 변경만 추가
//   #15 — 빈 catch 금지
//   #19 — errors 0 + warnings 0
//   #25 — 쉽게·정확하게·안전하게
[ApiController]
[Route("api/admin/pricing")]
[Authorize(Policy = "PlatformAdmin")]  // 본사 마스터 계정만 (2026-06-11 P0 봉합)
public class PricingAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<PricingAdminController> _logger;

    public PricingAdminController(IConfiguration config, ILogger<PricingAdminController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("plans")]
    public async Task<IActionResult> ListPlans(CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var plans = await db.QueryAsync<PlanRow>(@"
                SELECT plan_id AS PlanId, plan_name AS PlanName, description AS Description,
                       monthly_price AS MonthlyPrice, yearly_price AS YearlyPrice,
                       price_display AS PriceDisplay,
                       max_users AS MaxUsers, max_devices AS MaxDevices,
                       max_pc_devices AS MaxPcDevices, max_mobile_devices AS MaxMobileDevices,
                       ai_token_monthly AS AiTokenMonthly, features_json AS FeaturesJson,
                       is_active AS IsActive, is_visible AS IsVisible, display_order AS DisplayOrder,
                       created_at AS CreatedAt, updated_at AS UpdatedAt
                FROM pricing_plans
                ORDER BY display_order, plan_id");
            return Ok(new { success = true, data = plans });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PricingAdmin] list 실패");
            return StatusCode(500, new { success = false, message = "요금제 조회 실패" });
        }
    }

    [HttpGet("plans/{planId}")]
    public async Task<IActionResult> GetPlan(string planId, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var plan = await db.QueryFirstOrDefaultAsync<PlanRow>(@"
                SELECT plan_id AS PlanId, plan_name AS PlanName, description AS Description,
                       monthly_price AS MonthlyPrice, yearly_price AS YearlyPrice,
                       price_display AS PriceDisplay,
                       max_users AS MaxUsers, max_devices AS MaxDevices,
                       max_pc_devices AS MaxPcDevices, max_mobile_devices AS MaxMobileDevices,
                       ai_token_monthly AS AiTokenMonthly, features_json AS FeaturesJson,
                       is_active AS IsActive, is_visible AS IsVisible, display_order AS DisplayOrder
                FROM pricing_plans WHERE plan_id = @PlanId",
                new { PlanId = planId });
            if (plan is null) return NotFound(new { success = false, message = "요금제 없음" });
            return Ok(new { success = true, data = plan });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PricingAdmin] get 실패 planId={Id}", planId);
            return StatusCode(500, new { success = false, message = "조회 실패" });
        }
    }

    [HttpPut("plans/{planId}")]
    public async Task<IActionResult> UpdatePlan(string planId, [FromBody] UpdatePlanRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { success = false, message = "요청 비어있음" });
        if (req.MonthlyPrice < 0 || req.YearlyPrice < 0)
            return BadRequest(new { success = false, message = "가격은 0 이상이어야 합니다." });

        try
        {
            await using var db = await OpenAsync(ct);

            // 변경 전 스냅샷 (감사 추적용)
            var before = await db.QueryFirstOrDefaultAsync<PlanRow>(
                "SELECT * FROM pricing_plans WHERE plan_id = @PlanId",
                new { PlanId = planId });
            if (before is null) return NotFound(new { success = false, message = "요금제 없음" });

            await db.ExecuteAsync(@"
                UPDATE pricing_plans
                SET plan_name = @PlanName,
                    description = @Description,
                    monthly_price = @MonthlyPrice,
                    yearly_price = @YearlyPrice,
                    price_display = @PriceDisplay,
                    max_users = @MaxUsers,
                    max_devices = @MaxDevices,
                    max_pc_devices = @MaxPcDevices,
                    max_mobile_devices = @MaxMobileDevices,
                    ai_token_monthly = @AiTokenMonthly,
                    features_json = @FeaturesJson,
                    is_visible = @IsVisible,
                    display_order = @DisplayOrder,
                    updated_at = CURRENT_TIMESTAMP(6)
                WHERE plan_id = @PlanId",
                new
                {
                    PlanId = planId,
                    req.PlanName,
                    req.Description,
                    req.MonthlyPrice,
                    req.YearlyPrice,
                    PriceDisplay = string.IsNullOrWhiteSpace(req.PriceDisplay) ? "number" : req.PriceDisplay,
                    req.MaxUsers,
                    req.MaxDevices,
                    req.MaxPcDevices,
                    req.MaxMobileDevices,
                    req.AiTokenMonthly,
                    req.FeaturesJson,
                    IsVisible = req.IsVisible ? 1 : 0,
                    req.DisplayOrder
                });

            // 이력 추적 (헌법 #3 INSERT ONLY)
            var changedBy = req.ChangedBy ?? "system";
            var changedByName = req.ChangedByName ?? "시스템";
            await db.ExecuteAsync(@"
                INSERT INTO pricing_history (entity_type, entity_id, action, before_json, after_json, reason, changed_by, changed_by_name)
                VALUES ('plan', @PlanId, 'update', @Before, @After, @Reason, @ChangedBy, @ChangedByName)",
                new
                {
                    PlanId = planId,
                    Before = JsonSerializer.Serialize(before),
                    After = JsonSerializer.Serialize(req),
                    req.Reason,
                    ChangedBy = changedBy,
                    ChangedByName = changedByName
                });

            _logger.LogInformation("[PricingAdmin] updated planId={Id} monthly={M}→{NewM} by={By} reason={R}",
                planId, before.MonthlyPrice, req.MonthlyPrice, changedBy, req.Reason);

            return Ok(new { success = true, message = "요금제 수정 완료 (다음 청구 주기부터 자동 반영)" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PricingAdmin] update 실패 planId={Id}", planId);
            return StatusCode(500, new { success = false, message = "수정 실패" });
        }
    }

    [HttpPost("plans/{planId}/toggle-active")]
    public async Task<IActionResult> ToggleActive(string planId, [FromBody] ToggleRequest req, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var before = await db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT is_active FROM pricing_plans WHERE plan_id = @PlanId",
                new { PlanId = planId });
            if (before is null) return NotFound(new { success = false, message = "요금제 없음" });

            int newState = req.IsActive ? 1 : 0;
            await db.ExecuteAsync(
                "UPDATE pricing_plans SET is_active = @S, updated_at = CURRENT_TIMESTAMP(6) WHERE plan_id = @PlanId",
                new { S = newState, PlanId = planId });

            await db.ExecuteAsync(@"
                INSERT INTO pricing_history (entity_type, entity_id, action, before_json, after_json, reason, changed_by, changed_by_name)
                VALUES ('plan', @PlanId, @Action, @Before, @After, @Reason, @By, @ByName)",
                new
                {
                    PlanId = planId,
                    Action = req.IsActive ? "activate" : "deactivate",
                    Before = JsonSerializer.Serialize(before),
                    After = JsonSerializer.Serialize(new { is_active = newState }),
                    req.Reason,
                    By = req.ChangedBy ?? "system",
                    ByName = req.ChangedByName ?? "시스템"
                });

            return Ok(new { success = true, message = req.IsActive ? "활성화" : "비활성화" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PricingAdmin] toggle 실패 planId={Id}", planId);
            return StatusCode(500, new { success = false, message = "처리 실패" });
        }
    }

    [HttpGet("plans/{planId}/history")]
    public async Task<IActionResult> GetHistory(string planId, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var rows = await db.QueryAsync<HistoryRow>(@"
                SELECT history_id AS HistoryId, action AS Action,
                       before_json AS BeforeJson, after_json AS AfterJson,
                       reason AS Reason, changed_by AS ChangedBy,
                       changed_by_name AS ChangedByName, changed_at AS ChangedAt
                FROM pricing_history
                WHERE entity_type = 'plan' AND entity_id = @PlanId
                ORDER BY history_id DESC LIMIT 100",
                new { PlanId = planId });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PricingAdmin] history 실패");
            return StatusCode(500, new { success = false, message = "이력 조회 실패" });
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

    public class PlanRow
    {
        public string PlanId { get; set; } = "";
        public string PlanName { get; set; } = "";
        public string? Description { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public string PriceDisplay { get; set; } = "number";  // 'number' or 'contact'
        public int MaxUsers { get; set; }
        public int MaxDevices { get; set; }
        public int MaxPcDevices { get; set; }
        public int MaxMobileDevices { get; set; }
        public int AiTokenMonthly { get; set; }
        public string? FeaturesJson { get; set; }
        public int IsActive { get; set; }
        public int IsVisible { get; set; }
        public int DisplayOrder { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpdatePlanRequest
    {
        public string PlanName { get; set; } = "";
        public string? Description { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public string PriceDisplay { get; set; } = "number";
        public int MaxUsers { get; set; }
        public int MaxDevices { get; set; }
        public int MaxPcDevices { get; set; }
        public int MaxMobileDevices { get; set; }
        public int AiTokenMonthly { get; set; }
        public string? FeaturesJson { get; set; }
        public bool IsVisible { get; set; } = true;
        public int DisplayOrder { get; set; }
        public string? Reason { get; set; }
        public string? ChangedBy { get; set; }
        public string? ChangedByName { get; set; }
    }

    public class ToggleRequest
    {
        public bool IsActive { get; set; }
        public string? Reason { get; set; }
        public string? ChangedBy { get; set; }
        public string? ChangedByName { get; set; }
    }

    public class HistoryRow
    {
        public long HistoryId { get; set; }
        public string Action { get; set; } = "";
        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
        public string? Reason { get; set; }
        public string ChangedBy { get; set; } = "";
        public string? ChangedByName { get; set; }
        public DateTime ChangedAt { get; set; }
    }
}
