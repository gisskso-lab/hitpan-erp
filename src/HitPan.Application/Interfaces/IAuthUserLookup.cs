using HitPan.Domain.Entities;

namespace HitPan.Application.Interfaces;

public interface IAuthUserLookup
{
    Task<User?> FindUserByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> FindUserByIdAsync(string userId, CancellationToken ct = default);

    // W1-3 (작업지시서 20260707작2): 로그인·refresh 경로 employees 조회 전용.
    //   /api/auth/login·/refresh 는 TenantMiddleware 를 스킵해 CurrentTenant='' → AppDbContext 전역
    //   테넌트필터가 tenant_id='' 로 걸려 employees 조회가 항상 0건이었다(매 로그인 헛INSERT 1062 +
    //   employee_id claim 공백 = 결재·경비·HR 침묵 고장). 필터를 우회하되 반드시 명시적 tenantId
    //   조건으로 한정한다(헌법 #2 테넌트 격리 — user 행에서 얻은 TenantId 만 사용, 무조건 우회 금지).
    Task<Employee?> FindActiveEmployeeByUserAsync(string userId, string tenantId, CancellationToken ct = default);
    Task<List<Employee>> FindEmployeesByTenantAsync(string tenantId, CancellationToken ct = default);
}
