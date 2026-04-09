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

    [Required]
    public string ItemId { get; set; } = string.Empty;

    [Required]
    public string WarehouseId { get; set; } = string.Empty;

    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}
