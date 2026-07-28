namespace HitPan.Application.DTOs.Approval;

/// <summary>월마감 현황 DTO</summary>
public class MonthlyClosingDto
{
    public string ClosingId { get; set; } = string.Empty;
    public string YearMonth { get; set; } = string.Empty;
    public string YearMonthLabel { get; set; } = string.Empty;
    public string Status { get; set; } = "open";
    public string StatusLabel { get; set; } = string.Empty;
    public DateTime? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public string? ReopenedBy { get; set; }
    public string? ReopenReason { get; set; }
    public decimal SalesAmount { get; set; }
    public decimal PurchaseAmount { get; set; }
    public decimal ReceiptAmount { get; set; }
    public decimal PaymentAmount { get; set; }
    public string? Memo { get; set; }
}

/// <summary>월마감 처리 요청</summary>
public class CloseMonthRequest
{
    public string YearMonth { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

/// <summary>월마감 재오픈 요청</summary>
public class ReopenMonthRequest
{
    public string YearMonth { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
