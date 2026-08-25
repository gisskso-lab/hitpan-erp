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
        // 사장님 결재 2026-04-29: 견적현황 5종 풀스택. price=견적단가 조회.
        var sql = viewType switch
        {
            "partner" => Q_BY_PARTNER,
            "item" => Q_BY_ITEM,
            "employee" => Q_BY_EMPLOYEE,
            "price" => Q_PRICE,
            _ => Q_BY_PERIOD
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

    // ─── 견적현황 5종 SQL ───
    private const string Q_BY_PERIOD = """
        SELECT
            DATE_FORMAT(q.quote_date, '%Y-%m-%d') AS Label,
            COUNT(*) AS Count,
            0 AS Qty,
            COALESCE(SUM(q.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(q.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(q.total_amount + q.vat_amount), 0) AS TotalAmount
        FROM quotations q
        LEFT JOIN partners p ON p.partner_id = q.partner_id AND p.tenant_id = q.tenant_id
        WHERE q.tenant_id = @TenantId AND q.is_deleted = 0
          AND (@From IS NULL OR q.quote_date >= @From)
          AND (@To IS NULL OR q.quote_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY q.quote_date
        ORDER BY q.quote_date
        """;

    private const string Q_BY_PARTNER = """
        SELECT
            p.partner_name AS Label,
            COUNT(DISTINCT q.quote_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(q.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(q.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(q.total_amount + q.vat_amount), 0) AS TotalAmount
        FROM quotations q
        LEFT JOIN partners p ON p.partner_id = q.partner_id AND p.tenant_id = q.tenant_id
        WHERE q.tenant_id = @TenantId AND q.is_deleted = 0
          AND (@From IS NULL OR q.quote_date >= @From)
          AND (@To IS NULL OR q.quote_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY q.partner_id, p.partner_name
        ORDER BY SupplyAmount DESC
        """;

    private const string Q_BY_ITEM = """
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
        WHERE q.tenant_id = @TenantId AND q.is_deleted = 0
          AND (@From IS NULL OR q.quote_date >= @From)
          AND (@To IS NULL OR q.quote_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY qi.item_id, i.item_name
        ORDER BY SupplyAmount DESC
        """;

    private const string Q_BY_EMPLOYEE = """
        SELECT
            COALESCE(e.emp_name, e2.emp_name, '미지정') AS Label,
            COUNT(DISTINCT q.quote_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(q.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(q.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(q.total_amount + q.vat_amount), 0) AS TotalAmount
        FROM quotations q
        LEFT JOIN employees e  ON e.employee_id = q.employee_id AND e.tenant_id = q.tenant_id
        LEFT JOIN employees e2 ON e2.user_id    = q.created_by  AND e2.tenant_id = q.tenant_id
        WHERE q.tenant_id = @TenantId AND q.is_deleted = 0
          AND (@From IS NULL OR q.quote_date >= @From)
          AND (@To IS NULL OR q.quote_date <= @To)
          AND (@Partner IS NULL OR COALESCE(e.emp_name, e2.emp_name) LIKE CONCAT('%', @Partner, '%'))
        GROUP BY COALESCE(q.employee_id, q.created_by), Label
        ORDER BY SupplyAmount DESC
        """;

    // 견적단가 조회 — 품목별 최저/최고/평균 견적 단가
    private const string Q_PRICE = """
        SELECT
            i.item_name AS Label,
            COUNT(DISTINCT q.quote_id) AS Count,
            COALESCE(SUM(qi.qty), 0) AS Qty,
            COALESCE(MIN(qi.unit_price), 0) AS SupplyAmount,
            COALESCE(MAX(qi.unit_price), 0) AS VatAmount,
            COALESCE(AVG(qi.unit_price), 0) AS TotalAmount
        FROM quotation_items qi
        INNER JOIN quotations q ON q.quote_id = qi.quote_id
        LEFT JOIN items i ON i.item_id = qi.item_id AND i.tenant_id = q.tenant_id
        WHERE q.tenant_id = @TenantId AND q.is_deleted = 0
          AND qi.unit_price > 0
          AND (@From IS NULL OR q.quote_date >= @From)
          AND (@To IS NULL OR q.quote_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY qi.item_id, i.item_name
        ORDER BY i.item_name
        """;

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetSalesOrderReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        // 사장님 결재 2026-04-29: 수주현황 6종 풀스택.
        // backorder: ordered_qty > delivered_qty / delivery: delivery_date 기준
        var sql = viewType switch
        {
            "partner" => SO_BY_PARTNER,
            "item" => SO_BY_ITEM,
            "delivery" => SO_BY_DELIVERY,
            "backorder" => SO_BACKORDER,
            "price" => SO_PRICE,
            _ => SO_BY_PERIOD
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

    // ─── 수주현황 6종 SQL ───
    private const string SO_BY_PERIOD = """
        SELECT
            DATE_FORMAT(so.order_date, '%Y-%m-%d') AS Label,
            COUNT(*) AS Count,
            0 AS Qty,
            COALESCE(SUM(so.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(so.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(so.total_amount + so.vat_amount), 0) AS TotalAmount
        FROM sales_orders so
        LEFT JOIN partners p ON p.partner_id = so.partner_id AND p.tenant_id = so.tenant_id
        WHERE so.tenant_id = @TenantId AND so.is_deleted = 0 AND so.status <> 'cancelled'
          AND (@From IS NULL OR so.order_date >= @From)
          AND (@To IS NULL OR so.order_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY so.order_date
        ORDER BY so.order_date
        """;

    private const string SO_BY_PARTNER = """
        SELECT
            p.partner_name AS Label,
            COUNT(DISTINCT so.order_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(so.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(so.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(so.total_amount + so.vat_amount), 0) AS TotalAmount
        FROM sales_orders so
        LEFT JOIN partners p ON p.partner_id = so.partner_id AND p.tenant_id = so.tenant_id
        WHERE so.tenant_id = @TenantId AND so.is_deleted = 0 AND so.status <> 'cancelled'
          AND (@From IS NULL OR so.order_date >= @From)
          AND (@To IS NULL OR so.order_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY so.partner_id, p.partner_name
        ORDER BY SupplyAmount DESC
        """;

    private const string SO_BY_ITEM = """
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
        WHERE so.tenant_id = @TenantId AND so.is_deleted = 0 AND so.status <> 'cancelled'
          AND (@From IS NULL OR so.order_date >= @From)
          AND (@To IS NULL OR so.order_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY soi.item_id, i.item_name
        ORDER BY SupplyAmount DESC
        """;

    // 납기별 — sales_orders.delivery_date 기준
    private const string SO_BY_DELIVERY = """
        SELECT
            COALESCE(DATE_FORMAT(so.delivery_date, '%Y-%m-%d'), '미지정') AS Label,
            COUNT(DISTINCT so.order_id) AS Count,
            COALESCE(SUM(soi.ordered_qty - soi.delivered_qty), 0) AS Qty,
            COALESCE(SUM(soi.supply_amount), 0) AS SupplyAmount,
            COALESCE(SUM(soi.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(soi.supply_amount + soi.vat_amount), 0) AS TotalAmount
        FROM sales_orders so
        LEFT JOIN sales_order_items soi ON soi.order_id = so.order_id AND soi.tenant_id = so.tenant_id
        LEFT JOIN partners p ON p.partner_id = so.partner_id AND p.tenant_id = so.tenant_id
        WHERE so.tenant_id = @TenantId AND so.is_deleted = 0 AND so.status <> 'cancelled'
          AND (@From IS NULL OR so.delivery_date IS NULL OR so.delivery_date >= @From)
          AND (@To IS NULL OR so.delivery_date IS NULL OR so.delivery_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY so.delivery_date
        ORDER BY so.delivery_date
        """;

    // 미납품 현황 — ordered_qty > delivered_qty 인 품목/업체
    private const string SO_BACKORDER = """
        SELECT
            CONCAT(i.item_name, ' / ', p.partner_name) AS Label,
            COUNT(DISTINCT so.order_id) AS Count,
            COALESCE(SUM(soi.ordered_qty - soi.delivered_qty), 0) AS Qty,
            COALESCE(SUM((soi.ordered_qty - soi.delivered_qty) * soi.unit_price), 0) AS SupplyAmount,
            COALESCE(SUM(soi.ordered_qty), 0) AS VatAmount,
            COALESCE(SUM(soi.delivered_qty), 0) AS TotalAmount
        FROM sales_order_items soi
        INNER JOIN sales_orders so ON so.order_id = soi.order_id AND so.tenant_id = soi.tenant_id
        LEFT JOIN items i ON i.item_id = soi.item_id AND i.tenant_id = soi.tenant_id
        LEFT JOIN partners p ON p.partner_id = so.partner_id AND p.tenant_id = so.tenant_id
        WHERE so.tenant_id = @TenantId AND so.is_deleted = 0 AND so.status <> 'cancelled'
          AND soi.ordered_qty > soi.delivered_qty
          AND (@From IS NULL OR so.order_date >= @From)
          AND (@To IS NULL OR so.order_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%')
               OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY soi.item_id, i.item_name, so.partner_id, p.partner_name
        ORDER BY Qty DESC
        """;

    // 수주단가 변동현황
    private const string SO_PRICE = """
        SELECT
            i.item_name AS Label,
            COUNT(DISTINCT so.order_id) AS Count,
            COALESCE(SUM(soi.ordered_qty), 0) AS Qty,
            COALESCE(MIN(soi.unit_price), 0) AS SupplyAmount,
            COALESCE(MAX(soi.unit_price), 0) AS VatAmount,
            COALESCE(AVG(soi.unit_price), 0) AS TotalAmount
        FROM sales_order_items soi
        INNER JOIN sales_orders so ON so.order_id = soi.order_id AND so.tenant_id = soi.tenant_id
        LEFT JOIN items i ON i.item_id = soi.item_id AND i.tenant_id = soi.tenant_id
        WHERE so.tenant_id = @TenantId AND so.is_deleted = 0 AND so.status <> 'cancelled'
          AND soi.unit_price > 0
          AND (@From IS NULL OR so.order_date >= @From)
          AND (@To IS NULL OR so.order_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY soi.item_id, i.item_name
        ORDER BY i.item_name
        """;

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
    // P2 속도 봉합(2026-06-20): 종전엔 partner 행마다 4개 상관 서브쿼리(quotations·sales_deliveries
    // 각 2회)를 재실행해 거래처 500곳이면 2,000회 스캔이 났다. 거래처별로 1회만 사전집계한 두
    // 파생테이블을 LEFT JOIN 해 스캔을 집계 2회로 줄인다. 결과 컬럼·필터·HAVING 의미는 동일.
    private const string SALES_QUOTE_VS_SALES = """
        SELECT
            p.partner_name AS Label,
            COALESCE(qa.cnt, 0)       AS Count,
            COALESCE(sa.cnt, 0)       AS Qty,
            COALESCE(qa.amt, 0)       AS SupplyAmount,
            COALESCE(sa.amt, 0)       AS VatAmount,
            0 AS TotalAmount
        FROM partners p
        LEFT JOIN (
            SELECT q.tenant_id, q.partner_id,
                   COUNT(*) AS cnt, SUM(q.total_amount) AS amt
            FROM quotations q
            WHERE q.tenant_id = @TenantId AND q.is_deleted = 0
              AND (@From IS NULL OR q.quote_date >= @From)
              AND (@To   IS NULL OR q.quote_date <= @To)
            GROUP BY q.tenant_id, q.partner_id
        ) qa ON qa.tenant_id = p.tenant_id AND qa.partner_id = p.partner_id
        LEFT JOIN (
            SELECT sd2.tenant_id, sd2.partner_id,
                   COUNT(*) AS cnt, SUM(sd2.total_amount) AS amt
            FROM sales_deliveries sd2
            WHERE sd2.tenant_id = @TenantId AND sd2.is_deleted = 0 AND sd2.status <> 'cancelled'
              AND (@From IS NULL OR sd2.delivery_date >= @From)
              AND (@To   IS NULL OR sd2.delivery_date <= @To)
            GROUP BY sd2.tenant_id, sd2.partner_id
        ) sa ON sa.tenant_id = p.tenant_id AND sa.partner_id = p.partner_id
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
    // 사장님 결재 2026-08-25: 확정분(confirmed)만 집계. 종전에는 status 조건이 아예 없어
    // 미확정(draft)·취소분까지 매출반품에 합산됐다. 재고·회계가 confirmed 에만 반응하므로
    // 현황 숫자도 같은 잣대로 맞춘다(헌법 #6). 철자(canceled/cancelled) 혼재를 피하려
    // 부정(<>) 이 아닌 양성(=) 비교를 쓴다 — 철자에 의존하지 않는다.
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
        WHERE sr.tenant_id = @TenantId AND sr.is_deleted = 0 AND sr.status = 'confirmed'
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
            WHERE sr.tenant_id = @TenantId AND sr.is_deleted = 0 AND sr.status = 'confirmed'
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
        // 사장님 결재 2026-04-29: 발주현황 7종 풀스택. 사장님 §7번 요청 처리.
        // - forecast(발주예상량): 안전재고 미달 + 미발주분 산출
        // - backorder(미입고): ordered_qty - received_qty > 0
        // - delivery(납기별): expected_date 기준 그룹
        // - price(발주단가): 품목별 최저/최고/평균 단가
        var sql = viewType switch
        {
            "partner" => PO_BY_PARTNER,
            "item" => PO_BY_ITEM,
            "forecast" => PO_FORECAST,
            "delivery" => PO_BY_DELIVERY,
            "backorder" => PO_BACKORDER,
            "price" => PO_PRICE,
            _ => PO_BY_PERIOD
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

    // ─── 발주현황 7종 SQL 상수 (사장님 결재 2026-04-29) ───
    private const string PO_BY_PERIOD = """
        SELECT
            DATE_FORMAT(po.po_date, '%Y-%m-%d') AS Label,
            COUNT(*) AS Count,
            0 AS Qty,
            COALESCE(SUM(po.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(po.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(po.total_amount + po.vat_amount), 0) AS TotalAmount
        FROM purchase_orders po
        LEFT JOIN partners p ON p.partner_id = po.partner_id AND p.tenant_id = po.tenant_id
        WHERE po.tenant_id = @TenantId AND po.is_deleted = 0 AND po.status <> 'cancelled'
          AND (@From IS NULL OR po.po_date >= @From)
          AND (@To IS NULL OR po.po_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY po.po_date
        ORDER BY po.po_date
        """;

    private const string PO_BY_PARTNER = """
        SELECT
            p.partner_name AS Label,
            COUNT(DISTINCT po.po_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(po.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(po.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(po.total_amount + po.vat_amount), 0) AS TotalAmount
        FROM purchase_orders po
        LEFT JOIN partners p ON p.partner_id = po.partner_id AND p.tenant_id = po.tenant_id
        WHERE po.tenant_id = @TenantId AND po.is_deleted = 0 AND po.status <> 'cancelled'
          AND (@From IS NULL OR po.po_date >= @From)
          AND (@To IS NULL OR po.po_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY po.partner_id, p.partner_name
        ORDER BY SupplyAmount DESC
        """;

    private const string PO_BY_ITEM = """
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
        WHERE po.tenant_id = @TenantId AND po.is_deleted = 0 AND po.status <> 'cancelled'
          AND (@From IS NULL OR po.po_date >= @From)
          AND (@To IS NULL OR po.po_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY poi.item_id, i.item_name
        ORDER BY SupplyAmount DESC
        """;

    // 발주예상량 산출 — 안전재고(items.safe_stock) 미달분 - 진행중 발주잔량.
    // Qty: 부족수량 / SupplyAmount: 안전재고 / VatAmount: 현재고 / TotalAmount: 진행중 미입고분.
    private const string PO_FORECAST = """
        SELECT
            i.item_name AS Label,
            0 AS Count,
            GREATEST(
                COALESCE(i.safe_stock, 0)
                  - COALESCE((SELECT SUM(s.current_qty) FROM item_stock s
                              WHERE s.tenant_id = i.tenant_id AND s.item_id = i.item_id), 0)
                  - COALESCE((SELECT SUM(poi2.ordered_qty - poi2.received_qty)
                              FROM purchase_order_items poi2
                              INNER JOIN purchase_orders po2
                                ON po2.po_id = poi2.po_id AND po2.tenant_id = poi2.tenant_id
                              WHERE po2.tenant_id = i.tenant_id AND poi2.item_id = i.item_id
                                AND po2.status IN ('ordered', 'partial')
                                AND po2.is_deleted = 0), 0),
                0
            ) AS Qty,
            COALESCE(i.safe_stock, 0) AS SupplyAmount,
            COALESCE((SELECT SUM(s.current_qty) FROM item_stock s
                      WHERE s.tenant_id = i.tenant_id AND s.item_id = i.item_id), 0) AS VatAmount,
            COALESCE((SELECT SUM(poi2.ordered_qty - poi2.received_qty)
                      FROM purchase_order_items poi2
                      INNER JOIN purchase_orders po2
                        ON po2.po_id = poi2.po_id AND po2.tenant_id = poi2.tenant_id
                      WHERE po2.tenant_id = i.tenant_id AND poi2.item_id = i.item_id
                        AND po2.status IN ('ordered', 'partial')
                        AND po2.is_deleted = 0), 0) AS TotalAmount
        FROM items i
        WHERE i.tenant_id = @TenantId AND i.is_deleted = 0 AND i.is_active = 1
          AND COALESCE(i.safe_stock, 0) > 0
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        HAVING Qty > 0
        ORDER BY Qty DESC
        """;

    // 납기별 발주현황 — expected_date 기준. NULL이면 '미지정'.
    private const string PO_BY_DELIVERY = """
        SELECT
            COALESCE(DATE_FORMAT(po.expected_date, '%Y-%m-%d'), '미지정') AS Label,
            COUNT(DISTINCT po.po_id) AS Count,
            COALESCE(SUM(poi.ordered_qty - poi.received_qty), 0) AS Qty,
            COALESCE(SUM(poi.supply_amount), 0) AS SupplyAmount,
            COALESCE(SUM(poi.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(poi.supply_amount + poi.vat_amount), 0) AS TotalAmount
        FROM purchase_orders po
        INNER JOIN purchase_order_items poi ON poi.po_id = po.po_id AND poi.tenant_id = po.tenant_id
        LEFT JOIN partners p ON p.partner_id = po.partner_id AND p.tenant_id = po.tenant_id
        WHERE po.tenant_id = @TenantId AND po.is_deleted = 0 AND po.status <> 'cancelled'
          AND (@From IS NULL OR po.expected_date IS NULL OR po.expected_date >= @From)
          AND (@To IS NULL OR po.expected_date IS NULL OR po.expected_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY po.expected_date
        ORDER BY po.expected_date
        """;

    // 미입고 현황 — ordered_qty > received_qty 인 품목.
    // Qty: 미입고수량 = ordered - received, SupplyAmount: 미입고금액.
    private const string PO_BACKORDER = """
        SELECT
            CONCAT(i.item_name, ' / ', p.partner_name) AS Label,
            COUNT(DISTINCT po.po_id) AS Count,
            COALESCE(SUM(poi.ordered_qty - poi.received_qty), 0) AS Qty,
            COALESCE(SUM((poi.ordered_qty - poi.received_qty) * poi.unit_price), 0) AS SupplyAmount,
            COALESCE(SUM(poi.ordered_qty), 0) AS VatAmount,
            COALESCE(SUM(poi.received_qty), 0) AS TotalAmount
        FROM purchase_order_items poi
        INNER JOIN purchase_orders po ON po.po_id = poi.po_id AND po.tenant_id = poi.tenant_id
        LEFT JOIN items i ON i.item_id = poi.item_id AND i.tenant_id = poi.tenant_id
        LEFT JOIN partners p ON p.partner_id = po.partner_id AND p.tenant_id = po.tenant_id
        WHERE po.tenant_id = @TenantId AND po.is_deleted = 0 AND po.status <> 'cancelled'
          AND poi.ordered_qty > poi.received_qty
          AND (@From IS NULL OR po.po_date >= @From)
          AND (@To IS NULL OR po.po_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%')
               OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY poi.item_id, i.item_name, po.partner_id, p.partner_name
        ORDER BY Qty DESC
        """;

    // 발주단가 현황 — 품목별 최저/평균/최고 발주단가 + 누적 수량.
    // SupplyAmount: 최저단가, VatAmount: 최고단가, TotalAmount: 평균단가.
    private const string PO_PRICE = """
        SELECT
            i.item_name AS Label,
            COUNT(DISTINCT po.po_id) AS Count,
            COALESCE(SUM(poi.ordered_qty), 0) AS Qty,
            COALESCE(MIN(poi.unit_price), 0) AS SupplyAmount,
            COALESCE(MAX(poi.unit_price), 0) AS VatAmount,
            COALESCE(AVG(poi.unit_price), 0) AS TotalAmount
        FROM purchase_order_items poi
        INNER JOIN purchase_orders po ON po.po_id = poi.po_id AND po.tenant_id = poi.tenant_id
        LEFT JOIN items i ON i.item_id = poi.item_id AND i.tenant_id = poi.tenant_id
        WHERE po.tenant_id = @TenantId AND po.is_deleted = 0 AND po.status <> 'cancelled'
          AND poi.unit_price > 0
          AND (@From IS NULL OR po.po_date >= @From)
          AND (@To IS NULL OR po.po_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY poi.item_id, i.item_name
        ORDER BY i.item_name
        """;

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetPurchaseReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        // 사장님 결재 2026-04-29: 매입현황 7종 풀스택 (판매현황 패턴 대칭).
        // purchase_receipts 가 워크플로우 종착점(§#20). 매입원장은 receipt 시점에 INSERT.
        var sql = viewType switch
        {
            "partner" => PR_BY_PARTNER,
            "item" => PR_BY_ITEM,
            "monthly" => PR_MONTHLY,
            "partner-yearly" => PR_PARTNER_YEARLY,
            "price" => PR_PRICE,
            "employee" => PR_BY_EMPLOYEE,
            _ => PR_BY_PERIOD
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

    // ─── 매입현황 7종 SQL 상수 ───
    // ─── 매입현황 SQL — 20260825작17: 반품 반영 ───
    //
    // 🔴 사장님 지시 (2026-08-25)
    //   "반품은 조회전용이 아니라 워크플로우의 한 축이지. 따라서 매입목록이나,
    //    매입관련 현황·분석·통계자료·순위표 에도 반영되야 하고, 회계·재고에도 자동반영되야 함."
    //   "워크플로우 흐름에 따라 데이터 재고·금액의 정합성은 무조건 맞아야됨."
    //
    // 🔴 왜 유형을 더하지 않고 기존 SQL 자체를 바꾸나 (사장님 판단)
    //   사장님 원문: "불량을 매입했다고 쳐도, 불량을 매출로 잡지 않고, 매입 수량에 딱 떨어지게
    //   매출을 잡고, 창고에 재고를 0으로 맞추고 판매를 하는게 아니잖아."
    //
    //   ⇒ 불량 100개를 매입하고 반품하면 **그 100개는 처음부터 안 산 것**이다.
    //     창고에도 없고 팔 수도 없다. "매입 100 - 반품 100 = 0" 을 사람이 계산해 보는 게 아니라,
    //     **실제로 산 건 0** 이다. 그러니 「반품포함」 유형을 따로 만들어 고르게 하는 것은
    //     *"불량도 매입에 넣고 볼까요, 뺄까요"* 를 묻는 셈인데 답은 하나뿐이다.
    //
    //   ⚠️ 나는 처음에 판매쪽이 「판매종합현황(반품포함)」을 별도 유형으로 뒀다는 이유로
    //     매입도 유형 추가로 가려 했다. 사장님이 바로잡으셨다 —
    //     **"매입을 매출에 맞춰서 하지 않잖아."** 매입은 매입 논리로 판단한다.
    //
    // ⚠️ 세무 총액은 이 화면 책임이 아니다 — 부가세 신고는 FinanceService 가 따로 집계한다.
    //   두 자리는 소스가 별개다(이 화면은 조회 전용).
    //
    // ⚠️ status = 'confirmed' 인 반품만 뺀다 — 확정 안 한 반품은 아직 일어나지 않은 일이다(헌법 #6).
    //   양성(=) 비교라 'canceled'/'cancelled' 철자 혼재에도 안전하다.
    //   is_deleted = 0 도 함께 본다 — 지운 반품이 매입액을 깎으면 안 된다.
    private const string PR_BY_PERIOD = """
        SELECT Label,
               SUM(Count)        AS Count,
               0                 AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
            SELECT
                DATE_FORMAT(pr.receipt_date, '%Y-%m-%d') AS Label,
                COUNT(*) AS Count,
                COALESCE(SUM(pr.total_amount), 0) AS SupplyAmount,
                COALESCE(SUM(pr.vat_amount), 0) AS VatAmount,
                COALESCE(SUM(pr.total_amount + pr.vat_amount), 0) AS TotalAmount
            FROM purchase_receipts pr
            LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (@From IS NULL OR pr.receipt_date >= @From)
              AND (@To IS NULL OR pr.receipt_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pr.receipt_date

            UNION ALL

            SELECT
                DATE_FORMAT(rt.return_date, '%Y-%m-%d') AS Label,
                -COUNT(*) AS Count,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_returns rt
            LEFT JOIN purchase_return_items rti ON rti.return_id = rt.return_id AND rti.tenant_id = rt.tenant_id
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rt.return_date
        ) t
        GROUP BY Label
        ORDER BY Label
        """;

    private const string PR_BY_PARTNER = """
        SELECT Label,
               SUM(Count)        AS Count,
               0                 AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
            SELECT
                p.partner_name AS Label,
                COUNT(DISTINCT pr.receipt_id) AS Count,
                COALESCE(SUM(pr.total_amount), 0) AS SupplyAmount,
                COALESCE(SUM(pr.vat_amount), 0) AS VatAmount,
                COALESCE(SUM(pr.total_amount + pr.vat_amount), 0) AS TotalAmount
            FROM purchase_receipts pr
            LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (@From IS NULL OR pr.receipt_date >= @From)
              AND (@To IS NULL OR pr.receipt_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pr.partner_id, p.partner_name

            UNION ALL

            SELECT
                p.partner_name AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_returns rt
            LEFT JOIN purchase_return_items rti ON rti.return_id = rt.return_id AND rti.tenant_id = rt.tenant_id
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rt.partner_id, p.partner_name
        ) t
        GROUP BY Label
        -- 🔴 20260825작17 — 전량 반품된 건도 보여야 한다.
        --   순액도 0, 건수도 0(매입 1 + 반품 -1)이 되어 조용히 사라졌다.
        --   사장님 헌법: '목록에서 숨기면 사라지는 게 아니라 안 보이는 채로 쌓인다.'
        --   ⇒ 거래가 한 번이라도 있었으면 뜬다(절대값 합산). 0원으로 보이는 게 사실이다.
        HAVING SUM(ABS(Count)) <> 0 OR SUM(ABS(TotalAmount)) <> 0
        ORDER BY SupplyAmount DESC
        """;

    private const string PR_BY_ITEM = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
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
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (@From IS NULL OR pr.receipt_date >= @From)
              AND (@To IS NULL OR pr.receipt_date <= @To)
              AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pri.item_id, i.item_name

            UNION ALL

            SELECT
                i.item_name AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.qty), 0) AS Qty,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN items i ON i.item_id = rti.item_id AND i.tenant_id = rti.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rti.item_id, i.item_name
        ) t
        GROUP BY Label
        -- 🔴 20260825작17 — 전량 반품된 건도 보여야 한다.
        --   순액도 0, 건수도 0(매입 1 + 반품 -1)이 되어 조용히 사라졌다.
        --   사장님 헌법: '목록에서 숨기면 사라지는 게 아니라 안 보이는 채로 쌓인다.'
        --   ⇒ 거래가 한 번이라도 있었으면 뜬다(절대값 합산). 0원으로 보이는 게 사실이다.
        HAVING SUM(ABS(Count)) <> 0 OR SUM(ABS(Qty)) <> 0
        ORDER BY SupplyAmount DESC
        """;

    private const string PR_MONTHLY = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
            SELECT
                DATE_FORMAT(pr.receipt_date, '%Y-%m') AS Label,
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
            GROUP BY DATE_FORMAT(pr.receipt_date, '%Y-%m')

            UNION ALL

            SELECT
                DATE_FORMAT(rt.return_date, '%Y-%m') AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.qty), 0) AS Qty,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY DATE_FORMAT(rt.return_date, '%Y-%m')
        ) t
        GROUP BY Label
        ORDER BY Label
        """;

    private const string PR_PARTNER_YEARLY = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
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

            UNION ALL

            SELECT
                CONCAT(p.partner_name, ' (', DATE_FORMAT(rt.return_date, '%Y-%m'), ')') AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.qty), 0) AS Qty,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rt.partner_id, p.partner_name, DATE_FORMAT(rt.return_date, '%Y-%m')
        ) t
        GROUP BY Label
        ORDER BY Label
        """;

    // 매입단가 변동현황 — 같은 품목의 unit_price 추이
    private const string PR_PRICE = """
        SELECT
            CONCAT(i.item_name, ' (', DATE_FORMAT(pr.receipt_date, '%Y-%m'), ')') AS Label,
            COUNT(DISTINCT pr.receipt_id) AS Count,
            COALESCE(SUM(pri.qty), 0) AS Qty,
            COALESCE(MIN(pri.unit_price), 0) AS SupplyAmount,
            COALESCE(MAX(pri.unit_price), 0) AS VatAmount,
            COALESCE(AVG(pri.unit_price), 0) AS TotalAmount
        FROM purchase_receipt_items pri
        INNER JOIN purchase_receipts pr ON pr.receipt_id = pri.receipt_id AND pr.tenant_id = pri.tenant_id
        LEFT JOIN items i ON i.item_id = pri.item_id AND i.tenant_id = pri.tenant_id
        WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
          AND pri.unit_price > 0
          AND (@From IS NULL OR pr.receipt_date >= @From)
          AND (@To IS NULL OR pr.receipt_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY pri.item_id, i.item_name, DATE_FORMAT(pr.receipt_date, '%Y-%m')
        ORDER BY i.item_name, DATE_FORMAT(pr.receipt_date, '%Y-%m')
        """;

    // 사원별 매입현황 — purchase_receipts 에는 employee_id 가 없으므로 created_by 로 매핑.
    private const string PR_BY_EMPLOYEE = """
        SELECT
            COALESCE(e.emp_name, '미지정') AS Label,
            COUNT(DISTINCT pr.receipt_id) AS Count,
            COALESCE(SUM(pri.qty), 0) AS Qty,
            COALESCE(SUM(pri.supply_amount), 0) AS SupplyAmount,
            COALESCE(SUM(pri.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(pri.supply_amount + pri.vat_amount), 0) AS TotalAmount
        FROM purchase_receipts pr
        LEFT JOIN purchase_receipt_items pri ON pri.receipt_id = pr.receipt_id AND pri.tenant_id = pr.tenant_id
        LEFT JOIN employees e ON e.user_id = pr.created_by AND e.tenant_id = pr.tenant_id
        WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
          AND (@From IS NULL OR pr.receipt_date >= @From)
          AND (@To IS NULL OR pr.receipt_date <= @To)
          AND (@Partner IS NULL OR e.emp_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY pr.created_by, e.emp_name
        ORDER BY SupplyAmount DESC
        """;

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetReturnReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        // 사장님 결재 2026-04-29: 매입반품현황 4종. employee 추가 (created_by 기반).
        // 사장님 결재 2026-08-25: 확정분(confirmed)만 집계. 종전 필터는 status <> 'cancelled'(L둘)
        // 이었는데 실제 기록값은 'canceled'(L하나)라 항상 참이 되어 취소분이 그대로 남았다.
        // 양성(=) 비교로 바꿔 철자 혼재와 draft 포함 문제를 함께 해소한다.
        var sql = viewType switch
        {
            "partner" => RT_BY_PARTNER,
            "item" => RT_BY_ITEM,
            "employee" => RT_BY_EMPLOYEE,
            _ => RT_BY_PERIOD
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

    // ─── 매입반품현황 4종 SQL 상수 ───
    private const string RT_BY_PERIOD = """
        SELECT
            DATE_FORMAT(rt.return_date, '%Y-%m-%d') AS Label,
            COUNT(*) AS Count,
            0 AS Qty,
            COALESCE(SUM(rt.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(rt.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(rt.total_amount + rt.vat_amount), 0) AS TotalAmount
        FROM purchase_returns rt
        LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
        WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
          AND (@From IS NULL OR rt.return_date >= @From)
          AND (@To IS NULL OR rt.return_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY rt.return_date
        ORDER BY rt.return_date DESC
        """;

    private const string RT_BY_PARTNER = """
        SELECT
            p.partner_name AS Label,
            COUNT(DISTINCT rt.return_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(rt.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(rt.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(rt.total_amount + rt.vat_amount), 0) AS TotalAmount
        FROM purchase_returns rt
        LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
        WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
          AND (@From IS NULL OR rt.return_date >= @From)
          AND (@To IS NULL OR rt.return_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY rt.partner_id, p.partner_name
        ORDER BY SupplyAmount DESC
        """;

    private const string RT_BY_ITEM = """
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
        WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
          AND (@From IS NULL OR rt.return_date >= @From)
          AND (@To IS NULL OR rt.return_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY rti.item_id, i.item_name
        ORDER BY SupplyAmount DESC
        """;

    // 사원별 — 반품 전표를 실제로 작성한 사람(purchase_returns.created_by)으로 집계한다.
    //
    // 🔴 20260825작18 봉합: 종전 주석은 "purchase_returns 에 created_by 컬럼 없음" 이라 적고
    //   receipt_id → purchase_receipts.created_by 로 우회 조인했다. 그 주석이 **거짓**이었다 —
    //   DESCRIBE 실측 결과 purchase_returns.created_by 는 실재하고, 같은 레포의
    //   PurchaseService.GetReturnsAsync 는 이미 r.created_by 로 조인하고 있었다.
    //   우회 조인 때문에 receipt_id 가 NULL 인 반품(= 매입명세서 없이 직접 작성한 건)은
    //   작성자를 못 찾아 전부 '미지정' 으로 뭉쳤다. 게다가 매입을 친 사람과 반품을 친 사람이
    //   다르면 엉뚱한 사람 실적이 됐다. 틀린 주석 위에 코드가 서 있던 자리다.
    private const string RT_BY_EMPLOYEE = """
        SELECT
            COALESCE(e.emp_name, '미지정') AS Label,
            COUNT(DISTINCT rt.return_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(rt.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(rt.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(rt.total_amount + rt.vat_amount), 0) AS TotalAmount
        FROM purchase_returns rt
        LEFT JOIN employees e ON e.user_id = rt.created_by AND e.tenant_id = rt.tenant_id
        WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
          AND (@From IS NULL OR rt.return_date >= @From)
          AND (@To IS NULL OR rt.return_date <= @To)
          AND (@Partner IS NULL OR e.emp_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY rt.created_by, e.emp_name
        ORDER BY SupplyAmount DESC
        """;



    /// <inheritdoc />
    public async Task<List<ReportRow>> GetSalesReturnReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        // 사장님 결재 2026-08-25 (20260825작6): 매출반품현황 4종 신설.
        //   실측 반려: "반품현황메뉴 생성안됨" — 종전 /return-status 는 매입반품만 집계했다.
        //   매입반품(GetReturnReportAsync)의 거울이지만 분기가 아니라 별도 메서드다.
        //   방향이 반대인 별개 업무라, 한쪽을 고치다 다른 쪽이 흔들리면 안 된다.
        var sql = viewType switch
        {
            "partner" => SR_BY_PARTNER,
            "item" => SR_BY_ITEM,
            "employee" => SR_BY_EMPLOYEE,
            _ => SR_BY_PERIOD
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
    // ─── 매출반품현황 4종 SQL (20260825작6) ───
    //   사장님 정의(2026-08-25): "매출에 있는 반품 = 사용자의 고객사가 반품처리한 품목관리".
    //   매입반품(RT_*)의 거울이다 — 방향만 반대고 집계 축은 같다.
    //   ⚠️ 공통 규칙 세 가지를 앞선 결재에서 계승한다:
    //     ① status = 'confirmed' 만 (20260825작3) — 재고·회계가 confirmed 에만 반응하므로
    //        현황 숫자도 같은 잣대여야 한다(헌법 #6). 철자 혼재(canceled/cancelled)와도 무관해진다.
    //     ② 작성자는 ec.user_id = sr.created_by (20260825작5) — created_by 는 user_id 체계다.
    //     ③ 금액은 헤더(sr.total_amount) 기준. 품목별만 라인(sri) 을 편다.
    private const string SR_BY_PERIOD = """
        SELECT
            DATE_FORMAT(sr.return_date, '%Y-%m-%d') AS Label,
            COUNT(DISTINCT sr.return_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(sr.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sr.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sr.total_amount + sr.vat_amount), 0) AS TotalAmount
        FROM sales_returns sr
        LEFT JOIN partners p ON p.partner_id = sr.partner_id AND p.tenant_id = sr.tenant_id
        WHERE sr.tenant_id = @TenantId AND sr.is_deleted = 0 AND sr.status = 'confirmed'
          AND (@From IS NULL OR sr.return_date >= @From)
          AND (@To IS NULL OR sr.return_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sr.return_date
        ORDER BY sr.return_date DESC
        """;

    private const string SR_BY_PARTNER = """
        SELECT
            COALESCE(p.partner_name, '미지정') AS Label,
            COUNT(DISTINCT sr.return_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(sr.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sr.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sr.total_amount + sr.vat_amount), 0) AS TotalAmount
        FROM sales_returns sr
        LEFT JOIN partners p ON p.partner_id = sr.partner_id AND p.tenant_id = sr.tenant_id
        WHERE sr.tenant_id = @TenantId AND sr.is_deleted = 0 AND sr.status = 'confirmed'
          AND (@From IS NULL OR sr.return_date >= @From)
          AND (@To IS NULL OR sr.return_date <= @To)
          AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sr.partner_id, p.partner_name
        ORDER BY SupplyAmount DESC
        """;

    // 품목별만 라인(sales_return_items)을 편다 — 수량이 품목 단위라서다.
    private const string SR_BY_ITEM = """
        SELECT
            COALESCE(i.item_name, '미지정') AS Label,
            COUNT(DISTINCT sr.return_id) AS Count,
            COALESCE(SUM(sri.qty), 0) AS Qty,
            COALESCE(SUM(sri.supply_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sri.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sri.supply_amount + sri.vat_amount), 0) AS TotalAmount
        FROM sales_returns sr
        INNER JOIN sales_return_items sri ON sri.return_id = sr.return_id AND sri.tenant_id = sr.tenant_id
        LEFT JOIN items i ON i.item_id = sri.item_id AND i.tenant_id = sr.tenant_id
        WHERE sr.tenant_id = @TenantId AND sr.is_deleted = 0 AND sr.status = 'confirmed'
          AND (@From IS NULL OR sr.return_date >= @From)
          AND (@To IS NULL OR sr.return_date <= @To)
          AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sri.item_id, i.item_name
        ORDER BY SupplyAmount DESC
        """;

    // 사원별 — 매출반품은 자기 테이블에 created_by 가 있다(20260825작5 에서 채움).
    // 매입반품(RT_BY_EMPLOYEE)이 부모 입고전표를 경유하는 것과 다르다.
    private const string SR_BY_EMPLOYEE = """
        SELECT
            COALESCE(e.emp_name, '미지정') AS Label,
            COUNT(DISTINCT sr.return_id) AS Count,
            0 AS Qty,
            COALESCE(SUM(sr.total_amount), 0) AS SupplyAmount,
            COALESCE(SUM(sr.vat_amount), 0) AS VatAmount,
            COALESCE(SUM(sr.total_amount + sr.vat_amount), 0) AS TotalAmount
        FROM sales_returns sr
        LEFT JOIN employees e ON e.user_id = sr.created_by AND e.tenant_id = sr.tenant_id
        WHERE sr.tenant_id = @TenantId AND sr.is_deleted = 0 AND sr.status = 'confirmed'
          AND (@From IS NULL OR sr.return_date >= @From)
          AND (@To IS NULL OR sr.return_date <= @To)
          AND (@Partner IS NULL OR e.emp_name LIKE CONCAT('%', @Partner, '%'))
        GROUP BY sr.created_by, e.emp_name
        ORDER BY SupplyAmount DESC
        """;
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
        // 사장님 결재 2026-04-29: 매입순위표 5종 (판매 패턴 대칭).
        var sql = viewType switch
        {
            "partner" => PRR_BY_PARTNER,
            "item" => PRR_BY_ITEM,
            "period" => PRR_BY_PERIOD,
            "region" => PRR_BY_REGION,
            "employee" => PRR_BY_EMPLOYEE,
            _ => PRR_BY_PARTNER
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

    // ─── 매입순위표 5종 SQL 상수 ───
    // 🔴 20260825작17 — 반품을 UNION ALL 로 음수 합산한다.
    //   사장님: "불량100개가 들어왓다고 해도, 100개를 반품하고 재주문 하겠지."
    //   반품을 안 빼면 순위표가 200개 산 것처럼 보인다. 매입 1위 업체가 실은 반품 1위일 수 있다.
    //   ⇒ 순위(ORDER BY)를 반품 뺀 금액으로 매겨야 "누구한테 제일 많이 샀나"가 맞는다.
    // ⚠️ 반품 날짜축은 rt.return_date 다. 매입은 pr.receipt_date — 축을 섞으면 기간 필터가 거짓이 된다.
    // ⚠️ ORDER BY 는 서브쿼리 안이 아니라 바깥에 둔다. 안에 두면 MySQL 이 무시한다.
    // ⚠️ 확정 반품만(rt.status = 'confirmed'). 헌법 #6 — draft 반품은 아직 원장에 없다.
    // ⚠️ UNION 양쪽은 컬럼 개수·순서·별칭이 정확히 같아야 한다. MySQL 은 이름이 아니라 위치로 맞춘다.
    private const string PRR_BY_PARTNER = """
        SELECT Label,
               SUM(Count)        AS Count,
               0                 AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
            SELECT
                p.partner_name AS Label,
                COUNT(DISTINCT pr.receipt_id) AS Count,
                COALESCE(SUM(pr.total_amount), 0) AS SupplyAmount,
                COALESCE(SUM(pr.vat_amount), 0) AS VatAmount,
                COALESCE(SUM(pr.total_amount + pr.vat_amount), 0) AS TotalAmount
            FROM purchase_receipts pr
            LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (@From IS NULL OR pr.receipt_date >= @From)
              AND (@To IS NULL OR pr.receipt_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pr.partner_id, p.partner_name

            UNION ALL

            SELECT
                p.partner_name AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rt.partner_id, p.partner_name
        ) t
        GROUP BY Label
        ORDER BY SupplyAmount DESC
        """;

    private const string PRR_BY_ITEM = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
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
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (@From IS NULL OR pr.receipt_date >= @From)
              AND (@To IS NULL OR pr.receipt_date <= @To)
              AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pri.item_id, i.item_name

            UNION ALL

            SELECT
                i.item_name AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.qty), 0) AS Qty,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN items i ON i.item_id = rti.item_id AND i.tenant_id = rti.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rti.item_id, i.item_name
        ) t
        GROUP BY Label
        ORDER BY SupplyAmount DESC
        """;

    // ⚠️ 기간별 Label 은 '%Y-%m' 이다. 매입은 receipt_date, 반품은 return_date 를
    //   각각 같은 월 문자열로 만들어야 같은 달 칸에서 상계된다.
    private const string PRR_BY_PERIOD = """
        SELECT Label,
               SUM(Count)        AS Count,
               0                 AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
            SELECT
                DATE_FORMAT(pr.receipt_date, '%Y-%m') AS Label,
                COUNT(DISTINCT pr.receipt_id) AS Count,
                COALESCE(SUM(pr.total_amount), 0) AS SupplyAmount,
                COALESCE(SUM(pr.vat_amount), 0) AS VatAmount,
                COALESCE(SUM(pr.total_amount + pr.vat_amount), 0) AS TotalAmount
            FROM purchase_receipts pr
            LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (@From IS NULL OR pr.receipt_date >= @From)
              AND (@To IS NULL OR pr.receipt_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY DATE_FORMAT(pr.receipt_date, '%Y-%m')

            UNION ALL

            SELECT
                DATE_FORMAT(rt.return_date, '%Y-%m') AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY DATE_FORMAT(rt.return_date, '%Y-%m')
        ) t
        GROUP BY Label
        ORDER BY SupplyAmount DESC
        """;

    // ⚠️ 지역은 partners.address 첫 토큰이다. 반품도 같은 SUBSTRING_INDEX 식으로 잘라야
    //   '서울특별시' 가 매입/반품에서 서로 다른 칸으로 갈라지지 않는다.
    private const string PRR_BY_REGION = """
        SELECT Label,
               SUM(Count)        AS Count,
               0                 AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
            SELECT
                COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정') AS Label,
                COUNT(DISTINCT pr.receipt_id) AS Count,
                COALESCE(SUM(pr.total_amount), 0) AS SupplyAmount,
                COALESCE(SUM(pr.vat_amount), 0) AS VatAmount,
                COALESCE(SUM(pr.total_amount + pr.vat_amount), 0) AS TotalAmount
            FROM purchase_receipts pr
            LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (@From IS NULL OR pr.receipt_date >= @From)
              AND (@To IS NULL OR pr.receipt_date <= @To)
              AND (@Partner IS NULL OR p.address LIKE CONCAT('%', @Partner, '%'))
            GROUP BY Label

            UNION ALL

            SELECT
                COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정') AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR p.address LIKE CONCAT('%', @Partner, '%'))
            GROUP BY Label
        ) t
        GROUP BY Label
        ORDER BY SupplyAmount DESC
        """;

    // ⚠️ 사원별 조인축은 employees.user_id ← created_by 다 (매입 pr.created_by / 반품 rt.created_by).
    //   🔴 purchase_returns.created_by 는 DB-109(20260825작16)로 오늘 막 생긴 컬럼이라
    //      그 이전 반품 행은 전부 NULL 이다 ⇒ 당분간 '미지정' 칸에 음수로 뭉쳐 보인다.
    //      값이 비어 보인다고 조인 축을 바꾸지 마라. 축을 바꾸면 앞으로 들어올 데이터가 틀어진다.
    private const string PRR_BY_EMPLOYEE = """
        SELECT Label,
               SUM(Count)        AS Count,
               0                 AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
            SELECT
                COALESCE(e.emp_name, '미지정') AS Label,
                COUNT(DISTINCT pr.receipt_id) AS Count,
                COALESCE(SUM(pr.total_amount), 0) AS SupplyAmount,
                COALESCE(SUM(pr.vat_amount), 0) AS VatAmount,
                COALESCE(SUM(pr.total_amount + pr.vat_amount), 0) AS TotalAmount
            FROM purchase_receipts pr
            LEFT JOIN employees e ON e.user_id = pr.created_by AND e.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (@From IS NULL OR pr.receipt_date >= @From)
              AND (@To IS NULL OR pr.receipt_date <= @To)
              AND (@Partner IS NULL OR e.emp_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pr.created_by, e.emp_name

            UNION ALL

            SELECT
                COALESCE(e.emp_name, '미지정') AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN employees e ON e.user_id = rt.created_by AND e.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR e.emp_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rt.created_by, e.emp_name
        ) t
        GROUP BY Label
        ORDER BY SupplyAmount DESC
        """;

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetPurchaseStatisticsAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default)
    {
        // 사장님 결재 2026-04-29: 매입통계 8종. 4종 기본(품목/업체/사원/지역×월) + 4종 YoY.
        var sql = viewType switch
        {
            "item-monthly" => PRSTATS_ITEM_MONTHLY,
            "partner-monthly" => PRSTATS_PARTNER_MONTHLY,
            "employee-monthly" => PRSTATS_EMPLOYEE_MONTHLY,
            "region-monthly" => PRSTATS_REGION_MONTHLY,
            "item-yoy" => PRSTATS_YOY_ITEM,
            "partner-yoy" => PRSTATS_YOY_PARTNER,
            "employee-yoy" => PRSTATS_YOY_EMPLOYEE,
            "region-yoy" => PRSTATS_YOY_REGION,
            _ => PRSTATS_ITEM_MONTHLY
        };

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

    // ─── 매입통계 8종 SQL 상수 ───
    // 🔴 20260825작17 — 순위표와 같은 이유로 통계에서도 반품을 UNION ALL 로 음수 합산한다.
    //   사장님: "매입관련 현황·분석·통계자료·순위표 에도 반영되야",
    //           "워크플로우 흐름에 따라 데이터 재고·금액의 정합성은 무조건 맞아야됨."
    //   월별 매트릭스는 반품이 일어난 '그 달'에서 빠진다(반품 날짜축 = rt.return_date).
    //   ⚠️ 그래서 4월에 사고 5월에 반품하면 4월 매입은 그대로 남고 5월이 음수로 잡힌다.
    //      이건 버그가 아니라 회계 사실이다 — 매입은 4월에, 반품은 5월에 일어났다.
    //
    // 🔴 YoY 4종 함정 — 반드시 양쪽 다 빼야 한다.
    //   YoY 는 한 번 스캔하면서 CASE WHEN 으로 당해(@From~@To)와 전년(@FromPrev~@ToPrev)을
    //   같은 행에 두 칸으로 벌려 담는 구조다 (Count=당기건수, Qty=전년건수,
    //   SupplyAmount=당기액, VatAmount=전년액, TotalAmount=증감액).
    //   ⇒ 반품 UNION 브랜치도 똑같이 CASE 를 두 개 만들어 당해·전년 양쪽에서 빼야 한다.
    //      한쪽만 빼면 증감률이 거짓이 된다 — 예를 들어 전년 반품만 빼면 전년이 작아져
    //      올해가 실제보다 더 성장한 것처럼 보인다.
    //   ⚠️ WHERE 의 기간 조건도 반품 브랜치에서 두 구간을 OR 로 다 받아야 한다.
    //      당해 구간만 받으면 전년 CASE 가 영영 0 이라 전년 반품이 통째로 증발한다.
    //   ⚠️ HAVING 을 원본의 `> 0` 그대로 두면 안 된다. 전량 반품되어 순액이 0 이거나
    //      음수가 된 행이 조용히 사라져 "샀다가 다 물렸다"는 사실이 화면에서 없어진다.
    //      ⇒ `<> 0` 으로 바꿔 순액이 0 이 아닌 행은 남긴다.
    private const string PRSTATS_ITEM_MONTHLY = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
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
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (@From IS NULL OR pr.receipt_date >= @From)
              AND (@To IS NULL OR pr.receipt_date <= @To)
              AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pri.item_id, i.item_name, DATE_FORMAT(pr.receipt_date, '%Y-%m')

            UNION ALL

            SELECT
                CONCAT(i.item_name, ' (', DATE_FORMAT(rt.return_date, '%Y-%m'), ')') AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.qty), 0) AS Qty,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN items i ON i.item_id = rti.item_id AND i.tenant_id = rti.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rti.item_id, i.item_name, DATE_FORMAT(rt.return_date, '%Y-%m')
        ) t
        GROUP BY Label
        ORDER BY Label
        """;

    private const string PRSTATS_PARTNER_MONTHLY = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
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

            UNION ALL

            SELECT
                CONCAT(p.partner_name, ' (', DATE_FORMAT(rt.return_date, '%Y-%m'), ')') AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.qty), 0) AS Qty,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rt.partner_id, p.partner_name, DATE_FORMAT(rt.return_date, '%Y-%m')
        ) t
        GROUP BY Label
        ORDER BY Label
        """;

    private const string PRSTATS_EMPLOYEE_MONTHLY = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
            SELECT
                CONCAT(COALESCE(e.emp_name, '미지정'), ' (', DATE_FORMAT(pr.receipt_date, '%Y-%m'), ')') AS Label,
                COUNT(DISTINCT pr.receipt_id) AS Count,
                COALESCE(SUM(pri.qty), 0) AS Qty,
                COALESCE(SUM(pri.supply_amount), 0) AS SupplyAmount,
                COALESCE(SUM(pri.vat_amount), 0) AS VatAmount,
                COALESCE(SUM(pri.supply_amount + pri.vat_amount), 0) AS TotalAmount
            FROM purchase_receipt_items pri
            INNER JOIN purchase_receipts pr ON pr.receipt_id = pri.receipt_id AND pr.tenant_id = pri.tenant_id
            LEFT JOIN employees e ON e.user_id = pr.created_by AND e.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (@From IS NULL OR pr.receipt_date >= @From)
              AND (@To IS NULL OR pr.receipt_date <= @To)
              AND (@Partner IS NULL OR e.emp_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pr.created_by, e.emp_name, DATE_FORMAT(pr.receipt_date, '%Y-%m')

            UNION ALL

            SELECT
                CONCAT(COALESCE(e.emp_name, '미지정'), ' (', DATE_FORMAT(rt.return_date, '%Y-%m'), ')') AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.qty), 0) AS Qty,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN employees e ON e.user_id = rt.created_by AND e.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR e.emp_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rt.created_by, e.emp_name, DATE_FORMAT(rt.return_date, '%Y-%m')
        ) t
        GROUP BY Label
        ORDER BY Label
        """;

    private const string PRSTATS_REGION_MONTHLY = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(TotalAmount)  AS TotalAmount
        FROM (
            SELECT
                CONCAT(
                    COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정'),
                    ' (', DATE_FORMAT(pr.receipt_date, '%Y-%m'), ')'
                ) AS Label,
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
              AND (@Partner IS NULL OR p.address LIKE CONCAT('%', @Partner, '%'))
            GROUP BY COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정'),
                     DATE_FORMAT(pr.receipt_date, '%Y-%m')

            UNION ALL

            SELECT
                CONCAT(
                    COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정'),
                    ' (', DATE_FORMAT(rt.return_date, '%Y-%m'), ')'
                ) AS Label,
                -COUNT(DISTINCT rt.return_id) AS Count,
                -COALESCE(SUM(rti.qty), 0) AS Qty,
                -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (@From IS NULL OR rt.return_date >= @From)
              AND (@To IS NULL OR rt.return_date <= @To)
              AND (@Partner IS NULL OR p.address LIKE CONCAT('%', @Partner, '%'))
            GROUP BY COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정'),
                     DATE_FORMAT(rt.return_date, '%Y-%m')
        ) t
        GROUP BY Label
        ORDER BY Label
        """;

    // ─── 매입 YoY 4종 ─── (Count=당기건수, Qty=전년건수, Supply=당기액, Vat=전년액, Total=증감)
    // 🔴 반품은 당해·전년 두 CASE 를 모두 만들어 양쪽에서 뺀다. 위 블록 주석의 함정 설명 참조.
    private const string PRSTATS_YOY_ITEM = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(SupplyAmount) - SUM(VatAmount) AS TotalAmount
        FROM (
            SELECT
                i.item_name AS Label,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @From AND @To THEN pri.supply_amount ELSE 0 END), 0) AS SupplyAmount,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @FromPrev AND @ToPrev THEN pri.supply_amount ELSE 0 END), 0) AS VatAmount
            FROM purchase_receipt_items pri
            INNER JOIN purchase_receipts pr ON pr.receipt_id = pri.receipt_id AND pr.tenant_id = pri.tenant_id
            LEFT JOIN items i ON i.item_id = pri.item_id AND i.tenant_id = pri.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (pr.receipt_date BETWEEN @FromPrev AND @ToPrev OR pr.receipt_date BETWEEN @From AND @To)
              AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pri.item_id, i.item_name

            UNION ALL

            SELECT
                i.item_name AS Label,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @From AND @To THEN rti.supply_amount ELSE 0 END), 0) AS SupplyAmount,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @FromPrev AND @ToPrev THEN rti.supply_amount ELSE 0 END), 0) AS VatAmount
            FROM purchase_return_items rti
            INNER JOIN purchase_returns rt ON rt.return_id = rti.return_id AND rt.tenant_id = rti.tenant_id
            LEFT JOIN items i ON i.item_id = rti.item_id AND i.tenant_id = rti.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (rt.return_date BETWEEN @FromPrev AND @ToPrev OR rt.return_date BETWEEN @From AND @To)
              AND (@Partner IS NULL OR i.item_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rti.item_id, i.item_name
        ) t
        GROUP BY Label
        -- 🔴 20260825작17 — 전량 반품이면 당해·전년 금액이 둘 다 0 이라 조용히 사라졌다.
        --   거래가 있었으면 뜬다(절대값). 0 원으로 보이는 게 사실이다.
        HAVING SUM(ABS(Count)) <> 0 OR SUM(ABS(Qty)) <> 0
            OR SUM(ABS(SupplyAmount)) <> 0 OR SUM(ABS(VatAmount)) <> 0
        ORDER BY SupplyAmount DESC
        """;

    private const string PRSTATS_YOY_PARTNER = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(SupplyAmount) - SUM(VatAmount) AS TotalAmount
        FROM (
            SELECT
                p.partner_name AS Label,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @From AND @To THEN pr.total_amount ELSE 0 END), 0) AS SupplyAmount,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @FromPrev AND @ToPrev THEN pr.total_amount ELSE 0 END), 0) AS VatAmount
            FROM purchase_receipts pr
            LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (pr.receipt_date BETWEEN @FromPrev AND @ToPrev OR pr.receipt_date BETWEEN @From AND @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pr.partner_id, p.partner_name

            UNION ALL

            SELECT
                p.partner_name AS Label,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @From AND @To THEN rt.total_amount ELSE 0 END), 0) AS SupplyAmount,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @FromPrev AND @ToPrev THEN rt.total_amount ELSE 0 END), 0) AS VatAmount
            FROM purchase_returns rt
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (rt.return_date BETWEEN @FromPrev AND @ToPrev OR rt.return_date BETWEEN @From AND @To)
              AND (@Partner IS NULL OR p.partner_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rt.partner_id, p.partner_name
        ) t
        GROUP BY Label
        -- 🔴 20260825작17 — 전량 반품이면 당해·전년 금액이 둘 다 0 이라 조용히 사라졌다.
        --   거래가 있었으면 뜬다(절대값). 0 원으로 보이는 게 사실이다.
        HAVING SUM(ABS(Count)) <> 0 OR SUM(ABS(Qty)) <> 0
            OR SUM(ABS(SupplyAmount)) <> 0 OR SUM(ABS(VatAmount)) <> 0
        ORDER BY SupplyAmount DESC
        """;

    private const string PRSTATS_YOY_EMPLOYEE = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(SupplyAmount) - SUM(VatAmount) AS TotalAmount
        FROM (
            SELECT
                COALESCE(e.emp_name, '미지정') AS Label,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @From AND @To THEN pr.total_amount ELSE 0 END), 0) AS SupplyAmount,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @FromPrev AND @ToPrev THEN pr.total_amount ELSE 0 END), 0) AS VatAmount
            FROM purchase_receipts pr
            LEFT JOIN employees e ON e.user_id = pr.created_by AND e.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (pr.receipt_date BETWEEN @FromPrev AND @ToPrev OR pr.receipt_date BETWEEN @From AND @To)
              AND (@Partner IS NULL OR e.emp_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY pr.created_by, e.emp_name

            UNION ALL

            SELECT
                COALESCE(e.emp_name, '미지정') AS Label,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @From AND @To THEN rt.total_amount ELSE 0 END), 0) AS SupplyAmount,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @FromPrev AND @ToPrev THEN rt.total_amount ELSE 0 END), 0) AS VatAmount
            FROM purchase_returns rt
            LEFT JOIN employees e ON e.user_id = rt.created_by AND e.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (rt.return_date BETWEEN @FromPrev AND @ToPrev OR rt.return_date BETWEEN @From AND @To)
              AND (@Partner IS NULL OR e.emp_name LIKE CONCAT('%', @Partner, '%'))
            GROUP BY rt.created_by, e.emp_name
        ) t
        GROUP BY Label
        -- 🔴 20260825작17 — 전량 반품이면 당해·전년 금액이 둘 다 0 이라 조용히 사라졌다.
        --   거래가 있었으면 뜬다(절대값). 0 원으로 보이는 게 사실이다.
        HAVING SUM(ABS(Count)) <> 0 OR SUM(ABS(Qty)) <> 0
            OR SUM(ABS(SupplyAmount)) <> 0 OR SUM(ABS(VatAmount)) <> 0
        ORDER BY SupplyAmount DESC
        """;

    private const string PRSTATS_YOY_REGION = """
        SELECT Label,
               SUM(Count)        AS Count,
               SUM(Qty)          AS Qty,
               SUM(SupplyAmount) AS SupplyAmount,
               SUM(VatAmount)    AS VatAmount,
               SUM(SupplyAmount) - SUM(VatAmount) AS TotalAmount
        FROM (
            SELECT
                COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정') AS Label,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @From AND @To THEN pr.total_amount ELSE 0 END), 0) AS SupplyAmount,
                COALESCE(SUM(CASE WHEN pr.receipt_date BETWEEN @FromPrev AND @ToPrev THEN pr.total_amount ELSE 0 END), 0) AS VatAmount
            FROM purchase_receipts pr
            LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
              AND (pr.receipt_date BETWEEN @FromPrev AND @ToPrev OR pr.receipt_date BETWEEN @From AND @To)
              AND (@Partner IS NULL OR p.address LIKE CONCAT('%', @Partner, '%'))
            GROUP BY Label

            UNION ALL

            SELECT
                COALESCE(NULLIF(SUBSTRING_INDEX(TRIM(p.address), ' ', 1), ''), '미지정') AS Label,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @From AND @To THEN 1 ELSE 0 END), 0) AS Count,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @FromPrev AND @ToPrev THEN 1 ELSE 0 END), 0) AS Qty,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @From AND @To THEN rt.total_amount ELSE 0 END), 0) AS SupplyAmount,
                -COALESCE(SUM(CASE WHEN rt.return_date BETWEEN @FromPrev AND @ToPrev THEN rt.total_amount ELSE 0 END), 0) AS VatAmount
            FROM purchase_returns rt
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              AND (rt.return_date BETWEEN @FromPrev AND @ToPrev OR rt.return_date BETWEEN @From AND @To)
              AND (@Partner IS NULL OR p.address LIKE CONCAT('%', @Partner, '%'))
            GROUP BY Label
        ) t
        GROUP BY Label
        -- 🔴 20260825작17 — 전량 반품이면 당해·전년 금액이 둘 다 0 이라 조용히 사라졌다.
        --   거래가 있었으면 뜬다(절대값). 0 원으로 보이는 게 사실이다.
        HAVING SUM(ABS(Count)) <> 0 OR SUM(ABS(Qty)) <> 0
            OR SUM(ABS(SupplyAmount)) <> 0 OR SUM(ABS(VatAmount)) <> 0
        ORDER BY SupplyAmount DESC
        """;

    /// <inheritdoc />
    public async Task<List<StockLedgerRow>> GetStockLedgerAsync(
        string viewType, string tenantId,
        DateTime? from, DateTime? to,
        string? partner, CancellationToken ct, string? item = null)
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
                  AND (@Item IS NULL OR i.item_name LIKE CONCAT('%', @Item, '%'))
                GROUP BY sl.item_id, i.item_name
                ORDER BY i.item_name
                """
        };

        // 봉합 (2026-06-23, 19차 상품별 수불부 필터 값유실): 종전엔 item 뷰에서도 @Partner(품목명)를
        //   partner_name 으로 매칭해 "상품 골라 조회하면 항상 0건"이었다(동작 안 함). item 뷰는 @Item(item_name),
        //   partner 뷰는 @Partner(partner_name)로 시간원을 분리한다.
        var rows = await _db.QueryAsync<StockLedgerRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                From = from?.Date,
                To = to?.Date,
                Partner = partner,
                Item = item
            }, cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ReportRow>> GetStockStatusAsync(
        string viewType, string tenantId, string? keyword, CancellationToken ct)
    {
        // 사장님 결재 2026-04-29: 재고현황 7종 풀스택.
        // 기존 컬럼 오타 수정 (safety_stock → safe_stock).
        // current/warehouse/safety/optimal/dead-stock/loss-rate/monthly-io 7종.
        var sql = viewType switch
        {
            "warehouse" => STOCK_BY_WAREHOUSE,
            "safety" => STOCK_SAFETY_BELOW,
            "optimal" => STOCK_OPTIMAL,
            "dead-stock" => STOCK_DEAD,
            "loss-rate" => STOCK_LOSS_RATE,
            "monthly-io" => STOCK_MONTHLY_IO,
            _ => STOCK_CURRENT
        };

        // P1-8 봉합(2026-06-20): 품목 키워드 필터. 7종 SQL 모두 'items i' 별칭과
        // 'AND i.is_active = 1' 토큰을 공유하므로, 그 직후에 키워드 조건을 일괄 삽입한다.
        // 파라미터 바인딩(LIKE @KeywordLike) 사용 — SQL 인젝션 없음.
        var hasKeyword = !string.IsNullOrWhiteSpace(keyword);
        if (hasKeyword)
        {
            sql = sql.Replace(
                "AND i.is_active = 1",
                "AND i.is_active = 1\n          AND i.item_name LIKE @KeywordLike");
        }

        var rows = await _db.QueryAsync<ReportRow>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, KeywordLike = hasKeyword ? $"%{keyword!.Trim()}%" : null },
            cancellationToken: ct));

        return rows.ToList();
    }

    // ─── 재고현황 7종 SQL ───
    // 전체 현재고 (기본)
    private const string STOCK_CURRENT = """
        SELECT
            i.item_name AS Label,
            1 AS Count,
            COALESCE(SUM(s.current_qty), 0) AS Qty,
            COALESCE(SUM(s.current_qty) * i.sale_price, 0) AS SupplyAmount,
            COALESCE(i.safe_stock, 0) AS VatAmount,
            COALESCE(SUM(s.current_qty) * i.sale_price, 0) AS TotalAmount
        FROM items i
        LEFT JOIN item_stock s ON s.item_id = i.item_id AND s.tenant_id = i.tenant_id
        WHERE i.tenant_id = @TenantId
          AND i.is_active = 1
          AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
        GROUP BY i.item_id, i.item_name, i.sale_price, i.safe_stock
        ORDER BY i.item_name
        """;

    // 창고별 재고
    private const string STOCK_BY_WAREHOUSE = """
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
        """;

    // 안전재고 미달 (safe_stock 컬럼 사용)
    private const string STOCK_SAFETY_BELOW = """
        SELECT
            i.item_name AS Label,
            1 AS Count,
            COALESCE(SUM(s.current_qty), 0) AS Qty,
            COALESCE(i.safe_stock, 0) AS SupplyAmount,
            COALESCE(i.safe_stock, 0) - COALESCE(SUM(s.current_qty), 0) AS VatAmount,
            COALESCE(SUM(s.current_qty) * i.sale_price, 0) AS TotalAmount
        FROM items i
        LEFT JOIN item_stock s ON s.item_id = i.item_id AND s.tenant_id = i.tenant_id
        WHERE i.tenant_id = @TenantId
          AND i.is_active = 1
          AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
          AND COALESCE(i.safe_stock, 0) > 0
        GROUP BY i.item_id, i.item_name, i.safe_stock, i.sale_price
        HAVING Qty <= SupplyAmount
        ORDER BY VatAmount DESC
        """;

    // 적정재고대비 — 현재고 vs 안전재고 비율
    private const string STOCK_OPTIMAL = """
        SELECT
            i.item_name AS Label,
            1 AS Count,
            COALESCE(SUM(s.current_qty), 0) AS Qty,
            COALESCE(i.safe_stock, 0) AS SupplyAmount,
            CASE WHEN COALESCE(i.safe_stock, 0) = 0 THEN 0
                 ELSE ROUND(COALESCE(SUM(s.current_qty), 0) / i.safe_stock * 100, 1)
            END AS VatAmount,
            COALESCE(SUM(s.current_qty) * i.sale_price, 0) AS TotalAmount
        FROM items i
        LEFT JOIN item_stock s ON s.item_id = i.item_id AND s.tenant_id = i.tenant_id
        WHERE i.tenant_id = @TenantId
          AND i.is_active = 1
          AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
        GROUP BY i.item_id, i.item_name, i.safe_stock, i.sale_price
        ORDER BY VatAmount
        """;

    // 사장재고 — 현재고는 있는데 최근 3개월 출고 0인 품목
    private const string STOCK_DEAD = """
        SELECT
            i.item_name AS Label,
            1 AS Count,
            COALESCE(SUM(s.current_qty), 0) AS Qty,
            COALESCE(SUM(s.current_qty) * COALESCE(i.purchase_price, i.cost_price, 0), 0) AS SupplyAmount,
            DATEDIFF(CURDATE(),
              COALESCE((SELECT MAX(sd.delivery_date) FROM sales_deliveries sd
                        INNER JOIN sales_delivery_items sdi ON sdi.delivery_id = sd.delivery_id
                        WHERE sd.tenant_id = i.tenant_id AND sdi.item_id = i.item_id
                          AND sd.is_deleted = 0 AND sd.status <> 'cancelled'), CURDATE() - INTERVAL 9999 DAY)
            ) AS VatAmount,
            COALESCE(SUM(s.current_qty) * i.sale_price, 0) AS TotalAmount
        FROM items i
        LEFT JOIN item_stock s ON s.item_id = i.item_id AND s.tenant_id = i.tenant_id
        WHERE i.tenant_id = @TenantId
          AND i.is_active = 1
          AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
        GROUP BY i.item_id, i.item_name, i.purchase_price, i.cost_price, i.sale_price
        HAVING Qty > 0 AND VatAmount >= 90
        ORDER BY VatAmount DESC
        """;

    // 재고 로스율 — stock_adjust_logs 음수 조정만 합산 / 현재고
    private const string STOCK_LOSS_RATE = """
        SELECT
            i.item_name AS Label,
            COUNT(DISTINCT al.adjust_id) AS Count,
            COALESCE(SUM(al.adjust_qty), 0) AS Qty,
            COALESCE(MAX(s.current_qty), 0) AS SupplyAmount,
            CASE WHEN COALESCE(MAX(s.current_qty), 0) = 0 THEN 0
                 ELSE ROUND(ABS(COALESCE(SUM(al.adjust_qty), 0)) / COALESCE(MAX(s.current_qty), 0) * 100, 2)
            END AS VatAmount,
            COALESCE(SUM(al.adjust_qty * COALESCE(i.purchase_price, i.cost_price, 0)), 0) AS TotalAmount
        FROM items i
        LEFT JOIN item_stock s ON s.item_id = i.item_id AND s.tenant_id = i.tenant_id
        LEFT JOIN stock_adjust_logs al ON al.item_id = i.item_id AND al.tenant_id = i.tenant_id
        WHERE i.tenant_id = @TenantId
          AND i.is_active = 1
          AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
        GROUP BY i.item_id, i.item_name, i.purchase_price, i.cost_price
        HAVING Count > 0
        ORDER BY VatAmount DESC
        """;

    // 년간월별 입출재고 — 매입(입고) - 매출(출고)
    private const string STOCK_MONTHLY_IO = """
        SELECT
            CONCAT(i.item_name, ' (', t.ym, ')') AS Label,
            COALESCE(SUM(t.in_count), 0) AS Count,
            COALESCE(SUM(t.in_qty), 0) AS Qty,
            COALESCE(SUM(t.out_qty), 0) AS SupplyAmount,
            COALESCE(SUM(t.in_qty - t.out_qty), 0) AS VatAmount,
            COALESCE(SUM(t.in_amt - t.out_amt), 0) AS TotalAmount
        FROM items i
        INNER JOIN (
            SELECT pri.item_id, DATE_FORMAT(pr.receipt_date, '%Y-%m') AS ym,
                   COUNT(DISTINCT pr.receipt_id) AS in_count,
                   SUM(pri.qty) AS in_qty, 0 AS out_qty,
                   SUM(pri.supply_amount) AS in_amt, 0 AS out_amt,
                   pri.tenant_id
            FROM purchase_receipt_items pri
            INNER JOIN purchase_receipts pr ON pr.receipt_id = pri.receipt_id AND pr.tenant_id = pri.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.status <> 'cancelled'
            GROUP BY pri.item_id, ym, pri.tenant_id
            UNION ALL
            SELECT sdi.item_id, DATE_FORMAT(sd.delivery_date, '%Y-%m') AS ym,
                   0, 0, SUM(sdi.qty), 0, SUM(sdi.supply_amount), sdi.tenant_id
            FROM sales_delivery_items sdi
            INNER JOIN sales_deliveries sd ON sd.delivery_id = sdi.delivery_id AND sd.tenant_id = sdi.tenant_id
            WHERE sd.tenant_id = @TenantId AND sd.is_deleted = 0 AND sd.status <> 'cancelled'
            GROUP BY sdi.item_id, ym, sdi.tenant_id
        ) t ON t.item_id = i.item_id AND t.tenant_id = i.tenant_id
        WHERE i.tenant_id = @TenantId
          AND i.is_active = 1
          AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
        GROUP BY i.item_id, i.item_name, t.ym
        ORDER BY i.item_name, t.ym
        """;
}
