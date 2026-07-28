namespace HitPan.Application.Interfaces;

/// <summary>
/// PG사 추상화 인터페이스. 구현체: ManualBillingProvider(현재) / TossBillingProvider(추후).
/// 결제수단 등록 → 청구 → 환불의 PG 의존 동작만 위임.
/// </summary>
public interface IBillingProvider
{
    string ProviderCode { get; }   // "toss" / "manual"

    /// <summary>결제수단 등록 (토스: authKey → billingKey 교환).</summary>
    Task<RegisterMethodResult> RegisterPaymentMethodAsync(
        string tenantId,
        string customerKey,
        string? authKey,
        CancellationToken ct = default);

    /// <summary>인보이스 청구 (토스: billingKey 로 자동결제 / Manual: pending 유지).</summary>
    Task<ChargeResult> ChargeAsync(
        string tenantId,
        string invoiceId,
        decimal amount,
        string? billingKeyPlain,
        string customerKey,
        string orderName,
        CancellationToken ct = default);

    /// <summary>환불.</summary>
    Task<RefundResult> RefundAsync(
        string tenantId,
        string providerPaymentKey,
        decimal amount,
        string reason,
        CancellationToken ct = default);
}

public sealed class RegisterMethodResult
{
    public bool Success { get; set; }
    public string? BillingKey { get; set; }     // 평문(저장 시 암호화 책임은 호출자)
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardOwnerType { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawResponse { get; set; }
}

public sealed class ChargeResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = "pending"; // success / failed / pending
    public string? ProviderPaymentKey { get; set; }
    public string? ReceiptUrl { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawResponse { get; set; }
}

public sealed class RefundResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawResponse { get; set; }
}
