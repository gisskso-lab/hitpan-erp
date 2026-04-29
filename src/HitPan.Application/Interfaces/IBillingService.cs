using HitPan.Application.DTOs.Billing;

namespace HitPan.Application.Interfaces;

public interface IBillingService
{
    // 운영 설정
    Task<BillingSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default);
    Task UpdateSettingsAsync(string tenantId, UpdateBillingSettingsRequest request, CancellationToken ct = default);

    // 결제수단
    Task<List<PaymentMethodDto>> GetPaymentMethodsAsync(string tenantId, CancellationToken ct = default);
    Task<string> RegisterPaymentMethodAsync(string tenantId, RegisterPaymentMethodRequest request, CancellationToken ct = default);
    Task SetDefaultPaymentMethodAsync(string tenantId, string paymentMethodId, CancellationToken ct = default);
    Task DeletePaymentMethodAsync(string tenantId, string paymentMethodId, CancellationToken ct = default);

    // 구독 (현재는 읽기만 — 플랜 변경 화면은 차후)
    Task<SubscriptionDto?> GetCurrentSubscriptionAsync(string tenantId, CancellationToken ct = default);

    // 인보이스
    Task<List<InvoiceListDto>> GetInvoicesAsync(string tenantId, CancellationToken ct = default);
    Task<InvoiceDetailDto?> GetInvoiceAsync(string tenantId, string invoiceId, CancellationToken ct = default);
    Task<bool> PayInvoiceAsync(string tenantId, string invoiceId, PayInvoiceRequest request, CancellationToken ct = default);
    Task MarkInvoicePaidManuallyAsync(string tenantId, string invoiceId, MarkPaidRequest request, CancellationToken ct = default);
}
