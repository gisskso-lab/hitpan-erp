using System.Data;
using Dapper;
using HitPan.Application.DTOs.Report;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 현황 리포트 비즈니스 로직을 처리한다.
/// </summary>
public class ReportService : IReportService
{
    private readonly IDbConnection _db;

    public ReportService(IDbConnection db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetQuotationReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner" => """
                SELECT
                    p.partner_name AS Label,
                    COUNT(DISTINCT q.quote_id) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(q.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(q.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(q.total_amount + q.vat_amount), 0) AS TotalAmount
                FROM quotations q
                LEFT JOIN partners p
                    ON p.partner_id = q.partner_id AND p.tenant_id = q.tenant_id
                WHERE q.tenant_id = @TenantId AND q.is_deleted = 0
                  AND (@From IS NULL OR q.quote_date >= @From)
                  AND (@To IS NULL OR q.quote_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY q.partner_id, p.partner_name
                ORDER BY SupplyAmount DESC
                """,
            "item" => """
                SELECT
                    i.item_name AS Label,
                    COUNT(DISTINCT q.quote_id) AS Count,
                    COALESCE(SUM(qi.qty), 0) AS Qty,
                    COALESCE(SUM(qi.amount), 0) AS SupplyAmount,
                    COALESCE(SUM(qi.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(qi.amount + qi.vat_amount), 0) AS TotalAmount
                FROM quotation_items qi
                INNER JOIN quotations q ON q.quote_id = qi.quote_id
                LEFT JOIN items i ON i.item_id = qi.item_id AND i.tenant_id = q.tenant_id
                LEFT JOIN partners p ON p.partner_id = q.partner_id AND p.tenant_id = q.tenant_id
                WHERE q.tenant_id = @TenantId AND q.is_deleted = 0
                  AND (@From IS NULL OR q.quote_date >= @From)
                  AND (@To IS NULL OR q.quote_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY qi.item_id, i.item_name
                ORDER BY SupplyAmount DESC
                """,
            _ => """
                SELECT
                    DATE_FORMAT(q.quote_date, '%Y-%m-%d') AS Label,
                    COUNT(*) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(q.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(q.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(q.total_amount + q.vat_amount), 0) AS TotalAmount
                FROM quotations q
                LEFT JOIN partners p
                    ON p.partner_id = q.partner_id AND p.tenant_id = q.tenant_id
                WHERE q.tenant_id = @TenantId AND q.is_deleted = 0
                  AND (@From IS NULL OR q.quote_date >= @From)
                  AND (@To IS NULL OR q.quote_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY q.quote_date
                ORDER BY q.quote_date
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetSalesOrderReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner" => """
                SELECT
                    p.partner_name AS Label,
                    COUNT(DISTINCT so.order_id) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(so.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(so.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(so.total_amount + so.vat_amount), 0) AS TotalAmount
                FROM sales_orders so
                LEFT JOIN partners p
                    ON p.partner_id = so.partner_id AND p.tenant_id = so.tenant_id
                WHERE so.tenant_id = @TenantId AND so.is_deleted = 0
                  AND (@From IS NULL OR so.order_date >= @From)
                  AND (@To IS NULL OR so.order_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY so.partner_id, p.partner_name
                ORDER BY SupplyAmount DESC
                """,
            "item" => """
                SELECT
                    i.item_name AS Label,
                    COUNT(DISTINCT so.order_id) AS Count,
                    COALESCE(SUM(soi.ordered_qty), 0) AS Qty,
                    COALESCE(SUM(soi.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(soi.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(soi.supply_amount + soi.vat_amount), 0) AS TotalAmount
                FROM sales_order_items soi
                INNER JOIN sales_orders so ON so.order_id = soi.order_id AND so.tenant_id = soi.tenant_id
                LEFT JOIN items i ON i.item_id = soi.item_id AND i.tenant_id = soi.tenant_id
                LEFT JOIN partners p ON p.partner_id = so.partner_id AND p.tenant_id = so.tenant_id
                WHERE so.tenant_id = @TenantId AND so.is_deleted = 0
                  AND (@From IS NULL OR so.order_date >= @From)
                  AND (@To IS NULL OR so.order_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY soi.item_id, i.item_name
                ORDER BY SupplyAmount DESC
                """,
            _ => """
                SELECT
                    DATE_FORMAT(so.order_date, '%Y-%m-%d') AS Label,
                    COUNT(*) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(so.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(so.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(so.total_amount + so.vat_amount), 0) AS TotalAmount
                FROM sales_orders so
                LEFT JOIN partners p
                    ON p.partner_id = so.partner_id AND p.tenant_id = so.tenant_id
                WHERE so.tenant_id = @TenantId AND so.is_deleted = 0
                  AND (@From IS NULL OR so.order_date >= @From)
                  AND (@To IS NULL OR so.order_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY so.order_date
                ORDER BY so.order_date
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetSalesReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner" => """
                SELECT
                    p.partner_name AS Label,
                    COUNT(DISTINCT sd.delivery_id) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
                FROM sales_deliveries sd
                LEFT JOIN partners p
                    ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sd.partner_id, p.partner_name
                ORDER BY SupplyAmount DESC
                """,
            "item" => """
                SELECT
                    i.item_name AS Label,
                    COUNT(DISTINCT sd.delivery_id) AS Count,
                    COALESCE(SUM(sdi.qty), 0) AS Qty,
                    COALESCE(SUM(sdi.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(sdi.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(sdi.supply_amount + sdi.vat_amount), 0) AS TotalAmount
                FROM sales_delivery_items sdi
                INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
                LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
                LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sdi.item_id, i.item_name
                ORDER BY SupplyAmount DESC
                """,
            _ => """
                SELECT
                    DATE_FORMAT(sd.delivery_date, '%Y-%m-%d') AS Label,
                    COUNT(*) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
                FROM sales_deliveries sd
                LEFT JOIN partners p
                    ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sd.delivery_date
                ORDER BY sd.delivery_date
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetPurchaseOrderReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner" => """
                SELECT
                    p.partner_name AS Label,
                    COUNT(DISTINCT po.po_id) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(po.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(po.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(po.total_amount + po.vat_amount), 0) AS TotalAmount
                FROM purchase_orders po
                LEFT JOIN partners p
                    ON p.partner_id = po.partner_id AND p.tenant_id = po.tenant_id
                WHERE po.tenant_id = @TenantId AND po.is_deleted = 0
                  AND (@From IS NULL OR po.po_date >= @From)
                  AND (@To IS NULL OR po.po_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY po.partner_id, p.partner_name
                ORDER BY SupplyAmount DESC
                """,
            "item" => """
                SELECT
                    i.item_name AS Label,
                    COUNT(DISTINCT po.po_id) AS Count,
                    COALESCE(SUM(poi.ordered_qty), 0) AS Qty,
                    COALESCE(SUM(poi.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(poi.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(poi.supply_amount + poi.vat_amount), 0) AS TotalAmount
                FROM purchase_order_items poi
                INNER JOIN purchase_orders po ON po.po_id = poi.po_id AND po.tenant_id = poi.tenant_id
                LEFT JOIN items i ON i.item_id = poi.item_id AND i.tenant_id = poi.tenant_id
                LEFT JOIN partners p ON p.partner_id = po.partner_id AND p.tenant_id = po.tenant_id
                WHERE po.tenant_id = @TenantId AND po.is_deleted = 0
                  AND (@From IS NULL OR po.po_date >= @From)
                  AND (@To IS NULL OR po.po_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY poi.item_id, i.item_name
                ORDER BY SupplyAmount DESC
                """,
            _ => """
                SELECT
                    DATE_FORMAT(po.po_date, '%Y-%m-%d') AS Label,
                    COUNT(*) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(po.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(po.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(po.total_amount + po.vat_amount), 0) AS TotalAmount
                FROM purchase_orders po
                LEFT JOIN partners p
                    ON p.partner_id = po.partner_id AND p.tenant_id = po.tenant_id
                WHERE po.tenant_id = @TenantId AND po.is_deleted = 0
                  AND (@From IS NULL OR po.po_date >= @From)
                  AND (@To IS NULL OR po.po_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY po.po_date
                ORDER BY po.po_date
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetPurchaseReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner" => """
                SELECT
                    p.partner_name AS Label,
                    COUNT(DISTINCT pr.receipt_id) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(pr.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(pr.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(pr.total_amount + pr.vat_amount), 0) AS TotalAmount
                FROM purchase_receipts pr
                LEFT JOIN partners p
                    ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
                WHERE pr.tenant_id = @TenantId
                  AND (@From IS NULL OR pr.receipt_date >= @From)
                  AND (@To IS NULL OR pr.receipt_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY pr.partner_id, p.partner_name
                ORDER BY SupplyAmount DESC
                """,
            "item" => """
                SELECT
                    i.item_name AS Label,
                    COUNT(DISTINCT pr.receipt_id) AS Count,
                    COALESCE(SUM(pri.qty), 0) AS Qty,
                    COALESCE(SUM(pri.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(pri.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(pri.supply_amount + pri.vat_amount), 0) AS TotalAmount
                FROM purchase_receipt_items pri
                INNER JOIN purchase_receipts pr ON pr.receipt_id = pri.receipt_id AND pr.tenant_id = pri.tenant_id
                LEFT JOIN items i ON i.item_id = pri.item_id AND i.tenant_id = pri.tenant_id
                LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
                WHERE pr.tenant_id = @TenantId
                  AND (@From IS NULL OR pr.receipt_date >= @From)
                  AND (@To IS NULL OR pr.receipt_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY pri.item_id, i.item_name
                ORDER BY SupplyAmount DESC
                """,
            _ => """
                SELECT
                    DATE_FORMAT(pr.receipt_date, '%Y-%m-%d') AS Label,
                    COUNT(*) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(pr.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(pr.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(pr.total_amount + pr.vat_amount), 0) AS TotalAmount
                FROM purchase_receipts pr
                LEFT JOIN partners p
                    ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
                WHERE pr.tenant_id = @TenantId
                  AND (@From IS NULL OR pr.receipt_date >= @From)
                  AND (@To IS NULL OR pr.receipt_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY pr.receipt_date
                ORDER BY pr.receipt_date
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetReturnReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner" => """
                SELECT
                    p.partner_name AS Label,
                    COUNT(DISTINCT rt.return_id) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(rt.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(rt.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(rt.total_amount + rt.vat_amount), 0) AS TotalAmount
                FROM purchase_returns rt
                LEFT JOIN partners p
                    ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
                WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0
                  AND (@From IS NULL OR rt.return_date >= @From)
                  AND (@To IS NULL OR rt.return_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY rt.partner_id, p.partner_name
                ORDER BY SupplyAmount DESC
                """,
            "item" => """
                SELECT
                    i.item_name AS Label,
                    COUNT(DISTINCT rt.return_id) AS Count,
                    COALESCE(SUM(rti.qty), 0) AS Qty,
                    COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
                FROM purchase_return_items rti
                INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
                LEFT JOIN items i ON i.item_id = rti.item_id AND i.tenant_id = rti.tenant_id
                LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
                WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0
                  AND (@From IS NULL OR rt.return_date >= @From)
                  AND (@To IS NULL OR rt.return_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY rti.item_id, i.item_name
                ORDER BY SupplyAmount DESC
                """,
            _ => """
                SELECT
                    DATE_FORMAT(rt.return_date, '%Y-%m-%d') AS Label,
                    COUNT(*) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(rt.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(rt.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(rt.total_amount + rt.vat_amount), 0) AS TotalAmount
                FROM purchase_returns rt
                LEFT JOIN partners p
                    ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
                WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0
                  AND (@From IS NULL OR rt.return_date >= @From)
                  AND (@To IS NULL OR rt.return_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY rt.return_date
                ORDER BY rt.return_date
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetSalesRankingAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner" => """
                SELECT
                    p.partner_name AS Label,
                    COUNT(DISTINCT sd.delivery_id) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
                FROM sales_deliveries sd
                LEFT JOIN partners p
                    ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sd.partner_id, p.partner_name
                ORDER BY SupplyAmount DESC
                """,
            "item" => """
                SELECT
                    i.item_name AS Label,
                    COUNT(DISTINCT sd.delivery_id) AS Count,
                    COALESCE(SUM(sdi.qty), 0) AS Qty,
                    COALESCE(SUM(sdi.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(sdi.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(sdi.supply_amount + sdi.vat_amount), 0) AS TotalAmount
                FROM sales_delivery_items sdi
                INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
                LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
                LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sdi.item_id, i.item_name
                ORDER BY SupplyAmount DESC
                """,
            _ => """
                SELECT
                    DATE_FORMAT(sd.delivery_date, '%Y-%m-%d') AS Label,
                    COUNT(*) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
                FROM sales_deliveries sd
                LEFT JOIN partners p
                    ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sd.delivery_date
                ORDER BY SupplyAmount DESC
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ProfitReportRow>> GetSalesProfitabilityAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner" => """
                SELECT
                    p.partner_name AS Label,
                    COUNT(DISTINCT sd.delivery_id) AS Count,
                    COALESCE(SUM(sdi.supply_amount), 0) AS Revenue,
                    COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Cost,
                    COALESCE(SUM(sdi.supply_amount), 0) - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Profit,
                    CASE WHEN COALESCE(SUM(sdi.supply_amount), 0) = 0 THEN 0
                         ELSE ROUND((COALESCE(SUM(sdi.supply_amount), 0) - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0))
                              / COALESCE(SUM(sdi.supply_amount), 0) * 100, 2)
                    END AS ProfitRate
                FROM sales_delivery_items sdi
                INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
                LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
                LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sd.partner_id, p.partner_name
                ORDER BY Revenue DESC
                """,
            "item" => """
                SELECT
                    i.item_name AS Label,
                    COUNT(DISTINCT sd.delivery_id) AS Count,
                    COALESCE(SUM(sdi.supply_amount), 0) AS Revenue,
                    COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Cost,
                    COALESCE(SUM(sdi.supply_amount), 0) - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Profit,
                    CASE WHEN COALESCE(SUM(sdi.supply_amount), 0) = 0 THEN 0
                         ELSE ROUND((COALESCE(SUM(sdi.supply_amount), 0) - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0))
                              / COALESCE(SUM(sdi.supply_amount), 0) * 100, 2)
                    END AS ProfitRate
                FROM sales_delivery_items sdi
                INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
                LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
                LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sdi.item_id, i.item_name
                ORDER BY Revenue DESC
                """,
            _ => """
                SELECT
                    DATE_FORMAT(sd.delivery_date, '%Y-%m-%d') AS Label,
                    COUNT(DISTINCT sd.delivery_id) AS Count,
                    COALESCE(SUM(sdi.supply_amount), 0) AS Revenue,
                    COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Cost,
                    COALESCE(SUM(sdi.supply_amount), 0) - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Profit,
                    CASE WHEN COALESCE(SUM(sdi.supply_amount), 0) = 0 THEN 0
                         ELSE ROUND((COALESCE(SUM(sdi.supply_amount), 0) - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0))
                              / COALESCE(SUM(sdi.supply_amount), 0) * 100, 2)
                    END AS ProfitRate
                FROM sales_delivery_items sdi
                INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
                LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
                LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sd.delivery_date
                ORDER BY Revenue DESC
                """
        };

        var rows = await _db.QueryAsync<ProfitReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetSalesStatisticsAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner-monthly" => """
                SELECT
                    CONCAT(p.partner_name, ' (', DATE_FORMAT(sd.delivery_date, '%Y-%m'), ')') AS Label,
                    COUNT(DISTINCT sd.delivery_id) AS Count,
                    COALESCE(SUM(sdi.qty), 0) AS Qty,
                    COALESCE(SUM(sdi.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(sdi.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(sdi.supply_amount + sdi.vat_amount), 0) AS TotalAmount
                FROM sales_delivery_items sdi
                INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
                LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sd.partner_id, p.partner_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
                ORDER BY p.partner_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
                """,
            _ => """
                SELECT
                    CONCAT(i.item_name, ' (', DATE_FORMAT(sd.delivery_date, '%Y-%m'), ')') AS Label,
                    COUNT(DISTINCT sd.delivery_id) AS Count,
                    COALESCE(SUM(sdi.qty), 0) AS Qty,
                    COALESCE(SUM(sdi.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(sdi.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(sdi.supply_amount + sdi.vat_amount), 0) AS TotalAmount
                FROM sales_delivery_items sdi
                INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
                LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
                LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
                WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0
                  AND (@From IS NULL OR sd.delivery_date >= @From)
                  AND (@To IS NULL OR sd.delivery_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sdi.item_id, i.item_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
                ORDER BY i.item_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetPurchaseRankingAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner" => """
                SELECT
                    p.partner_name AS Label,
                    COUNT(DISTINCT pr.receipt_id) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(pr.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(pr.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(pr.total_amount + pr.vat_amount), 0) AS TotalAmount
                FROM purchase_receipts pr
                LEFT JOIN partners p
                    ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
                WHERE pr.tenant_id = @TenantId
                  AND (@From IS NULL OR pr.receipt_date >= @From)
                  AND (@To IS NULL OR pr.receipt_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY pr.partner_id, p.partner_name
                ORDER BY SupplyAmount DESC
                """,
            "item" => """
                SELECT
                    i.item_name AS Label,
                    COUNT(DISTINCT pr.receipt_id) AS Count,
                    COALESCE(SUM(pri.qty), 0) AS Qty,
                    COALESCE(SUM(pri.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(pri.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(pri.supply_amount + pri.vat_amount), 0) AS TotalAmount
                FROM purchase_receipt_items pri
                INNER JOIN purchase_receipts pr ON pr.receipt_id = pri.receipt_id AND pr.tenant_id = pri.tenant_id
                LEFT JOIN items i ON i.item_id = pri.item_id AND i.tenant_id = pri.tenant_id
                LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
                WHERE pr.tenant_id = @TenantId
                  AND (@From IS NULL OR pr.receipt_date >= @From)
                  AND (@To IS NULL OR pr.receipt_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY pri.item_id, i.item_name
                ORDER BY SupplyAmount DESC
                """,
            _ => """
                SELECT
                    DATE_FORMAT(pr.receipt_date, '%Y-%m-%d') AS Label,
                    COUNT(*) AS Count,
                    0 AS Qty,
                    COALESCE(SUM(pr.total_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(pr.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(pr.total_amount + pr.vat_amount), 0) AS TotalAmount
                FROM purchase_receipts pr
                LEFT JOIN partners p
                    ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
                WHERE pr.tenant_id = @TenantId
                  AND (@From IS NULL OR pr.receipt_date >= @From)
                  AND (@To IS NULL OR pr.receipt_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY pr.receipt_date
                ORDER BY SupplyAmount DESC
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetPurchaseStatisticsAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        var sql = viewType switch
        {
            "partner-monthly" => """
                SELECT
                    CONCAT(p.partner_name, ' (', DATE_FORMAT(pr.receipt_date, '%Y-%m'), ')') AS Label,
                    COUNT(DISTINCT pr.receipt_id) AS Count,
                    COALESCE(SUM(pri.qty), 0) AS Qty,
                    COALESCE(SUM(pri.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(pri.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(pri.supply_amount + pri.vat_amount), 0) AS TotalAmount
                FROM purchase_receipt_items pri
                INNER JOIN purchase_receipts pr ON pr.receipt_id = pri.receipt_id AND pr.tenant_id = pri.tenant_id
                LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
                WHERE pr.tenant_id = @TenantId
                  AND (@From IS NULL OR pr.receipt_date >= @From)
                  AND (@To IS NULL OR pr.receipt_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY pr.partner_id, p.partner_name, DATE_FORMAT(pr.receipt_date, '%Y-%m')
                ORDER BY p.partner_name, DATE_FORMAT(pr.receipt_date, '%Y-%m')
                """,
            _ => """
                SELECT
                    CONCAT(i.item_name, ' (', DATE_FORMAT(pr.receipt_date, '%Y-%m'), ')') AS Label,
                    COUNT(DISTINCT pr.receipt_id) AS Count,
                    COALESCE(SUM(pri.qty), 0) AS Qty,
                    COALESCE(SUM(pri.supply_amount), 0) AS SupplyAmount,
                    COALESCE(SUM(pri.vat_amount), 0) AS VatAmount,
                    COALESCE(SUM(pri.supply_amount + pri.vat_amount), 0) AS TotalAmount
                FROM purchase_receipt_items pri
                INNER JOIN purchase_receipts pr ON pr.receipt_id = pri.receipt_id AND pr.tenant_id = pri.tenant_id
                LEFT JOIN items i ON i.item_id = pri.item_id AND i.tenant_id = pri.tenant_id
                LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
                WHERE pr.tenant_id = @TenantId
                  AND (@From IS NULL OR pr.receipt_date >= @From)
                  AND (@To IS NULL OR pr.receipt_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY pri.item_id, i.item_name, DATE_FORMAT(pr.receipt_date, '%Y-%m')
                ORDER BY i.item_name, DATE_FORMAT(pr.receipt_date, '%Y-%m')
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }
}
