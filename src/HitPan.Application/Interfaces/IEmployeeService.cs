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

    // 작20260429 연차 관리 — 부여·사용 일수만 단독 저장 (사원관리 그리드용).
    Task UpdateAnnualLeaveAsync(string tenantId, string employeeId,
        decimal annualLeaveTotal, decimal annualLeaveUsed, CancellationToken ct = default);
}
