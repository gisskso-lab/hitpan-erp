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

    // ───────────────────────────────────────────────────────────────
    // 사업장 노무 정보 — 작(2026-08-13) 그룹웨어 단계4 토대
    //
    // 🔴 사장님(2026-08-12): "사업장의 직원수, 규모, 법인,개인,면세사업장, 등
    //    여러상황이 있어서 자동화는 현실적으로 어려워. 반자동원칙"
    //    연차·퇴직금·수당이 이 조건들로 갈린다.
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 과세 유형: <c>taxable</c> 과세 / <c>tax_free</c> 면세.
    /// </summary>
    /// <remarks>
    /// 🔴 컬럼은 처음부터 있었는데 <b>조회·저장·화면 어디에도 없어 값을 넣을 방법이 없었다</b>
    /// (늘 기본값 <c>taxable</c>). 단계4 에서 살린다.
    /// </remarks>
    public string? TaxType { get; set; }

    /// <summary>
    /// 상시근로자수. <c>null</c> = 미정.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>자동 계산하지 않는다.</b> 법정 상시근로자수는 '연인원 ÷ 가동일수' 이고
    /// 알바·가족까지 세는 등 계산이 까다롭다. 사원 행을 세서 채우면 그럴듯하지만 틀린 숫자가 나오고,
    /// 그 숫자로 "5인 미만 → 연차 없음" 이 판정되면 <b>법정 미달</b>이 된다.
    /// 화면이 현재 재직자 수를 <b>제안</b>하고 사람이 확정한다(반자동 3단).
    /// </remarks>
    public int? RegularEmployeeCount { get; set; }

    /// <summary>
    /// 법인/개인 구분: <c>corporate</c> 법인 / <c>individual</c> 개인. <c>null</c> = 미정.
    /// </summary>
    /// <remarks>
    /// ⚠️ <see cref="CorpNo"/>(법인등록번호) 유무로 추정하지 않는다 —
    /// 개인사업자도 비어 있고 법인도 안 적을 수 있어 그걸로 판정하면 틀린다.
    /// </remarks>
    public string? BusinessEntityType { get; set; }

    /// <summary>
    /// 상시근로자수 기준일. 이 숫자가 <b>언제 기준</b>인지.
    /// </summary>
    /// <remarks>
    /// 설계도 §0 지침: <i>"값마다 적용시작일을 둔다. 과거분은 옛 값으로 계산해야 한다."</i>
    /// "지금 7명" 이 아니라 "언제 기준 7명" 이어야 뜻이 산다.
    /// </remarks>
    public DateTime? EmployeeCountAsOf { get; set; }
}
