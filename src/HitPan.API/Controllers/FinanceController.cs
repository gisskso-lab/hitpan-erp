using HitPan.API.Authorization;
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
    [RequirePermission("ACCOUNTING", "view")]
    public async Task<IActionResult> GetCashbook([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetCashbookAsync(tid, from, to, ct));
    }

    [HttpPost("cashbook")]
    [RequirePermission("ACCOUNTING", "create")]
    public async Task<IActionResult> CreateCashbook([FromBody] CreateCashbookRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();
        var id = await _svc.CreateCashbookAsync(req, tid, uid, ct);
        return Created($"/api/finance/cashbook/{id}", new { id });
    }

    [HttpDelete("cashbook/{id}")]
    [RequirePermission("ACCOUNTING", "delete")]
    public async Task<IActionResult> DeleteCashbook(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        await _svc.DeleteCashbookAsync(id, tid, ct);
        return Ok(new { message = "삭제됨" });
    }

    // ── 매입매출장 ──

    [HttpGet("purchase-sales-ledger")]
    [RequirePermission("ACCOUNTING", "view")]
    public async Task<IActionResult> GetPurchaseSalesLedger([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetPurchaseSalesLedgerAsync(tid, from, to, ct));
    }

    // ── 부가세 신고자료 ──

    [HttpGet("vat")]
    [RequirePermission("ACCOUNTING", "view")]
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
    [RequirePermission("ACCOUNTING", "view")]
    public async Task<IActionResult> GetExpenses([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? limit, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        // 헌법 #19 정합 — limit 미지정 시 500 (5/26 진범 #4·#7: 27,640건 폭탄 봉합)
        return Ok(await _svc.GetExpensesAsync(tid, from, to, limit ?? 500, ct));
    }

    [HttpPost("expenses")]
    [RequirePermission("ACCOUNTING", "create")]
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
    [RequirePermission("ACCOUNTING", "update")]
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
    [RequirePermission("ACCOUNTING", "view")]
    public async Task<IActionResult> CheckIntegrity(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.CheckIntegrityAsync(tid, ct));
    }

    // ── 손익현황 ──

    [HttpGet("profit")]
    [RequirePermission("ACCOUNTING", "view")]
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

    // ── 계정과목 ──

    [HttpGet("accounts")]
    [RequirePermission("ACCOUNTING", "view")]
    public async Task<IActionResult> GetAccounts(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetAccountsAsync(tid, ct));
    }

    [HttpPost("accounts")]
    [RequirePermission("ACCOUNTING", "create")]
    public async Task<IActionResult> CreateAccount([FromBody] CreateAccountRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        var code = await _svc.CreateAccountAsync(tid, req, ct);
        return Created($"/api/finance/accounts/{code}", new { code });
    }

    [HttpPut("accounts/{code}")]
    [RequirePermission("ACCOUNTING", "update")]
    public async Task<IActionResult> UpdateAccount(string code, [FromBody] UpdateAccountRequest req, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        await _svc.UpdateAccountAsync(tid, code, req, ct);
        return Ok(new { message = "수정됨" });
    }

    [HttpDelete("accounts/{code}")]
    [RequirePermission("ACCOUNTING", "delete")]
    public async Task<IActionResult> DeleteAccount(string code, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        await _svc.DeleteAccountAsync(tid, code, ct);
        return Ok(new { message = "삭제됨" });
    }
}
