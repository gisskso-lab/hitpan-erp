using HitPan.Application.DTOs.Bom;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/bom")]
[Authorize]
public class BomController : ControllerBase
{
    private readonly IBomService _svc;

    public BomController(IBomService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetListAsync(tid, ct).ConfigureAwait(false));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        var result = await _svc.GetAsync(id, tid, ct).ConfigureAwait(false);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBomDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        try
        {
            var id = await _svc.CreateAsync(dto, tid, ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id }, new { id });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(string id, [FromBody] CreateBomDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        try
        {
            await _svc.UpdateAsync(id, dto, tid, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        await _svc.DeleteAsync(id, tid, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("{id:guid}/check-assemble")]
    public async Task<IActionResult> CheckAssemble(string id, [FromBody] decimal produceQty, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        var result = await _svc.CheckAssembleAsync(id, produceQty, tid, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPost("assemble")]
    public async Task<IActionResult> Assemble([FromBody] BomAssembleDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.User.FindFirst("sub")?.Value ?? "";
        if (string.IsNullOrEmpty(tid)) return Forbid();
        try
        {
            await _svc.AssembleAsync(dto, tid, uid, ct).ConfigureAwait(false);
            return Ok(new { message = "조립이 완료됐습니다. 완성품 재고가 반영됐습니다." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetAlertsAsync(tid, ct).ConfigureAwait(false));
    }

    [HttpPost("alerts/{alertId}/dismiss")]
    public async Task<IActionResult> Dismiss(string alertId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        await _svc.DismissAlertAsync(alertId, tid, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("alerts/{alertId}/order")]
    public async Task<IActionResult> Order(string alertId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        await _svc.OrderAlertAsync(alertId, tid, ct).ConfigureAwait(false);
        return Ok(new { message = "발주 처리됐습니다." });
    }
}
