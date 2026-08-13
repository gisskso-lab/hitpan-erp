using HitPan.Application.DTOs.Position;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>직급 마스터 CRUD API.</summary>
[ApiController]
[Route("api/positions")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class PositionController : ControllerBase
{
    private readonly IPositionService _service;

    public PositionController(IPositionService service)
    {
        _service = service;
    }

    // 🔴 봉합 (2026-08-14, 1.2.74 실사용 P0): 조회는 직원에게 연다.
    //    직급 목록은 **결재선**이 직급으로 짜이고 사원 화면이 직급을 보여주므로 직원도 읽어야 한다.
    //    ⚠️ 만들기·고치기·지우기는 그대로 관리자 전용이다(클래스 정책 유지).
    [Authorize(Policy = "TenantOnly")]
    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var list = await _service.GetListAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePositionRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var positionId = await _service.CreateAsync(tenantId, request, ct).ConfigureAwait(false);
        return Ok(new { positionId });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdatePositionRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        await _service.UpdateAsync(tenantId, id, request, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        await _service.DeleteAsync(tenantId, id, ct).ConfigureAwait(false);
        return Ok();
    }
}
