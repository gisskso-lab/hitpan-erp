using HitPan.Application.DTOs.Leave;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HitPan.API.Controllers;

/// <summary>
/// 휴직 API. 작(2026-08-13) 그룹웨어 단계6.
/// </summary>
/// <remarks>
/// 🔴 <b>권한이 두 갈래다.</b> 휴직은 연차 부여와 달리 <b>직원이 신청하는 것</b>이다.
/// <list type="bullet">
///   <item>신청·조회 — 로그인한 직원이면 누구나. 단 <b>본인 것만</b> 보인다.</item>
///   <item>승인·반려·복직·전원 조회 — 관리자만.</item>
/// </list>
/// 관리자 여부를 <b>서버가 판정</b>한다. 화면이 보내온 값을 믿지 않는다.
/// </remarks>
[ApiController]
[Route("api/absence")]
[Authorize]
public sealed class AbsenceController : ControllerBase
{
    private readonly IAbsenceService _service;

    public AbsenceController(IAbsenceService service)
    {
        _service = service;
    }

    /// <summary>목록. 관리자는 전원, 일반 직원은 본인 것만.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? employeeId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        // 🔴 일반 직원이 employeeId 를 바꿔 보내도 본인 것만 나가게 **서버가 덮는다.**
        var scoped = IsAdmin() ? employeeId : CurrentEmployeeId();

        var list = await _service.GetListAsync(tenantId, scoped, status, from, to, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>한 건.</summary>
    [HttpGet("{absenceId}")]
    public async Task<IActionResult> Get(string absenceId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var dto = await _service.GetAsync(tenantId, absenceId, ct).ConfigureAwait(false);
        if (dto is null) return NotFound();

        // 남의 휴직 사유가 보이면 안 된다.
        if (!IsAdmin() && dto.EmployeeId != CurrentEmployeeId()) return Forbid();

        return Ok(dto);
    }

    /// <summary>
    /// 그 달 휴직자와 <b>정해 둔 급여 금액</b>. 🔴 급여(단계8)·회계가 여기서 가져간다.
    /// </summary>
    /// <remarks>
    /// 사장님(2026-08-13): <i>"급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함"</i>.
    /// 사람이 금액을 정해 뒀으므로 급여가 기간·비율을 다시 계산할 필요가 없다.
    /// 급여 자료라 관리자만 본다.
    /// </remarks>
    [HttpGet("pay")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Pay([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var y = year > 0 ? year : DateTime.Today.Year;
        var m = month is >= 1 and <= 12 ? month : DateTime.Today.Month;

        var list = await _service.GetPayForMonthAsync(tenantId, y, m, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>진행 단계 목록 — 화면이 고르게 한다.</summary>
    [HttpGet("statuses")]
    public IActionResult Statuses()
        => Ok(AbsenceStatusLabels.All.Select(x => new { code = x.Key, name = x.Value }));

    /// <summary>저장(신규·수정).</summary>
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveAbsenceRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        // 🔴 남의 이름으로 휴직을 넣지 못하게 막는다.
        if (!IsAdmin())
        {
            var me = CurrentEmployeeId();
            if (string.IsNullOrEmpty(me)) return Forbid();
            request.EmployeeId = me;
        }

        try
        {
            var result = await _service
                .SaveAsync(tenantId, CurrentEmployeeId() ?? "unknown", CurrentEmployeeName(), request, ct)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            // "사유를 남겨야 합니다" 같은 이유를 그대로 전한다.
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>결재에 올린다.</summary>
    [HttpPost("{absenceId}/submit")]
    public async Task<IActionResult> Submit(string absenceId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var dto = await _service.GetAsync(tenantId, absenceId, ct).ConfigureAwait(false);
        if (dto is null) return NotFound();
        if (!IsAdmin() && dto.EmployeeId != CurrentEmployeeId()) return Forbid();

        try
        {
            var result = await _service
                .SubmitAsync(tenantId, CurrentEmployeeId() ?? "unknown", CurrentEmployeeName(), absenceId, ct)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>승인 — 관리자만.</summary>
    [HttpPost("{absenceId}/approve")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Approve(string absenceId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            await _service.ApproveAsync(tenantId, CurrentEmployeeId() ?? "unknown", absenceId, ct)
                .ConfigureAwait(false);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>반려 — 관리자만.</summary>
    [HttpPost("{absenceId}/reject")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Reject(string absenceId, [FromBody] RejectBody body,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _service.RejectAsync(tenantId, CurrentEmployeeId() ?? "unknown", absenceId, body?.Reason, ct)
            .ConfigureAwait(false);
        return Ok(new { ok = true });
    }

    /// <summary>취소(신청 철회).</summary>
    [HttpPost("{absenceId}/cancel")]
    public async Task<IActionResult> Cancel(string absenceId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var dto = await _service.GetAsync(tenantId, absenceId, ct).ConfigureAwait(false);
        if (dto is null) return NotFound();
        if (!IsAdmin() && dto.EmployeeId != CurrentEmployeeId()) return Forbid();

        try
        {
            await _service.CancelAsync(tenantId, CurrentEmployeeId() ?? "unknown", absenceId, ct)
                .ConfigureAwait(false);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>복직 처리 — 관리자만. 실제 복직일을 사람이 넣는다.</summary>
    [HttpPost("return")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Return([FromBody] ReturnFromAbsenceRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            await _service.ReturnAsync(tenantId, CurrentEmployeeId() ?? "unknown", request, ct)
                .ConfigureAwait(false);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>시작일이 된 건을 '휴직중' 으로 맞춘다 — 관리자만.</summary>
    [HttpPost("sync")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var changed = await _service.SyncStatusAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(new { changed });
    }

    // ───────────────────────────────────────────────────────────────

    public sealed class RejectBody
    {
        public string? Reason { get; set; }
    }

    private string? CurrentEmployeeId() => User.FindFirstValue("employee_id");

    private string CurrentEmployeeName()
        => User.FindFirstValue("emp_name") ?? User.Identity?.Name ?? "";

    /// <summary>
    /// 관리자인가. 🔴 <b>서버가 판정</b>한다 — 화면이 보내온 값을 믿지 않는다.
    /// </summary>
    /// <remarks>
    /// 🔴 판정 근거를 <c>TenantAdminOnly</c> 정책과 <b>똑같이</b> 맞춘다.
    /// (<c>Program.cs:343</c> — <c>ctx.User.HasClaim("account_type", "tenant_admin")</c>)
    ///
    /// 처음에 <c>role</c> 클레임을 봤다가 실측에서 잡았다. 그대로 뒀으면
    /// <b>같은 관리자가 [승인] 은 되고 [전원 목록] 은 본인 것만 보이는</b> 어긋남이 났다 —
    /// 속성은 정책으로 막고 여기는 다른 클레임을 보기 때문이다.
    /// 두 곳의 판정 기준이 갈라지면 안 된다(게이트: AbsenceGuardTests).
    /// </remarks>
    private bool IsAdmin() => User.HasClaim("account_type", "tenant_admin");
}
