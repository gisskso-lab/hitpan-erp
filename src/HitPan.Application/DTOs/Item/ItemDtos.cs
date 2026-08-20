using System.ComponentModel.DataAnnotations;

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
    public bool AutoOrderEnabled { get; set; }

    public string? AutoOrderPartnerId { get; set; }

    public decimal AutoOrderQty { get; set; }

    // 사장님 헌법 (2026-04-26): 자동발주 시 매입확정까지 자동 사슬.
    public bool AutoReceiveOnOrder { get; set; }

    // 20260821작1 W6 (사장님 결재 A안): 품목 기본창고.
    //   종전엔 화면에 드롭다운만 있고 저장되지 않아 설정해도 발주·매입이 그대로였다.
    public string? DefaultWarehouseId { get; set; }

    public string? Barcode { get; set; }

    public string? Memo { get; set; }

    public int RowVersion { get; set; }
}

public class CreateItemDto
{
    [Required]
    [MaxLength(100)]
    public string ItemName { get; set; } = "";

    [MaxLength(30)]
    public string? ItemCode { get; set; }

    [MaxLength(50)]
    public string? ItemGroup { get; set; }

    [MaxLength(20)]
    public string ItemType { get; set; } = "product";

    [MaxLength(10)]
    public string Unit { get; set; } = "EA";

    [MaxLength(100)]
    public string? Spec { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "매입단가는 음수일 수 없습니다.")]
    public decimal PurchasePrice { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "판매단가는 음수일 수 없습니다.")]
    public decimal SalePrice { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "표준단가는 음수일 수 없습니다.")]
    public decimal StandardPrice { get; set; }

    public string TaxType { get; set; } = "taxable";

    [Range(0, double.MaxValue, ErrorMessage = "안전재고는 음수일 수 없습니다.")]
    public decimal SafetyStock { get; set; }

    public bool AutoOrderEnabled { get; set; }

    public string? AutoOrderPartnerId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "자동발주수량은 음수일 수 없습니다.")]
    public decimal AutoOrderQty { get; set; }

    // 사장님 헌법 (2026-04-26): 자동발주 시 매입확정까지 자동 사슬.
    public bool AutoReceiveOnOrder { get; set; }

    // 20260821작1 W6 (사장님 결재 A안): 품목 기본창고.
    //   빈 값이면 테넌트 기본창고(MAIN 우선) 폴백이 받는다 — 끊기지 않는다 (§#20).
    [MaxLength(36)]
    public string? DefaultWarehouseId { get; set; }

    [MaxLength(50)]
    public string? Barcode { get; set; }

    [MaxLength(500)]
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

    /// <summary>할인율(%) — PriceType='discount' 일 때 사용. 그 외엔 null.</summary>
    public decimal? DiscountRate { get; set; }

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
