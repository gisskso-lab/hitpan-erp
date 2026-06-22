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

        // 봉합 (2026-06-23, 5차 전수조사 LV-W-03 P2): 종전엔 request.EmployeeId 를 그대로 신뢰해
        //   타인 명의 연차 신청이 가능했다. 본인 확인은 user_id 가 아니라 employee_id 클레임으로 해야 한다
        //   (user_id≠employee_id 별개 체계). 부모계정(tenant_admin)은 대리신청을 허용하고,
        //   일반 사용자(tenant_user)는 자기 employee_id 로 강제 치환해 위조를 차단한다.
        // 죽은 분기 청소 (2026-06-22, 헌법 #38): platform_admin 은 ERP 토큰에 발급 안 되는 본사 계층이라
        //   제거(격벽 2026-06-18). ERP 관리자 = 부모계정(tenant_admin) 뿐.
        var myEmployeeId = User.FindFirst("employee_id")?.Value;
        var accountType = User.FindFirst("account_type")?.Value;
        var isAdmin = accountType is "tenant_admin";
        if (!isAdmin)
        {
            if (string.IsNullOrEmpty(myEmployeeId))
            {
                return Forbid();
            }
            request.EmployeeId = myEmployeeId; // 일반 사용자는 본인 신청만 — 서버가 강제
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
        // LV-01 봉합(2026-06-20): 처리 대상이 없으면(미존재/이미 처리) 무음 Ok 대신 409로 정직하게 알린다.
        var done = await _leaveRequestService.ApproveAsync(tenantId, approverId, request, ct).ConfigureAwait(false);
        if (!done)
        {
            return Conflict(new { error = "이미 처리되었거나 존재하지 않는 연차 신청입니다." });
        }
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
        // LV-01 봉합(2026-06-20): 처리 대상이 없으면 409로 정직하게 알린다.
        var done = await _leaveRequestService.RejectAsync(tenantId, approverId, request, ct).ConfigureAwait(false);
        if (!done)
        {
            return Conflict(new { error = "이미 처리되었거나 존재하지 않는 연차 신청입니다." });
        }
        return Ok();
    }
}
