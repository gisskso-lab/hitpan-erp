using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/purchase")]
[Authorize(Policy = "PurchaseOnly")]
public class PurchaseController : ControllerBase
{
    private readonly IPurchaseService _purchaseService;

    public PurchaseController(IPurchaseService purchaseService)
    {
        _purchaseService = purchaseService;
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

        var result = await _purchaseService.GetOrdersAsync(tenantId, from, to, status, ct);
        return Ok(result);
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var id = await _purchaseService.CreateOrderAsync(request, ct);
        return Created($"/api/purchase/orders/{id}", new { id });
    }

    [HttpGet("orders/{id}")]
    public async Task<IActionResult> GetOrder(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var detail = await _purchaseService.GetOrderDetailAsync(id, tenantId, ct);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    [HttpDelete("orders/{id}")]
    public async Task<IActionResult> DeleteOrder(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            await _purchaseService.DeletePurchaseOrderAsync(id, tenantId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("returns/{id}")]
    public async Task<IActionResult> GetReturn(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var detail = await _purchaseService.GetReturnDetailAsync(id, tenantId, ct);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    [HttpPost("orders/{id}/convert-to-receipt")]
    public async Task<IActionResult> ConvertOrderToReceipt(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var (receiptId, receiptNo) = await _purchaseService.ConvertOrderToReceiptAsync(id, tenantId, ct);
            return Ok(new { receiptId, receiptNo });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("receipts")]
    public async Task<IActionResult> GetReceipts(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _purchaseService.GetReceiptsAsync(tenantId, from, to, status, ct);
        return Ok(result);
    }

    [HttpGet("returns")]
    public async Task<IActionResult> GetReturns([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var result = await _purchaseService.GetReturnsAsync(tenantId, from, to, ct);
        return Ok(result);
    }

    [HttpPost("returns/{id}/confirm")]
    public async Task<IActionResult> ConfirmReturn(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        try
        {
            await _purchaseService.ConfirmPurchaseReturnAsync(id, tenantId, employeeId, ct);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("returns/{id}")]
    public async Task<IActionResult> DeleteReturn(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            await _purchaseService.DeletePurchaseReturnAsync(id, tenantId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("receipts/{id}/convert-to-return")]
    public async Task<IActionResult> ConvertReceiptToReturn(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            var (returnId, returnNo) = await _purchaseService.ConvertReceiptToReturnAsync(id, tenantId, ct);
            return Ok(new { returnId, returnNo });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("receipts")]
    public async Task<IActionResult> CreateReceipt([FromBody] CreateReceiptRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var id = await _purchaseService.CreateReceiptAsync(request, ct);
        return Created($"/api/purchase/receipts/{id}", new { id });
    }

    [HttpGet("receipts/{id}")]
    public async Task<IActionResult> GetReceipt(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var detail = await _purchaseService.GetReceiptDetailAsync(id, tenantId, ct);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    [HttpDelete("receipts/{id}")]
    public async Task<IActionResult> DeleteReceipt(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            await _purchaseService.DeletePurchaseReceiptAsync(id, tenantId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("receipts/{id}/confirm")]
    public async Task<IActionResult> ConfirmReceipt(string id, [FromBody] ConfirmReceiptRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _purchaseService.ConfirmReceiptAsync(id, request, ct);
        return Ok(new { id, status = "confirmed" });
    }
}
