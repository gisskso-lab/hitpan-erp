using HitPan.Application.DTOs.Chatbot;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 히트판 AI 챗봇 API.
/// Phase A: FAQ/KB 매칭 기반 답변 + 대화 이력 축적.
/// TenantOnly — tenant_id 는 JWT 클레임에서만 추출.
/// </summary>
[ApiController]
[Route("api/chatbot")]
[Authorize(Policy = "TenantOnly")]
public sealed class ChatbotController : ControllerBase
{
    private readonly IChatbotService _chatbot;

    public ChatbotController(IChatbotService chatbot)
    {
        _chatbot = chatbot;
    }

    // ─────────────────────────────────────────────────────────────
    // 질문 → 답변
    // ─────────────────────────────────────────────────────────────
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] ChatAskRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var userId = HttpContext.Items["UserId"]?.ToString()
                     ?? User.FindFirst("sub")?.Value
                     ?? User.FindFirst("user_id")?.Value
                     ?? "unknown";

        if (req is null || string.IsNullOrWhiteSpace(req.Message))
        {
            return BadRequest(new { error = "질문 내용을 입력해주세요." });
        }

        // 봉합 (2026-06-20, 3차 전수조사 AICHAT-SEC-01-F1): 로그인 사용자의 직무권한을 정책 집합으로 만들어
        //   넘긴다. AI 직원 쓰기 Tool(거래명세서 등)은 정식 판매 경로(SalesOnly)와 동일 권한이 있어야 실행됨.
        //   조회 Tool 은 RequiredPolicy 가 없어 영향 없음(평직원도 챗봇 질문·조회 가능).
        var policies = BuildPolicySet();
        var result = await _chatbot.AskAsync(req, tenantId, userId, policies, ct).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>JWT role 클레임을 챗봇 Tool 게이트용 정책 집합으로 변환(정식 정책 정의와 동일 role 매핑).</summary>
    private IReadOnlySet<string> BuildPolicySet()
    {
        // 봉합 (2026-06-23, 5차 전수조사 PERM-02): 종전엔 SalesOnly 한 정책만 매핑해, 향후 PurchaseOnly·
        //   AccountOnly·HROnly 등을 RequiredPolicy 로 요구하는 AI Tool 이 추가되면 권한 있는 매니저조차
        //   ctx.Policies 에 정책이 안 담겨 fail-closed 로 차단됐다(정당 사용자 차단). Program.cs 의 role 기반
        //   정책 정의(237~248)와 단일 출처로 동기화한다 — 새 RequiredPolicy Tool 추가 시 누락 위험 제거(헌법 #12).
        var set = new HashSet<string>(StringComparer.Ordinal);

        // Program.cs RequireRole 정책과 1:1 일치하는 매핑 테이블.
        var policyRoles = new (string Policy, string[] Roles)[]
        {
            ("SalesOnly",    new[] { "system_admin", "sales_manager", "sales_user", "TenantAdmin", "tenant_admin" }),
            // W1-4 동기화 (작업지시서 20260707작2, 검증팀 SoD 적발): Program.cs SalesManager 정책에
            //   tenant_admin 이 추가됨 — 이 미러 테이블은 "정책 정의와 1:1 일치" 계약이므로 함께 갱신.
            ("SalesManager", new[] { "system_admin", "sales_manager", "TenantAdmin", "tenant_admin" }),
            ("PurchaseOnly", new[] { "system_admin", "purchase_manager", "TenantAdmin", "tenant_admin" }),
            ("AccountOnly",  new[] { "system_admin", "account_manager", "TenantAdmin", "tenant_admin" }),
            ("HROnly",       new[] { "system_admin", "hr_manager", "TenantAdmin", "tenant_admin" }),
            ("AdminOnly",    new[] { "system_admin" }),
        };

        foreach (var (policy, roles) in policyRoles)
        {
            if (roles.Any(User.IsInRole))
                set.Add(policy);
        }
        return set;
    }

    // ─────────────────────────────────────────────────────────────
    // AI 직원 초안 승인 — Lv.3 워크플로우 연쇄 (거래명세서 확정 + 수주 자동생성)
    //   사람이 승인 버튼을 눌렀을 때만 호출(확정은 사람 — 헌법 #6).
    //   진범 봉합 (2026-06-20, 2차 전수조사 AICHAT-SEC-01 P1):
    //     이 경로는 거래명세서 확정(원장·재고 반영)+수주 자동생성을 실행한다 = 실제 판매 업무.
    //     컨트롤러 기본 정책 TenantOnly 는 평직원(tenant_user)도 통과시켜, 판매 직무권한이 없는
    //     직원이 챗봇으로 SalesController(SalesOnly) 게이트를 우회해 판매를 확정할 수 있었다(헌법 #7 위반).
    //     정식 판매 엔드포인트와 동일하게 SalesOnly 직무권한 게이트를 메서드 레벨로 적용해 우회를 차단.
    //     (조회·질문(ask)은 TenantOnly 유지 — 평직원도 챗봇 질문은 가능해야 함.)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("approve-action")]
    [Authorize(Policy = "SalesOnly")]
    public async Task<IActionResult> ApproveAction([FromBody] ApproveActionRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var userId = HttpContext.Items["UserId"]?.ToString()
                     ?? User.FindFirst("sub")?.Value
                     ?? User.FindFirst("user_id")?.Value
                     ?? "unknown";

        if (req is null || string.IsNullOrWhiteSpace(req.DraftId))
        {
            return BadRequest(new { error = "승인할 초안 정보가 없습니다." });
        }

        var result = await _chatbot.ApproveActionAsync(req, tenantId, userId, ct).ConfigureAwait(false);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────
    // 답변 피드백 (도움됨/도움안됨)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback([FromBody] ChatFeedbackRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        if (req is null || string.IsNullOrWhiteSpace(req.ConvId))
        {
            return BadRequest(new { error = "conv_id 가 필요합니다." });
        }

        await _chatbot.RecordFeedbackAsync(req, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    // ─────────────────────────────────────────────────────────────
    // 토큰 할당량 조회
    // ─────────────────────────────────────────────────────────────
    [HttpGet("quota")]
    public async Task<IActionResult> Quota(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var dto = await _chatbot.GetQuotaAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    // ─────────────────────────────────────────────────────────────
    // 이번 달 토큰 사용량 집계 (ai_usage_logs / ym 기준)
    // ─────────────────────────────────────────────────────────────
    [HttpGet("usage")]
    public async Task<IActionResult> Usage(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var dto = await _chatbot.GetUsageAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    // ─────────────────────────────────────────────────────────────
    // KB 검색
    // ─────────────────────────────────────────────────────────────
    [HttpGet("kb")]
    public async Task<IActionResult> SearchKb(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        // limit 기본값 20, 1~50 범위
        if (limit <= 0)
        {
            limit = 20;
        }

        var rows = await _chatbot.SearchKbAsync(q ?? string.Empty, category, limit, ct).ConfigureAwait(false);
        return Ok(rows);
    }

    // ─────────────────────────────────────────────────────────────
    // 인기 KB
    // ─────────────────────────────────────────────────────────────
    [HttpGet("kb/popular")]
    public async Task<IActionResult> PopularKb([FromQuery] int limit, CancellationToken ct)
    {
        if (limit <= 0)
        {
            limit = 10;
        }

        var rows = await _chatbot.GetPopularKbAsync(limit, ct).ConfigureAwait(false);
        return Ok(rows);
    }
}
