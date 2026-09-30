using HitPan.API.Services.LocalRollback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 자료관리 「이전 버전으로 되돌리기」(수동 되돌리기) 입구 — 20260930작1 갈래 A · 설계 §13-4.
/// </summary>
/// <remarks>
/// <para>문 = <c>TenantAdminOnly</c> + <c>[MainPcOnly]</c>(<c>BackupController</c> 와 같은 두 줄). 메뉴 숨김은 편의 · 막는 것은 여기.
/// 터널로 들어온 요청은 <c>MainPcOnly</c> 판정 그대로 403 <c>main_pc_only</c>. 익명 입구·로그인창 변형 0(병렬이슈 03 전례).</para>
/// <para>주소 <c>/api/system/local-rollback</c> 은 <c>TenantMiddleware</c> 익명 통과 목록의 어느 접두사 밑에도 없다(병렬이슈 04).</para>
/// <para>🔴 요청 본문은 1회용 확인 번호 하나뿐 — 경로·명령·판을 받지 않는다(병렬이슈 04).</para>
/// </remarks>
[ApiController]
[Route("api/system/local-rollback")]
[Authorize(Policy = "TenantAdminOnly")]
[HitPan.API.Security.MainPcOnly]
public sealed class LocalRollbackController : HitPanControllerBase
{
    private readonly ILocalRollbackService _service;

    public LocalRollbackController(ILocalRollbackService service)
    {
        _service = service;
    }

    /// <summary>지금 되돌릴 수 있나 · 되돌릴 판 · 확인 번호 · 마지막 교체 결과.</summary>
    [HttpGet]
    public IActionResult GetStatus()
    {
        if (EnsureTenant() is { } err) return err;
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        return Ok(_service.GetStatus(UserId));
    }

    /// <summary>[예] — 확인 번호만 받는다. 시작되면 202(곧 히트판이 멈춘다 · 결과는 다시 로그인한 뒤 GET 의 <c>last</c>).</summary>
    [HttpPost]
    public IActionResult Start([FromBody] LocalRollbackStartBody? body)
    {
        if (EnsureTenant() is { } err) return err;
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = _service.Start(UserId, body?.Ticket);
        if (result.Started) return Accepted(new { started = true, reason = result.Reason });
        return Conflict(new { started = false, reason = result.Reason });
    }
}

/// <summary>[예] 요청 본문 — 확인 번호 하나(그 밖의 칸은 받지 않는다).</summary>
public sealed class LocalRollbackStartBody
{
    public string? Ticket { get; set; }
}
