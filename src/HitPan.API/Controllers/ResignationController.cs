using HitPan.API.Authorization;
using HitPan.Application.DTOs.Employee;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 전자 퇴직서(사직서) — 작20260824작2 [4].
/// </summary>
/// <remarks>
/// <para>
/// 사장님 지시(2026-08-24): <i>"전자근로계약서 = 입사/퇴사 로 메뉴변경
/// 전자근로계약서 작성, 전자 퇴직서 작성"</i>
/// </para>
/// <para>
/// 🔴 <b>관리자 퇴사 처리와 다른 자리다.</b> 그쪽(<c>EmployeeController</c>)은 관리자가 결과를
/// 찍는 것이고, 여기는 <b>직원이 문서를 올리는</b> 자리다. 수리될 때 그 로직을 <b>부른다</b>.
/// </para>
/// <para>
/// 🔴 <c>employee_id</c> 축이다(6/21 A-P0-1 과 같은 자리). <c>user_id</c> 를 쓰면 목록이 빈다.
/// </para>
/// </remarks>
[ApiController]
[Route("api/resignations")]
[Authorize(Policy = "TenantOnly")]
public sealed class ResignationController : ControllerBase
{
    private readonly IResignationService _service;
    private readonly ILogger<ResignationController> _logger;

    public ResignationController(IResignationService service, ILogger<ResignationController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// 목록.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>권한 없는 직원도 자기 것은 본다.</b> 자기가 낸 사직서를 못 보게 하면
    /// 결재가 어디까지 갔는지 알 길이 없다 — 그건 차단이 아니라 고장이다.
    /// 권한(<c>RESIGNATION</c> view)이 있어야 <b>남의 것</b>까지 본다.
    /// <para>
    /// ⚠️ 범위를 <b>서버가</b> 정한다. 화면이 보내는 값으로 정하면 요청을 고쳐 남의 것을 본다.
    /// </para>
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();

        var canSeeAll = await HasResignationViewAsync(ct);
        return Ok(await _service.GetListAsync(tenantId, employeeId, onlyMine: !canSeeAll, ct));
    }

    /// <summary>상세.</summary>
    [HttpGet("{resignationId}")]
    public async Task<IActionResult> Get(string resignationId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();

        var doc = await _service.GetAsync(resignationId, tenantId, ct);
        if (doc is null) return NotFound();

        // 🔴 남의 사직서는 권한이 있어야 본다. 주소만 알면 열리는 일이 없게 한다.
        if (doc.EmployeeId != employeeId && !await HasResignationViewAsync(ct))
        {
            return Forbid();
        }

        return Ok(doc);
    }

    /// <summary>작성·수정(작성중 상태에서만).</summary>
    /// <remarks>
    /// 🔴 <b>남의 명의로 못 쓴다.</b> 권한이 없으면 자기 <c>employee_id</c> 로 강제 치환한다 —
    /// 2026-06-23 LV-W-03 과 같은 자리다(그때 타인 명의 연차 신청이 가능했다).
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveResignationRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();

        // 인사 담당(권한자)은 대리 작성이 된다 — 권고사직·계약만료는 회사가 쓰는 문서다.
        if (!await HasResignationViewAsync(ct))
        {
            request.EmployeeId = employeeId;
        }
        else if (string.IsNullOrEmpty(request.EmployeeId))
        {
            request.EmployeeId = employeeId;
        }

        try
        {
            var id = await _service.SaveAsync(request, tenantId, employeeId, ct);
            return Ok(new { resignationId = id });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "사직서 저장 거부 employeeId={EmployeeId}", request.EmployeeId);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>제출 — 결재를 올린다.</summary>
    /// <remarks>
    /// 🔴 결재를 <b>못 걸면 이유를 돌려준다</b>(400). 조용히 200 을 주면 직원은 냈다고 보는데
    /// 결재함엔 안 뜬다 — 8/21 휴직 P0 가 정확히 그 자리였다.
    /// </remarks>
    [HttpPost("{resignationId}/submit")]
    public async Task<IActionResult> Submit(string resignationId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();

        var doc = await _service.GetAsync(resignationId, tenantId, ct);
        if (doc is null) return NotFound();
        if (doc.EmployeeId != employeeId && !await HasResignationViewAsync(ct)) return Forbid();

        try
        {
            var blocker = await _service.SubmitAsync(resignationId, tenantId, employeeId, ct);
            if (blocker is not null) return BadRequest(new { message = blocker });
            return Ok(new { message = "사직서를 제출했습니다." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "사직서 제출 거부 id={Id}", resignationId);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>철회 — 본인이 거둬들인다.</summary>
    [HttpPost("{resignationId}/withdraw")]
    public async Task<IActionResult> Withdraw(string resignationId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();

        var doc = await _service.GetAsync(resignationId, tenantId, ct);
        if (doc is null) return NotFound();

        // 🔴 철회는 **본인만** 한다. 남이 대신 거둬들이면 그건 반려다 — 다른 말이어야 한다.
        if (doc.EmployeeId != employeeId) return Forbid();

        try
        {
            await _service.WithdrawAsync(resignationId, tenantId, employeeId, ct);
            return Ok(new { message = "사직서를 철회했습니다." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "사직서 철회 거부 id={Id}", resignationId);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>수리 — 회사가 실제 퇴사일을 정해 확정한다.</summary>
    /// <remarks>
    /// 🔴 <c>update</c> 권한을 요구한다. 사직서를 <b>보는 것</b>과 <b>퇴사를 확정하는 것</b>은 다르다 —
    /// 확정되면 그 사람의 계정·결재선이 정리된다(되돌리기 어렵다).
    /// </remarks>
    [HttpPost("{resignationId}/accept")]
    [RequirePermission("RESIGNATION", "update")]
    public async Task<IActionResult> Accept(string resignationId,
        [FromBody] AcceptResignationRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(employeeId)) return Forbid();

        try
        {
            await _service.AcceptAsync(resignationId, request, tenantId, employeeId, ct);
            return Ok(new { message = "사직서를 수리하고 퇴사 처리했습니다." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "사직서 수리 거부 id={Id}", resignationId);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 이 사람이 <b>남의 사직서까지</b> 볼 수 있나 — <c>RESIGNATION</c> view 권한.
    /// </summary>
    /// <remarks>
    /// 🔴 속성(<c>[RequirePermission]</c>)으로 막지 않고 <b>물어본다.</b>
    /// 속성으로 막으면 권한 없는 직원이 <b>자기 사직서도</b> 못 본다.
    /// </remarks>
    private Task<bool> HasResignationViewAsync(CancellationToken ct)
    {
        var permSvc = HttpContext.RequestServices.GetService<IPermissionService>();
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();

        if (permSvc is null || string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(false);
        }

        // ⚠️ 인자 순서가 (userId, tenantId) 다 — 반대로 넣으면 조회가 조용히 0건이 되고
        //    권한이 있는 사람도 "없다" 로 판정된다. 실측으로 확인했다.
        return permSvc.HasPermissionAsync(userId, tenantId, "RESIGNATION", "view", ct);
    }
}
