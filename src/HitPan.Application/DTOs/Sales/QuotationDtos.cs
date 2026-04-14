using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Sales;

/// <summary>
/// 견적서 목록 조회 DTO다.
/// </summary>
public class QuotationListDto
{
    /// <summary>
    /// 견적서 식별자다.
    /// </summary>
    public string QuoteId { get; set; } = string.Empty;

    /// <summary>
    /// 견적서 번호다.
    /// </summary>
    public string QuoteNo { get; set; } = string.Empty;

    /// <summary>
    /// 거래처 식별자다.
    /// </summary>
    public string PartnerId { get; set; } = string.Empty;

    /// <summary>
    /// 거래처명이다.
    /// </summary>
    public string PartnerName { get; set; } = string.Empty;

    /// <summary>
    /// 견적일자다.
    /// </summary>
    public DateTime QuoteDate { get; set; }

    /// <summary>
    /// 유효기한이다.
    /// </summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>
    /// 상태 문자열이다.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 공급가액이다.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// 부가세다.
    /// </summary>
    public decimal VatAmount { get; set; }
}

/// <summary>
/// 견적서 상세 조회 DTO다.
/// </summary>
public class QuotationDetailDto : QuotationListDto
{
    /// <summary>
    /// 테넌트 식별자다.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// 담당자 식별자다.
    /// </summary>
    public string? EmployeeId { get; set; }

    /// <summary>
    /// 메모다.
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// 전환 수주서 식별자다.
    /// </summary>
    public string? ConvertedOrderId { get; set; }

    /// <summary>
    /// 생성 일시다.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 생성자다.
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// 수정 일시다.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 수정자다.
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// 품목 목록이다.
    /// </summary>
    public List<QuotationItemDto> Items { get; set; } = new();
}

/// <summary>
/// 견적서 품목 DTO다.
/// </summary>
public class QuotationItemDto
{
    /// <summary>
    /// 품목 식별자다.
    /// </summary>
    [Required]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// 품목명이다.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// 규격이다.
    /// </summary>
    public string? Spec { get; set; }

    /// <summary>
    /// 단위다.
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// 수량이다.
    /// </summary>
    public decimal Qty { get; set; }

    /// <summary>
    /// 단가다.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// 할인율이다.
    /// </summary>
    public decimal DiscountRate { get; set; }

    /// <summary>
    /// 공급가액이다.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 부가세다.
    /// </summary>
    public decimal VatAmount { get; set; }

    /// <summary>
    /// 메모다.
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// 정렬 순서다.
    /// </summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// 견적서 생성 요청 DTO다.
/// </summary>
public class CreateQuotationRequest
{
    /// <summary>
    /// 거래처 식별자다.
    /// </summary>
    [Required]
    public string PartnerId { get; set; } = string.Empty;

    /// <summary>
    /// 담당자 식별자다.
    /// </summary>
    public string? EmployeeId { get; set; }

    /// <summary>
    /// 견적일자다.
    /// </summary>
    public DateTime QuoteDate { get; set; }

    /// <summary>
    /// 유효기한이다.
    /// </summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>
    /// 메모다.
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// 품목 목록이다.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<QuotationItemDto> Items { get; set; } = new();
}

/// <summary>
/// 견적서 수정 요청 DTO다.
/// </summary>
public class UpdateQuotationRequest
{
    /// <summary>
    /// 거래처 식별자다.
    /// </summary>
    [Required]
    public string PartnerId { get; set; } = string.Empty;

    /// <summary>
    /// 담당자 식별자다.
    /// </summary>
    public string? EmployeeId { get; set; }

    /// <summary>
    /// 견적일자다.
    /// </summary>
    public DateTime QuoteDate { get; set; }

    /// <summary>
    /// 유효기한이다.
    /// </summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>
    /// 상태다.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// 메모다.
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// 품목 목록이다.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<QuotationItemDto> Items { get; set; } = new();
}
