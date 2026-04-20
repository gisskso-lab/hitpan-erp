using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportController(IReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>
    /// 견적 현황을 조회한다.
    /// </summary>
    [HttpGet("quotations")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetQuotationReport(
        [FromQuery] string view = "period",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetQuotationReportAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 수주 현황을 조회한다.
    /// </summary>
    [HttpGet("sales-orders")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetSalesOrderReport(
        [FromQuery] string view = "period",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetSalesOrderReportAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 판매(거래명세서) 현황을 조회한다.
    /// </summary>
    [HttpGet("sales")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetSalesReport(
        [FromQuery] string view = "period",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetSalesReportAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 발주 현황을 조회한다.
    /// </summary>
    [HttpGet("purchase-orders")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetPurchaseOrderReport(
        [FromQuery] string view = "period",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetPurchaseOrderReportAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 매입 현황을 조회한다.
    /// </summary>
    [HttpGet("purchases")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetPurchaseReport(
        [FromQuery] string view = "period",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetPurchaseReportAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 반품 현황을 조회한다.
    /// </summary>
    [HttpGet("returns")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetReturnReport(
        [FromQuery] string view = "period",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetReturnReportAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 판매 순위표를 조회한다.
    /// </summary>
    [HttpGet("sales-ranking")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetSalesRanking(
        [FromQuery] string view = "partner",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetSalesRankingAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 판매 수익성 분석을 조회한다.
    /// </summary>
    [HttpGet("sales-profitability")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetSalesProfitability(
        [FromQuery] string view = "partner",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetSalesProfitabilityAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 판매 통계를 조회한다.
    /// </summary>
    [HttpGet("sales-statistics")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetSalesStatistics(
        [FromQuery] string view = "item-monthly",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetSalesStatisticsAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 매입 순위표를 조회한다.
    /// </summary>
    [HttpGet("purchase-ranking")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetPurchaseRanking(
        [FromQuery] string view = "item",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetPurchaseRankingAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 매입 통계를 조회한다.
    /// </summary>
    [HttpGet("purchase-statistics")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetPurchaseStatistics(
        [FromQuery] string view = "item-monthly",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetPurchaseStatisticsAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 수불부(원장)를 조회한다. (상품별 / 업체별)
    /// </summary>
    [HttpGet("stock-ledger")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetStockLedger(
        [FromQuery] string view = "item",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? partner = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetStockLedgerAsync(view, tenantId, from, to, partner, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>
    /// 재고현황을 조회한다. (전체 현재고 / 안전재고 미달 / 창고별)
    /// </summary>
    [HttpGet("stock-status")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetStockStatus(
        [FromQuery] string view = "current",
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var rows = await _reportService.GetStockStatusAsync(view, tenantId, ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }
}
