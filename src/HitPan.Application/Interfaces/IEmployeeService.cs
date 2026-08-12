using HitPan.Application.DTOs.Employee;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 사원관리(직원 CRUD) 서비스 인터페이스이다.
/// </summary>
public interface IEmployeeService
{
    Task<List<EmployeeListDto>> GetListAsync(string tenantId, CancellationToken ct = default);

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
}
