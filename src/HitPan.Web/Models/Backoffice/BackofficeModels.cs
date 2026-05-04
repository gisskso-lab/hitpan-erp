using MudBlazor;

namespace HitPan.Web.Models.Backoffice;

// ── 공통 래퍼 ────────────────────────────────────────────────────────────────

public class ApiResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int TotalPages { get; set; }
}

// ── 인증 ────────────────────────────────────────────────────────────────────

public class BackofficeLoginResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public AdminLoginResponse? Data { get; set; }
    public ResellerLoginResponse? ResellerData { get; set; }
}

public class AdminLoginResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string Role { get; set; } = "";
    public string AdminName { get; set; } = "";
    public string AdminId { get; set; } = "";
    public string AccountType { get; set; } = "platform_admin";
}

public class ResellerLoginResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string Role { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string ResellerId { get; set; } = "";
    public string ResellerName { get; set; } = "";
    public string AccountType { get; set; } = "reseller_admin";
}

// ── 본사 대시보드 ────────────────────────────────────────────────────────────

public class AdminDashboardData
{
    public int TotalCustomers { get; set; }
    public int NewCustomersThisMonth { get; set; }
    public decimal MonthlyBillingTotal { get; set; }
    public int UnpaidCustomers { get; set; }
    public int ActiveResellers { get; set; }
    public decimal PendingCommissionTotal { get; set; }
    public List<MonthlyCustomerTrend> MonthlyCustomerTrend { get; set; } = new();
    public List<ResellerPerformance> TopResellerPerformances { get; set; } = new();
}

public class MonthlyCustomerTrend
{
    public string Month { get; set; } = "";
    public int NewCount { get; set; }
}

public class ResellerPerformance
{
    public string ResellerName { get; set; } = "";
    public int CustomerCount { get; set; }
    public decimal MonthlyRevenue { get; set; }
    public decimal MonthlyCommission { get; set; }
}

// ── 본사 고객사 ──────────────────────────────────────────────────────────────

public class AdminTenantListItem
{
    public string TenantId { get; set; } = "";
    public string TenantCode { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string BizNo { get; set; } = "";
    public string? ResellerName { get; set; }
    public string Status { get; set; } = "";
    public string? PlanType { get; set; }
    public int UserCount { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public string StatusKo => Status switch
    {
        "active" => "활성",
        "trial" => "체험",
        "suspended" => "중지",
        "expired" => "만료",
        _ => Status
    };
    public Color StatusColor => Status switch
    {
        "active" => Color.Success,
        "trial" => Color.Info,
        "suspended" => Color.Warning,
        "expired" => Color.Error,
        _ => Color.Default
    };
}

public class AdminTenantDetail : AdminTenantListItem
{
    public string CeoName { get; set; } = "";
    public string? Tel { get; set; }
    public string? Address { get; set; }
    public string? ResellerId { get; set; }
    public TenantSubscription? Subscription { get; set; }
    public List<TenantInvoice> Invoices { get; set; } = new();
}

public class TenantSubscription
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
    public int TotalFee => BaseFee + ExtraUsers * ExtraFeePerUser;
}

public class TenantInvoice
{
    public string InvoiceId { get; set; } = "";
    public string BillingMonth { get; set; } = "";
    public int TotalAmount { get; set; }
    public string Status { get; set; } = "";
    public DateTime? PaidAt { get; set; }

    public string StatusKo => Status switch
    {
        "paid" => "납부",
        "pending" => "대기",
        "failed" => "실패",
        "cancelled" => "취소",
        _ => Status
    };
    public Color StatusColor => Status switch
    {
        "paid" => Color.Success,
        "pending" => Color.Warning,
        "failed" => Color.Error,
        _ => Color.Default
    };
}

// ── 대리점 ───────────────────────────────────────────────────────────────────

public class ResellerListItem
{
    public string ResellerId { get; set; } = "";
    public string ResellerCode { get; set; } = "";
    public string ResellerName { get; set; } = "";
    public string Status { get; set; } = "";
    public int CustomerCount { get; set; }
    public decimal MonthlyRevenue { get; set; }
    public decimal MonthlyCommission { get; set; }
    public DateTime JoinDate { get; set; }

