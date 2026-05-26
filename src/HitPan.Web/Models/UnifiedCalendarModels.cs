namespace HitPan.Web.Models;

/// <summary>
/// 통합 캘린더 월간 모델 — 4축(수금/입금/연차/이슈) 집계.
/// 사장님 결재 2026-05-26.
/// </summary>
public sealed class UnifiedCalendarModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int DayCount { get; set; }
    public List<UnifiedCalendarDay> Days { get; set; } = new();
    public UnifiedCalendarTotals Totals { get; set; } = new();
}

public sealed class UnifiedCalendarDay
{
    public int Day { get; set; }
    public int Collection { get; set; }
    public int Payment { get; set; }
    public int Leave { get; set; }
    public int Issue { get; set; }
    public decimal CollectionAmount { get; set; }
    public decimal PaymentAmount { get; set; }
}

public sealed class UnifiedCalendarTotals
{
    public int Collection { get; set; }
    public int Payment { get; set; }
    public int Leave { get; set; }
    public int Issue { get; set; }
}

/// <summary>날짜 클릭 시 카드 모달 상세 데이터.</summary>
public sealed class UnifiedDayDetailModel
{
    public DateTime Date { get; set; }
    public List<CollectionItem> Collections { get; set; } = new();
    public List<PaymentItem> Payments { get; set; } = new();
    public List<LeaveItem> Leaves { get; set; } = new();
    public List<IssueItem> Issues { get; set; } = new();

    public sealed class CollectionItem
    {
        public string? Id { get; set; }
        public string? PartnerId { get; set; }
        public string? PartnerName { get; set; }
        public decimal Amount { get; set; }
        public string? Method { get; set; }
        public string? Memo { get; set; }
        public string? RefDocType { get; set; }
        public string? RefDocId { get; set; }
    }

    public sealed class PaymentItem
    {
        public string? Id { get; set; }
        public string? PartnerId { get; set; }
        public string? PartnerName { get; set; }
        public decimal Amount { get; set; }
        public string? AccountNo { get; set; }
        public string? BankName { get; set; }
        public string? Description { get; set; }
    }

    public sealed class LeaveItem
    {
        public string? Id { get; set; }
        public string? EmployeeId { get; set; }
        public string? EmpName { get; set; }
        public string? LeaveType { get; set; }
        public decimal LeaveDays { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? Status { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class IssueItem
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Type { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public string? PartnerId { get; set; }
        public string? PartnerName { get; set; }
        public string? ParticipantId { get; set; }
        public string? ParticipantName { get; set; }
        public string? Memo { get; set; }
        public bool IsCompleted { get; set; }
    }
}
