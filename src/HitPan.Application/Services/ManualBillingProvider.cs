using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 무통장입금(본사 가상계좌) 결제 처리 — 실제 PG 통신 없이 DB만 기록.
/// 사장님(본사)이 입금 확인 후 어드민 화면에서 paid 처리.
/// 토스 도입 시 TossBillingProvider 추가하면 됨.
/// </summary>
public sealed class ManualBillingProvider : IBillingProvider
{
    public string ProviderCode => "manual";

    public Task<RegisterMethodResult> RegisterPaymentMethodAsync(
        string tenantId, string customerKey, string? authKey, CancellationToken ct = default)
    {
        // 무통장은 빌링키 없음. 계좌이체 안내만.
        return Task.FromResult(new RegisterMethodResult
        {
            Success = true,
            BillingKey = null
        });
    }

    public Task<ChargeResult> ChargeAsync(
        string tenantId, string invoiceId, decimal amount, string? billingKeyPlain,
        string customerKey, string orderName, CancellationToken ct = default)
    {
        // 무통장은 자동청구 불가 — pending 상태로 두고 입금 대기.
        return Task.FromResult(new ChargeResult
        {
            Success = false,
            Status = "pending",
            ErrorCode = "MANUAL_PENDING",
            ErrorMessage = "본사 가상계좌 입금 대기 — 어드민 입금확인 후 paid 처리"
        });
    }

    public Task<RefundResult> RefundAsync(
        string tenantId, string providerPaymentKey, decimal amount, string reason, CancellationToken ct = default)
    {
        return Task.FromResult(new RefundResult
        {
            Success = false,
            ErrorCode = "MANUAL_NOT_SUPPORTED",
            ErrorMessage = "무통장 환불은 본사가 직접 송금 처리"
        });
    }
}
