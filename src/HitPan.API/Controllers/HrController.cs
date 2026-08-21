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

    // ── 🔴 대리 근태 (작10 A) ──────────────────────────────────────────────
    //
    // 사장님(2026-08-21): "사원등록만 되있고 계정이 없는 직원은 인사담당자가 수동으로
    //   근퇴처리 할 수 있는 장치를 만들어야 됨" / "남의 근퇴 넣는건 권한설정에 넣자."
    //
    // 🔴 권한을 HR 이 아니라 ★HR_PROXY★ 로 가른다. HR 5축은 "내 데이터에 무엇을 하나" 축이고
    //    대리입력은 "남의 데이터를 건드리나" 축이다. HR update 에 얹으면 자기 근태 고치라고
    //    준 권한이 남의 근태까지 연다.
    //
    // 🔴 역할("인사담당자")을 코드에 박지 않는다 — 누구에게 줄지는 고객사가 정한다(헌법 #11).
    //    사장님: "이건 고객사 마음이지" / "우리가 정할게 아님"
    //
    // 🚨 employeeId 를 파라미터로 받는 첫 경로다. tenantId 는 여전히 JWT 에서만 오고(#2),
    //    서비스가 대상 사원의 소속을 검증한다.

    [HttpPost("attendance/proxy/check-in")]
    [RequirePermission("HR_PROXY", "create")]
    public async Task<IActionResult> CheckInProxy([FromBody] ProxyCheckInRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var actor = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(actor)) return Forbid();
        if (string.IsNullOrWhiteSpace(req.EmployeeId)) return BadRequest(new { message = "대상 직원을 선택해 주세요." });

        var id = await _svc.CheckInProxyAsync(tid, req.EmployeeId, actor,
            new CheckInOutRequest { Memo = req.Memo }, ct);
        return Ok(new { id, message = "출근 처리했습니다." });
    }

    [HttpPost("attendance/proxy/check-out")]
    [RequirePermission("HR_PROXY", "update")]
    public async Task<IActionResult> CheckOutProxy([FromBody] ProxyCheckOutRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var actor = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(actor)) return Forbid();
        if (string.IsNullOrWhiteSpace(req.EmployeeId)) return BadRequest(new { message = "대상 직원을 선택해 주세요." });

        await _svc.CheckOutProxyAsync(tid, req.EmployeeId, actor, ct);
        return Ok(new { message = "퇴근 처리했습니다." });
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

    // 봉합 (2026-06-23, 19차): 초과근무 승인/반려 — 신청만 되고 승인 경로가 없던 워크플로우 끊김 봉합.
    [HttpPost("overtime/{id}/approve")]
    [RequirePermission("HR", "update")]
    public async Task<IActionResult> ApproveOvertime(string id, [FromBody] OvertimeApproveRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        var ok = await _svc.ApproveOvertimeAsync(id, tid, req.Action, ct);
        return ok ? Ok() : BadRequest(new { error = "이미 처리되었거나 대상을 찾을 수 없습니다." });
    }

    /// <summary>초과근무 승인/반려 요청 — action: approved | rejected.</summary>
    public sealed class OvertimeApproveRequest
    {
        public string Action { get; set; } = "approved";
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

    /// <summary>
    /// 경비 신청. 작(2026-08-13) 단계7 — <b>결재가 실제로 올라갔는지 사실대로 돌려준다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 단계3 P0-1 교훈: 종전엔 무조건 <c>201 Created</c> 만 돌려줬다. 결재 설정이 꺼져 있으면
    /// 결재 생성이 조용히 건너뛰는데, 화면은 "신청했습니다" 를 띄우고 직원은 올라간 줄 안다.
    /// 그러면 문서는 <c>pending</c> 에 갇히고 결재함엔 안 뜬다 — <b>되는 척</b>이다.
    ///
    /// ⚠️ 결재가 안 올라가도 <b>신청 자체는 성공</b>이다(201 유지). 경비 행은 이미 저장됐고,
    /// 결재를 안 쓰는 회사도 정상 운영이다. 다만 <b>그 사실을 감추지 않는다.</b>
    /// </remarks>
    [HttpPost("expense-requests")]
    [RequirePermission("HR", "create")]
    public async Task<IActionResult> CreateHrExpense([FromBody] CreateHrExpenseRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var eid = HttpContext.Items["EmployeeId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(eid)) return Forbid();

        try
        {
            var id = await _svc.CreateHrExpenseAsync(req, tid, eid, ct);

            var (approvalCreated, skipReason) = await _svc
                .CheckHrExpenseApprovalAsync(tid, id, ct).ConfigureAwait(false);

            return Created($"/api/hr/expense-requests/{id}",
                new { id, approvalCreated, approvalSkipReason = skipReason });
        }
        catch (InvalidOperationException ex)
        {
            // "마감된 기간입니다" 같은 이유를 그대로 전한다.
            return BadRequest(new { message = ex.Message });
        }
    }
}
