using System.Security.Claims;
using HitPan.Application.DTOs.Partner;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/partners")]
[Authorize]
public class PartnerController : ControllerBase
{
    private readonly IPartnerService _partnerService;

    public PartnerController(IPartnerService partnerService)
    {
        _partnerService = partnerService;
    }

    [HttpGet]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetList([FromQuery] string? search, [FromQuery] string? type, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var list = await _partnerService.GetPartnerListAsync(tenantId, search, type, ct).ConfigureAwait(false);
        return Ok(list);
    }

    [HttpGet("search")]
    [Authorize(Policy = "SalesOnly")]
    public async Task<IActionResult> SearchPartners([FromQuery] string q, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _partnerService.SearchPartnersAsync(tenantId, q, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetDetail(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var row = await _partnerService.GetPartnerDetailAsync(id, tenantId, ct).ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        return Ok(row);
    }

    [HttpPost]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreatePartnerDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        try
        {
            var newId = await _partnerService.CreatePartnerAsync(dto, tenantId, ct).ConfigureAwait(false);
            return Ok(new { partnerId = newId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdatePartnerDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        try
        {
            await _partnerService.UpdatePartnerAsync(id, dto, tenantId, ct).ConfigureAwait(false);
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
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _partnerService.DeletePartnerAsync(id, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpGet("{id}/master-special-prices")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetMasterSpecialPrices(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var list = await _partnerService.GetPartnerSpecialPricesAsync(id, tenantId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    [HttpPost("{id}/master-special-prices")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> UpsertMasterSpecialPrice(string id, [FromBody] PartnerSpecialPriceDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _partnerService.UpsertPartnerSpecialPriceAsync(id, dto, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpDelete("master-special-prices/{priceId}")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> DeleteMasterSpecialPrice(string priceId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _partnerService.DeletePartnerSpecialPriceByIdAsync(priceId, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpGet("{id}/balance")]
    [Authorize(Policy = "SalesOnly")]
    public async Task<IActionResult> GetBalance(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var balance = await _partnerService.GetBalanceAsync(id, ct);
        if (balance is null)
        {
            return NotFound();
        }

        return Ok(balance);
    }

    [HttpGet("{id}/special-prices")]
    [Authorize(Policy = "SalesOnly")]
    public async Task<IActionResult> GetSpecialPrices(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var employeeId = User.FindFirst("employee_id")?.Value;
        if (role == "sales_user")
        {
            var ok = await _partnerService.IsAssignedPartnerAsync(employeeId, id, tenantId, ct);
            if (!ok) return Forbid();
        }

        var result = await _partnerService.GetSpecialPricesAsync(id, tenantId, ct);
        return Ok(result);
    }

    [HttpPost("{id}/special-prices")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> UpsertSpecialPrice(string id, [FromBody] SpecialPriceUpsertDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var userId = User.FindFirst("employee_id")?.Value ?? string.Empty;
        await _partnerService.UpsertSpecialPriceAsync(id, dto, tenantId, userId, ct);
        return Ok();
    }

    [HttpDelete("{id}/special-prices/{itemId}")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> DeleteSpecialPrice(string id, string itemId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _partnerService.DeleteSpecialPriceAsync(id, itemId, tenantId, ct);
        return Ok();
    }
}
