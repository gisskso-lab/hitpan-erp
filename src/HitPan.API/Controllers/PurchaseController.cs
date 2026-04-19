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

    [HttpPost("orders/{id}/convert-to-receipt")]
    public async Task<IActionResult> ConvertOrderToReceipt(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var (receiptId, receiptNo) = await _purchaseService.ConvertOrderToReceiptAsync(id, tenantId, ct);
        return Ok(new { receiptId, receiptNo });
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

    [HttpPost("receipts")]
    public async Task<IActionResult> CreateReceipt([FromBody] CreateReceiptRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var id = await _purchaseService.CreateReceiptAsync(request, ct);
        return Created($"/api/purchase/receipts/{id}", new { id });
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
