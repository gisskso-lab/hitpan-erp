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
        var result = await _stockService.GetBalanceAsync(ct);
        return Ok(result);
    }

    [HttpPost("ledger")]
    public async Task<IActionResult> GetLedger([FromBody] StockLedgerQueryRequest request, CancellationToken ct)
    {
        var result = await _stockService.GetLedgerAsync(request, ct);
        return Ok(result);
    }
}
