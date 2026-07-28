namespace HitPan.Application.DTOs.Settings;

/// <summary>
/// tenants 테이블에 반영할 사업장(회사) 기본 정보이다. 사용자정보설정 화면 저장용이다.
/// </summary>
public sealed class UpdateTenantCompanyDto
{
    /// <summary>상호(사용업체명).</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>대표자명.</summary>
    public string CeoName { get; set; } = string.Empty;

    /// <summary>사업자등록번호.</summary>
    public string BizNo { get; set; } = string.Empty;

    /// <summary>업태.</summary>
    public string? BizType { get; set; }

    /// <summary>업종(tenants.biz_item).</summary>
    public string? BizItem { get; set; }

    /// <summary>대표 전화(tenants.tel).</summary>
    public string? Tel { get; set; }

    /// <summary>팩스.</summary>
    public string? Fax { get; set; }

    /// <summary>이메일.</summary>
    public string? Email { get; set; }

    /// <summary>홈페이지 URL.</summary>
    public string? Homepage { get; set; }

    /// <summary>우편번호.</summary>
    public string? ZipCode { get; set; }

    /// <summary>기본 주소(도로명/지번 등).</summary>
    public string? Address { get; set; }

    /// <summary>상세주소(별도 컬럼이 없어 저장 시 기본 주소와 합쳐 tenants.address에 반영).</summary>
    public string? AddressDetail { get; set; }

    /// <summary>법인등록번호(tenants.corp_no).</summary>
    public string? CorpNo { get; set; }

    /// <summary>종사업장번호(tenants.subsidiary_no, 최대 4자).</summary>
    public string? SubsidiaryNo { get; set; }
}
