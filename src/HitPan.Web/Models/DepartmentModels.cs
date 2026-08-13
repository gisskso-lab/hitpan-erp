namespace HitPan.Web.Models;

/// <summary>
/// 부서 목록 항목. 작(2026-08-13) 그룹웨어 단계4 토대.
/// </summary>
/// <remarks>
/// ⚠️ 타입은 API 응답 그대로여야 한다 — 단계0 에서 <c>decimal</c> vs <c>decimal?</c> 하나 어긋나
/// <c>JsonException</c> 이 나고 그걸 <c>catch</c> 가 삼켜 <b>목록 전체가 빈 화면</b>이 됐다.
/// </remarks>
public sealed class DepartmentListItemModel
{
    public string DeptId { get; set; } = string.Empty;
    public string? ParentDeptId { get; set; }
    public string DeptName { get; set; } = string.Empty;
    public string? DeptCode { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string? ParentDeptName { get; set; }
    public int EmployeeCount { get; set; }

    /// <summary>"영업부 &gt; 영업1팀" 처럼 보여줄 이름.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(ParentDeptName)
        ? DeptName
        : $"{ParentDeptName} > {DeptName}";
}

/// <summary>부서 등록·수정 입력.</summary>
public sealed class DepartmentEditModel
{
    public string? DeptId { get; set; }
    public string DeptName { get; set; } = string.Empty;
    public string? DeptCode { get; set; }
    public string? ParentDeptId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 부서 삭제 결과. <b>지운 것</b>과 <b>비활성으로 돌린 것</b>을 가른다.
/// </summary>
/// <remarks>
/// 🔴 "삭제했습니다" 라고 해놓고 목록에 그대로 남아 있으면 되는 척이다
/// (단계3 검증에서 같은 병으로 P0 를 받았다).
/// </remarks>
public sealed class DepartmentDeleteResultModel
{
    public bool Deleted { get; set; }
    public bool Deactivated { get; set; }
    public string? Message { get; set; }
}
