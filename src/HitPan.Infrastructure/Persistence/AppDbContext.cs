using System.Linq.Expressions;
using HitPan.Application.Interfaces;
using HitPan.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HitPan.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (!typeof(ITenantEntity).IsAssignableFrom(clrType))
            {
                continue;
            }

            modelBuilder.Entity(clrType).HasQueryFilter(BuildTenantFilterExpression(clrType));
        }
    }

    private LambdaExpression BuildTenantFilterExpression(Type entityClrType)
    {
        var entityParameter = Expression.Parameter(entityClrType, "entity");
        var entityTenantId = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            new[] { typeof(string) },
            entityParameter,
            Expression.Constant("TenantId"));

        var currentTenantId = Expression.Property(
            Expression.Constant(this),
            nameof(CurrentTenantId));

        var equalExpression = Expression.Equal(entityTenantId, currentTenantId);

        return Expression.Lambda(equalExpression, entityParameter);
    }

    public string CurrentTenantId => _currentTenant.TenantId;
}
