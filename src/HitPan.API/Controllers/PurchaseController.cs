using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/purchase")]
public class PurchaseController : ControllerBase
{
    private readonly IPurchaseService _purchaseService;

    public PurchaseController(IPurchaseService purchaseService)
    {
        _purchaseService = purchaseService;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        var id = await _purchaseService.CreateOrderAsync(request, ct);
        return Created($"/api/purchase/orders/{id}", new { id });
    }

    [HttpPost("receipts")]
    public async Task<IActionResult> CreateReceipt([FromBody] CreateReceiptRequest request, CancellationToken ct)
    {
        var id = await _purchaseService.CreateReceiptAsync(request, ct);
        return Created($"/api/purchase/receipts/{id}", new { id });
    }

    [HttpPost("receipts/{id}/confirm")]
    public async Task<IActionResult> ConfirmReceipt(string id, [FromBody] ConfirmReceiptRequest request, CancellationToken ct)
    {
        await _purchaseService.ConfirmReceiptAsync(id, request, ct);
        return Ok(new { id, status = "confirmed" });
    }
}
