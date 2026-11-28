using HitPan.API.Security;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 🔴 W-G1-52 갈래 C — 대표 비번 되찾기 재설정 API(설계 §8-4 3 · 덧 지시서 §2).
/// </summary>
/// <remarks>
/// <para>🔴 <b>로그인 전 길</b>이라 토큰이 없다 → <c>[AllowAnonymous]</c> + <c>TenantMiddleware</c> 통과 목록에 이 주소 한 줄(그 파일 주석 「토큰 없이 부르는 길을 새로 낼 때는 이 목록을 먼저 본다」).</para>
/// <para>🔴 <c>[MainPcOnly]</c> — 메인PC(로컬 직결)에서 온 요청만. 그래도 <b>본인 확인의 몸은 1회용 표</b>다(윈도우 관리자 승인을 지난 명령만 만든다 · 10분 · 1회 · DB 엔 지문만).</para>
/// <para>🔴 #2 — 회사 · 대표 id 를 요청에서 받지 않는다(표 행에서만). 🔴 #40 — 응답 · 로그에 비번 · 표 값 0.</para>
/// <para>AuthController 에 넣지 않은 까닭 = 크기(작업지시서 §4 C 행 「새 컨트롤러 1」).</para>
/// </remarks>
[ApiController]
[Route("api/parent-reset")]
[AllowAnonymous]
[MainPcOnly]
public sealed class ParentResetController : ControllerBase
{
    private readonly IParentPasswordResetService _service;

    /// <summary>만든다.</summary>
    public ParentResetController(IParentPasswordResetService service) => _service = service;

    /// <summary>표로 대표 아이디를 본다(소비 0 · 감사 0). 못 쓰는 표면 400 + 한 문구.</summary>
    [HttpPost("peek")]
    public async Task<IActionResult> Peek([FromBody] PeekRequest? body, CancellationToken ct)
    {
        var r = await _service.PeekAsync(body?.Token, ct);
        if (!r.Usable)
            return BadRequest(new { error = "token_rejected", message = HitPan.Application.Services.ParentPasswordResetService.TokenRejectedMessage });
        return Ok(new { loginId = r.LoginId });
    }

    /// <summary>새 비번으로 바꾼다.</summary>
    [HttpPost]
    public async Task<IActionResult> Reset([FromBody] ResetRequest? body, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var r = await _service.ResetAsync(new ParentResetRequest(body?.Token, body?.NewPassword, body?.ConfirmPassword), ip, ct);
        return r.Status switch
        {
            ParentResetStatus.Succeeded => Ok(new { ok = true, message = r.Message }),
            ParentResetStatus.AuditUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "audit_unavailable", message = r.Message }),
            ParentResetStatus.TokenRejected => BadRequest(new { error = "token_rejected", message = r.Message }),
            ParentResetStatus.PasswordMismatch => BadRequest(new { error = "password_mismatch", message = r.Message }),
            _ => BadRequest(new { error = "password_rule", message = r.Message }),
        };
    }

    /// <summary>보기 요청.</summary>
    public sealed class PeekRequest
    {
        /// <summary>1회용 표(주소 <c>#</c> 뒤에서 화면이 꺼냄).</summary>
        public string? Token { get; set; }
    }

    /// <summary>재설정 요청.</summary>
    public sealed class ResetRequest
    {
        /// <summary>1회용 표.</summary>
        public string? Token { get; set; }
        /// <summary>새 비번.</summary>
        public string? NewPassword { get; set; }
        /// <summary>새 비번 다시.</summary>
        public string? ConfirmPassword { get; set; }
    }
}
