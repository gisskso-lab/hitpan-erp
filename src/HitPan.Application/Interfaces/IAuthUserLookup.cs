using HitPan.Domain.Entities;

namespace HitPan.Application.Interfaces;

public interface IAuthUserLookup
{
    Task<User?> FindUserByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> FindUserByIdAsync(string userId, CancellationToken ct = default);
}
