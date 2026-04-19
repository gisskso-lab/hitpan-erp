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
}
