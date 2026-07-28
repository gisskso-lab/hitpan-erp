using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Sales;

public class CreateDeliveryRequest
{
    public string? OrderId { get; set; }

    [Required]
    public string PartnerId { get; set; } = string.Empty;

    public string? EmployeeId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public string? Memo { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateDeliveryItemRequest> Items { get; set; } = new();
}

public class CreateDeliveryItemRequest
{
    public string? OrderItemId { get; set; }

    /// <summary>비어 있으면 <see cref="ItemName"/>으로 품목 마스터에서 첫 매칭.</summary>
    public string ItemId { get; set; } = string.Empty;

    public string? ItemName { get; set; }

    /// <summary>비어 있으면 테넌트 기본 창고(첫 번째 활성 창고).</summary>
    public string WarehouseId { get; set; } = string.Empty;

    [Range(0.0001, double.MaxValue, ErrorMessage = "수량은 0보다 커야 합니다.")]
    public decimal Qty { get; set; }

    [Range(0.0001, double.MaxValue, ErrorMessage = "단가는 0보다 커야 합니다.")]
    public decimal UnitPrice { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "공급가액은 음수일 수 없습니다.")]
    public decimal SupplyAmount { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "부가세는 음수일 수 없습니다.")]
    public decimal VatAmount { get; set; }
}
