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

    /// <summary>지금 자재 재고로 몇 개까지 만들 수 있나 (20260825작1 W6).</summary>
    public decimal ProducibleQty { get; set; }

    /// <summary>제조 단계 — 자재=1, 반제품=2, 완제품=3… (20260825작1 W5). BomVersion 과 다른 것이다.</summary>
    public int BomLevel { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class BomDetailModel
{
    public string BomId { get; set; } = "";
    public string ProductItemId { get; set; } = "";
    public string ProductItemName { get; set; } = "";
    public string BomName { get; set; } = "";
    public int BomVersion { get; set; }
    public bool IsDefault { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string? Memo { get; set; }
    public List<BomItemModel> Items { get; set; } = new();
    public decimal TotalCost { get; set; }
}

public class BomItemModel
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
    public string? Memo { get; set; }
}

public class CreateBomModel
{
    public string ProductItemId { get; set; } = "";
    public string? ProductItemName { get; set; }   // 신규 완제품 자동 등록용
    public string BomName { get; set; } = "";
    public bool IsDefault { get; set; } = true;
    public string? Memo { get; set; }
    public List<CreateBomItemModel> Items { get; set; } = new();
}

public class CreateBomItemModel
{
    public int SeqNo { get; set; }
    public string MaterialItemId { get; set; } = "";
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "EA";
    public decimal LossRate { get; set; }
    public string? Memo { get; set; }
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

    // 사장님 헌법 (2026-04-26): 반제품 부족 → 자동발주 금지·즉시 반려.
    public string ItemType { get; set; } = "material";

    // items.auto_receive_on_order — 자동 사슬 vs 반자동 분기 키.
    public bool AutoReceiveOnOrder { get; set; }
}

public class BomAssembleCheckModel
{
    public string BomId { get; set; } = "";
    public decimal ProduceQty { get; set; }
    public List<BomMaterialCheckModel> Materials { get; set; } = new();
    public bool CanProduce { get; set; }
    public decimal TotalCost { get; set; }
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
