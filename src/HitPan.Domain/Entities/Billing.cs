using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public sealed class BillingSettings : ITenantEntity
{
    public string SettingId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string? HeadOfficeBank { get; set; }
    public string? HeadOfficeAccount { get; set; }
    public string? HeadOfficeHolder { get; set; }
    public byte AutoBillingDay { get; set; } = 1;
    public byte GracePeriodDays { get; set; } = 7;
    public string? NotifyEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class BillingPaymentMethod : BaseEntity, ITenantEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string Provider { get; set; } = "manual";    // toss / manual
    public string MethodType { get; set; } = "bank_transfer"; // card / bank_transfer
    public byte[]? ProviderBillingKey { get; set; }     // 암호화 저장
    public string? CustomerKey { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardOwnerType { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime RegisteredAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public sealed class BillingSubscription : ITenantEntity
{
    public string SubscriptionId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }
    public int LicenseCount { get; set; } = 1;
    public string? PaymentMethodId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class BillingInvoice : BaseEntity, ITenantEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
    public string? SubscriptionId { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime BillingPeriodStart { get; set; }
    public DateTime BillingPeriodEnd { get; set; }
    public decimal Amount { get; set; }
    public decimal Vat { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime IssuedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? Provider { get; set; }
    public string? ProviderPaymentKey { get; set; }
    public string? ReceiptUrl { get; set; }
    public bool TaxInvoiceIssued { get; set; }
    public string? TaxInvoiceNo { get; set; }
    public string? Memo { get; set; }
}

public sealed class BillingPaymentAttempt : ITenantEntity
{
    public string AttemptId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string? PaymentMethodId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public DateTime AttemptedAt { get; set; }
    public string Status { get; set; } = string.Empty; // success/failed/pending
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProviderResponseJson { get; set; }
}
