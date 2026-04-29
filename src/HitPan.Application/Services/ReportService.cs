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
        // 사장님 결재 2026-04-29: 판매현황 11종 조회유형 풀스택 분기.
        // 모든 SQL 공통: tenant 격리(§#2) + is_deleted=0 + status<>'cancelled'.
        // 거래명세서(sales_deliveries)가 워크플로우 종착점(§#20) 이므로 매출 통계 기준점.
        var sql = viewType switch
        {
            "partner" => SALES_BY_PARTNER,
            "item" => SALES_BY_ITEM,
            "monthly" => SALES_MONTHLY,
            "partner-yearly" => SALES_PARTNER_YEARLY,
            "employee" => SALES_BY_EMPLOYEE,
            "quote-vs-sales" => SALES_QUOTE_VS_SALES,
            "price-change" => SALES_PRICE_CHANGE,
            "return" => SALES_RETURN,
            "total" => SALES_TOTAL_INCLUDING_RETURN,
            "new-partner" => SALES_NEW_PARTNER,
            _ => SALES_BY_PERIOD
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

    // ─── 판매현황 11종 SQL 상수 (사장님 결재 2026-04-29) ───
    private const string SALES_BY_PERIOD = """
        SELECT
            DATE_FORMAT(sd.delivery_date, '%Y-%m-%d') AS Label,
            COUNT(*) AS Count,
            0 AS Qty,
            COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
        FROM sales_deliveries sd
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sd.delivery_date
        ORDER BY sd.delivery_date
        """;

    private const string SALES_BY_PARTNER = """
        SELECT
            p.partner_name AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
        FROM sales_deliveries sd
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sd.partner_id, p.partner_name
        ORDER BY SupplyAmount DESC
        """;

    private const string SALES_BY_ITEM = """
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
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sdi.item_id, i.item_name
        ORDER BY SupplyAmount DESC
        """;

    // 월별 집계 (상품판매일(월)계표) — 일별 상세를 월로 묶어서 흐름 보기.
    private const string SALES_MONTHLY = """
        SELECT
            DATE_FORMAT(sd.delivery_date, '%Y-%m') AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            COALESCE(SUM(sdi.qty), 0) AS Qty,
            COALESCE(SUM(sdi.supply_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sdi.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sdi.supply_amount + sdi.vat_amount), 0) AS TotalAmount
        FROM sales_delivery_items sdi
        INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY DATE_FORMAT(sd.delivery_date, '%Y-%m')
        ORDER BY Label
        """;

    // 업체별 판매집계표(년간) — 업체 + 월의 매트릭스. Label: "업체명 (YYYY-MM)".
    private const string SALES_PARTNER_YEARLY = """
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
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sd.partner_id, p.partner_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
        ORDER BY p.partner_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
        """;

    // 사원별 세부판매현황 — sales_deliveries.employee_id 기준. 미지정(NULL)이면 작성자(created_by)로 fallback.
    private const string SALES_BY_EMPLOYEE = """
        SELECT
            COALESCE(e.emp_name, e2.emp_name, '미지정') AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            COALESCE(SUM(sdi.qty), 0) AS Qty,
            COALESCE(SUM(sdi.supply_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sdi.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sdi.supply_amount + sdi.vat_amount), 0) AS TotalAmount
        FROM sales_deliveries sd
        LEFT JOIN sales_delivery_items sdi ON sdi.delivery_id = sd.delivery_id AND sdi.tenant_id = sd.tenant_id
        LEFT JOIN employees e  ON e.employee_id  = sd.employee_id AND e.tenant_id = sd.tenant_id
        LEFT JOIN employees e2 ON e2.user_id     = sd.created_by  AND e2.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR COALESCE(e.emp_name, e2.emp_name) LIKE CONCAT('%', @Partner, '%'))
        GROUP BY COALESCE(sd.employee_id, sd.created_by), Label
        ORDER BY SupplyAmount DESC
        """;

    // 견적제출대비 판매현황 — 견적은 quotations 테이블, 매출은 sales_deliveries. 업체별 비교.
    private const string SALES_QUOTE_VS_SALES = """
        SELECT
            p.partner_name AS Label,
            (SELECT COUNT(*) FROM quotations q
              WHERE q.tenant_id = p.tenant_id AND q.partner_id = p.partner_id AND q.is_deleted = 0
                AND (@From IS NULL OR q.quote_date >= @From)
                AND (@To IS NULL OR q.quote_date <= @To)) AS Count,
            (SELECT COUNT(*) FROM sales_deliveries sd2
              WHERE sd2.tenant_id = p.tenant_id AND sd2.partner_id = p.partner_id
                AND sd2.is_deleted = 0 AND sd2.status <> 'cancelled'
                AND (@From IS NULL OR sd2.delivery_date >= @From)
                AND (@To IS NULL OR sd2.delivery_date <= @To)) AS Qty,
            COALESCE((SELECT SUM(q.total_amount) FROM quotations q
              WHERE q.tenant_id = p.tenant_id AND q.partner_id = p.partner_id AND q.is_deleted = 0
                AND (@From IS NULL OR q.quote_date >= @From)
                AND (@To IS NULL OR q.quote_date <= @To)), 0) AS SupplyAmount,
            COALESCE((SELECT SUM(sd2.total_amount) FROM sales_deliveries sd2
              WHERE sd2.tenant_id = p.tenant_id AND sd2.partner_id = p.partner_id
                AND sd2.is_deleted = 0 AND sd2.status <> 'cancelled'
                AND (@From IS NULL OR sd2.delivery_date >= @From)
                AND (@To IS NULL OR sd2.delivery_date <= @To)), 0) AS VatAmount,
            0 AS TotalAmount
        FROM partners p
        WHERE p.tenant_id = @TenantId AND p.is_deleted = 0
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        HAVING Count > 0 OR Qty > 0
        ORDER BY VatAmount DESC
        """;

    // 기간별 판매단가변동현황 — 같은 품목의 unit_price 추이.
    private const string SALES_PRICE_CHANGE = """
        SELECT
            CONCAT(i.item_name, ' (', DATE_FORMAT(sd.delivery_date, '%Y-%m'), ')') AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            COALESCE(SUM(sdi.qty), 0) AS Qty,
            COALESCE(MIN(sdi.unit_price), 0) AS SupplyAmount,
            COALESCE(MAX(sdi.unit_price), 0) AS VatAmount,
            COALESCE(AVG(sdi.unit_price), 0) AS TotalAmount
        FROM sales_delivery_items sdi
        INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
        LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sdi.item_id, i.item_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
        ORDER BY i.item_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
        """;

    // 매출반품현황 — sales_returns 기준.
    private const string SALES_RETURN = """
        SELECT
            DATE_FORMAT(sr.return_date, '%Y-%m-%d') AS Label,
            COUNT(DISTINCT sr.return_id) AS Count,
            COALESCE(SUM(sri.qty), 0) AS Qty,
            COALESCE(SUM(sri.supply_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sri.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sri.supply_amount + sri.vat_amount), 0) AS TotalAmount
        FROM sales_returns sr
        LEFT JOIN sales_return_items sri ON sri.return_id = sr.return_id AND sri.tenant_id = sr.tenant_id
        LEFT JOIN partners p ON p.partner_id = sr.partner_id AND p.tenant_id = sr.tenant_id
        WHERE sr.tenant_id = @TenantId AND sr.is_deleted = 0
          AND (@From IS NULL OR sr.return_date >= @From)
          AND (@To IS NULL OR sr.return_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sr.return_date
        ORDER BY sr.return_date DESC
        """;

    // 판매종합현황(반품포함) — 매출에서 반품 차감. UNION ALL 로 양수/음수 합산.
    private const string SALES_TOTAL_INCLUDING_RETURN = """
        SELECT Label,
               SUM(Count)        AS Count,
               0                 AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
            SELECT
                DATE_FORMAT(sd.delivery_date, '%Y-%m-%d') AS Label,
                COUNT(*) AS Count,
                COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
                COALESCE(SUM(sd.vat_amount), 0)   AS VatAmount,
                COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
            FROM sales_deliveries sd
            LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
            WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
              AND (@From IS NULL OR sd.delivery_date >= @From)
              AND (@To IS NULL OR sd.delivery_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY sd.delivery_date

            UNION ALL

            SELECT
                DATE_FORMAT(sr.return_date, '%Y-%m-%d') AS Label,
                -COUNT(*) AS Count,
                -COALESCE(SUM(sri.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(sri.vat_amount), 0)   AS VatAmount,
                -COALESCE(SUM(sri.supply_amount + sri.vat_amount), 0) AS TotalAmount
            FROM sales_returns sr
            LEFT JOIN sales_return_items sri ON sri.return_id = sr.return_id AND sri.tenant_id = sr.tenant_id
            LEFT JOIN partners p ON p.partner_id = sr.partner_id AND p.tenant_id = sr.tenant_id
            WHERE sr.tenant_id = @TenantId AND sr.is_deleted = 0
              AND (@From IS NULL OR sr.return_date >= @From)
              AND (@To IS NULL OR sr.return_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY sr.return_date
        ) t
        GROUP BY Label
        ORDER BY Label
        """;

    // 신규업체개척현황 — 조회기간 안에서 첫 거래가 발생한 업체.
    private const string SALES_NEW_PARTNER = """
        SELECT
            p.partner_name AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
        FROM sales_deliveries sd
        INNER JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
          AND NOT EXISTS (
            SELECT 1 FROM sales_deliveries sd_prev
            WHERE sd_prev.tenant_id = sd.tenant_id
              AND sd_prev.partner_id = sd.partner_id
              AND sd_prev.is_deleted = 0
              AND sd_prev.status <> 'cancelled'
              AND sd_prev.delivery_date < @From
          )
        GROUP BY sd.partner_id, p.partner_name
        ORDER BY SupplyAmount DESC
        """;

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
                WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
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
                WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
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
                WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
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
        // 사장님 결재 2026-04-29: 판매순위표 5종 풀스택. 지역별/사원별 추가.
        // 지역별: partners.address 의 첫 토큰 (시·도) 으로 묶음. 컬럼 추가 없이 SUBSTRING_INDEX 활용.
        var sql = viewType switch
        {
            "partner" => RANKING_BY_PARTNER,
            "item" => RANKING_BY_ITEM,
            "period" => RANKING_BY_PERIOD,
            "region" => RANKING_BY_REGION,
            "employee" => RANKING_BY_EMPLOYEE,
            _ => RANKING_BY_PARTNER
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

    // ─── 판매순위표 5종 SQL 상수 ───
    private const string RANKING_BY_PARTNER = """
        SELECT
            p.partner_name AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
        FROM sales_deliveries sd
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sd.partner_id, p.partner_name
        ORDER BY SupplyAmount DESC
        """;

    private const string RANKING_BY_ITEM = """
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
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sdi.item_id, i.item_name
        ORDER BY SupplyAmount DESC
        """;

    // 기간별 — 월 단위 집계로 묶음 (일별이 너무 잘게 쪼개져 순위 의미 X)
    private const string RANKING_BY_PERIOD = """
        SELECT
            DATE_FORMAT(sd.delivery_date, '%Y-%m') AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
        FROM sales_deliveries sd
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY DATE_FORMAT(sd.delivery_date, '%Y-%m')
        ORDER BY SupplyAmount DESC
        """;

    // 지역별 — partners.address 의 첫 단어를 시·도로 간주. 빈 주소는 '미지정'.
    private const string RANKING_BY_REGION = """
        SELECT
            COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정') AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
        FROM sales_deliveries sd
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.address LIKE CONCAT('%', @Partner, '%'))
        GROUP BY Label
        ORDER BY SupplyAmount DESC
        """;

    // 사원별 — sales_deliveries.employee_id 우선, 없으면 created_by → employees.user_id 매칭.
    private const string RANKING_BY_EMPLOYEE = """
        SELECT
            COALESCE(e.emp_name, e2.emp_name, '미지정') AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(sd.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sd.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sd.total_amount + sd.vat_amount), 0) AS TotalAmount
        FROM sales_deliveries sd
        LEFT JOIN employees e  ON e.employee_id = sd.employee_id AND e.tenant_id = sd.tenant_id
        LEFT JOIN employees e2 ON e2.user_id    = sd.created_by  AND e2.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR COALESCE(e.emp_name, e2.emp_name) LIKE CONCAT('%', @Partner, '%'))
        GROUP BY COALESCE(sd.employee_id, sd.created_by), Label
        ORDER BY SupplyAmount DESC
        """;

    /// <inheritdoc />
    public async Task<List<ProfitReportRow>> GetSalesProfitabilityAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        // 사장님 결재 2026-04-29: 수익성분석 5종. 원가는 items.purchase_price → cost_price 순서로 fallback.
        // 매출(Revenue) - 원가(Cost) = 이익(Profit). 이익률 = Profit / Revenue * 100.
        var sql = viewType switch
        {
            "partner" => PROFIT_BY_PARTNER,
            "item" => PROFIT_BY_ITEM,
            "period" => PROFIT_BY_PERIOD,
            "region" => PROFIT_BY_REGION,
            "employee" => PROFIT_BY_EMPLOYEE,
            _ => PROFIT_BY_PARTNER
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

    // 수익성 SQL 템플릿: 그룹 키만 변경. 원가 = qty * COALESCE(purchase_price, cost_price, 0).
    private const string PROFIT_BY_PARTNER = """
        SELECT
            p.partner_name AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            COALESCE(SUM(sdi.supply_amount), 0) AS Revenue,
            COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Cost,
            COALESCE(SUM(sdi.supply_amount), 0)
              - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Profit,
            CASE WHEN COALESCE(SUM(sdi.supply_amount), 0) = 0 THEN 0
                 ELSE ROUND((COALESCE(SUM(sdi.supply_amount), 0)
                      - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0))
                    / COALESCE(SUM(sdi.supply_amount), 0) * 100, 2)
            END AS ProfitRate
        FROM sales_delivery_items sdi
        INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
        LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sd.partner_id, p.partner_name
        ORDER BY Revenue DESC
        """;

    private const string PROFIT_BY_ITEM = """
        SELECT
            i.item_name AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            COALESCE(SUM(sdi.supply_amount), 0) AS Revenue,
            COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Cost,
            COALESCE(SUM(sdi.supply_amount), 0)
              - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Profit,
            CASE WHEN COALESCE(SUM(sdi.supply_amount), 0) = 0 THEN 0
                 ELSE ROUND((COALESCE(SUM(sdi.supply_amount), 0)
                      - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0))
                    / COALESCE(SUM(sdi.supply_amount), 0) * 100, 2)
            END AS ProfitRate
        FROM sales_delivery_items sdi
        INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
        LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sdi.item_id, i.item_name
        ORDER BY Revenue DESC
        """;

    private const string PROFIT_BY_PERIOD = """
        SELECT
            DATE_FORMAT(sd.delivery_date, '%Y-%m') AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            COALESCE(SUM(sdi.supply_amount), 0) AS Revenue,
            COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Cost,
            COALESCE(SUM(sdi.supply_amount), 0)
              - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Profit,
            CASE WHEN COALESCE(SUM(sdi.supply_amount), 0) = 0 THEN 0
                 ELSE ROUND((COALESCE(SUM(sdi.supply_amount), 0)
                      - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0))
                    / COALESCE(SUM(sdi.supply_amount), 0) * 100, 2)
            END AS ProfitRate
        FROM sales_delivery_items sdi
        INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
        LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
        GROUP BY DATE_FORMAT(sd.delivery_date, '%Y-%m')
        ORDER BY Label
        """;

    private const string PROFIT_BY_REGION = """
        SELECT
            COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정') AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            COALESCE(SUM(sdi.supply_amount), 0) AS Revenue,
            COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Cost,
            COALESCE(SUM(sdi.supply_amount), 0)
              - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Profit,
            CASE WHEN COALESCE(SUM(sdi.supply_amount), 0) = 0 THEN 0
                 ELSE ROUND((COALESCE(SUM(sdi.supply_amount), 0)
                      - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0))
                    / COALESCE(SUM(sdi.supply_amount), 0) * 100, 2)
            END AS ProfitRate
        FROM sales_delivery_items sdi
        INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
        LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.address LIKE CONCAT('%', @Partner, '%'))
        GROUP BY Label
        ORDER BY Revenue DESC
        """;

    private const string PROFIT_BY_EMPLOYEE = """
        SELECT
            COALESCE(e.emp_name, e2.emp_name, '미지정') AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            COALESCE(SUM(sdi.supply_amount), 0) AS Revenue,
            COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Cost,
            COALESCE(SUM(sdi.supply_amount), 0)
              - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS Profit,
            CASE WHEN COALESCE(SUM(sdi.supply_amount), 0) = 0 THEN 0
                 ELSE ROUND((COALESCE(SUM(sdi.supply_amount), 0)
                      - COALESCE(SUM(sdi.qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0))
                    / COALESCE(SUM(sdi.supply_amount), 0) * 100, 2)
            END AS ProfitRate
        FROM sales_delivery_items sdi
        INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
        LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
        LEFT JOIN employees e  ON e.employee_id = sd.employee_id AND e.tenant_id = sd.tenant_id
        LEFT JOIN employees e2 ON e2.user_id    = sd.created_by  AND e2.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR COALESCE(e.emp_name, e2.emp_name) LIKE CONCAT('%', @Partner, '%'))
        GROUP BY COALESCE(sd.employee_id, sd.created_by), Label
        ORDER BY Revenue DESC
        """;

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetSalesStatisticsAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        // 사장님 결재 2026-04-29: 판매통계 8종 풀스택.
        // 4종 기본(품목/업체/사원/지역) + 4종 전년동기대비.
        // 전년동기는 from/to 를 1년 시프트한 기간을 같이 조회해 컬럼 비교.
        var sql = viewType switch
        {
            "item-monthly" => STATS_ITEM_MONTHLY,
            "partner-monthly" => STATS_PARTNER_MONTHLY,
            "employee-monthly" => STATS_EMPLOYEE_MONTHLY,
            "region-monthly" => STATS_REGION_MONTHLY,
            "item-yoy" => STATS_YOY_ITEM,
            "partner-yoy" => STATS_YOY_PARTNER,
            "employee-yoy" => STATS_YOY_EMPLOYEE,
            "region-yoy" => STATS_YOY_REGION,
            _ => STATS_ITEM_MONTHLY
        };

        // 전년동기는 추가 파라미터(@FromPrev / @ToPrev) 필요.
        var fromPrev = from?.AddYears(-1).Date;
        var toPrev = to?.AddYears(-1).Date;

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                FromPrev = fromPrev,
                ToPrev = toPrev,
                Partner = partner
            }, cancellationToken: ct));

        return rows.ToList();
    }

    // ─── 판매통계 8종 SQL 상수 ───
    // 4종 기본 (품목·업체·사원·지역 × 월별 매트릭스)
    private const string STATS_ITEM_MONTHLY = """
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
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sdi.item_id, i.item_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
        ORDER BY i.item_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
        """;

    private const string STATS_PARTNER_MONTHLY = """
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
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sd.partner_id, p.partner_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
        ORDER BY p.partner_name, DATE_FORMAT(sd.delivery_date, '%Y-%m')
        """;

    private const string STATS_EMPLOYEE_MONTHLY = """
        SELECT
            CONCAT(COALESCE(e.emp_name, e2.emp_name, '미지정'), ' (', DATE_FORMAT(sd.delivery_date, '%Y-%m'), ')') AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            COALESCE(SUM(sdi.qty), 0) AS Qty,
            COALESCE(SUM(sdi.supply_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sdi.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sdi.supply_amount + sdi.vat_amount), 0) AS TotalAmount
        FROM sales_delivery_items sdi
        INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
        LEFT JOIN employees e  ON e.employee_id = sd.employee_id AND e.tenant_id = sd.tenant_id
        LEFT JOIN employees e2 ON e2.user_id    = sd.created_by  AND e2.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR COALESCE(e.emp_name, e2.emp_name) LIKE CONCAT('%', @Partner, '%'))
        GROUP BY COALESCE(sd.employee_id, sd.created_by), COALESCE(e.emp_name, e2.emp_name, '미지정'),
                 DATE_FORMAT(sd.delivery_date, '%Y-%m')
        ORDER BY Label
        """;

    private const string STATS_REGION_MONTHLY = """
        SELECT
            CONCAT(
                COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정'),
                ' (', DATE_FORMAT(sd.delivery_date, '%Y-%m'), ')'
            ) AS Label,
            COUNT(DISTINCT sd.delivery_id) AS Count,
            COALESCE(SUM(sdi.qty), 0) AS Qty,
            COALESCE(SUM(sdi.supply_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sdi.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sdi.supply_amount + sdi.vat_amount), 0) AS TotalAmount
        FROM sales_delivery_items sdi
        INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (@From IS NULL OR sd.delivery_date >= @From)
          AND (@To IS NULL OR sd.delivery_date <= @To)
          AND (@Partner IS NULL OR p.address LIKE CONCAT('%', @Partner, '%'))
        GROUP BY COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정'),
                 DATE_FORMAT(sd.delivery_date, '%Y-%m')
        ORDER BY Label
        """;

    // 4종 전년동기대비 (Yoy = Year over Year)
    // SupplyAmount = 당기, VatAmount = 전년동기, TotalAmount = 증감액 (당기 - 전년).
    // Count = 당기 건수, Qty = 전년동기 건수.
    private const string STATS_YOY_ITEM = """
        SELECT
            i.item_name AS Label,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN sdi.supply_amount ELSE 0 END), 0) AS SupplyAmount,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN sdi.supply_amount ELSE 0 END), 0) AS VatAmount,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN sdi.supply_amount ELSE 0 END), 0)
              - COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN sdi.supply_amount ELSE 0 END), 0) AS TotalAmount
        FROM sales_delivery_items sdi
        INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
        LEFT JOIN items i ON i.item_id = sdi.item_id AND i.tenant_id = sdi.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (sd.delivery_date BETWEEN @FromPrev AND @ToPrev OR sd.delivery_date BETWEEN @From AND @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sdi.item_id, i.item_name
        HAVING SupplyAmount > 0 OR VatAmount > 0
        ORDER BY SupplyAmount DESC
        """;

    private const string STATS_YOY_PARTNER = """
        SELECT
            p.partner_name AS Label,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN sd.total_amount ELSE 0 END), 0) AS SupplyAmount,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN sd.total_amount ELSE 0 END), 0) AS VatAmount,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN sd.total_amount ELSE 0 END), 0)
              - COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN sd.total_amount ELSE 0 END), 0) AS TotalAmount
        FROM sales_deliveries sd
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (sd.delivery_date BETWEEN @FromPrev AND @ToPrev OR sd.delivery_date BETWEEN @From AND @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sd.partner_id, p.partner_name
        HAVING SupplyAmount > 0 OR VatAmount > 0
        ORDER BY SupplyAmount DESC
        """;

    private const string STATS_YOY_EMPLOYEE = """
        SELECT
            COALESCE(e.emp_name, e2.emp_name, '미지정') AS Label,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN sd.total_amount ELSE 0 END), 0) AS SupplyAmount,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN sd.total_amount ELSE 0 END), 0) AS VatAmount,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN sd.total_amount ELSE 0 END), 0)
              - COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN sd.total_amount ELSE 0 END), 0) AS TotalAmount
        FROM sales_deliveries sd
        LEFT JOIN employees e  ON e.employee_id = sd.employee_id AND e.tenant_id = sd.tenant_id
        LEFT JOIN employees e2 ON e2.user_id    = sd.created_by  AND e2.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (sd.delivery_date BETWEEN @FromPrev AND @ToPrev OR sd.delivery_date BETWEEN @From AND @To)
          AND (@Partner IS NULL OR COALESCE(e.emp_name, e2.emp_name) LIKE CONCAT('%', @Partner, '%'))
        GROUP BY COALESCE(sd.employee_id, sd.created_by), Label
        HAVING SupplyAmount > 0 OR VatAmount > 0
        ORDER BY SupplyAmount DESC
        """;

    private const string STATS_YOY_REGION = """
        SELECT
            COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정') AS Label,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN sd.total_amount ELSE 0 END), 0) AS SupplyAmount,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN sd.total_amount ELSE 0 END), 0) AS VatAmount,
            COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @From AND @To THEN sd.total_amount ELSE 0 END), 0)
              - COALESCE(SUM(CASE WHEN sd.delivery_date BETWEEN @FromPrev AND @ToPrev THEN sd.total_amount ELSE 0 END), 0) AS TotalAmount
        FROM sales_deliveries sd
        LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = sd.tenant_id
        WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
          AND (sd.delivery_date BETWEEN @FromPrev AND @ToPrev OR sd.delivery_date BETWEEN @From AND @To)
          AND (@Partner IS NULL OR p.address LIKE CONCAT('%', @Partner, '%'))
        GROUP BY Label
        HAVING SupplyAmount > 0 OR VatAmount > 0
        ORDER BY SupplyAmount DESC
        """;

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
                WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
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
                WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
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
                WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
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
                WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
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
                WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
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

    /// <inheritdoc />
    public async Task<List<StockLedgerRow>> GetStockLedgerAsync(
        string viewType, string tenantId,
        DateTime? from, DateTime? to,
        string? partner, CancellationToken ct)
    {
        // 수불부: 상품별 또는 업체별로 입출고·잔량을 집계한다.
        var sql = viewType switch
        {
            "partner" => """
                SELECT
                    p.partner_name AS Label,
                    SUM(sl.qty_in)  AS QtyIn,
                    SUM(sl.qty_out) AS QtyOut,
                    SUM(sl.qty_in) - SUM(sl.qty_out) AS Balance,
                    SUM(CASE WHEN sl.qty_in  > 0 THEN COALESCE(sl.supply_amount, 0) ELSE 0 END) AS AmountIn,
                    SUM(CASE WHEN sl.qty_out > 0 THEN COALESCE(sl.supply_amount, 0) ELSE 0 END) AS AmountOut
                FROM stock_ledger sl
                LEFT JOIN partners p ON p.partner_id = sl.partner_id AND p.tenant_id = sl.tenant_id
                WHERE sl.tenant_id = @TenantId
                  AND (@From IS NULL OR sl.ledger_date >= @From)
                  AND (@To   IS NULL OR sl.ledger_date <= @To)
                  AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
                GROUP BY sl.partner_id, p.partner_name
                ORDER BY p.partner_name
                """,
            _ => """
                SELECT
                    i.item_name AS Label,
                    SUM(sl.qty_in)  AS QtyIn,
                    SUM(sl.qty_out) AS QtyOut,
                    SUM(sl.qty_in) - SUM(sl.qty_out) AS Balance,
                    SUM(CASE WHEN sl.qty_in  > 0 THEN COALESCE(sl.supply_amount, 0) ELSE 0 END) AS AmountIn,
                    SUM(CASE WHEN sl.qty_out > 0 THEN COALESCE(sl.supply_amount, 0) ELSE 0 END) AS AmountOut
                FROM stock_ledger sl
                LEFT JOIN items i ON i.item_id = sl.item_id AND i.tenant_id = sl.tenant_id
                WHERE sl.tenant_id = @TenantId
                  AND (@From IS NULL OR sl.ledger_date >= @From)
                  AND (@To   IS NULL OR sl.ledger_date <= @To)
                  AND (@Partner IS NULL OR EXISTS (
                      SELECT 1 FROM partners pp
                      WHERE pp.partner_id = sl.partner_id
                        AND pp.tenant_id  = sl.tenant_id
                        AND pp.partner_name LIKE CONCAT('%', @Partner, '%')
                  ))
                GROUP BY sl.item_id, i.item_name
                ORDER BY i.item_name
                """
        };

        var rows = await _db.QueryAsync<StockLedgerRow>(
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
    public async Task<List<ReportRow>> GetStockStatusAsync(
        string viewType, string tenantId, CancellationToken ct)
    {
        var sql = viewType switch
        {
            // 안전재고 미달 품목만 조회한다.
            "safety" => """
                SELECT
                    i.item_name AS Label,
                    1 AS Count,
                    COALESCE(s.current_qty, 0) AS Qty,
                    COALESCE(s.current_qty * i.sale_price, 0) AS SupplyAmount,
                    0 AS VatAmount,
                    COALESCE(s.current_qty * i.sale_price, 0) AS TotalAmount
                FROM items i
                LEFT JOIN item_stock s ON s.item_id = i.item_id AND s.tenant_id = i.tenant_id
                WHERE i.tenant_id = @TenantId
                  AND i.is_active = 1
                  AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
                  AND i.safety_stock > 0
                  AND COALESCE(s.current_qty, 0) <= i.safety_stock
                ORDER BY i.item_name
                """,
            // 창고별 재고현황을 조회한다.
            "warehouse" => """
                SELECT
                    COALESCE(w.wh_name, '기본창고') AS Label,
                    COUNT(DISTINCT s.item_id) AS Count,
                    COALESCE(SUM(s.current_qty), 0) AS Qty,
                    COALESCE(SUM(s.current_qty * i.sale_price), 0) AS SupplyAmount,
                    0 AS VatAmount,
                    COALESCE(SUM(s.current_qty * i.sale_price), 0) AS TotalAmount
                FROM item_stock s
                LEFT JOIN items i ON i.item_id = s.item_id AND i.tenant_id = s.tenant_id
                LEFT JOIN warehouses w ON w.warehouse_id = s.warehouse_id AND w.tenant_id = s.tenant_id
                WHERE s.tenant_id = @TenantId
                  AND i.is_active = 1
                  AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
                GROUP BY s.warehouse_id, w.wh_name
                ORDER BY w.wh_name
                """,
            // 전체 현재고를 조회한다. (기본)
            _ => """
                SELECT
                    i.item_name AS Label,
                    1 AS Count,
                    COALESCE(s.current_qty, 0) AS Qty,
                    COALESCE(s.current_qty * i.sale_price, 0) AS SupplyAmount,
                    0 AS VatAmount,
                    COALESCE(s.current_qty * i.sale_price, 0) AS TotalAmount
                FROM items i
                LEFT JOIN item_stock s ON s.item_id = i.item_id AND s.tenant_id = i.tenant_id
                WHERE i.tenant_id = @TenantId
                  AND i.is_active = 1
                  AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
                ORDER BY i.item_name
                """
        };

        var rows = await _db.QueryAsync<ReportRow>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct));

        return rows.ToList();
    }
}
