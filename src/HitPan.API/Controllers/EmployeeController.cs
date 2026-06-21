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
