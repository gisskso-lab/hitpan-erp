using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Purchase;

public class CreateReceiptRequest
{
    public string? PoId { get; set; }

    [Required]
    public string PartnerId { get; set; } = string.Empty;

    public DateTime ReceiptDate { get; set; }
    public string? Memo { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateReceiptItemRequest> Items { get; set; } = new();
}

public class CreateReceiptItemRequest
{
    public string? PoItemId { get; set; }

    [Required]
    public string ItemId { get; set; } = string.Empty;

    [Required]
    public string WarehouseId { get; set; } = string.Empty;

    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}
