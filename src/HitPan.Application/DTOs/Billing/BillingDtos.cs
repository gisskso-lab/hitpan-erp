namespace HitPan.Application.DTOs.Billing;

// ─── 운영 설정 ───
public sealed class BillingSettingsDto
{
    public string? HeadOfficeBank { get; set; }
    public string? HeadOfficeAccount { get; set; }
    public string? HeadOfficeHolder { get; set; }
    public byte AutoBillingDay { get; set; } = 1;
    public byte GracePeriodDays { get; set; } = 7;
    public string? NotifyEmail { get; set; }
}

public sealed class UpdateBillingSettingsRequest
{
    public string? HeadOfficeBank { get; set; }
    public string? HeadOfficeAccount { get; set; }
    public string? HeadOfficeHolder { get; set; }
    public byte AutoBillingDay { get; set; } = 1;
    public byte GracePeriodDays { get; set; } = 7;
    public string? NotifyEmail { get; set; }
}

// ─── 결제수단 ───
public sealed class PaymentMethodDto
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;       // toss / manual
    public string MethodType { get; set; } = string.Empty;     // card / bank_transfer
    public string DisplayName { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardOwnerType { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public sealed class RegisterPaymentMethodRequest
{
    public string Provider { get; set; } = "manual";
    public string MethodType { get; set; } = "bank_transfer";
    public string DisplayName { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardOwnerType { get; set; }
    public string? TossAuthKey { get; set; }     // 토스 빌링키 발급용 authKey (나중에)
    public string? CustomerKey { get; set; }     // 토스 customerKey
    public bool SetAsDefault { get; set; } = true;
}

// ─── 구독 ───
public sealed class SubscriptionDto
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }
    public int LicenseCount { get; set; }
    public string? PaymentMethodId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty;
}

// ─── 인보이스 ───
public class InvoiceListDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime BillingPeriodStart { get; set; }
    public DateTime BillingPeriodEnd { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? Provider { get; set; }
    public bool TaxInvoiceIssued { get; set; }
}

public sealed class InvoiceDetailDto : InvoiceListDto
{
    public string? SubscriptionId { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Vat { get; set; }
    public DateTime? DueDate { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? ProviderPaymentKey { get; set; }
    public string? ReceiptUrl { get; set; }
    public string? TaxInvoiceNo { get; set; }
    public string? Memo { get; set; }
}

// ─── 결제 처리 ───
public sealed class PayInvoiceRequest
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public sealed class MarkPaidRequest
{
    public DateTime? PaidAt { get; set; }
    public string? Memo { get; set; }
}
