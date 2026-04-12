using System.Security.Claims;
using HitPan.Application.DTOs.Partner;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/partners")]
public class PartnerController : ControllerBase
{
    private readonly IPartnerService _partnerService;

    public PartnerController(IPartnerService partnerService)
    {
        _partnerService = partnerService;
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

        var result = await _partnerService.SearchPartnersAsync(tenantId, q, ct);
        return Ok(result);
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
