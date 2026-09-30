using HitPan.API.Services.ManualUpdate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 2026-09-30 작1 갈래 U — 자료관리 「최신 버전 확인/업데이트」(수동 업데이트) API. 설계 §13 · 작업지시서 §9·§10.
///
/// ■ 용어(사장님 9/30 정의) — 자동 = 로그인 때 팝업으로 · 수동 = 메뉴로 직접. 이 컨트롤러는 <b>수동</b>이다.
///   자동 경로(<c>update-consent</c>·<c>update-consent-local</c>·팝업)는 건드리지 않는다(G-43).
///
/// ■ 문(G-U1) — 자료관리와 같은 두 줄(<c>BackupController</c>):
///   · <c>TenantAdminOnly</c> — 관리자(부모계정)만 · 익명은 인증 단계에서 401
///   · <c>[MainPcOnly]</c> — 메인컴퓨터만(403 <c>main_pc_only</c>) · 사장님 「히트판 시스템과 데이터의 제어는 메인컴퓨터에 관리자만」
///   · 주소 <c>/api/manual-update</c> 는 <c>TenantMiddleware</c> 익명 통과 목록의 어느 접두사 밑에도 없다(병렬이슈 [3-V] 04 확인).
///   세 경우 모두 서비스까지 오지 않는다 ⇒ 다운로드·요청서·작업 등록 0.
///
/// ■ 요청 본문에서 판·경로를 받지 않는다(병렬이슈 [3-V] 04) — 받는 것은 입구(menu|login) 하나. 판은 서버가 피드·설치 판으로 계산.
/// </summary>
[ApiController]
[Route("api/manual-update")]
[Authorize(Policy = "TenantAdminOnly")]
[HitPan.API.Security.MainPcOnly]
public sealed class ManualUpdateController : HitPanControllerBase
{
    private readonly ManualUpdateService _service;

    public ManualUpdateController(ManualUpdateService service)
    {
        _service = service;
    }

    /// <summary>최신 버전 확인 — 피드·서명·판 비교만(받기·백업·기록 0).</summary>
    [HttpGet("check")]
    public async Task<IActionResult> Check(CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var result = await _service.CheckAsync(ct).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>[예] — 수동 업데이트 시작. 받아들이면 202 + 진행 상태, 아니면 409 + 사유 코드.</summary>
    [HttpPost("apply")]
    public IActionResult Apply([FromBody] ManualUpdateApplyRequest? request)
    {
        if (EnsureTenant() is { } err) return err;
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();

        var outcome = _service.Start(TenantId!, UserId, request?.Entry ?? "menu");
        if (outcome.Accepted)
            return Accepted(outcome.Job);
        return Conflict(new { reason = outcome.Reason });
    }

    /// <summary>진행 상태 — 없으면 204.</summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        if (EnsureTenant() is { } err) return err;
        var status = _service.GetStatus(TenantId!);
        return status is null ? NoContent() : Ok(status);
    }
}

/// <summary>수동 업데이트 요청 본문 — 입구만(menu|login). 판·경로 칸은 두지 않는다.</summary>
public sealed class ManualUpdateApplyRequest
{
    public string? Entry { get; set; }
}
