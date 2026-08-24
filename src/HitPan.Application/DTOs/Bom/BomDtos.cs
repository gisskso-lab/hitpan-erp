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

    /// <summary>
    /// 지금 자재 재고로 <b>몇 개까지 만들 수 있나</b> (20260825작1 W6, 사장님 지시).
    /// 자재별 (현재고 ÷ 1개당 소요량) 중 <b>가장 작은 값</b> — 제일 모자란 자재가 결정한다.
    /// 창고는 합산한다. 자재가 없는 BOM 은 0.
    /// </summary>
    public decimal ProducibleQty { get; set; }

    /// <summary>
    /// <b>제조 단계</b> (20260825작1 W5, 사장님 지시). 사 오는 자재 = 1,
    /// 자재로 만든 반제품 = 2, 그 반제품을 쓰는 완제품 = 3 …
    /// <para>
    /// ⚠️ <c>BomVersion</c>(문서 개정 회차)과 <b>다른 것</b>이다. 섞지 마라.
    /// </para>
    /// </summary>
    public int BomLevel { get; set; }

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

/// <summary>
/// 자동발주 한 건의 결과 (20260825작1 W2). 화면이 <b>어떤 안내를 띄울지</b> 정하는 근거다.
/// </summary>
/// <remarks>
/// 🔴 사장님이 요구하신 안내가 두 가지라 <b>결과를 구분해서 돌려줘야</b> 한다:
/// 발주만 된 건은 <i>"매입처리 하셔야 재고에 반영됩니다"</i>,
/// 사슬까지 간 건은 <i>"매입처리까지 완료되어 재고에 반영되었습니다"</i>.
/// </remarks>
public class OrderAlertResultDto
{
    public string ItemName { get; set; } = "";

    /// <summary>발주서가 만들어졌나. 이게 false 면 아무 일도 안 일어났다.</summary>
    public bool OrderCreated { get; set; }

    /// <summary>매입확정까지 갔나 — 재고·장부에 실제로 올라갔다는 뜻.</summary>
    public bool ReceiptConfirmed { get; set; }

    /// <summary>
    /// 사슬을 안 탄 이유. 🔴 사람에게 그대로 보여주는 글이라 <b>개발용어를 쓰지 않는다.</b>
    /// 발주만 하기로 한 정상 경우(스위치 꺼짐)엔 비운다 — 이유를 댈 일이 아니다.
    /// </summary>
    public string? ChainSkippedReason { get; set; }
}
