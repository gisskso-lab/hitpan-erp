using HitPan.Application.DTOs.Permission;

namespace HitPan.Application.Interfaces;

public interface IPermissionService
{
    Task<List<UserPermissionDto>> GetAllUsersPermissionsAsync(string tenantId, CancellationToken ct = default);
    Task<UserPermissionDto?> GetAsync(string userId, string tenantId, CancellationToken ct = default);
    Task SaveAsync(SavePermissionsDto dto, string tenantId, CancellationToken ct = default);
    Task<bool> HasPermissionAsync(string userId, string tenantId, string menuCode, string action, CancellationToken ct = default);
}
