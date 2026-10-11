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
//   POST   /api/admin/pricing/plans                — 요금제 등록 (20261011작2 §B-1 신설 · 중복은 409)
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

    // ══════════════════════════════════════════════════════════════════════
    // 🔴 20261011작2 §B-1 — 요금제 **등록**(POST). 신설이고, 기존 5개는 한 글자도 안 바꿨다(#1).
    //
    // 사장님 오더: "요금제 넣고, 요금제 제외 버튼을 추가로 넣을것."
    // 왜 신설인가: 이 컨트롤러엔 생성 입구가 **아예 없었다**. PUT 은 수정 전용이라
    //   행이 없으면 NotFound 로 끝난다(:109). 운영 pricing_plans 가 0행이라
    //   화면에서 **첫 요금제를 만들 길이 없었다**(선행검증 실측).
    //
    // 🔴 기존 행은 한 글자도 안 바꾼다 — 그래서 일부러 **맨 INSERT** 다.
    //   ON DUPLICATE KEY UPDATE / REPLACE INTO 를 쓰면 같은 plan_id 로 한 번 더 누르는 순간
    //   운영 요금제가 조용히 덮인다(=가격 사고). 중복은 409 로 **거절**한다.
    //   PK 경주(동시 두 번 누름)도 1062 를 409 로 돌려 같은 결론을 낸다.
    //
    // 🔴 is_active 는 **이 입구에서 안 받는다.** 포함/제외 축은 toggle-active 한 곳뿐이어야 한다
    //   (두 곳에서 쓰면 과금 축이 갈라진다). 새 행은 출하 DDL 기본값(1 = 포함)으로 선다.
    //   그래서 INSERT 컬럼 목록에 is_active 가 없다 — #36 단일 진실원을 그대로 따른다.
    //
    // 🔴 기기 두 칸은 그대로(NULL 가능) 넣는다. 0 으로 바꾸지 않는다 —
    //   0 이면 DeviceRegistration 의 COALESCE(…,5)/(…,3) 폴백이 죽어 상한 0 이 된다(작16 결재 D-2).
    // ══════════════════════════════════════════════════════════════════════
    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { success = false, message = "요청 비어있음" });

        var planId = (req.PlanId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(planId))
            return BadRequest(new { success = false, message = "요금제 코드를 입력해주세요." });
        // plan_id varchar(20) — 넘치면 DB 가 잘라내거나 1406 으로 터진다(DESCRIBE 확인 2026-10-11).
        if (planId.Length > 20)
            return BadRequest(new { success = false, message = "요금제 코드는 20자 이내여야 합니다." });
        if (string.IsNullOrWhiteSpace(req.PlanName))
            return BadRequest(new { success = false, message = "요금제명을 입력해주세요." });
        // PUT 과 같은 규칙(:98) — 음수 가격 거부.
        if (req.MonthlyPrice < 0 || req.YearlyPrice < 0)
            return BadRequest(new { success = false, message = "가격은 0 이상이어야 합니다." });

        try
        {
            await using var db = await OpenAsync(ct);

            var exists = await db.ExecuteScalarAsync<int?>(
                "SELECT 1 FROM pricing_plans WHERE plan_id = @PlanId",
                new { PlanId = planId });
            if (exists is not null)
            {
                _logger.LogWarning("[PricingAdmin] create 거절(중복) planId={Id}", planId);
                return Conflict(new { success = false, message = $"요금제 코드 '{planId}' 는 이미 있습니다. 다른 코드를 쓰거나 기존 요금제를 수정해주세요." });
            }

            var changedBy = req.ChangedBy ?? "system";
            var changedByName = req.ChangedByName ?? "시스템";
            var priceDisplay = string.IsNullOrWhiteSpace(req.PriceDisplay) ? "number" : req.PriceDisplay;

            try
            {
                await db.ExecuteAsync(@"
                    INSERT INTO pricing_plans
                        (plan_id, plan_name, description, monthly_price, yearly_price, price_display,
                         max_users, max_devices, max_pc_devices, max_mobile_devices,
                         ai_token_monthly, features_json, is_visible, display_order)
                    VALUES
                        (@PlanId, @PlanName, @Description, @MonthlyPrice, @YearlyPrice, @PriceDisplay,
                         @MaxUsers, @MaxDevices, @MaxPcDevices, @MaxMobileDevices,
                         @AiTokenMonthly, @FeaturesJson, @IsVisible, @DisplayOrder)",
                    new
                    {
                        PlanId = planId,
                        req.PlanName,
                        req.Description,
                        req.MonthlyPrice,
                        req.YearlyPrice,
                        PriceDisplay = priceDisplay,
                        req.MaxUsers,
                        req.MaxDevices,
                        req.MaxPcDevices,
                        req.MaxMobileDevices,
                        req.AiTokenMonthly,
                        req.FeaturesJson,
                        IsVisible = req.IsVisible ? 1 : 0,
                        req.DisplayOrder
                    });
            }
            catch (MySqlException dup) when (dup.Number == 1062)
            {
                // PK 경주 — 눈으로 본 SELECT 와 INSERT 사이에 누가 먼저 넣었다.
                // 결론은 위 409 와 같다. 🔴 기존 행을 덮지 않는다.
                _logger.LogWarning(dup, "[PricingAdmin] create 경주(1062) planId={Id}", planId);
                return Conflict(new { success = false, message = $"요금제 코드 '{planId}' 는 이미 있습니다." });
            }

            // 이력 추적 (헌법 #3 INSERT ONLY) — PUT(:154) 과 같은 방식. before_json 은 생성이라 없다.
            await db.ExecuteAsync(@"
                INSERT INTO pricing_history (entity_type, entity_id, action, before_json, after_json, reason, changed_by, changed_by_name)
                VALUES ('plan', @PlanId, 'create', NULL, @After, @Reason, @ChangedBy, @ChangedByName)",
                new
                {
                    PlanId = planId,
                    After = JsonSerializer.Serialize(req),
                    req.Reason,
                    ChangedBy = changedBy,
                    ChangedByName = changedByName
                });

            _logger.LogInformation("[PricingAdmin] created planId={Id} name={Name} monthly={M} by={By} reason={R}",
                planId, req.PlanName, req.MonthlyPrice, changedBy, req.Reason);

            return StatusCode(201, new { success = true, planId, message = "요금제 등록 완료" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PricingAdmin] create 실패 planId={Id}", planId);
            return StatusCode(500, new { success = false, message = "등록 실패" });
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

            // 🔴 20261008작16 §B (설계 §4-2 ② · PM 결재 D-2) — 기기 두 칸만 COALESCE 로 감쌌다.
            //   화면이 두 칸을 안 보내면 NULL 이 오는데, 그대로 대입하면 **기존 설정이 지워진다**.
            //   NULL 이면 기존 값을 그대로 둔다(#1 추가만의 정신). 0 으로 덮지도 않는다 —
            //   0 은 DeviceRegistration 의 COALESCE(…,5)/(…,3) 폴백을 무력화해 상한 0 을 만든다.
            //   나머지 칸의 대입식은 한 글자도 안 바꿨다.
            await db.ExecuteAsync(@"
                UPDATE pricing_plans
                SET plan_name = @PlanName,
                    description = @Description,
                    monthly_price = @MonthlyPrice,
                    yearly_price = @YearlyPrice,
                    price_display = @PriceDisplay,
                    max_users = @MaxUsers,
                    max_devices = @MaxDevices,
                    max_pc_devices = COALESCE(@MaxPcDevices, max_pc_devices),
                    max_mobile_devices = COALESCE(@MaxMobileDevices, max_mobile_devices),
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

        // 🔴 20261008작16 §B (설계 §4-2 ② · PM 결재 D-2) — int 가 아니라 int? 다.
        //   두 칸은 pricing_plans 에 NULL DEFAULT NULL 로 더해진다(미설정 = NULL).
        //   int 로 받으면 Dapper 가 NULL 을 int 에 못 담아 **다시 500** 이다.
        //   NULL = 「미설정」이고, 기기 상한 폴백은 DeviceRegistrationController 의
        //   COALESCE(…, 5) / COALESCE(…, 3) 이 맡는다. 🔴 0 으로 바꾸면 상한이 0 이 된다.
        public int? MaxPcDevices { get; set; }
        public int? MaxMobileDevices { get; set; }
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

        // 🔴 20261008작16 §B — int? 다. 화면이 두 칸을 안 보내면(= 미설정) NULL 로 오고,
        //   UPDATE 는 COALESCE(@MaxPcDevices, max_pc_devices) 로 **기존 값을 0 으로 덮지 않는다**(#1 정신).
        public int? MaxPcDevices { get; set; }
        public int? MaxMobileDevices { get; set; }
        public int AiTokenMonthly { get; set; }
        public string? FeaturesJson { get; set; }
        public bool IsVisible { get; set; } = true;
        public int DisplayOrder { get; set; }
        public string? Reason { get; set; }
        public string? ChangedBy { get; set; }
        public string? ChangedByName { get; set; }
    }

    // 🔴 20261011작2 §B-1 — 등록 입력칸. UpdatePlanRequest 와 **같은 구성 + PlanId** 다.
    //   상속으로 안 쓴 이유: 등록에만 필요한 기본값 두 개(MaxUsers·MaxDevices)를
    //   출하 DDL 의 DEFAULT 3 과 같게 둬야 한다. 상속 프로퍼티의 기본값은 바꿀 수 없다.
    //   🔴 MaxDevices 는 화면에 안 낸다 — 집행에 안 쓰이는 표시용 레거시 칸이고
    //     (집행은 max_pc_devices / max_mobile_devices), 처음 쓰는 사람에게 묻을 칸이 아니다.
    //     그래서 화면이 안 보내고, 그때 DDL 과 같은 3 이 들어간다(0 이 조용히 들어가지 않게).
    //   🔴 IsActive 는 없다 — 포함/제외는 toggle-active 한 곳에서만 바뀐다(위 주석 참조).
    public class CreatePlanRequest
    {
        public string PlanId { get; set; } = "";
        public string PlanName { get; set; } = "";
        public string? Description { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public string PriceDisplay { get; set; } = "number";
        public int MaxUsers { get; set; } = 3;
        public int MaxDevices { get; set; } = 3;

        // 🔴 작16 §B 와 같은 이유로 int? 다. 비워 두면 NULL 로 들어가고,
        //   기기 상한 폴백은 DeviceRegistrationController 의 COALESCE(…,5)/(…,3) 가 맡는다.
        //   0 을 넣으면 그 요금제의 기기 등록이 전부 막힌다.
        public int? MaxPcDevices { get; set; }
        public int? MaxMobileDevices { get; set; }
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
