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
    // 🔴 W-S-01 갈래 A (2026-11-27 · 작업지시서 20261127작 §2 · 설계 §2-2 · PM 결재 P3 ① · P4) — 01a-10 도우미 연동 서버 닫음.
    //   [NonAction] — 서버 라우트에 등록하지 않는다(부르면 404). 닫는 액션 = 이 GET · PUT apikey · PUT apikey/{providerId} ·
    //   PUT provider/{providerId} · POST check/{providerId} 5개. **DELETE 2개(apikey · apikey/{providerId})는 남긴다** —
    //   이미 저장된 키를 지울 길이다. 아래 속성·본문은 보존한다(#1 · 다시 열 때 = [NonAction] 한 줄 뗌 · 정식 판).
    [NonAction]
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
    // W-S-01 갈래 A — 01a-10 PUT apikey 닫음(위 GET 주석과 같음 · 본문 보존 #1).
    [NonAction]
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
    // W-S-01 갈래 A — 01a-10 PUT apikey/{providerId} 닫음(본문 보존 #1).
    [NonAction]
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
    // W-S-01 갈래 A — 01a-10 PUT provider/{providerId} 닫음(본문 보존 #1).
    [NonAction]
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
    // W-S-01 갈래 A — 01a-10 POST check/{providerId} 닫음(본문 보존 #1) · 이 액션이 CheckConnectionAsync 의 유일한 호출자였다.
    [NonAction]
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
