using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Backoffice;

// ── 수수료 정책 ────────────────────────────────────────────────
public class CommissionPolicyItem
{
    public string CommissionId { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateCommissionPolicyRequest
{
    [Required]
    public string PlanCode { get; set; } = string.Empty;

    [Required]
    [Range(0, 100)]
    public decimal Rate { get; set; }

    [Required]
    public DateTime EffectiveFrom { get; set; }
}

// ── 수수료 정산 목록 ───────────────────────────────────────────
public class SettlementListItem
{
    public string SettlementId { get; set; } = string.Empty;
    public string ResellerId { get; set; } = string.Empty;
    public string ResellerName { get; set; } = string.Empty;
    public string SettlementMonth { get; set; } = string.Empty;
    public int ActiveCustomerCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal PaymentAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? PaymentDate { get; set; }
    public DateTime? ApprovalDate { get; set; }
}

public class SettlementListResponse
{
    public List<SettlementListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int TotalPages { get; set; }
    public decimal MonthlyTotalRevenue { get; set; }
    public decimal MonthlyTotalCommission { get; set; }
}

// ── 정산 생성 ──────────────────────────────────────────────────
public class GenerateSettlementRequest
{
    [Required]
    [RegularExpression(@"^\d{4}-\d{2}$", ErrorMessage = "YYYY-MM 형식으로 입력하세요")]
    public string SettlementMonth { get; set; } = string.Empty;

    public string? ResellerId { get; set; }
}

public class GenerateSettlementResponse
{
    public int GeneratedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> SkippedReasons { get; set; } = new();
}

// ── 정산 승인 ──────────────────────────────────────────────────
public class ApproveSettlementRequest
{
    public string? Memo { get; set; }
}

// ── 정산 지급 ──────────────────────────────────────────────────
public class PaySettlementRequest
{
    [Required]
    public DateTime PaymentDate { get; set; }
    public string? Memo { get; set; }
}

// ── 정산 취소 ──────────────────────────────────────────────────
public class CancelSettlementRequest
{
    [Required]
    public string Reason { get; set; } = string.Empty;
}

// ── 대리점 대시보드 ────────────────────────────────────────────
public class ResellerDashboardResponse
{
    public int CustomerCount { get; set; }
    public int NewCustomersThisMonth { get; set; }
    public decimal EstimatedCommission { get; set; }
    public decimal LastMonthCommission { get; set; }
    public string LastMonthSettlementStatus { get; set; } = string.Empty;
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
    public string Month { get; set; } = string.Empty;
    public decimal Commission { get; set; }
}

// ── 본사 대시보드 ──────────────────────────────────────────────
public class AdminDashboardResponse
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
    public string Month { get; set; } = string.Empty;
    public int NewCount { get; set; }
}

public class ResellerPerformance
{
    public string ResellerName { get; set; } = string.Empty;
    public int CustomerCount { get; set; }
    public decimal MonthlyRevenue { get; set; }
    public decimal MonthlyCommission { get; set; }
}
