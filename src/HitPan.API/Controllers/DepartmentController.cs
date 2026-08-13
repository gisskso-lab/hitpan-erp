using HitPan.Application.DTOs.Department;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 부서 마스터 CRUD API. 작(2026-08-13) 그룹웨어 단계4 토대.
/// </summary>
/// <remarks>
/// 🔴 종전엔 <c>GET api/employees/departments</c> 조회 하나뿐이라
/// <b>부서를 만들 방법이 없었다</b>. 등록·수정·삭제를 여기서 연다.
/// 권한은 직급(<see cref="PositionController"/>)과 같다 — 조직 편성은 관리자 일이다(헌법 #11).
/// </remarks>
[ApiController]
[Route("api/departments")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class DepartmentController : ControllerBase
{
    private readonly IDepartmentService _service;

    public DepartmentController(IDepartmentService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var list = await _service.GetListAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var deptId = await _service.CreateAsync(tenantId, request, ct).ConfigureAwait(false);
            return Ok(new { deptId });
        }
        catch (InvalidOperationException ex)
        {
            // 🔴 "이미 있는 부서" · "상위 부서 없음" 같은 사유를 그대로 전한다.
            //    그냥 실패라고만 하면 고객이 같은 걸 다시 눌러 본다.
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateDepartmentRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var ok = await _service.UpdateAsync(tenantId, id, request, ct).ConfigureAwait(false);
            return ok
                ? Ok()
                : BadRequest(new { message = "부서를 찾을 수 없습니다." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _service.DeleteAsync(tenantId, id, ct).ConfigureAwait(false);

        // 🔴 지운 것과 비활성으로 돌린 것을 가른다 — 화면이 사실대로 말해야 한다.
        //    "삭제했습니다" 라고 해놓고 목록에 그대로 남아 있으면 되는 척이다.
        return Ok(new
        {
            deleted = result.Deleted,
            deactivated = result.Deactivated,
            message = result.Message
        });
    }
}
