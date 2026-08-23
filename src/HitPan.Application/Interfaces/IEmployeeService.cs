using HitPan.Application.DTOs.Employee;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 사원관리(직원 CRUD) 서비스 인터페이스이다.
/// </summary>
public interface IEmployeeService
{
    /// <summary>
    /// 사원 목록. <b>기본은 재직자만</b> — 퇴사자는 감춘다(사장님 지시 2026-08-14).
    /// </summary>
    /// <param name="includeResigned">퇴사자까지 볼지 여부. 화면의 "퇴사자 포함" 스위치가 정한다.</param>
    Task<List<EmployeeListDto>> GetListAsync(
        string tenantId, bool includeResigned = false, CancellationToken ct = default);

    // 봉합 (2026-06-22, 10차 P1-1): 부서 드롭다운용 목록 조회 (departments 마스터, 읽기 전용).
    Task<List<DepartmentDto>> GetDepartmentsAsync(string tenantId, CancellationToken ct = default);

    Task<EmployeeDetailDto?> GetAsync(string tenantId, string employeeId, CancellationToken ct = default);
    Task<string> CreateAsync(string tenantId, CreateEmployeeRequest request, CancellationToken ct = default);
    Task UpdateAsync(string tenantId, string employeeId, UpdateEmployeeRequest request, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, string employeeId, CancellationToken ct = default);

    // 작(2026-08-12) 그룹웨어 단계0 P0-A·C: 퇴사 처리.
    // DeleteAsync 는 이 메서드에 위임한다(기존 호출부 보존 — 헌법 #1).
    // 봉합 전에는 employees 만 껐고 users(로그인 계정)는 살아 있었다.
    // 반환값 = 로그인 계정을 실제로 차단했는가. 계정이 없거나 대표계정이면 false 다.
    // 화면이 "계정도 차단했다"고 단정하지 않게 하려고 돌려준다.
    Task<bool> ResignAsync(string tenantId, string employeeId,
        DateTime? resignDate, string? resignReason, CancellationToken ct = default);

    // 작(2026-08-12) 그룹웨어 단계0 P0-B: 퇴사 전 사전 점검.
    // 막지 않고 알린다(반자동 원칙) — 결재선에 걸린 사람이 나가면 결재가 멈춘다(헌법 #20).
    Task<EmployeeResignPrecheckDto> GetResignPrecheckAsync(string tenantId, string employeeId,
        CancellationToken ct = default);

    // 작20260429 연차 관리 — 부여·사용 일수만 단독 저장 (사원관리 그리드용).
    Task UpdateAnnualLeaveAsync(string tenantId, string employeeId,
        decimal annualLeaveTotal, decimal annualLeaveUsed, CancellationToken ct = default);

    // ── 작20260822작1 G1-[B] 결재자 후보 목록 (사장님 결재 2026-08-23) ──
    // 사장님 확정: "대표이사 마지막 결재 외는 권한가진자가 하는걸로."
    //              "부모계정, 그리고 권한자만"
    //
    // 🔴 왜 사원 목록을 그대로 못 쓰는가.
    //    결재선에 APPROVAL 권한 없는 사람이 들어가면 그 사람은 결재함에
    //    [RequirePermission("APPROVAL","view")] 에서 막혀 진입 자체를 못 한다.
    //    ⇒ 그 문서가 영영 안 간다. 아무도 모른 채 일이 선다.
    //
    // 🔴 부모계정을 반드시 함께 뽑는 이유 (사장님이 PM 권고를 정정하신 자리).
    //    PermissionService.HasPermissionAsync 는 Layer 0 에서 부모계정(tenant_admin)을
    //    user_permissions 조회 전에 통과시킨다(락아웃 방지).
    //    ⇒ 대표는 user_permissions 에 줄이 없을 수 있다.
    //    ⇒ 권한자만 뽑으면 **대표가 목록에서 사라진다.**
    //    ⇒ 그런데 최종 결재 단계는 대표여야 한다 — 스스로와 충돌한다.
    Task<List<EmployeeListDto>> GetApproverCandidatesAsync(
        string tenantId, CancellationToken ct = default);
}
