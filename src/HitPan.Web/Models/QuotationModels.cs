namespace HitPan.Web.Models;

/// <summary>
/// 견적서 라인 그리드 밀도 옵션이다.
/// </summary>
public enum QuotationGridDensity
{
    /// <summary>조밀</summary>
    Compact,
    /// <summary>기본</summary>
    Normal,
    /// <summary>넉넉</summary>
    Comfortable
}

/// <summary>
/// 견적서 라인 모델이다.
/// </summary>
public sealed class QuotationLineModel
{
    /// <summary>행 번호다.</summary>
    public int No { get; set; }

    /// <summary>플레이스홀더 여부다.</summary>
    public bool IsPlaceholder { get; set; }

    /// <summary>선택 여부다.</summary>
    public bool IsSelected { get; set; }

    /// <summary>품목 식별자다.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>품목명이다.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>규격이다.</summary>
    public string Spec { get; set; } = string.Empty;

    /// <summary>단위다.</summary>
    public string Unit { get; set; } = "EA";

    /// <summary>수량 원본 필드다.</summary>
    public decimal Quantity { get; set; }

    /// <summary>수량 별칭이다.</summary>
    public decimal Qty
    {
        get => Quantity;
        set => Quantity = value;
    }

    /// <summary>단가다.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>할인율(%)이다.</summary>
    public decimal DiscountRate { get; set; }

    /// <summary>공급가액이다.</summary>
    public decimal Amount { get; set; }

    /// <summary>부가세액이다.</summary>
    public decimal VatAmount { get; set; }

    /// <summary>비고다.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>정렬 순서다.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 수량/단가/할인율 기반 금액을 재계산한다.
    /// </summary>
    public void RecalculateAmount()
    {
        var discountRatio = 1m - (DiscountRate / 100m);
        if (discountRatio < 0m)
        {
            discountRatio = 0m;
        }

        Amount = decimal.Round(Qty * UnitPrice * discountRatio, 2, MidpointRounding.AwayFromZero);
        VatAmount = decimal.Round(Amount * 0.1m, 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// 견적서 작성 초안 모델이다.
/// </summary>
public sealed class QuotationDraftModel
{
    /// <summary>문서 식별자다.</summary>
    public string? Id { get; set; }

    /// <summary>문서번호(견적번호)다.</summary>
    public string? DocumentNumber { get; set; }

    /// <summary>견적일자다.</summary>
    public DateTime QuoteDate { get; set; }

    /// <summary>유효기한이다.</summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>거래처 식별자다.</summary>
    public string? PartnerId { get; set; }

    /// <summary>거래처명이다.</summary>
    public string SalesCompany { get; set; } = string.Empty;

    /// <summary>담당자 식별자다.</summary>
    public string? EmployeeId { get; set; }

    /// <summary>메모다.</summary>
    public string? Memo { get; set; }

    /// <summary>상태다.</summary>
    public string Status { get; set; } = "draft";

    /// <summary>문서 타입명이다.</summary>
    public string DocumentType { get; set; } = "견적서";

    /// <summary>라인 목록이다.</summary>
    public List<QuotationLineModel> Lines { get; set; } = new();

    /// <summary>그리드 밀도다.</summary>
    public QuotationGridDensity Density { get; set; } = QuotationGridDensity.Normal;
}

/// <summary>
/// 견적서 요약 모델이다.
/// </summary>
public sealed class QuotationSummaryModel
{
    /// <summary>공급가액 합계다.</summary>
    public decimal SupplyAmount { get; set; }

    /// <summary>부가세 합계다.</summary>
    public decimal VatAmount { get; set; }

    /// <summary>총합계다.</summary>
    public decimal TotalAmount { get; set; }
}

/// <summary>
/// 견적서 목록 행 모델이다.
/// </summary>
public sealed class QuotationListItem
{
    /// <summary>견적서 식별자다.</summary>
    public string QuoteId { get; set; } = string.Empty;

    /// <summary>견적서 번호다.</summary>
    public string QuoteNo { get; set; } = string.Empty;

    /// <summary>거래처명이다.</summary>
    public string PartnerName { get; set; } = string.Empty;

    /// <summary>견적일자다.</summary>
    public DateTime QuoteDate { get; set; }

    /// <summary>유효기한이다.</summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>상태다.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>공급가액이다.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>부가세다.</summary>
    public decimal VatAmount { get; set; }
}
