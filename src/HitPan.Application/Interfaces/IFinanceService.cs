using HitPan.Application.DTOs.Approval;

namespace HitPan.Application.Interfaces;

/// <summary>경리·세무 통합 서비스</summary>
public interface IFinanceService
{
    // 현금출납장
    Task<List<CashbookDto>> GetCashbookAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<string> CreateCashbookAsync(CreateCashbookRequest req, string tenantId, string userId, CancellationToken ct = default);
    Task DeleteCashbookAsync(string id, string tenantId, CancellationToken ct = default);

    // 매입매출장
    Task<List<PurchaseSalesLedgerDto>> GetPurchaseSalesLedgerAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default);

    // 부가세 신고자료
    Task<VatSummaryDto> GetVatSummaryAsync(string tenantId, int year, int half, CancellationToken ct = default);

    // 경비
    Task<List<ExpenseDto>> GetExpensesAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default);
    // 헌법 #19 정합 — limit 추가 시그니처 (5/26 진범 #4·#7 봉합)
    Task<List<ExpenseDto>> GetExpensesAsync(string tenantId, DateTime? from, DateTime? to, int limit, CancellationToken ct = default);
    Task<string> CreateExpenseAsync(CreateExpenseRequest req, string tenantId, string userId, CancellationToken ct = default);
    Task ApproveExpenseAsync(string expenseId, string tenantId, string action, CancellationToken ct = default);

    // 손익현황
    Task<List<ProfitSummaryDto>> GetProfitAsync(string tenantId, int year, CancellationToken ct = default);

    // 정합성 검증
    Task<DataIntegrityReport> CheckIntegrityAsync(string tenantId, CancellationToken ct = default);

    // 대시보드
    Task<DashboardSummaryDto> GetDashboardAsync(string tenantId, CancellationToken ct = default);

    // 계정과목
    Task<List<AccountDto>> GetAccountsAsync(string tenantId, CancellationToken ct = default);
    Task<string> CreateAccountAsync(string tenantId, CreateAccountRequest req, CancellationToken ct = default);
    Task UpdateAccountAsync(string tenantId, string accountCode, UpdateAccountRequest req, CancellationToken ct = default);
    Task DeleteAccountAsync(string tenantId, string accountCode, CancellationToken ct = default);
}
