using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize(Policy = "SalesOnly")]
public class SalesController : ControllerBase
{
    private readonly ISalesService _salesService;

    public SalesController(ISalesService salesService)
    {
        _salesService = salesService;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateSalesOrderRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var id = await _salesService.CreateOrderAsync(request, ct);
        return Created($"/api/sales/orders/{id}", new { id });
    }

    [HttpPost("deliveries")]
    public async Task<IActionResult> CreateDelivery([FromBody] CreateDeliveryRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var id = await _salesService.CreateDeliveryAsync(request, ct);
        return Created($"/api/sales/deliveries/{id}", new { id });
    }

    [HttpPost("deliveries/{id}/confirm")]
    public async Task<IActionResult> ConfirmDelivery(string id, [FromBody] ConfirmDeliveryRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _salesService.ConfirmDeliveryAsync(id, request, ct);
        return Ok(new { id, status = "confirmed" });
    }
}
