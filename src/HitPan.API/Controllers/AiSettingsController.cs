using HitPan.Application.DTOs.Chatbot;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 히트판 AI 도우미 연동(BYOK) 설정 API.
/// 고객사 보유 키를 AES-256 암호화하여 local_subscription 에 저장한다(키 호출은 고객 PC 직통, 본사 프록시 없음).
/// 키 설정은 관리자 전용 — TenantAdminOnly. tenant_id 는 JWT 클레임에서만 추출(헌법 #2).
/// </summary>
[ApiController]
[Route("api/ai-settings")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class AiSettingsController : ControllerBase
{
    private readonly IChatbotService _chatbot;

    public AiSettingsController(IChatbotService chatbot)
    {
        _chatbot = chatbot;
    }

    // ─────────────────────────────────────────────────────────────
    // 설정 현황 조회 (키 설정 여부·last4·한도) — 평문 키 반환 없음
    // ─────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var dto = await _chatbot.GetAiSettingsAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    // ─────────────────────────────────────────────────────────────
    // 연동 키 저장 (AES-256 암호화)
    // ─────────────────────────────────────────────────────────────
    [HttpPut("apikey")]
    public async Task<IActionResult> SaveApiKey([FromBody] SaveApiKeyRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        if (req is null || string.IsNullOrWhiteSpace(req.ApiKey))
        {
            return BadRequest(new { error = "연동 키를 입력해주세요." });
        }

        try
        {
            await _chatbot.SaveApiKeyAsync(req.ApiKey, tenantId, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok();
    }

    // ─────────────────────────────────────────────────────────────
    // 연동 키 삭제
    // ─────────────────────────────────────────────────────────────
    [HttpDelete("apikey")]
    public async Task<IActionResult> DeleteApiKey(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _chatbot.DeleteApiKeyAsync(tenantId, ct).ConfigureAwait(false);
        return Ok();
    }
}
