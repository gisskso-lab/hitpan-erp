namespace HitPan.Web.Models;

public class ItemListModel
{
    public string ItemId { get; set; } = "";

    public string ItemCode { get; set; } = "";

    public string ItemName { get; set; } = "";

    public string? ItemGroup { get; set; }

    public string ItemType { get; set; } = "product";

    public string Unit { get; set; } = "EA";

    public string? Spec { get; set; }

    public decimal SalePrice { get; set; }

    public decimal PurchasePrice { get; set; }

    public decimal StandardPrice { get; set; }

    public decimal CurrentStock { get; set; }

    public decimal SafetyStock { get; set; }

    public string TaxType { get; set; } = "taxable";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}

public class ItemDetailModel : ItemListModel
{
    public bool AutoOrderEnabled { get; set; }

    public string? AutoOrderPartnerId { get; set; }

    public decimal AutoOrderQty { get; set; }

    public string? Barcode { get; set; }

    public string? Memo { get; set; }

    public int RowVersion { get; set; }
}
