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
    public async Task<IActionResult> GetAttendance([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? employeeId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetAttendanceAsync(tid, from, to, employeeId, ct));
    }

    [HttpPost("check-in")]
    public async Task<IActionResult> CheckIn([FromBody] CheckInOutRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();
        var id = await _svc.CheckInAsync(tid, uid, req, ct);
        return Ok(new { id, message = "출근 완료" });
    }

    [HttpPost("check-out")]
    public async Task<IActionResult> CheckOut(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();
        await _svc.CheckOutAsync(tid, uid, ct);
        return Ok(new { message = "퇴근 완료" });
    }

    // ── 초과근무 ──

    [HttpGet("overtime")]
    public async Task<IActionResult> GetOvertime([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetOvertimeAsync(tid, from, to, ct));
    }

    [HttpPost("overtime")]
    public async Task<IActionResult> CreateOvertime([FromBody] CreateOvertimeRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();
        var id = await _svc.CreateOvertimeAsync(req, tid, uid, ct);
        return Created($"/api/hr/overtime/{id}", new { id });
    }

    // ── HR 경비신청 ──

    [HttpGet("expense-requests")]
    public async Task<IActionResult> GetHrExpenses([FromQuery] string? employeeId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetHrExpensesAsync(tid, employeeId, ct));
    }

    [HttpPost("expense-requests")]
    public async Task<IActionResult> CreateHrExpense([FromBody] CreateHrExpenseRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();
        var id = await _svc.CreateHrExpenseAsync(req, tid, uid, ct);
        return Created($"/api/hr/expense-requests/{id}", new { id });
    }
}
