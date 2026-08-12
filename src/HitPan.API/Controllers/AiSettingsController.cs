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

    // ═════════════════════════════════════════════════════════════════
    // AI 연동 3사 확장 (2026-08-12 · 작업지시서 20260812작1 · 사장님 결재)
    //   오더: "기존 : 클로드API만 지원 -> 수정 : 클로드, 챗지피티, 제미나이API까지 받을 수 있게"
    //   위 기존 3개 엔드포인트는 그대로 둔다(헌법 #1). 아래는 공급자를 명시하는 경로다.
    // ═════════════════════════════════════════════════════════════════

    /// <summary>지정 공급자(anthropic/openai/google)의 연동 키 저장.</summary>
    [HttpPut("apikey/{providerId}")]
    public async Task<IActionResult> SaveApiKeyForProvider(
        string providerId, [FromBody] SaveApiKeyRequest req, CancellationToken ct)
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
            await _chatbot.SaveApiKeyAsync(providerId, req.ApiKey, tenantId, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok();
    }

    /// <summary>지정 공급자의 연동 키만 삭제. 다른 공급자 키는 보존된다.</summary>
    [HttpDelete("apikey/{providerId}")]
    public async Task<IActionResult> DeleteApiKeyForProvider(string providerId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _chatbot.DeleteApiKeyAsync(providerId, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    /// <summary>지금 사용할 공급자를 바꾼다(키는 건드리지 않는다).</summary>
    [HttpPut("provider/{providerId}")]
    public async Task<IActionResult> SetActiveProvider(string providerId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        try
        {
            await _chatbot.SetActiveProviderAsync(providerId, tenantId, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok();
    }

    /// <summary>
    /// 🔴 지정 공급자에 <b>실제로 연결해 본다</b>. 저장 여부만 보는 것이 아니다.
    ///    실패해도 200 으로 결과(Succeeded=false)를 돌려준다 — 사유를 화면이 그대로 보여줘야 하기 때문.
    /// </summary>
    [HttpPost("check/{providerId}")]
    public async Task<IActionResult> CheckConnection(string providerId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _chatbot.CheckConnectionAsync(providerId, tenantId, ct).ConfigureAwait(false);
        return Ok(result);
    }
}
