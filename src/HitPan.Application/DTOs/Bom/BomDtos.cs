using System.ComponentModel.DataAnnotations;

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
    public bool AutoReceiveOnOrder { get; set; }
    public string ItemType { get; set; } = "material";
    public string? Memo { get; set; }
    public bool HasChildBom { get; set; }
}

public class CreateBomDto
{
    // 기존 상품에 BOM을 덧붙이는 경우 선택. 신규 완제품으로 등록하려면 비워둔다.
    public string ProductItemId { get; set; } = "";

    // 신규 완제품명. 이 값이 있고 ProductItemId가 비어있으면 서비스가 items INSERT 후 연결.
    // 사장님 지시 흐름: "BOM 생성 → 상품등록 확인 → 상품마스터 반영".
    public string? ProductItemName { get; set; }

    public string BomName { get; set; } = "";
    public bool IsDefault { get; set; } = true;
    public string? Memo { get; set; }
    public List<CreateBomItemDto> Items { get; set; } = new();
}

public class CreateBomItemDto
{
    public int SeqNo { get; set; }
    public string MaterialItemId { get; set; } = "";

    [Range(0.0001, double.MaxValue, ErrorMessage = "수량은 0보다 커야 합니다.")]
    public decimal Qty { get; set; }

    public string Unit { get; set; } = "EA";

    [Range(0, 100, ErrorMessage = "로스율은 0~100 사이여야 합니다.")]
    public decimal LossRate { get; set; }

    public string? Memo { get; set; }
}

public class BomAssembleDto
{
    public string BomId { get; set; } = "";

    [Range(0.0001, double.MaxValue, ErrorMessage = "생산 수량은 0보다 커야 합니다.")]
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

    // 사장님 헌법 (2026-04-26): 반제품 부족 시 자동발주 금지·즉시 반려.
    public string ItemType { get; set; } = "material";

    // items.auto_receive_on_order — 부족 자재 모두 Y면 자동 사슬, 하나라도 N이면 반자동.
    public bool AutoReceiveOnOrder { get; set; }
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
