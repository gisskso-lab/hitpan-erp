using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Sales;

// 봉합 (2026-06-22, 11차전 수주재편집): 수주(draft) 재편집 전용 요청 DTO.
// 10차 P0-1이 신규저장만 api/sales/orders로 봉합 → 수정 경로 부재로 PUT api/sales/deliveries로
// 잘못 흘러 "거래명세서를 찾을 수 없습니다" 발생. CreateSalesOrderRequest와 동일 필드 구성.
public class UpdateSalesOrderRequest
{
    [Required]
    public string PartnerId { get; set; } = string.Empty;

    public DateTime OrderDate { get; set; }
    public string? Memo { get; set; }

    [Required]
    [MinLength(1)]
    public List<UpdateSalesOrderItemRequest> Items { get; set; } = new();
}

public class UpdateSalesOrderItemRequest
{
    [Required]
    public string ItemId { get; set; } = string.Empty;

    [Range(0.0001, double.MaxValue, ErrorMessage = "수량은 0보다 커야 합니다.")]
    public decimal OrderedQty { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "단가는 음수일 수 없습니다.")]
    public decimal UnitPrice { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "공급가액은 음수일 수 없습니다.")]
    public decimal SupplyAmount { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "부가세는 음수일 수 없습니다.")]
    public decimal VatAmount { get; set; }
}
