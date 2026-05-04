using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Backoffice;

// ── 본사용 고객사 목록 ─────────────────────────────────────────
public class AdminTenantListItem
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string BizNo { get; set; } = string.Empty;
    public string? ResellerName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PlanType { get; set; }
    public int UserCount { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminTenantListResponse
{
    public List<AdminTenantListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int TotalPages { get; set; }
}

// ── 본사용 고객사 상세 ─────────────────────────────────────────
public class AdminTenantDetail
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string BizNo { get; set; } = string.Empty;
    public string CeoName { get; set; } = string.Empty;
    public string? Tel { get; set; }
    public string? Address { get; set; }
    public string? ResellerId { get; set; }
    public string? ResellerName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? TrialEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public AdminTenantSubscription? Subscription { get; set; }
    public List<AdminTenantInvoice> Invoices { get; set; } = new();
}

public class AdminTenantSubscription
{
    public string? PlanType { get; set; }
    public int BaseUsers { get; set; }
    public int ExtraUsers { get; set; }
    public int BaseFee { get; set; }
    public int ExtraFeePerUser { get; set; }
    public string? BillingCycle { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public string? Status { get; set; }
}

public class AdminTenantInvoice
{
    public string InvoiceId { get; set; } = string.Empty;
    public string BillingMonth { get; set; } = string.Empty;
    public int TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
}

// ── 고객사 상태 변경 ───────────────────────────────────────────
public class UpdateTenantStatusRequest
{
    [Required]
    public string NewStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

// ── 신규 고객사 등록 (본사 수동) ──────────────────────────────
public class AdminCreateTenantRequest
{
    [Required]
    [MaxLength(100)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    public string BizNo { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string CeoName { get; set; } = string.Empty;

    public string? Tel { get; set; }
    public string? Address { get; set; }
    public string? ResellerId { get; set; }

    [Required]
    public string PlanType { get; set; } = "basic";

    [Required]
    public string BillingCycle { get; set; } = "monthly";

    public int ExtraUsers { get; set; } = 0;

    [Required]
    public string StartType { get; set; } = "trial";

    [Required]
    [EmailAddress]
    public string AdminEmail { get; set; } = string.Empty;
}

public class AdminCreateTenantResponse
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string TempPassword { get; set; } = string.Empty;
}

// ── 대리점용 내 고객사 목록 ────────────────────────────────────
public class ResellerTenantListItem
{
    public string TenantId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PlanType { get; set; }
    public int UserCount { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }
}

public class ResellerTenantListResponse
{
    public List<ResellerTenantListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int TotalPages { get; set; }
}

// ── 대리점용 내 고객사 상세 (읽기전용) ─────────────────────────
public class ResellerTenantDetail
{
    public string TenantId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string BizNo { get; set; } = string.Empty;
    public string CeoName { get; set; } = string.Empty;
    public string? Tel { get; set; }
    public string? Address { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? TrialEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public AdminTenantSubscription? Subscription { get; set; }
    public List<AdminTenantInvoice> Invoices { get; set; } = new();
}
