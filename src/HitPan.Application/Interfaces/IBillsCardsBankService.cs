using HitPan.Application.DTOs.Finance;

namespace HitPan.Application.Interfaces;

/// <summary>어음·카드결제·은행거래 통합 서비스 (사장님 결재 2026-04-29).</summary>
public interface IBillsCardsBankService
{
    // Bills
    Task<List<BillDto>> ListBillsAsync(string tenantId, string? type, string? status, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<string> CreateBillAsync(string tenantId, CreateBillRequest req, CancellationToken ct = default);
    Task UpdateBillStatusAsync(string tenantId, string billId, UpdateBillStatusRequest req, CancellationToken ct = default);

    // Card Payments
    Task<List<CardPaymentDto>> ListCardPaymentsAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<CardPaymentDto?> GetCardPaymentAsync(string tenantId, string cardPaymentId, CancellationToken ct = default);
    Task<string> CreateCardPaymentAsync(string tenantId, CreateCardPaymentRequest req, CancellationToken ct = default);

    // Bank Transactions (INSERT ONLY)
    Task<List<BankTxDto>> ListBankTxAsync(string tenantId, string? accountNo, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<string> CreateBankTxAsync(string tenantId, CreateBankTxRequest req, CancellationToken ct = default);
}
