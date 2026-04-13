namespace HitPan.Application.DTOs.Item;

public class ItemListDto
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

public class ItemDetailDto : ItemListDto
{
    public string? Barcode { get; set; }

    public string? Memo { get; set; }

    public int RowVersion { get; set; }
}

public class CreateItemDto
{
    public string ItemName { get; set; } = "";

    public string? ItemCode { get; set; }

    public string? ItemGroup { get; set; }

    public string ItemType { get; set; } = "product";

    public string Unit { get; set; } = "EA";

    public string? Spec { get; set; }

    public decimal PurchasePrice { get; set; }

    public decimal SalePrice { get; set; }

    public decimal StandardPrice { get; set; }

    public string TaxType { get; set; } = "taxable";

    public decimal SafetyStock { get; set; }

    public string? Barcode { get; set; }

    public string? Memo { get; set; }
}

public class UpdateItemDto : CreateItemDto
{
    public bool IsActive { get; set; } = true;

    public int RowVersion { get; set; }
}

public class ItemSpecialPriceDto
{
    public string PriceId { get; set; } = "";

    public string PartnerId { get; set; } = "";

    public string? PartnerName { get; set; }

    public string PriceType { get; set; } = "fixed";

    public decimal UnitPrice { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; } = true;
}

public class ItemGroupDto
{
    public string GroupId { get; set; } = "";

    public string GroupName { get; set; } = "";

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
