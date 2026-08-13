using HitPan.Application.DTOs.Employee;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 사원관리 CRUD API 컨트롤러이다.
/// </summary>
[ApiController]
[Route("api/employees")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class EmployeeController : ControllerBase
{
    private readonly IEmployeeService _employeeService;

    public EmployeeController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    // 🔴 봉합 (2026-08-14, 1.2.74 실사용 P0): 사원 목록 조회는 직원에게 연다.
    //    사장님: "권한설정으로 모든걸 풀었지만 ... 아무것도 못함."
    //    직원 목록은 **메신저 상대 찾기·조직도·결재 상신**의 선행조건이다.
    //    종전엔 클래스 레벨 TenantAdminOnly 가 조회까지 막아, 자식계정은 화면을 열어도
    //    빈 목록만 봤다(웹 서비스가 403 을 빈 배열로 삼켜 "0명" 으로 보였다).
    //    ⚠️ 등록·수정·삭제·퇴사처리는 그대로 관리자 전용이다(클래스 정책 유지).
    [Authorize(Policy = "TenantOnly")]
    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var list = await _employeeService.GetListAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>
    /// 봉합 (2026-06-22, 10차 P1-1): 부서 드롭다운 목록 (읽기 전용).
    /// 사원 부서는 dept_id 로 저장되므로 화면이 부서를 선택할 수 있게 (dept_id, dept_name) 목록을 제공한다.
    /// </summary>
    // 조회는 직원에게 연다(위 GetList 와 같은 이유 — 부서는 메신저 부서방의 선행조건).
    [Authorize(Policy = "TenantOnly")]
    [HttpGet("departments")]
    public async Task<IActionResult> GetDepartments(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var list = await _employeeService.GetDepartmentsAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var dto = await _employeeService.GetAsync(tenantId, id, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return NotFound();
        }

        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var employeeId = await _employeeService.CreateAsync(tenantId, request, ct).ConfigureAwait(false);
        return Ok(new { employeeId });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateEmployeeRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _employeeService.UpdateAsync(tenantId, id, request, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _employeeService.DeleteAsync(tenantId, id, ct).ConfigureAwait(false);
        return Ok();
    }

    /// <summary>
    /// 작(2026-08-12) 그룹웨어 단계0 P0-B: 퇴사 처리 사전 점검.
    /// 퇴사시키면 무슨 일이 벌어지는지 <b>미리 알려준다</b>. 막지는 않는다(반자동 원칙).
    /// </summary>
    [HttpGet("{id}/resign-precheck")]
    public async Task<IActionResult> ResignPrecheck(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _employeeService.GetResignPrecheckAsync(tenantId, id, ct).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// 작(2026-08-12) 그룹웨어 단계0 P0-A·C: 퇴사 처리.
    /// </summary>
    /// <remarks>
    /// 기존 <c>DELETE</c> 는 <c>employees.is_active</c> 만 껐고 <b>로그인 계정은 살아 있었다</b>
    /// (로그인은 <c>users.IsActive</c> 를 본다 — 다른 표의 다른 칸).
    /// 이 경로는 계정 차단 + 퇴사 사유·<b>실제 퇴사일</b> 기록까지 한다.
    /// 퇴사일을 받는 이유는 처리한 날과 실제 퇴사일이 다를 수 있기 때문이다(소급 처리).
    /// </remarks>
    [HttpPost("{id}/resign")]
    public async Task<IActionResult> Resign(string id,
        [FromBody] ResignEmployeeRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        // 계정을 실제로 껐는지 화면에 알려준다. 계정이 없는 사원(실측 12명 중 11명)에게도
        // "계정도 차단했다"고 안내하던 거짓 표시를 없애기 위해서다(검증팀 P1-5).
        var accountBlocked = await _employeeService.ResignAsync(
            tenantId, id, request.ResignDate, request.ResignReason, ct).ConfigureAwait(false);
        return Ok(new { accountBlocked });
    }

    /// <summary>
    /// 작20260429 연차 관리 (사장님 결재): 부여·사용 일수 단독 저장.
    /// 사원관리 그리드의 연차 컬럼 인라인 편집 후 호출된다.
    /// 다른 사원 정보는 변경하지 않는다 (워크플로우 영향 0건).
    /// </summary>
    [HttpPut("{id}/annual-leave")]
    public async Task<IActionResult> UpdateAnnualLeave(string id,
        [FromBody] UpdateAnnualLeaveRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _employeeService.UpdateAnnualLeaveAsync(tenantId, id,
            request.AnnualLeaveTotal, request.AnnualLeaveUsed, ct).ConfigureAwait(false);
        return Ok();
    }
}
