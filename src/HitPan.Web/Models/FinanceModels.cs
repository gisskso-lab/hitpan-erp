namespace HitPan.Web.Models;

// ── 현금출납장 ──
public class CashbookModel
{
    public string CashbookId { get; set; } = string.Empty;
    public DateTime TxDate { get; set; }
    public string TxType { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? PartnerName { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal IncomeAmount { get; set; }
    public decimal ExpenseAmount { get; set; }
    public decimal Balance { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public class CreateCashbookModel
{
    public DateTime TxDate { get; set; } = DateTime.Today;
    public string TxType { get; set; } = "income";
    public string? Category { get; set; }
    public string? PartnerId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string? Memo { get; set; }
}

// ── 매입매출장 ──
public class PurchaseSalesLedgerModel
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

// ── 부가세 ──
public class VatSummaryModel
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
public class ExpenseModel
{
    public string ExpenseId { get; set; } = string.Empty;
    public DateTime ExpenseDate { get; set; }
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

public class CreateExpenseModel
{
    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public string PaymentMethod { get; set; } = "card";
    public bool ReceiptYn { get; set; } = true;
    public string? Memo { get; set; }
}

// ── 손익 ──
public class ProfitSummaryModel
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
