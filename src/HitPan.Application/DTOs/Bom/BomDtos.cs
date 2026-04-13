namespace HitPan.Application.DTOs.Bom;

public class BomListDto
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

public class BomDetailDto
{
    public string BomId { get; set; } = "";
    public string ProductItemId { get; set; } = "";
    public string ProductItemName { get; set; } = "";
    public string BomName { get; set; } = "";
    public int BomVersion { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public string? Memo { get; set; }
    public List<BomItemDto> Items { get; set; } = new();
    public decimal TotalCost { get; set; }
}

public class BomItemDto
{
    public string BomItemId { get; set; } = "";
    public int SeqNo { get; set; }
    public string MaterialItemId { get; set; } = "";
    public string MaterialItemName { get; set; } = "";
    public string? Spec { get; set; }
    public string Unit { get; set; } = "EA";
    public decimal Qty { get; set; }
    public decimal LossRate { get; set; }
    public decimal ActualQty { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal SafetyStock { get; set; }
    public bool AutoOrderEnabled { get; set; }
    public string? AutoOrderPartnerId { get; set; }
    public decimal AutoOrderQty { get; set; }
    public string? Memo { get; set; }
    public bool HasChildBom { get; set; }
}

public class CreateBomDto
{
    public string ProductItemId { get; set; } = "";
    public string BomName { get; set; } = "";
    public bool IsDefault { get; set; } = true;
    public string? Memo { get; set; }
    public List<CreateBomItemDto> Items { get; set; } = new();
}

public class CreateBomItemDto
{
    public int SeqNo { get; set; }
    public string MaterialItemId { get; set; } = "";
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "EA";
    public decimal LossRate { get; set; }
    public string? Memo { get; set; }
}

public class BomAssembleDto
{
    public string BomId { get; set; } = "";
    public decimal ProduceQty { get; set; }
    public string? Memo { get; set; }
}

public class BomAssembleCheckDto
{
    public string BomId { get; set; } = "";
    public decimal ProduceQty { get; set; }
    public List<BomMaterialCheckDto> Materials { get; set; } = new();
    public bool CanProduce { get; set; }
    public decimal TotalCost { get; set; }
}

public class BomMaterialCheckDto
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

public class StockAlertDto
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
