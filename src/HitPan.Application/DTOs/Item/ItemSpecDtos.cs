using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Item;

// 상품 규격 1:N (사장님 작업지시 2026-05-31 — 그리드 콤보박스 변환)
// items.spec 단일 컬럼 호환 유지 + 다중 규격은 item_specs 저장
public class ItemSpecDto
{
    public string SpecId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string SpecValue { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CreateItemSpecRequest
{
    [Required]
    [MaxLength(100)]
    public string SpecValue { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
}

public class UpdateItemSpecRequest
{
    [Required]
    [MaxLength(100)]
    public string SpecValue { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
