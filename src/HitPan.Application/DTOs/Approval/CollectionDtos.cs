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
