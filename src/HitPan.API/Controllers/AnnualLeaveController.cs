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
/// 연차 부여는 인사 권한이라 관리자만 한다.
/// </remarks>
[ApiController]
[Route("api/annual-leave")]
[Authorize(Policy = "TenantAdminOnly")]
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
    [HttpPost("confirm")]
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
