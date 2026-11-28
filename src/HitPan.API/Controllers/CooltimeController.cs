using HitPan.API.Services.Cooltime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 쿨타임 팝업이 부르는 API 3개 — 20260928작4 절D · 설계 §3 · §4 · §5-2.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 테넌트·사용자 = JWT(<c>HttpContext.Items</c> · <c>TenantMiddleware</c>) · 세션 번호 = JWT <c>sid</c> 클레임 — <b>요청 본문·주소로 받지 않는다</b>(#2).
/// </para>
/// <para>
/// 기존 <c>AuthController</c> 에 끼우지 않고 따로 둔 이유: 손 로그아웃 무접촉(설계 §4) · 생성자 무접촉(#1).
/// 주소 머리는 <c>api/auth/cooltime</c> 로 작업지시서 절D 그대로다.
/// ⚠️ 이 세 주소는 토큰이 <b>반드시</b> 있어야 한다 — <c>TenantMiddleware</c>·<c>TermsConsentMiddleware</c> 면제 목록에 넣지 않았다.
/// 미들웨어를 실제로 지나 200/409 가 나는지는 게이트 G-C14(<c>WebApplicationFactory</c>)가 잰다.
/// </para>
/// </remarks>
[ApiController]
[Route("api/auth/cooltime")]
[Authorize]
public sealed class CooltimeController : ControllerBase
{
    private readonly CooltimeEngine _engine;
    private readonly ILogger<CooltimeController> _logger;

    public CooltimeController(CooltimeEngine engine, ILogger<CooltimeController> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// [예 — 계속 작업] — 1시간 뒤 다시 묻는다(Q2). 이미 끝났으면 409.
    /// </summary>
    [HttpPost("continue")]
    public async Task<IActionResult> Continue(CancellationToken ct)
    {
        if (!TryReadIdentity(out var tenantId, out var userId, out var sid)) return Unauthorized(new { message = "다시 로그인해 주세요" });
        // 세션 번호가 없는 토큰(옛 토큰 · 세션 기록 실패) = 세션 행이 원래 없다 ⇒ 서버가 물은 적도 없다(작2 절C 와 같은 판단).
        if (sid.Length == 0) return Conflict(new { message = "이미 로그아웃되었습니다", code = "cooltime_already_ended" });

        var ok = await _engine.ContinueAsync(tenantId, sid, userId, ct).ConfigureAwait(false);
        if (!ok)
        {
            return Conflict(new { message = "이미 로그아웃되었습니다", code = "cooltime_already_ended" });
        }
        return Ok(new { snoozeMinutes = 60 });
    }

    /// <summary>
    /// [아니요 — 지금 로그아웃] 또는 카운트다운 0 — 끝내기 함수 하나(설계 §4)를 부른다.
    /// 이미 끝났어도 200(<c>ended=false</c>) — 화면은 어느 쪽이든 저장소를 비우고 로그인 화면으로 간다.
    /// 🆕 11/27 고침 차례(§273 V1 · §275 E-5) — 위 줄의 화면 동작은 바뀌었다: <c>ended=false</c> 면 화면이 저장소를 비우지 않고
    /// <c>GET state</c> 로 다시 확인한다(다른 탭이 [계속] 했거나 · 팝업 중 끔 = 팝업만 닫음). 물음이 65초를 넘긴 뒤의 요청도
    /// <c>ended=false</c>(끝내기 = 서버 작업 ① 몫 · 설계 §E).
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CooltimeLogoutRequest? request,
        CancellationToken ct)
    {
        if (!TryReadIdentity(out var tenantId, out var userId, out var sid)) return Unauthorized(new { message = "다시 로그인해 주세요" });
        // 세션 번호가 없는 토큰(옛 토큰 · 세션 기록 실패) = 세션 행이 원래 없다 ⇒ 서버가 물은 적도 없다(작2 절C 와 같은 판단).
        if (sid.Length == 0) return Ok(new { ended = false });

        var cause = string.Equals(request?.Trigger, "countdown", StringComparison.OrdinalIgnoreCase)
            ? CooltimeEndCause.Countdown
            : CooltimeEndCause.UserChoice;
        try
        {
            var r = await _engine.CooltimeLogoutAsync(tenantId, sid, DateTime.Now, cause, jwtUserId: userId, ct: ct)
                .ConfigureAwait(false);
            if (r.Outcome == CooltimeEndOutcome.Deadlock)
            {
                // 서버 작업 ①(물은 뒤 65초)이 다시 끝낸다 — 화면은 로그인 화면으로 간다.
                return StatusCode(503, new { message = "로그아웃이 완료되지 않았습니다", code = "cooltime_retry" });
            }
            return Ok(new { ended = r.Outcome == CooltimeEndOutcome.Ended });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // #15 — 손 로그아웃과 같이 실패는 500 으로 알린다(200 금지 · 작2 설계 §13-4). 서버 작업 ①이 다시 끝낸다.
            _logger.LogWarning(ex, "[쿨타임] 로그아웃 실패 — UserId: {UserId}", userId);
            return StatusCode(500, new { message = "로그아웃이 완료되지 않았습니다" });
        }
    }

    /// <summary>놓친 물음 확인 — 화면이 다시 연결될 때 부른다(설계 §5-2).</summary>
    [HttpGet("state")]
    public async Task<IActionResult> State(CancellationToken ct)
    {
        if (!TryReadIdentity(out var tenantId, out var userId, out var sid)) return Unauthorized(new { message = "다시 로그인해 주세요" });
        // 세션 번호가 없는 토큰(옛 토큰 · 세션 기록 실패) = 세션 행이 원래 없다 ⇒ 서버가 물은 적도 없다(작2 절C 와 같은 판단).
        if (sid.Length == 0) return Ok(new CooltimeStateResponse(false, 0, null));

        var s = await _engine.GetStateAsync(tenantId, sid, userId, ct).ConfigureAwait(false);
        return Ok(s);
    }

    private bool TryReadIdentity(out string tenantId, out string userId, out string sid)
    {
        tenantId = HttpContext.Items["TenantId"]?.ToString() ?? string.Empty;
        userId = HttpContext.Items["UserId"]?.ToString() ?? string.Empty;
        sid = User.FindFirst("sid")?.Value ?? string.Empty;
        return tenantId.Length > 0 && userId.Length > 0;
    }
}
