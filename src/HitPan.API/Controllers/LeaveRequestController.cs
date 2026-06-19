using HitPan.Application.DTOs.Employee;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 연차 신청/승인/반려 API 컨트롤러이다.
/// </summary>
[ApiController]
[Route("api/leave-requests")]
[Authorize]
public sealed class LeaveRequestController : ControllerBase
{
    private readonly ILeaveRequestService _leaveRequestService;

    public LeaveRequestController(ILeaveRequestService leaveRequestService)
    {
        _leaveRequestService = leaveRequestService;
    }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] string? employeeId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var list = await _leaveRequestService.GetListAsync(tenantId, employeeId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>
    /// 작20260429 (사장님 결재): 대시보드 월간 연차 캘린더.
    /// </summary>
    [HttpGet("calendar")]
    public async Task<IActionResult> GetCalendar([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var y = year ?? DateTime.Today.Year;
        var m = month ?? DateTime.Today.Month;
        if (m < 1 || m > 12) return BadRequest(new { message = "month must be 1~12" });

        var result = await _leaveRequestService.GetCalendarAsync(tenantId, y, m, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLeaveRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var requestId = await _leaveRequestService.CreateAsync(tenantId, request, ct).ConfigureAwait(false);
        return Ok(new { requestId });
    }

    [HttpPost("{id}/approve")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Approve(string id, [FromBody] ApproveLeaveRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        // P1-3 봉합: 실제 승인자 = JWT 의 UserId. 누가 승인했는지 추적 가능하게 기록한다.
        var approverId = HttpContext.Items["UserId"]?.ToString() ?? string.Empty;
        request.RequestId = id;
        request.Approved = true;
        await _leaveRequestService.ApproveAsync(tenantId, approverId, request, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("{id}/reject")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Reject(string id, [FromBody] ApproveLeaveRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        // P1-3 봉합: 실제 반려자 = JWT 의 UserId.
        var approverId = HttpContext.Items["UserId"]?.ToString() ?? string.Empty;
        request.RequestId = id;
        request.Approved = false;
        await _leaveRequestService.RejectAsync(tenantId, approverId, request, ct).ConfigureAwait(false);
        return Ok();
    }
}
