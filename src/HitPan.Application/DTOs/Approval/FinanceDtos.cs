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

// ── 계정과목 ──

public class AccountDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string? ParentCode { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public class CreateAccountRequest
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = "asset";
    public string? ParentCode { get; set; }
    public int SortOrder { get; set; }
}

public class UpdateAccountRequest
{
    public string AccountName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// 🔴 20260827작4 — <b>합계잔액시산표</b> 한 줄(계정 하나).
/// </summary>
/// <remarks>
/// <para>
/// <b>시산표(試算表)</b> = 모든 계정의 차변·대변을 모아 <b>좌우 합계가 맞는지 검산</b>하는 표.
/// 복식부기는 차변합 = 대변합 이 항상 성립해야 하고, 안 맞으면 장부가 틀렸다는 뜻이다.
/// 경리가 <b>월마감 직전에 반드시 보는 화면</b>이다.
/// </para>
/// <para>
/// 사장님 오더: <i>"매입매출, 그밖에 모든 돈의 흐름을 한번에, 전체 돈 숫자가 모이는 곳"</i>
/// ⇒ 그게 바로 이 시산표다.
/// </para>
/// </remarks>
public class TrialBalanceRowDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;

    /// <summary>asset(자산)·liability(부채)·equity(자본)·revenue(수익)·expense(비용).</summary>
    public string AccountType { get; set; } = string.Empty;

    /// <summary>기간 내 차변 합계.</summary>
    public decimal DebitTotal { get; set; }

    /// <summary>기간 내 대변 합계.</summary>
    public decimal CreditTotal { get; set; }

    /// <summary>
    /// 잔액. <b>계정 성격에 따라 방향이 다르다</b> —
    /// 자산·비용은 차변이 늘면 +, 부채·자본·수익은 대변이 늘면 +.
    /// 서버가 계산해서 내려보낸다(화면이 회계 규칙을 다시 알 필요 없게).
    /// </summary>
    public decimal Balance { get; set; }
}

/// <summary>
/// 🔴 20260827작4 — 시산표 전체(줄 + 검산 결과).
/// </summary>
public class TrialBalanceDto
{
    public List<TrialBalanceRowDto> Rows { get; set; } = new();

    /// <summary>전체 차변 합계.</summary>
    public decimal TotalDebit { get; set; }

    /// <summary>전체 대변 합계.</summary>
    public decimal TotalCredit { get; set; }

    /// <summary>
    /// 🔴 <b>검산 통과 여부.</b> 차변합 == 대변합 이면 true.
    /// 화면이 직접 빼서 비교하지 않는다 — 서버가 판정한 값을 그대로 쓴다.
    /// </summary>
    public bool IsBalanced { get; set; }

    /// <summary>
    /// ⚠️ <b>아직 장부에 안 잡히는 업무가 있는지</b> 화면에 알려주기 위한 값.
    /// 수금·지급·경비·급여는 현재 기표되지 않는다(20260827 설계 결재서 §4).
    /// 이 숫자가 0 이 아니면 화면이 "아직 일부만 보입니다" 라고 말해준다.
    /// ⇒ 담당자가 "고장났나" 하지 않게 하는 장치다.
    /// </summary>
    public int UnpostedCount { get; set; }

    /// <summary>기표 안 되는 업무 이름들(화면 안내문용).</summary>
    public List<string> UnpostedSources { get; set; } = new();
}
