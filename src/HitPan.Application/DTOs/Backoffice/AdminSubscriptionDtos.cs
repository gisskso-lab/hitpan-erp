using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Backoffice;

// 작8 백오피스 P0 — 구독 결재 상태 관리 (W2 매니저 가도용 스켈레톤)
// 헌법 #22 본사 메타정보만 — 금액·결재 PG 메타만, 고객사 업무 데이터 0

public class AdminSubscriptionListItem
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // active · pastdue · cancelled · refunded
    public decimal MonthlyAmount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? LastPgProvider { get; set; } // toss · kakao · naver
}

public class AdminSubscriptionDetail : AdminSubscriptionListItem
{
    public List<AdminSubscriptionPaymentItem> Payments { get; set; } = new();
}

public class AdminSubscriptionPaymentItem
{
    public string PaymentId { get; set; } = string.Empty;
    public DateTime PaidAt { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty; // paid · failed · refunded
    public string? PgKey { get; set; }
    public string? FailReason { get; set; }
}

public class AdminCancelSubscriptionRequest
{
    [Required] public string Reason { get; set; } = string.Empty;
    public bool RefundLastPayment { get; set; }
}

public class AdminSubscriptionListResponse
{
    public List<AdminSubscriptionListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int TotalPages { get; set; }
}
