using System.Linq.Expressions;
using HitPan.Application.Interfaces;
using HitPan.Domain.Common;
using HitPan.Domain.Entities;
using HitPan.Infrastructure.Persistence.Configurations;
using HitPan.Infrastructure.Security;
using HitPan.Infrastructure.Security.Converters;
using Microsoft.EntityFrameworkCore;

namespace HitPan.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IEncryptionService _encryptionService;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentTenant currentTenant,
        IEncryptionService encryptionService)
        : base(options)
    {
        _currentTenant = currentTenant;
        _encryptionService = encryptionService;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var encryptedConverter = new EncryptedValueConverter(_encryptionService);
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new DepartmentConfiguration());
        modelBuilder.ApplyConfiguration(new ItemConfiguration());
        modelBuilder.ApplyConfiguration(new PartnerConfiguration(encryptedConverter));
        modelBuilder.ApplyConfiguration(new EmployeeConfiguration(encryptedConverter));
        modelBuilder.ApplyConfiguration(new WarehouseConfiguration());

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
