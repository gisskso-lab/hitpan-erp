namespace HitPan.Web.Models;

public sealed class BillingSettingsModel
{
    public string? HeadOfficeBank { get; set; }
    public string? HeadOfficeAccount { get; set; }
    public string? HeadOfficeHolder { get; set; }
    public byte AutoBillingDay { get; set; } = 1;
    public byte GracePeriodDays { get; set; } = 7;
    public string? NotifyEmail { get; set; }
}

public sealed class PaymentMethodModel
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string MethodType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardOwnerType { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public sealed class RegisterPaymentMethodModel
{
    public string Provider { get; set; } = "manual";
    public string MethodType { get; set; } = "bank_transfer";
    public string DisplayName { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardOwnerType { get; set; }
    public string? TossAuthKey { get; set; }
    public string? CustomerKey { get; set; }
    public bool SetAsDefault { get; set; } = true;
}

public sealed class SubscriptionModel
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

public sealed class InvoiceListItemModel
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
