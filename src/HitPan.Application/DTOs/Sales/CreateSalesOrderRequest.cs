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
    public decimal OrderedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}
