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
