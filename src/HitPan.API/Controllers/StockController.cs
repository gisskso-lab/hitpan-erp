using HitPan.Application.DTOs.Stock;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/stock")]
[Authorize(Policy = "SalesOnly")]
public class StockController : ControllerBase
{
    private readonly IStockService _stockService;

    public StockController(IStockService stockService)
    {
        _stockService = stockService;
    }

    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _stockService.GetBalanceAsync(ct);
        return Ok(result);
    }

    [HttpPost("ledger")]
    public async Task<IActionResult> GetLedger([FromBody] StockLedgerQueryRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _stockService.GetLedgerAsync(request, ct);
        return Ok(result);
    }

    // ── 재고 실사·조정 ──

    /// <summary>재고 실사 조정 실행</summary>
    [HttpPost("adjust")]
    public async Task<IActionResult> AdjustStock([FromBody] StockAdjustRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();

        var result = await _stockService.AdjustStockAsync(tenantId, userId, request, ct);
        return Ok(result);
    }

    /// <summary>재고 조정 이력 조회</summary>
    [HttpGet("adjust/history")]
    public async Task<IActionResult> GetAdjustHistory([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _stockService.GetAdjustHistoryAsync(tenantId, from, to, ct);
        return Ok(result);
    }

    // ── 재고 이송 ──

    /// <summary>재고 이송 실행</summary>
    [HttpPost("transfer")]
    public async Task<IActionResult> TransferStock([FromBody] StockTransferRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();

        await _stockService.TransferStockAsync(tenantId, userId, request, ct);
        return Ok(new { message = "이송 완료" });
    }

    /// <summary>재고 이송 이력 조회</summary>
    [HttpGet("transfer/history")]
    public async Task<IActionResult> GetTransferHistory([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _stockService.GetTransferHistoryAsync(tenantId, from, to, ct);
        return Ok(result);
    }

    // ── 창고 분리 현황 ──

    /// <summary>창고 분리(자사/위탁) 현황 조회</summary>
    [HttpGet("warehouse-split")]
    public async Task<IActionResult> GetWarehouseSplit(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _stockService.GetWarehouseSplitAsync(tenantId, ct);
        return Ok(result);
    }
}
