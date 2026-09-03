namespace HitPan.Application.DTOs.Sales;

// 매출반품 목록/상세 DTO — 13차 후순위 봉합(2026-06-22, A 매입반품 대칭).
// 매입반품(PurchaseReturnListDto/DetailDto)의 거울 — ReceiptId 자리에 DeliveryId.

public class SalesReturnListDto
{
    /// <summary>전표를 작성한 사원 이름이다 (created_by = user_id 조인, 20260825작5).</summary>
    public string? CreatedByName { get; set; }

    public string ReturnId { get; set; } = string.Empty;
    public string ReturnNo { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }

    /// <summary>
    /// 🔴 원 거래명세서 FK — 20260903작16. <b>목록에서 원전표 아래에 붙이는 근거</b>.
    /// </summary>
    /// <remarks>
    /// 사장님 결재 3-4(20260828): <i>목록 조회에서 원전표 바로 아래에 (−) 반품이 보여야 한다</i>
    /// — 그 배치가 그대로 <b>경리의 대사 화면</b>이 된다.
    /// ⚠️ NULL 가능(원 명세서 없이 직접 작성한 반품) ⇒ 화면은 그냥 제 날짜 자리에 둔다.
    /// </remarks>
    public string? DeliveryId { get; set; }
}

public class SalesReturnDetailDto
{
    public string ReturnId { get; set; } = string.Empty;
    public string ReturnNo { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string? DeliveryId { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public string? ReturnReason { get; set; }
    public string? ReturnReasonMemo { get; set; }
    public List<SalesReturnDetailItemDto> Items { get; set; } = new();
}

public class SalesReturnDetailItemDto
{
    public string ReturnItemId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public string? WarehouseId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }

    /// <summary>원 판매 줄 연결 (20260825작7) — 다시 열어 고쳐도 링크가 살아 있게 한다.</summary>
    public string? DeliveryItemId { get; set; }

    /// <summary>파손 로스 여부 (20260825작6) — true 면 확정해도 재고에 안 들어갔다.</summary>
    public bool IsLoss { get; set; }
}
