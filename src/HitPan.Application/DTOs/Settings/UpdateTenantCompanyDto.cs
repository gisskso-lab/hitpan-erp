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

    /// <summary>
    /// 상세주소(동·호수 등). DB-85 부터 <c>local_company.address_detail</c> 에 <b>분리 저장</b>한다.
    /// </summary>
    /// <remarks>
    /// 종전에는 별도 컬럼이 없어 기본주소와 합쳐 한 칸에 넣었고, 조회는 상세주소를 안 읽어
    /// 저장할 때마다 주소가 중복 누적됐다(2026-08-09 사장님 지적 · 작4 ⑤번 봉합).
    /// </remarks>
    public string? AddressDetail { get; set; }

    /// <summary>법인등록번호(tenants.corp_no).</summary>
    public string? CorpNo { get; set; }

    /// <summary>종사업장번호(tenants.subsidiary_no, 최대 4자).</summary>
    public string? SubsidiaryNo { get; set; }

    // ── 출력 이미지 (DB-85) ──
    // 이미지 자체(base64)가 아니라 파일 경로만 담는다. 회사 정보 한 행이 수 MB 가 되면
    // 조회마다 그 무게를 지불하게 된다. 실제 파일은 고객 PC 로컬에 둔다(헌법 #30).

    /// <summary>로고 이미지 경로.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>인장 이미지 경로. 거래명세서·견적서 출력에 쓰인다.</summary>
    public string? SealUrl { get; set; }

    /// <summary>출력 헤더 이미지 경로.</summary>
    public string? HeaderUrl { get; set; }

    /// <summary>
    /// 랜딩 가입으로 사업자등록증이 자동 반영돼 회사명·사업자번호·대표자명이 잠겼는지 여부(읽기 전용).
    /// </summary>
    /// <remarks>
    /// 🔴 조회 응답 전용이다. 저장 시에는 무시한다 — 잠금 해제는 본사 고객지원 경로로만 한다(헌법 #35).
    /// 종전에는 이 필드가 DTO 에 없어 화면의 잠금 안내·읽기전용이 <b>절대 동작하지 않았다</b>
    /// (작4 §2-6-2). 화면에서만 선언되고 어디서도 강제되지 않던 것을 여기서 잇는다.
    /// </remarks>
    public bool IsLockedFromLanding { get; set; }
}
