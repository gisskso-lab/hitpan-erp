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

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _salesService.GetOrdersAsync(tenantId, from, to, status, ct);
        return Ok(result);
    }

    [HttpPost("orders/{id}/convert-to-delivery")]
    public async Task<IActionResult> ConvertOrderToDelivery(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var (deliveryId, documentNumber) = await _salesService.ConvertOrderToDeliveryAsync(id, tenantId, ct);
        return Ok(new { deliveryId, documentNumber });
    }

    [HttpPost("deliveries")]
    public async Task<IActionResult> CreateDelivery([FromBody] CreateDeliveryRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var (id, documentNumber) = await _salesService.CreateDeliveryAsync(request, ct);
        return Created($"/api/sales/deliveries/{id}", new { id, documentNumber });
    }

    [HttpPost("deliveries/{id}/confirm")]
    public async Task<IActionResult> ConfirmDelivery(string id, [FromBody] ConfirmDeliveryRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _salesService.ConfirmDeliveryAsync(id, request, ct);
        return Ok(new { id, status = "confirmed" });
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> GetDeliveries(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? partner,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _salesService.GetDeliveriesAsync(tenantId, from, to, partner, status, ct);
        return Ok(result);
    }

    [HttpGet("deliveries/{id}")]
    public async Task<IActionResult> GetDelivery(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _salesService.GetDeliveryAsync(id, tenantId, ct);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPut("deliveries/{id}")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> UpdateDelivery(string id, [FromBody] UpdateDeliveryDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = User.FindFirst("employee_id")?.Value;
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _salesService.UpdateDeliveryAsync(id, dto, tenantId, userId ?? string.Empty, ct);
        return Ok();
    }

    [HttpDelete("deliveries/{id}")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> DeleteDelivery(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _salesService.DeleteDeliveryAsync(id, tenantId, ct);
        return Ok();
    }
}
