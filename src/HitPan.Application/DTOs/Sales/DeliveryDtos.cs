namespace HitPan.Application.DTOs.Sales;

public class DeliveryListDto
{
    public string DeliveryId { get; set; } = string.Empty;
    public string DeliveryNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public class DeliveryDetailDto : DeliveryListDto
{
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public List<DeliveryItemDto> Items { get; set; } = new();
    public decimal PrevReceivable { get; set; }
    public decimal TodaySales { get; set; }
    public decimal TodayReceipt { get; set; }
}

public class DeliveryItemDto
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public string? Memo { get; set; }
    public int RowNo { get; set; }
}

public class UpdateDeliveryDto
{
    public DateTime OrderDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? Memo { get; set; }
    public List<DeliveryItemDto> Items { get; set; } = new();
}

public class PartnerSearchDto
{
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string? BizNo { get; set; }
    public string? Tel { get; set; }
    public string? Address { get; set; }
}
