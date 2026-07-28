using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Purchase;

// 매입반품 신규 작성 — P0 #1 (헌법 #20 흐름 끊김 봉합).
// receipt_id 없이 발행 가능 (창고 청소·재고 정리 시 별도 반품).
public class CreatePurchaseReturnRequest
{
    public string? ReceiptId { get; set; }

    [Required]
    public string PartnerId { get; set; } = string.Empty;

    public DateTime ReturnDate { get; set; }
    public string? Memo { get; set; }

    // 작지③ 반품 전용 컬럼 (사장님 작업지시 2026-05-31)
    // defect·wrong_item·over_qty·customer_cancel·etc
    public string? ReturnReason { get; set; }
    [MaxLength(500)]
    public string? ReturnReasonMemo { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreatePurchaseReturnItemRequest> Items { get; set; } = new();
}

public class CreatePurchaseReturnItemRequest
{
    [Required]
    public string ItemId { get; set; } = string.Empty;

    public string? WarehouseId { get; set; }

    [Range(0.0001, double.MaxValue, ErrorMessage = "수량은 0보다 커야 합니다.")]
    public decimal Qty { get; set; }

    [Range(0.0001, double.MaxValue, ErrorMessage = "단가는 0보다 커야 합니다.")]
    public decimal UnitPrice { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "공급가액은 음수일 수 없습니다.")]
    public decimal SupplyAmount { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "부가세는 음수일 수 없습니다.")]
    public decimal VatAmount { get; set; }
}

// draft 상태 매입반품 수정 — P0 #1.
public class UpdatePurchaseReturnRequest
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
    public List<CreatePurchaseReturnItemRequest> Items { get; set; } = new();
}
