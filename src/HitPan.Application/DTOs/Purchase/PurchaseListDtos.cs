namespace HitPan.Application.DTOs.Purchase;

/// <summary>
/// 발주 목록 조회 응답 DTO.
/// </summary>
public class PurchaseOrderListDto
{
    public string PoId { get; set; } = string.Empty;
    public string PoNo { get; set; } = string.Empty;
    public DateTime PoDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

/// <summary>
/// 매입명세 목록 조회 응답 DTO.
/// </summary>
public class PurchaseReceiptListDto
{
    public string ReceiptId { get; set; } = string.Empty;
    public string ReceiptNo { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public class PurchaseReturnListDto
{
    public string ReturnId { get; set; } = string.Empty;
    public string ReturnNo { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
}
