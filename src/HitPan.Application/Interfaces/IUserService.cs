using HitPan.Application.DTOs.User;

namespace HitPan.Application.Interfaces;

public interface IUserService
{
    Task<List<UserListDto>> GetListAsync(string tenantId, CancellationToken ct = default);

    Task<UserListDto?> GetAsync(string userId, string tenantId, CancellationToken ct = default);

    Task<string> CreateAsync(CreateUserDto dto, string tenantId, CancellationToken ct = default);

    Task UpdateAsync(string userId, UpdateUserDto dto, string tenantId, CancellationToken ct = default);

    Task DeactivateAsync(string userId, string tenantId, CancellationToken ct = default);

    Task<string> ResetPasswordAsync(string userId, string tenantId, CancellationToken ct = default);
}
