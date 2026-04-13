namespace HitPan.Web.Models;

public class BomListModel
{
    public string BomId { get; set; } = "";
    public string ProductItemId { get; set; } = "";
    public string ProductItemName { get; set; } = "";
    public string BomName { get; set; } = "";
    public int BomVersion { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int MaterialCount { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BomMaterialCheckModel
{
    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "EA";
    public decimal RequiredQty { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal ShortageQty { get; set; }
    public bool IsEnough { get; set; }
    public string? AutoOrderPartnerId { get; set; }
    public string? AutoOrderPartnerName { get; set; }
    public decimal AutoOrderQty { get; set; }
    public bool AutoOrderEnabled { get; set; }
}

public class StockAlertModel
{
    public string AlertId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string AlertType { get; set; } = "";
    public decimal CurrentQty { get; set; }
    public decimal SafetyQty { get; set; }
    public decimal ShortageQty { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public decimal OrderQty { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
