namespace HitPan.Application.Interfaces;

/// <summary>
/// 리포트 쿼리 빌더 Facade — ReportService의 SQL 쿼리 조립 로직을
/// 추후 영역별(판매/매입/재고/재무)로 분리할 수 있는 추상화 포인트.
/// MVP 단계에서는 ReportService가 이 인터페이스를 구현(동일 클래스 내).
/// 베타 중 영역별 구현체로 분할 예정.
/// </summary>
public interface IReportQueryBuilder
{
    /// <summary>
    /// 뷰 코드(period/partner/item 등)와 필터를 받아 완성된 SQL + 파라미터 셋을 반환한다.
    /// </summary>
    (string Sql, object Parameters) BuildQuotationQuery(string view, string tenantId, DateTime? from, DateTime? to, string? partner);
    (string Sql, object Parameters) BuildSalesOrderQuery(string view, string tenantId, DateTime? from, DateTime? to, string? partner);
    (string Sql, object Parameters) BuildSalesDeliveryQuery(string view, string tenantId, DateTime? from, DateTime? to, string? partner);
    (string Sql, object Parameters) BuildPurchaseOrderQuery(string view, string tenantId, DateTime? from, DateTime? to, string? partner);
    (string Sql, object Parameters) BuildPurchaseReceiptQuery(string view, string tenantId, DateTime? from, DateTime? to, string? partner);
    (string Sql, object Parameters) BuildReturnQuery(string view, string tenantId, DateTime? from, DateTime? to, string? partner);
    (string Sql, object Parameters) BuildStockLedgerQuery(string view, string tenantId, DateTime? from, DateTime? to, string? partner);
    (string Sql, object Parameters) BuildStockStatusQuery(string view, string tenantId);
}
