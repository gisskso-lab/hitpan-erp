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
}
