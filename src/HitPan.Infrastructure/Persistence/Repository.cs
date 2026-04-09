using System.Linq.Expressions;
using HitPan.Application.Interfaces;
using HitPan.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HitPan.Infrastructure.Persistence;

public class Repository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<T> DbSet;

    public Repository(AppDbContext context)
    {
        Context = context;
        DbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(string id)
    {
        return await DbSet.FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<IReadOnlyList<T>> GetAllAsync()
    {
        return await DbSet.AsNoTracking().ToListAsync();
    }

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return await DbSet.AsNoTracking().Where(predicate).ToListAsync();
    }

    public async Task AddAsync(T entity)
    {
        await DbSet.AddAsync(entity);
    }

    public void Update(T entity)
    {
        DbSet.Update(entity);
    }

    public void Remove(T entity)
    {
        // Soft-delete rule: set IsActive=false when entity supports it.
        var isActiveProperty = typeof(T).GetProperty("IsActive");
        if (isActiveProperty is not null && isActiveProperty.PropertyType == typeof(bool))
        {
            isActiveProperty.SetValue(entity, false);
            DbSet.Update(entity);
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} does not support soft delete via IsActive.");
    }
}
