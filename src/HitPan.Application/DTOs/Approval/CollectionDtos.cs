namespace HitPan.Application.DTOs.Approval;

/// <summary>수금 목록 DTO</summary>
public class CollectionListDto
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

/// <summary>수금 등록 요청</summary>
public class CreateCollectionRequest
{
    public string PartnerId { get; set; } = string.Empty;
    public DateTime CollectionDate { get; set; }
    public decimal Amount { get; set; }
    public string CollectionMethod { get; set; } = "cash";
    public string? RefDocType { get; set; }
    public string? RefDocId { get; set; }
    public string? Memo { get; set; }
}

/// <summary>지급 목록 DTO (기존 payments 테이블 기반)</summary>
public class PaymentListDto
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

/// <summary>지급 등록 요청</summary>
public class CreatePaymentRequest
{
    public string PartnerId { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string PaymentType { get; set; } = "purchase";
    public string? RefOrderId { get; set; }
    public string? Memo { get; set; }
}

// ═══════════════════════════════════════════
// 미수/미지급 정공법 (WS-20260427-04, 사장님 헌법 §20)
// ═══════════════════════════════════════════

/// <summary>거래처별 미수금 요약 (수금 메뉴 상단 표)</summary>
public class ReceivableSummaryDto
{
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal Outstanding { get; set; }
    public decimal Aging0_30 { get; set; }
    public decimal Aging31_60 { get; set; }
    public decimal Aging61_90 { get; set; }
    public decimal Aging90Plus { get; set; }
    public bool IsOverdue { get; set; }
    public int DocumentCount { get; set; }
}

/// <summary>거래처별 미수금 전표 한 건 (펼침 시 노출)</summary>
public class ReceivableDocumentDto
{
    public string PartnerId { get; set; } = string.Empty;
    public string DeliveryId { get; set; } = string.Empty;
    public string DeliveryNo { get; set; } = string.Empty;
    public DateTime DeliveryDate { get; set; }
    public decimal TotalWithVat { get; set; }
    public decimal Collected { get; set; }
    public decimal Outstanding { get; set; }
    public string AgingBucket { get; set; } = string.Empty;
}

/// <summary>미수금 응답 (요약 + 전표 한 묶음)</summary>
public class ReceivablesResponseDto
{
    public List<ReceivableSummaryDto> Summary { get; set; } = new();
    public List<ReceivableDocumentDto> Documents { get; set; } = new();
}

/// <summary>거래처별 미지급금 요약 (지급 메뉴 상단 표)</summary>
public class PayableSummaryDto
{
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal Outstanding { get; set; }
    public decimal Aging0_30 { get; set; }
    public decimal Aging31_60 { get; set; }
    public decimal Aging61_90 { get; set; }
    public decimal Aging90Plus { get; set; }
    public bool IsOverdue { get; set; }
    public int DocumentCount { get; set; }
}

/// <summary>거래처별 미지급금 전표 한 건 (펼침 시 노출)</summary>
public class PayableDocumentDto
{
    public string PartnerId { get; set; } = string.Empty;
    public string ReceiptId { get; set; } = string.Empty;
    public string ReceiptNo { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public decimal TotalWithVat { get; set; }
    public decimal Paid { get; set; }
    public decimal Outstanding { get; set; }
    public string AgingBucket { get; set; } = string.Empty;
}

/// <summary>미지급금 응답</summary>
public class PayablesResponseDto
{
    public List<PayableSummaryDto> Summary { get; set; } = new();
    public List<PayableDocumentDto> Documents { get; set; } = new();
}
