using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Purchase;

public class CreatePurchaseOrderRequest
{
    [Required]
    public string PartnerId { get; set; } = string.Empty;

    public string? EmployeeId { get; set; }
    public DateTime PoDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string? Memo { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreatePurchaseOrderItemRequest> Items { get; set; } = new();
}

public class CreatePurchaseOrderItemRequest
{
    [Required]
    public string ItemId { get; set; } = string.Empty;

    public decimal OrderedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string? WarehouseId { get; set; }
}
