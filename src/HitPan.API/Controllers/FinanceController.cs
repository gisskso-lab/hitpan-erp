using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>경리·세무 API — 현금출납장, 매입매출장, 부가세, 경비, 손익</summary>
[ApiController]
[Route("api/finance")]
[Authorize(Policy = "SalesOnly")]
public class FinanceController : ControllerBase
{
    private readonly IFinanceService _svc;

    public FinanceController(IFinanceService svc) => _svc = svc;

    // ── 현금출납장 ──

    [HttpGet("cashbook")]
    public async Task<IActionResult> GetCashbook([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetCashbookAsync(tid, from, to, ct));
    }

    [HttpPost("cashbook")]
    public async Task<IActionResult> CreateCashbook([FromBody] CreateCashbookRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();
        var id = await _svc.CreateCashbookAsync(req, tid, uid, ct);
        return Created($"/api/finance/cashbook/{id}", new { id });
    }

    [HttpDelete("cashbook/{id}")]
    public async Task<IActionResult> DeleteCashbook(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        await _svc.DeleteCashbookAsync(id, tid, ct);
        return Ok(new { message = "삭제됨" });
    }

    // ── 매입매출장 ──

    [HttpGet("purchase-sales-ledger")]
    public async Task<IActionResult> GetPurchaseSalesLedger([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetPurchaseSalesLedgerAsync(tid, from, to, ct));
    }

    // ── 부가세 신고자료 ──

    [HttpGet("vat")]
    public async Task<IActionResult> GetVat([FromQuery] int? year, [FromQuery] int? half, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        var y = year ?? DateTime.Today.Year;
        var h = half ?? (DateTime.Today.Month <= 6 ? 1 : 2);
        return Ok(await _svc.GetVatSummaryAsync(tid, y, h, ct));
    }

    // ── 경비 ──

    [HttpGet("expenses")]
    public async Task<IActionResult> GetExpenses([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetExpensesAsync(tid, from, to, ct));
    }

    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense([FromBody] CreateExpenseRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();
        var id = await _svc.CreateExpenseAsync(req, tid, uid, ct);
        return Created($"/api/finance/expenses/{id}", new { id });
    }

    /// <summary>경비 승인/반려</summary>
    [HttpPost("expenses/{id}/process")]
    public async Task<IActionResult> ProcessExpense(string id, [FromBody] ProcessExpenseRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        await _svc.ApproveExpenseAsync(id, tid, req.Action, ct);
        return Ok(new { message = req.Action == "approved" ? "승인됨" : "반려됨" });
    }

    // ── 정합성 검증 ──

    /// <summary>데이터 정합성 자동 검증 (8개 항목)</summary>
    [HttpGet("integrity-check")]
    public async Task<IActionResult> CheckIntegrity(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.CheckIntegrityAsync(tid, ct));
    }

    // ── 손익현황 ──

    [HttpGet("profit")]
    public async Task<IActionResult> GetProfit([FromQuery] int? year, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetProfitAsync(tid, year ?? DateTime.Today.Year, ct));
    }

    // ── 대시보드 ──

    /// <summary>대시보드 요약 데이터 (KPI, 차트, 최근거래)</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetDashboardAsync(tid, ct));
    }
}
