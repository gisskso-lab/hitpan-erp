namespace HitPan.Web.Models;

/// <summary>
/// PUT api/settings/company 요청 본문에 사용하는 사업장(tenants) 정보 모델이다.
/// </summary>
public sealed class TenantCompanyModel
{
    /// <summary>상호(사용업체명).</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>대표자명.</summary>
    public string CeoName { get; set; } = string.Empty;

    /// <summary>사업자등록번호.</summary>
    public string BizNo { get; set; } = string.Empty;

    /// <summary>업태.</summary>
    public string? BizType { get; set; }

    /// <summary>업종.</summary>
    public string? BizItem { get; set; }

    /// <summary>대표 전화.</summary>
    public string? Tel { get; set; }

    /// <summary>팩스.</summary>
    public string? Fax { get; set; }

    /// <summary>이메일.</summary>
    public string? Email { get; set; }

    /// <summary>홈페이지.</summary>
    public string? Homepage { get; set; }

    /// <summary>우편번호.</summary>
    public string? ZipCode { get; set; }

    /// <summary>기본 주소.</summary>
    public string? Address { get; set; }

    /// <summary>상세주소.</summary>
    public string? AddressDetail { get; set; }

    /// <summary>법인등록번호.</summary>
    public string? CorpNo { get; set; }

    /// <summary>종사업장번호.</summary>
    public string? SubsidiaryNo { get; set; }

    // ── 사업장 노무 정보 (작 2026-08-13, 그룹웨어 단계4 토대) ──
    // 서버 DTO(UpdateTenantCompanyDto)와 이름이 같아야 역직렬화된다.
    // 🔴 이름이 어긋나면 서버가 값을 줘도 화면이 못 받고, 저장 때도 조용히 빠져나간다
    //    (출력 이미지 3필드가 정확히 그 사고를 겪었다 — 바로 위 주석 참조).

    /// <summary>과세 유형: taxable 과세 / tax_free 면세. null = 미정.</summary>
    public string? TaxType { get; set; }

    /// <summary>상시근로자수. null = 미정 — 자동 계산하지 않는다.</summary>
    public int? RegularEmployeeCount { get; set; }

    /// <summary>법인/개인: corporate / individual. null = 미정.</summary>
    public string? BusinessEntityType { get; set; }

    /// <summary>상시근로자수 기준일.</summary>
    public DateTime? EmployeeCountAsOf { get; set; }

    // ── 출력 이미지 (DB-85) ──
    // 서버 DTO(UpdateTenantCompanyDto)와 이름이 같아야 역직렬화된다. 종전에는 이 세 필드가
    // 화면 모델에 없어, 서버가 값을 돌려줘도 화면이 받지 못하고 저장 때도 빠져나갔다.
    // 이미지 자체가 아니라 파일 경로만 담는다(컬럼 varchar(200)).

    /// <summary>로고 이미지 경로.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>인장 이미지 경로. 거래명세서·견적서 출력에 쓰인다.</summary>
    public string? SealUrl { get; set; }

    /// <summary>출력 헤더 이미지 경로.</summary>
    public string? HeaderUrl { get; set; }

    /// <summary>헌법 #35 (사장님 결재 2026-06-04) — 랜딩 자동 반영 잠금 (회사명·사업자번호·대표자명 변경 불가).</summary>
    public bool IsLockedFromLanding { get; set; }
}

public class TenantSettingsModel
{
    public string StockEvalMethod { get; set; } = "moving_avg";

    public bool UseMultiWarehouse { get; set; }

    public bool StockShortageAlert { get; set; } = true;

    public bool AllowMinusStock { get; set; }

    public string PriceInputType { get; set; } = "net";

    public bool AutoVatAdjust { get; set; } = true;

    public string VatRoundType { get; set; } = "round";

    public decimal PriceARate { get; set; } = 1.00m;

    public decimal PriceBRate { get; set; } = 1.10m;

    public decimal PriceCRate { get; set; } = 1.20m;

    public decimal PriceDRate { get; set; } = 1.30m;

    public decimal PriceERate { get; set; } = 1.50m;

    public bool UseCreditLimit { get; set; } = true;

    public decimal CreditLimitAmount { get; set; } = 1000000;

    public bool ShowPurchasePrice { get; set; }

    public bool UseSalesByEmployee { get; set; } = true;

    public bool AllowForcePriceInput { get; set; } = true;

    public bool AllowForceVatInput { get; set; }

    public bool AllowZeroPrice { get; set; }

    public bool AllowPastEdit { get; set; }

    public bool AllowForceStockAdjust { get; set; } = true;

    public bool AllowCreditOverride { get; set; }

    public int PriceDeviationLimit { get; set; } = 50;

    public bool ForceEditRequirePassword { get; set; } = true;

    public bool UsePersonalInfoProtect { get; set; } = true;

    public string IndustryType { get; set; } = "retail";
}
