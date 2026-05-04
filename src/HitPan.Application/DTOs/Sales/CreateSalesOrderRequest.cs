using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Sales;

public class CreateSalesOrderRequest
{
    [Required]
    public string PartnerId { get; set; } = string.Empty;

    public string? EmployeeId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string? Memo { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateSalesOrderItemRequest> Items { get; set; } = new();
}

public class CreateSalesOrderItemRequest
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
