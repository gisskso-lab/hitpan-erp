using HitPan.Application.DTOs.Department;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 부서 마스터 서비스. 작(2026-08-13) 그룹웨어 단계4 토대.
/// </summary>
/// <remarks>
/// 사장님 결재 원칙(헌법 #11): <b>권한·조직은 어드민이 직접 설정</b>한다.
/// 업종·규모별 템플릿을 우리가 만들어 주지 않는다 — 회사마다 조직이 다르다.
/// <para>
/// ⚠️ 조회는 종전부터 <see cref="IEmployeeService"/> 에 <c>GetDepartmentsAsync</c> 로 있었다.
/// 그쪽은 <b>사원 화면의 드롭다운 채우기용</b>(활성 부서만, 2개 칸)이라 그대로 두고,
/// 여기서는 <b>관리 화면용</b>(비활성 포함, 상위부서·사원수까지)을 다룬다.
/// 둘을 합치지 않는 이유 — 사원 화면이 비활성 부서를 고를 수 있게 되면 안 된다.
/// </para>
/// </remarks>
public interface IDepartmentService
{
    /// <summary>부서 목록(관리 화면용 — 비활성 포함, 상위부서명·사원수 포함).</summary>
    Task<List<DepartmentListDto>> GetListAsync(string tenantId, CancellationToken ct = default);

    /// <summary>부서를 만든다. 반환은 부서 ID.</summary>
    Task<string> CreateAsync(string tenantId, CreateDepartmentRequest request, CancellationToken ct = default);

    /// <summary>부서를 고친다.</summary>
    Task<bool> UpdateAsync(string tenantId, string deptId, UpdateDepartmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// 부서를 지운다. <b>사원이나 하위 부서가 물려 있으면 지우지 않고 비활성</b>으로 돌린다.
    /// </summary>
    Task<DeleteDepartmentResult> DeleteAsync(string tenantId, string deptId, CancellationToken ct = default);
}
