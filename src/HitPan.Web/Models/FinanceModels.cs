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

// ── 계정과목 ──
/// <summary>
/// 🔴 20260827작4 — <b>합계잔액시산표</b> 한 줄. 서버 <c>TrialBalanceRowDto</c> 와 짝.
/// </summary>
public class TrialBalanceRowModel
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public decimal DebitTotal { get; set; }
    public decimal CreditTotal { get; set; }
    public decimal Balance { get; set; }

    /// <summary>화면 표기용 한글 이름. <see cref="AccountModel.AccountTypeLabel"/> 와 같은 규칙.</summary>
    public string AccountTypeLabel => AccountType switch
    {
        "asset" => "자산",
        "liability" => "부채",
        "equity" => "자본",
        "revenue" => "수익",
        "expense" => "비용",
        _ => "미분류"
    };
}

/// <summary>
/// 🔴 20260827작4 — 시산표 전체. 사장님 <i>"전체 돈 숫자가 모이는 곳"</i>.
/// </summary>
public class TrialBalanceModel
{
    public List<TrialBalanceRowModel> Rows { get; set; } = new();
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }

    /// <summary>차변합 == 대변합. <b>서버가 판정한 값</b>을 그대로 쓴다(화면이 다시 빼지 않는다).</summary>
    public bool IsBalanced { get; set; }

    /// <summary>아직 장부에 안 잡히는 업무 건수(수금·지급·경비).</summary>
    public int UnpostedCount { get; set; }

    /// <summary>안내문에 쓸 이름들 (예: "수금 12건").</summary>
    public List<string> UnpostedSources { get; set; } = new();
}

public class AccountModel
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string? ParentCode { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public string AccountTypeLabel => AccountType switch
    {
        "asset" => "자산",
        "liability" => "부채",
        "equity" => "자본",
        "revenue" => "수익",
        "expense" => "비용",
        _ => AccountType
    };
}

public class CreateAccountModel
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = "asset";
    public string? ParentCode { get; set; }
    public int SortOrder { get; set; }
}

public class UpdateAccountModel
{
    public string AccountName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

// ── 정합성 검사 (20260827작8 W4) ──
// 🔴 서버 DataIntegrityReport 수신용. Score 는 의도적으로 담지 않는다 —
//    화면에 점수를 띄우면 "92점" 이 "8건 틀림" 을 가린다(헌법 #32).
public class IntegrityReportModel
{
    public DateTime CheckedAt { get; set; }
    public List<IntegrityItemModel> Items { get; set; } = new();
    public int TotalChecks { get; set; }
    public int PassCount { get; set; }
    public int FailCount { get; set; }
}

public class IntegrityItemModel
{
    /// <summary>재고 · 매입 · 매출 · 마스터 · BOM · 결재 · 회계</summary>
    public string Category { get; set; } = string.Empty;
    public string CheckName { get; set; } = string.Empty;
    /// <summary>OK · WARN · FAIL — 화면에는 한글로 바꿔 표시한다.</summary>
    public string Status { get; set; } = "OK";
    /// <summary>이상일 때 "3건 음수" 처럼 무엇이 몇 건인지.</summary>
    public string? Detail { get; set; }
}
