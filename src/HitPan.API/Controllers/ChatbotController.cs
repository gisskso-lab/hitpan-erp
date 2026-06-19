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

        var result = await _chatbot.AskAsync(req, tenantId, userId, ct).ConfigureAwait(false);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────
    // AI 직원 초안 승인 — Lv.3 워크플로우 연쇄 (거래명세서 확정 + 수주 자동생성)
    //   사람이 승인 버튼을 눌렀을 때만 호출(확정은 사람 — 헌법 #6).
    // ─────────────────────────────────────────────────────────────
    [HttpPost("approve-action")]
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
