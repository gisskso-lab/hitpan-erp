namespace HitPan.Application.DTOs.Department;

/// <summary>
/// 부서 목록 항목.
/// </summary>
/// <remarks>
/// 작(2026-08-13) 그룹웨어 단계4 토대.
/// 🔴 종전엔 부서를 <b>만들 방법 자체가 없었다</b> — <c>src/</c> 전체에
/// <c>INSERT INTO departments</c> 가 0건이었고 화면도 조회 전용이었다.
/// 그래서 <c>departments</c> 가 0행이고, 사원 등록의 부서 드롭다운 선택지가 0개고,
/// 결국 사원 전원이 부서 없음이었다. <b>버그가 아니라 필연이었다.</b>
/// 부서방(메신저)·조직도·결재선이 전부 이 축을 딛고 서므로 토대에서 세운다.
/// </remarks>
public sealed class DepartmentListDto
{
    public string DeptId { get; set; } = string.Empty;
    public string? ParentDeptId { get; set; }
    public string DeptName { get; set; } = string.Empty;
    public string? DeptCode { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }

    /// <summary>상위 부서 이름. 화면에서 "영업부 &gt; 영업1팀" 처럼 보여주기 위한 것.</summary>
    public string? ParentDeptName { get; set; }

    /// <summary>이 부서에 속한 재직 사원 수. 삭제 전 경고에 쓴다.</summary>
    public int EmployeeCount { get; set; }
}

/// <summary>부서 등록 요청.</summary>
public sealed class CreateDepartmentRequest
{
    public string DeptName { get; set; } = string.Empty;
    public string? DeptCode { get; set; }

    /// <summary>상위 부서. 비우면 최상위.</summary>
    public string? ParentDeptId { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>부서 수정 요청.</summary>
public sealed class UpdateDepartmentRequest
{
    public string DeptName { get; set; } = string.Empty;
    public string? DeptCode { get; set; }
    public string? ParentDeptId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 부서 삭제 결과.
/// </summary>
/// <remarks>
/// 🔴 <b>왜 못 지웠는지</b> 를 알려준다. 그냥 실패라고만 하면 고객이 계속 다시 눌러 본다
/// (단계3 검증에서 같은 지적을 받았다).
/// </remarks>
public sealed class DeleteDepartmentResult
{
    public bool Deleted { get; set; }

    /// <summary>지우지 않고 <b>비활성</b>으로 돌렸는가(사원·하위부서가 물려 있을 때).</summary>
    public bool Deactivated { get; set; }

    /// <summary>사용자에게 그대로 보여줄 사유.</summary>
    public string? Message { get; set; }
}
