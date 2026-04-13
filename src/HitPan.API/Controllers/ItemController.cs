using HitPan.Application.DTOs.Item;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/items")]
[Authorize]
public sealed class ItemController : ControllerBase
{
    private readonly IItemService _svc;

    public ItemController(IItemService svc)
    {
        _svc = svc;
    }

    [HttpGet]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetList([FromQuery] string? search, [FromQuery] string? group, [FromQuery] string? type, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        var result = await _svc.GetListAsync(tid, search, group, type, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("groups")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetGroups(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        var result = await _svc.GetGroupsAsync(tid, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("{id}/special-prices")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetSpecialPrices(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        var result = await _svc.GetSpecialPricesAsync(id, tid, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPut("{id}/special-prices")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> UpsertSpecialPrice(string id, [FromBody] ItemSpecialPriceDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        await _svc.UpsertSpecialPriceAsync(id, dto, tid, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpDelete("{id}/special-prices/{priceId}")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> DeleteSpecialPrice(string id, string priceId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        _ = id;
        await _svc.DeleteSpecialPriceAsync(priceId, tid, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        var result = await _svc.GetAsync(id, tid, ct).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateItemDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        try
        {
            var id = await _svc.CreateAsync(dto, tid, ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id }, new { id });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateItemDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        try
        {
            await _svc.UpdateAsync(id, dto, tid, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        await _svc.DeleteAsync(id, tid, ct).ConfigureAwait(false);
        return Ok();
    }
}
