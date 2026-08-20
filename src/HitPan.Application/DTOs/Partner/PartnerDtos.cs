using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Partner;

public class PartnerListDto
{
    public string PartnerId { get; set; } = "";

    public string PartnerCode { get; set; } = "";

    public string PartnerName { get; set; } = "";

    public string PartnerType { get; set; } = "";

    public string? BizNo { get; set; }

    public string? CeoName { get; set; }

    public string? Tel { get; set; }

    public string? Email { get; set; }

    public string? ManagerName { get; set; }

    public string PriceGrade { get; set; } = "A";

    public decimal CreditLimit { get; set; }

    public decimal Balance { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}

public class PartnerDetailDto : PartnerListDto
{
    public string? BizType { get; set; }

    public string? BizItem { get; set; }

    public string? Fax { get; set; }

    public string? ZipCode { get; set; }

    public string? Address { get; set; }

    public string? AddressDetail { get; set; }

    // 20260821작1 W1: 카카오맵·내비 딥링크용 좌표. NULL 이면 주소 방식으로 폴백한다 (§#20).
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public string? ManagerTel { get; set; }

    public string TaxType { get; set; } = "taxable";

    public int PaymentTerms { get; set; } = 30;

    public string? Memo { get; set; }

    public int RowVersion { get; set; }
}

public class CreatePartnerDto
{
    [Required]
    [MaxLength(100)]
    public string PartnerName { get; set; } = "";

    [MaxLength(20)]
    public string PartnerType { get; set; } = "both";

    [MaxLength(20)]
    public string? PartnerCode { get; set; }

    [MaxLength(12)]
    public string? BizNo { get; set; }

    [MaxLength(50)]
    public string? CeoName { get; set; }

    [MaxLength(50)]
    public string? BizType { get; set; }

    [MaxLength(50)]
    public string? BizItem { get; set; }

    [MaxLength(20)]
    public string? Tel { get; set; }

    [MaxLength(20)]
    public string? Fax { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    [MaxLength(200)]
    public string? Address { get; set; }

    [MaxLength(200)]
    public string? AddressDetail { get; set; }

    // 20260821작1 W1: 좌표. 저장 시 서버가 주소로 채워 넣는다 (반자동 — 사람이 수정 가능).
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? ManagerName { get; set; }

    [MaxLength(20)]
    public string? ManagerTel { get; set; }

    public decimal CreditLimit { get; set; }

    [MaxLength(1)]
    public string PriceGrade { get; set; } = "A";

    [MaxLength(20)]
    public string TaxType { get; set; } = "taxable";

    public int PaymentTerms { get; set; } = 30;

    [MaxLength(500)]
    public string? Memo { get; set; }
}

public class UpdatePartnerDto : CreatePartnerDto
{
    public bool IsActive { get; set; } = true;

    public int RowVersion { get; set; }
}

public class PartnerSpecialPriceDto
{
    public string PriceId { get; set; } = "";

    public string ItemId { get; set; } = "";

    public string? ItemName { get; set; }

    public string PriceType { get; set; } = "fixed";

    public decimal UnitPrice { get; set; }

    // 봉합 (2026-06-23, 19차 업체특별단가 할인율): 상품 특별단가(ItemSpecialPriceDto.DiscountRate)와 대칭.
    //   price_type='discount' 일 때 할인율(%), 고정모드는 null. 종전엔 이 필드가 없어 업체 특별단가의
    //   할인율 모드가 화면·DTO·서비스 전 계층에서 통째 유실됐다(상품은 되는데 업체는 안 되는 비대칭).
    public decimal? DiscountRate { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; } = true;
}
