using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HitPan.Infrastructure.Persistence;

public sealed class AuthUserLookup : IAuthUserLookup
{
    private readonly AppDbContext _dbContext;

    public AuthUserLookup(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<User?> FindUserByEmailAsync(string email, CancellationToken ct = default)
    {
        return _dbContext.FindUserByEmailAsync(email);
    }

    public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct = default)
    {
        return _dbContext.FindUserByIdAsync(userId);
    }

    // W1-3 (작업지시서 20260707작2): 로그인 경로 employees 조회 — AppDbContext.FindUserByEmailAsync 와
    //   동일 패턴(IgnoreQueryFilters). 익명 경로는 CurrentTenant='' 라 전역 테넌트필터가 0건을 만들므로
    //   필터를 우회하되, 헌법 #2 테넌트 격리를 위해 반드시 명시적 tenantId 조건을 동반한다
    //   (호출부는 user 행에서 얻은 user.TenantId 만 전달 — 무조건 우회 아님).
    public Task<Employee?> FindActiveEmployeeByUserAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        return _dbContext.Set<Employee>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId && e.IsActive, ct);
    }

    public Task<List<Employee>> FindEmployeesByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        return _dbContext.Set<Employee>()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .ToListAsync(ct);
    }
}
