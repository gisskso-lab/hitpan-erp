using HitPan.Application.DTOs.ApprovalLine;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>결재라인 CRUD API.</summary>
[ApiController]
[Route("api/approval-lines")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class ApprovalLineController : ControllerBase
{
    private readonly IApprovalLineService _service;

    public ApprovalLineController(IApprovalLineService service)
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

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var dto = await _service.GetAsync(tenantId, id, ct).ConfigureAwait(false);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveApprovalLineRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var lineId = await _service.CreateAsync(tenantId, request, ct).ConfigureAwait(false);
        return Ok(new { approvalLineId = lineId });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] SaveApprovalLineRequest request, CancellationToken ct)
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
