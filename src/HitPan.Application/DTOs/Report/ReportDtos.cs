namespace HitPan.Application.DTOs.Report;

/// <summary>
/// 현황 리포트 공통 행 DTO다.
/// </summary>
public class ReportRow
{
    /// <summary>
    /// 라벨이다. (기간/업체명/품목명)
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// 건수다.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 수량이다. (품목별 보기에서만 사용)
    /// </summary>
    public decimal Qty { get; set; }

    /// <summary>
    /// 공급가액이다.
    /// </summary>
    public decimal SupplyAmount { get; set; }

    /// <summary>
    /// 부가세다.
    /// </summary>
    public decimal VatAmount { get; set; }

    /// <summary>
    /// 합계금액이다.
    /// </summary>
    public decimal TotalAmount { get; set; }
}

/// <summary>
/// 수익성 분석 리포트 행 DTO다.
/// </summary>
public class ProfitReportRow
{
    /// <summary>
    /// 라벨이다. (업체명/품목명/기간)
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// 건수다.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 매출(수익)이다.
    /// </summary>
    public decimal Revenue { get; set; }

    /// <summary>
    /// 원가다.
    /// </summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// 이익이다.
    /// </summary>
    public decimal Profit { get; set; }

    /// <summary>
    /// 이익률(%)이다.
    /// </summary>
    public decimal ProfitRate { get; set; }
}

/// <summary>
/// 수불부(원장) 행 DTO다.
/// </summary>
public class StockLedgerRow
{
    /// <summary>
    /// 라벨이다. (품목명 또는 업체명)
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// 입고수량 합계다.
    /// </summary>
    public decimal QtyIn { get; set; }

    /// <summary>
    /// 출고수량 합계다.
    /// </summary>
    public decimal QtyOut { get; set; }

    /// <summary>
    /// 잔량이다. (입고 - 출고)
    /// </summary>
    public decimal Balance { get; set; }

    /// <summary>
    /// 입고금액이다.
    /// </summary>
    public decimal AmountIn { get; set; }

    /// <summary>
    /// 출고금액이다.
    /// </summary>
    public decimal AmountOut { get; set; }
}
