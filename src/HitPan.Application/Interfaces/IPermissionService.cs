using HitPan.Application.DTOs.Permission;

namespace HitPan.Application.Interfaces;

public interface IPermissionService
{
    Task<List<UserPermissionDto>> GetAllUsersPermissionsAsync(string tenantId, CancellationToken ct = default);
    Task<UserPermissionDto?> GetAsync(string userId, string tenantId, CancellationToken ct = default);
    Task SaveAsync(SavePermissionsDto dto, string tenantId, CancellationToken ct = default);
    Task<bool> HasPermissionAsync(string userId, string tenantId, string menuCode, string action, CancellationToken ct = default);

    // 20261005작5 §5-1 — 「직원 계정 관리」 단계(0~3). 구현체 전수 grep(#12): PermissionService 하나 · 시험 가짜 0(Moq 만).
    Task<int> GetUsersLevelAsync(string userId, string tenantId, CancellationToken ct = default);
}
