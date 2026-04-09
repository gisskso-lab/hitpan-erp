using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;

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
}
