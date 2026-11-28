using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 🔴 W-G1-52 갈래 C 고침 1 X2(설계 R16 ⑤ · [3-V] P2-1) — 대표 다음 로그인 때 「대표 비밀번호가 ○월 ○일 ○시 메인PC 에서 다시 정해졌습니다」 1줄.
/// </summary>
/// <remarks>
/// <para>🔴 로그인한 사람만(<c>TenantOnly</c>) — 재설정 길(<c>/api/parent-reset</c> · 익명)과 <b>다른 주소</b>다.
/// <c>TenantMiddleware</c> 의 토큰 없는 길 목록은 경로 <b>마디</b>로 비교하므로 <c>/api/parent-reset-notice</c> 는 그 목록에 안 걸린다(JWT 대조를 그대로 지남).</para>
/// <para>🔴 #2 — 회사 · 사용자 id 는 JWT(미들웨어가 넣은 값)에서만. 대표가 아닌 사람은 표 행이 없어 늘 빈 값.</para>
/// <para>한 번만 — 알린 뒤 다시 부르면 빈 값(DB <c>parent_reset_tokens.notified_at</c>).</para>
/// </remarks>
[ApiController]
[Route("api/parent-reset-notice")]
[Authorize(Policy = "TenantOnly")]
public sealed class ParentResetNoticeController : ControllerBase
{
    private readonly IParentPasswordResetService _service;

    /// <summary>만든다.</summary>
    public ParentResetNoticeController(IParentPasswordResetService service) => _service = service;

    /// <summary>아직 안 알린 다시 정함이 있으면 그 시각(UTC)을 돌려주고 「알림」 표시. 없으면 <c>resetAtUtc = null</c>.</summary>
    [HttpPost("take")]
    public async Task<IActionResult> Take(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Unauthorized();
        var at = await _service.TakeResetNoticeAsync(tenantId, userId, ct);
        return Ok(new { resetAtUtc = at });
    }
}
