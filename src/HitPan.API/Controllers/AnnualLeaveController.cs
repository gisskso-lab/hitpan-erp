using HitPan.API.Authorization;
using HitPan.Application.DTOs.Leave;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HitPan.API.Controllers;

/// <summary>
/// 연차 엔진 API. 작(2026-08-13) 그룹웨어 단계5.
/// </summary>
/// <remarks>
/// 🔴 반자동 3단 — <c>suggest</c>(제안) 는 <b>저장하지 않고</b>,
/// <c>confirm</c>(확정) 을 불러야 비로소 잔여에 반영된다.
/// <para>
/// 🔴 <b>작(2026-08-24) 작2 [3] — 권한을 붙였다.</b> 사장님: <i>"권한에 연차부여 권한 추가"</i>
/// </para>
/// <para>
/// 종전엔 <c>TenantAdminOnly</c> 하나였다 — <b>대표만</b> 연차를 줄 수 있었다는 뜻이다.
/// 경리·인사 직원에게 맡기려면 대표 계정을 빌려주는 수밖에 없었다.
/// 사장님이 8/21 에 짚으신 것과 같은 자리다: <i>"경리없이 대표가 직접 경리업무 보는
/// 소규모 회사도 있고"</i> — 반대로 <b>경리가 따로 있는 회사</b>가 막혀 있었다.
/// </para>
/// <para>
/// ⇒ <c>TenantOnly</c> + <c>ANNUAL_LEAVE_GRANT</c> 권한으로 바꾼다.
/// 부모계정은 <c>PermissionService</c> 가 권한 조회 <b>전에</b> 통과시키므로(락아웃 방지)
/// <b>대표는 종전과 똑같이 된다.</b> 달라지는 것은 "대표가 남에게 맡길 수 있다" 뿐이다.
/// </para>
/// <para>
/// 🔴 기본 OFF 다(헌법 #11). 안 켜면 대표만 쓰는 종전 상태 그대로다 — 회귀 0.
/// </para>
/// </remarks>
[ApiController]
[Route("api/annual-leave")]
[Authorize(Policy = "TenantOnly")]
[RequirePermission("ANNUAL_LEAVE_GRANT", "view")]
public sealed class AnnualLeaveController : ControllerBase
{
    private readonly IAnnualLeaveService _service;

    public AnnualLeaveController(IAnnualLeaveService service)
    {
        _service = service;
    }

    /// <summary>① 제안 — 계산해서 보여만 준다. 저장하지 않는다.</summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest([FromQuery] int year, [FromQuery] string? employeeId,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var grantYear = year > 0 ? year : DateTime.Today.Year;
        var list = await _service.SuggestAsync(tenantId, grantYear, employeeId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>②③ 수정 + 확정 — 사람이 정한 일수를 저장하고 잔여에 반영한다.</summary>
    /// <remarks>
    /// 🔴 여기만 <c>update</c> 를 요구한다. 위 클래스 속성은 <c>view</c> 다.
    /// <b>보는 것과 주는 것을 가른다</b> — 연차는 돈이다(미사용분 수당).
    /// 인사 담당이 현황만 보게 하고 확정은 대표가 하는 회사가 있다.
    /// 한 칸으로 묶으면 그 회사는 <b>보여주려면 주는 권한까지</b> 줘야 한다.
    /// </remarks>
    [HttpPost("confirm")]
    [RequirePermission("ANNUAL_LEAVE_GRANT", "update")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmAnnualLeaveRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var actor = User.FindFirstValue("employee_id") ?? "unknown";

        try
        {
            var grantId = await _service.ConfirmAsync(tenantId, actor, request, ct).ConfigureAwait(false);
            return Ok(new { grantId });
        }
        catch (InvalidOperationException ex)
        {
            // 🔴 "사유를 남겨야 확정할 수 있습니다" 같은 사유를 그대로 전한다.
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>부여 이력 — 언제 누가 얼마를 왜 정했는지.</summary>
    [HttpGet("grants")]
    public async Task<IActionResult> Grants([FromQuery] int? year, [FromQuery] string? employeeId,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var list = await _service.GetGrantsAsync(tenantId, year, employeeId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>노무 기준값 — 법이 바뀌면 여기 값을 갈아끼운다.</summary>
    [HttpGet("policies")]
    public async Task<IActionResult> Policies([FromQuery] DateTime? asOf, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var list = await _service.GetPoliciesAsync(tenantId, asOf, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>기준값을 고친다. 새 시행일로 행이 추가된다(옛 값은 남는다).</summary>
    [HttpPost("policies")]
    public async Task<IActionResult> SavePolicy([FromBody] SaveLaborPolicyRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var actor = User.FindFirstValue("employee_id") ?? "unknown";

        try
        {
            var policyId = await _service.SavePolicyAsync(tenantId, actor, request, ct).ConfigureAwait(false);
            return Ok(new { policyId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
