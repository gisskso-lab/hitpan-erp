using HitPan.API.Authorization;
using HitPan.Application.DTOs.Employee;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>인사·근태 API — 출퇴근, 초과근무, HR경비</summary>
[ApiController]
[Route("api/hr")]
[Authorize(Policy = "TenantOnly")]
public class HrController : ControllerBase
{
    private readonly IHrService _svc;
    public HrController(IHrService svc) => _svc = svc;

    // ── 출퇴근 ──

    [HttpGet("attendance")]
    [RequirePermission("HR", "view")]
    public async Task<IActionResult> GetAttendance([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? employeeId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetAttendanceAsync(tid, from, to, employeeId, ct));
    }

    // 봉합 (2026-06-22, 13차 축7 P1 — 7차 A-P0-1 HR 모듈 누락분): HR 근태/초과근무/경비는 employee_id
    //   체계(attendance·overtime·hr_expense_requests.employee_id → employees.employee_id JOIN)인데 종전엔
    //   Items["UserId"](user_id, employee_id 와 별개 GUID)를 넘겨, 목록 조회 JOIN 영구 미매치 → 사원명 NULL,
    //   CheckOut 본인조회 "출근 기록 없음" 오류였다. 결재 모듈(7차 봉합)과 동일하게 Items["EmployeeId"] 사용.
    [HttpPost("check-in")]
    [RequirePermission("HR", "create")]
    public async Task<IActionResult> CheckIn([FromBody] CheckInOutRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var eid = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(eid)) return Forbid();
        var id = await _svc.CheckInAsync(tid, eid, req, ct);
        return Ok(new { id, message = "출근 완료" });
    }

    [HttpPost("check-out")]
    [RequirePermission("HR", "update")]
    public async Task<IActionResult> CheckOut(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var eid = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(eid)) return Forbid();
        await _svc.CheckOutAsync(tid, eid, ct);
        return Ok(new { message = "퇴근 완료" });
    }

    // ── 초과근무 ──

    [HttpGet("overtime")]
    [RequirePermission("HR", "view")]
    public async Task<IActionResult> GetOvertime([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetOvertimeAsync(tid, from, to, ct));
    }

    [HttpPost("overtime")]
    [RequirePermission("HR", "create")]
    public async Task<IActionResult> CreateOvertime([FromBody] CreateOvertimeRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var eid = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(eid)) return Forbid();
        var id = await _svc.CreateOvertimeAsync(req, tid, eid, ct);
        return Created($"/api/hr/overtime/{id}", new { id });
    }

    // ── HR 경비신청 ──

    [HttpGet("expense-requests")]
    [RequirePermission("HR", "view")]
    public async Task<IActionResult> GetHrExpenses([FromQuery] string? employeeId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetHrExpensesAsync(tid, employeeId, ct));
    }

    [HttpPost("expense-requests")]
    [RequirePermission("HR", "create")]
    public async Task<IActionResult> CreateHrExpense([FromBody] CreateHrExpenseRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var eid = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(eid)) return Forbid();
        var id = await _svc.CreateHrExpenseAsync(req, tid, eid, ct);
        return Created($"/api/hr/expense-requests/{id}", new { id });
    }
}
