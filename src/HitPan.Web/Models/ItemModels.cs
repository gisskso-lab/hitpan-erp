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

    // 사장님 헌법 (2026-04-26): 자동발주 시 매입확정까지 자동 사슬.
    public bool AutoReceiveOnOrder { get; set; }

    // 20260821작1 W6 (사장님 결재 A안): 품목 기본창고.
    //   종전엔 화면 변수(_selectedWarehouseId)로만 있고 저장되지 않았다.
    public string? DefaultWarehouseId { get; set; }

    public string? Barcode { get; set; }

    public string? Memo { get; set; }

    public int RowVersion { get; set; }
}

/// <summary>창고 조회용 모델 (상품 상세 등에서 사용)</summary>
public class WarehouseItem
{
    public string WarehouseId { get; set; } = "";
    public string WhCode { get; set; } = "";
    public string WhName { get; set; } = "";
    public string WhType { get; set; } = "";
    public string? Location { get; set; }
    public bool IsActive { get; set; }
}

public class ItemSpecialPriceModel
{
    public string PriceId { get; set; } = "";
    public string PartnerId { get; set; } = "";
    public string? PartnerName { get; set; }
    public string PriceType { get; set; } = "fixed";
    public decimal UnitPrice { get; set; }
    /// <summary>할인율(%) — PriceType='discount' 일 때 사용.</summary>
    public decimal? DiscountRate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}
