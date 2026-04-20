namespace HitPan.Web.Models;

// ── 결재 설정 ──

public class ApprovalSettingModel
{
    public string SettingId { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string DocTypeLabel { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public decimal ThresholdAmount { get; set; }
    public bool AutoApproveBelow { get; set; }
    public int MaxLines { get; set; } = 3;
}

public class SaveApprovalSettingModel
{
    public string DocType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public decimal ThresholdAmount { get; set; }
    public bool AutoApproveBelow { get; set; }
    public int MaxLines { get; set; } = 3;
}

// ── 결재 라인 ──

public class ApprovalLineModel
{
    public string LineId { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public int SeqNo { get; set; }
    public string ApproverId { get; set; } = string.Empty;
    public string ApproverName { get; set; } = string.Empty;
    public string? RoleLabel { get; set; }
    public string? DelegateId { get; set; }
    public string? DelegateName { get; set; }
    public DateTime? DelegateStart { get; set; }
    public DateTime? DelegateEnd { get; set; }
}

public class SaveApprovalLinesModel
{
    public string DocType { get; set; } = string.Empty;
    public List<ApprovalLineItemModel> Lines { get; set; } = new();
}

public class ApprovalLineItemModel
{
    public int SeqNo { get; set; }
    public string ApproverId { get; set; } = string.Empty;
    public string ApproverName { get; set; } = string.Empty;
    public string? RoleLabel { get; set; }
    public string? DelegateId { get; set; }
    public string? DelegateName { get; set; }
    public DateTime? DelegateStart { get; set; }
    public DateTime? DelegateEnd { get; set; }
}

// ── 결재 문서 ──

public class ApprovalDocumentModel
{
    public string ApprovalId { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string DocTypeLabel { get; set; } = string.Empty;
    public string RefId { get; set; } = string.Empty;
    public string? RefNo { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public int CurrentSeq { get; set; }
    public int TotalLines { get; set; }
    public string RequesterId { get; set; } = string.Empty;
    public string RequesterName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Memo { get; set; }
    public string? CurrentApproverName { get; set; }
}

public class ApprovalHistoryModel
{
    public string HistoryId { get; set; } = string.Empty;
    public int SeqNo { get; set; }
    public string ApproverId { get; set; } = string.Empty;
    public string ApproverName { get; set; } = string.Empty;
    public bool IsDelegated { get; set; }
    public string? OriginalApproverId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime ActedAt { get; set; }
}

public class ApprovalDetailModel
{
    public ApprovalDocumentModel Document { get; set; } = new();
    public List<ApprovalHistoryModel> History { get; set; } = new();
    public List<ApprovalLineModel> Lines { get; set; } = new();
}

// ── 월마감 ──

public class MonthlyClosingModel
{
    public string ClosingId { get; set; } = string.Empty;
    public string YearMonth { get; set; } = string.Empty;
    public string YearMonthLabel { get; set; } = string.Empty;
    public string Status { get; set; } = "open";
    public string StatusLabel { get; set; } = string.Empty;
    public DateTime? ClosedAt { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public string? ReopenReason { get; set; }
    public decimal SalesAmount { get; set; }
    public decimal PurchaseAmount { get; set; }
    public decimal ReceiptAmount { get; set; }
    public decimal PaymentAmount { get; set; }
    public string? Memo { get; set; }
}

// ── 수금·지급 ──

public class CollectionModel
{
    public string CollectionId { get; set; } = string.Empty;
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public DateTime CollectionDate { get; set; }
    public decimal Amount { get; set; }
    public string CollectionMethod { get; set; } = string.Empty;
    public string CollectionMethodLabel { get; set; } = string.Empty;
    public string? RefDocType { get; set; }
    public string? RefDocId { get; set; }
    public string? Memo { get; set; }
}

public class CreateCollectionModel
{
    public string PartnerId { get; set; } = string.Empty;
    public DateTime CollectionDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string CollectionMethod { get; set; } = "cash";
    public string? RefDocType { get; set; }
    public string? RefDocId { get; set; }
    public string? Memo { get; set; }
}

public class PaymentModel
{
    public string PaymentId { get; set; } = string.Empty;
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentMethodLabel { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    public string? RefOrderId { get; set; }
    public string? Memo { get; set; }
}

public class CreatePaymentModel
{
    public string PartnerId { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string PaymentType { get; set; } = "purchase";
    public string? RefOrderId { get; set; }
    public string? Memo { get; set; }
}