    public string StatusKo => Status switch
    {
        "active" => "활성",
        "suspended" => "중지",
        "inactive" => "비활성",
        "terminated" => "종료",
        _ => Status
    };
    public Color StatusColor => Status switch
    {
        "active" => Color.Success,
        "suspended" => Color.Warning,
        "terminated" => Color.Error,
        _ => Color.Default
    };
}

public class ResellerDetail : ResellerListItem
{
    public string BizNo { get; set; } = "";
    public string CeoName { get; set; } = "";
    public string? Tel { get; set; }
    public string? Address { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountMasked { get; set; }
    public string? AccountHolder { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ResellerAccountItem
{
    public string AccountId { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CommissionPolicyItem
{
    public string CommissionId { get; set; } = "";
    public string PlanCode { get; set; } = "";
    public decimal Rate { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string PlanKo => PlanCode switch
    {
        "basic" => "Basic",
        "pro" => "Pro",
        "enterprise" => "Enterprise",
        _ => PlanCode
    };
}

// ── 정산 ────────────────────────────────────────────────────────────────────

public class SettlementListItem
{
    public string SettlementId { get; set; } = "";
    public string ResellerId { get; set; } = "";
    public string ResellerName { get; set; } = "";
    public string SettlementMonth { get; set; } = "";
    public int ActiveCustomerCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal PaymentAmount { get; set; }
    public string Status { get; set; } = "";
    public DateTime? PaymentDate { get; set; }
    public DateTime? ApprovalDate { get; set; }

    public string StatusKo => Status switch
    {
        "draft" => "검토중",
        "approved" => "승인됨",
        "paid" => "지급완료",
        "cancelled" => "취소",
        _ => Status
    };
    public Color StatusColor => Status switch
    {
        "draft" => Color.Default,
        "approved" => Color.Info,
        "paid" => Color.Success,
        "cancelled" => Color.Error,
        _ => Color.Default
    };
}

public class PagedSettlementResult : PagedResult<SettlementListItem>
{
    public decimal MonthlyTotalRevenue { get; set; }
    public decimal MonthlyTotalCommission { get; set; }
}

// ── 대리점 대시보드 ──────────────────────────────────────────────────────────

public class ResellerDashboardData
{
    public int CustomerCount { get; set; }
    public int NewCustomersThisMonth { get; set; }
    public decimal EstimatedCommission { get; set; }
    public decimal LastMonthCommission { get; set; }
    public string LastMonthSettlementStatus { get; set; } = "";
    public CustomerStatusSummary CustomerStatusSummary { get; set; } = new();
    public List<MonthlyCommissionTrend> MonthlyCommissionTrend { get; set; } = new();
}

public class CustomerStatusSummary
{
    public int Active { get; set; }
    public int Trial { get; set; }
    public int Suspended { get; set; }
}

public class MonthlyCommissionTrend
{
    public string Month { get; set; } = "";
    public decimal Commission { get; set; }
}

// ── 대리점 고객사 ────────────────────────────────────────────────────────────

public class ResellerTenantListItem
{
    public string TenantId { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? PlanType { get; set; }
    public int UserCount { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }

    public string StatusKo => Status switch
    {
        "active" => "활성",
        "trial" => "체험",
        "suspended" => "중지",
        "expired" => "만료",
        _ => Status
    };
    public Color StatusColor => Status switch
    {
        "active" => Color.Success,
        "trial" => Color.Info,
        "suspended" => Color.Warning,
        _ => Color.Default
    };
}

public class ResellerTenantDetail : ResellerTenantListItem
{
    public string BizNo { get; set; } = "";
    public string CeoName { get; set; } = "";
    public string? Tel { get; set; }
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; }
    public TenantSubscription? Subscription { get; set; }
    public List<TenantInvoice> Invoices { get; set; } = new();
}
