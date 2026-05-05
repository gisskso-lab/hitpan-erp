using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Approval;

// ── 현금출납장 ──

public class CashbookDto
{
    public string CashbookId { get; set; } = string.Empty;
    public DateTime TxDate { get; set; }
    public string TxType { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal IncomeAmount { get; set; }
    public decimal ExpenseAmount { get; set; }
    public decimal Balance { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public class CreateCashbookRequest
{
    public DateTime TxDate { get; set; }
    public string TxType { get; set; } = "income";
    public string? Category { get; set; }
    public string? PartnerId { get; set; }
    public string Description { get; set; } = string.Empty;

    [Range(0.01, double.MaxValue, ErrorMessage = "금액은 0보다 커야 합니다.")]
    public decimal Amount { get; set; }

    public string PaymentMethod { get; set; } = "cash";
    public string? Memo { get; set; }
}

// ── 매입매출장 ──

public class PurchaseSalesLedgerDto
{
    public DateTime TxDate { get; set; }
    public string DocType { get; set; } = string.Empty;
    public string DocNo { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Memo { get; set; }
}

// ── 부가세 신고자료 ──

public class VatSummaryDto
{
    public string Period { get; set; } = string.Empty;
    public decimal SalesSupply { get; set; }
    public decimal SalesVat { get; set; }
    public decimal PurchaseSupply { get; set; }
    public decimal PurchaseVat { get; set; }
    public decimal NetVat { get; set; }
    public int SalesCount { get; set; }
    public int PurchaseCount { get; set; }
}

// ── 경비 ──

public class ExpenseDto
{
    public string ExpenseId { get; set; } = string.Empty;
    public DateTime ExpenseDate { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public bool ReceiptYn { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public class CreateExpenseRequest
{
    public DateTime ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    [Range(0.01, double.MaxValue, ErrorMessage = "경비금액은 0보다 커야 합니다.")]
    public decimal Amount { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "부가세는 음수일 수 없습니다.")]
    public decimal VatAmount { get; set; }

    public string PaymentMethod { get; set; } = "card";
    public bool ReceiptYn { get; set; } = true;
    public string? Memo { get; set; }
}

// ── 정합성 검증 ──

public class DataIntegrityReport
{
    public DateTime CheckedAt { get; set; }
    public List<IntegrityItem> Items { get; set; } = new();
    public int TotalChecks { get; set; }
    public int PassCount { get; set; }
    public int FailCount { get; set; }
    public decimal Score { get; set; }
}

public class IntegrityItem
{
    public string Category { get; set; } = string.Empty;
    public string CheckName { get; set; } = string.Empty;
    public string Status { get; set; } = "OK";
    public string? Detail { get; set; }
}

/// <summary>경비 승인/반려 요청</summary>
public class ProcessExpenseRequest
{
    public string Action { get; set; } = "approved"; // approved, rejected
}

// ── 손익현황 ──

public class ProfitSummaryDto
{
    public string YearMonth { get; set; } = string.Empty;
    public string YearMonthLabel { get; set; } = string.Empty;
    public decimal SalesAmount { get; set; }
    public decimal PurchaseAmount { get; set; }
    public decimal ExpenseAmount { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal NetProfit { get; set; }
    public decimal ProfitRate { get; set; }
}

// ── 대시보드 ──

/// <summary>대시보드 요약 데이터</summary>
public class DashboardSummaryDto
{
    // KPI 카드
    public decimal TodaySales { get; set; }
    public decimal MonthSales { get; set; }
    public decimal MonthPurchase { get; set; }
    public decimal UnpaidReceivable { get; set; }  // 미수금
    public decimal UnpaidPayable { get; set; }      // 미지급
    public int LowStockCount { get; set; }          // 안전재고 미달 품목 수
    public int PendingApprovalCount { get; set; }   // 결재 대기 건수

    // 월별 매출·매입 추이 (최근 6개월)
    public List<MonthlyTrendItem> MonthlyTrend { get; set; } = new();

    // 거래처 매출 TOP 5
    public List<PartnerRankItem> TopPartners { get; set; } = new();

    // 최근 거래 5건
    public List<RecentTransactionItem> RecentTransactions { get; set; } = new();

    // 안전재고 미달 품목 목록 (최대 10건)
    public List<LowStockItem> LowStockItems { get; set; } = new();
}

public class MonthlyTrendItem
{
    public string YearMonth { get; set; } = string.Empty;  // "2026-04"
    public string Label { get; set; } = string.Empty;       // "4월"
    public decimal SalesAmount { get; set; }
    public decimal PurchaseAmount { get; set; }
}

public class PartnerRankItem
{
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int OrderCount { get; set; }
}

public class RecentTransactionItem
{
    public DateTime TxDate { get; set; }
    public string DocType { get; set; } = string.Empty;  // "판매", "매입", "수금"
    public string DocNo { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class LowStockItem
{
    public string ItemName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public decimal CurrentQty { get; set; }
    public decimal SafetyStock { get; set; }
}
