using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Sales;

// 매출반품 신규 작성 — 13차 후순위 봉합(2026-06-22, A 매입반품 대칭 풀스택).
// 고객이 판매분을 돌려보내는 반품. 확정 시 재고 IN(증가) + 매출 역분개(매출↓·외상매출금↓).
// 매입반품(CreatePurchaseReturnRequest)의 정확한 거울 — receipt_id 자리에 delivery_id.
// delivery_id 없이도 발행 가능(거래명세서 연결 없는 단독 반품).
public class CreateSalesReturnRequest
{
    public string? DeliveryId { get; set; }

    [Required]
    public string PartnerId { get; set; } = string.Empty;

    public DateTime ReturnDate { get; set; }
    public string? Memo { get; set; }

    // 반품 사유 (매입반품과 동일 체계): defect·wrong_item·over_qty·customer_cancel·etc
    public string? ReturnReason { get; set; }
    [MaxLength(500)]
    public string? ReturnReasonMemo { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateSalesReturnItemRequest> Items { get; set; } = new();
}

public class CreateSalesReturnItemRequest
{
    [Required]
    public string ItemId { get; set; } = string.Empty;

    // 원거래명세서 라인 연결 (단독 반품이면 null)
    public string? DeliveryItemId { get; set; }

    public string? WarehouseId { get; set; }

    [Range(0.0001, double.MaxValue, ErrorMessage = "수량은 0보다 커야 합니다.")]
    public decimal Qty { get; set; }

    [Range(0.0001, double.MaxValue, ErrorMessage = "단가는 0보다 커야 합니다.")]
    public decimal UnitPrice { get; set; }

    // 원판매 단가 (반품 단가와 다를 수 있어 별도 보관 — sales_return_items.original_unit_price)
    public decimal? OriginalUnitPrice { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "공급가액은 음수일 수 없습니다.")]
    public decimal SupplyAmount { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "부가세는 음수일 수 없습니다.")]
    public decimal VatAmount { get; set; }

    /// <summary>파손 로스 여부 — true 면 재고에 반영하지 않는다 (20260825작6).</summary>
    /// <remarks>
    /// 사장님 정의(2026-08-25): <i>"파손이면 로스로 정의, 파손이 아니면 재입고(재고반영)"</i>.
    /// <b>줄 단위</b>인 이유 — 한 반품에 정상품과 파손품이 섞여 온다.
    /// <b>판정은 고객사가 한다</b> — 반품사유로 자동 판정하지 않는다(사장님 지시).
    /// 로스여도 <b>매출·미수 차감은 그대로</b>다 — 물건은 못 쓰지만 돈은 돌려주기 때문이다.
    /// </remarks>
    public bool IsLoss { get; set; }
}

// draft 상태 매출반품 수정.
public class UpdateSalesReturnRequest
{
    [Required]
    public string PartnerId { get; set; } = string.Empty;

    public DateTime ReturnDate { get; set; }
    public string? Memo { get; set; }

    public string? ReturnReason { get; set; }
    [MaxLength(500)]
    public string? ReturnReasonMemo { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateSalesReturnItemRequest> Items { get; set; } = new();
}
