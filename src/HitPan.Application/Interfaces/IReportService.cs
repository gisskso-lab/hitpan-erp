using HitPan.Application.DTOs.Report;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 현황 리포트 서비스 인터페이스다.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// 견적 현황을 조회한다.
    /// </summary>
    Task<List<ReportRow>> GetQuotationReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 수주 현황을 조회한다.
    /// </summary>
    Task<List<ReportRow>> GetSalesOrderReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 판매(거래명세서) 현황을 조회한다.
    /// </summary>
    Task<List<ReportRow>> GetSalesReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 발주 현황을 조회한다.
    /// </summary>
    Task<List<ReportRow>> GetPurchaseOrderReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 매입 현황을 조회한다.
    /// </summary>
    Task<List<ReportRow>> GetPurchaseReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 반품 현황을 조회한다.
    /// </summary>
    Task<List<ReportRow>> GetReturnReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 매출반품(반품확인서) 현황을 조회한다 — 기간·업체·품목·사원별 4종 (20260825작6).
    /// </summary>
    /// <remarks>
    /// 사장님 정의(2026-08-25): <i>"매출에 있는 반품 = 사용자의 고객사가 반품처리한 품목관리"</i>.
    /// 매입반품(<see cref="GetReturnReportAsync"/>)과 <b>방향이 반대인 별개 업무</b>라
    /// 분기가 아니라 <b>별도 메서드</b>로 둔다 — 한쪽을 고치다 다른 쪽을 흔들지 않기 위해서다.
    /// </remarks>
    Task<List<ReportRow>> GetSalesReturnReportAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 판매 순위표를 조회한다.
    /// </summary>
    Task<List<ReportRow>> GetSalesRankingAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 판매 수익성 분석을 조회한다.
    /// </summary>
    Task<List<ProfitReportRow>> GetSalesProfitabilityAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 판매 통계를 조회한다.
    /// </summary>
    Task<List<ReportRow>> GetSalesStatisticsAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 매입 순위표를 조회한다.
    /// </summary>
    Task<List<ReportRow>> GetPurchaseRankingAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 매입 통계를 조회한다.
    /// </summary>
    Task<List<ReportRow>> GetPurchaseStatisticsAsync(
        string viewType, string tenantId,
        DateTime? from = null, DateTime? to = null,
        string? partner = null, CancellationToken ct = default);

    /// <summary>
    /// 수불부(원장)를 조회한다. (상품별 / 업체별)
    /// </summary>
    Task<List<StockLedgerRow>> GetStockLedgerAsync(
        string viewType, string tenantId,
        DateTime? from, DateTime? to,
        string? partner, CancellationToken ct, string? item = null);

    /// <summary>
    /// 재고현황을 조회한다. (전체 현재고 / 안전재고 미달)
    /// </summary>
    Task<List<ReportRow>> GetStockStatusAsync(
        string viewType, string tenantId, string? keyword, CancellationToken ct);
}
