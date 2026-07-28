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
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptItem> PurchaseReceiptItems => Set<PurchaseReceiptItem>();
    public DbSet<StockLedger> StockLedgers => Set<StockLedger>();
    public DbSet<WorkflowSetting> WorkflowSettings => Set<WorkflowSetting>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderItem> SalesOrderItems => Set<SalesOrderItem>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();
    public DbSet<SalesDelivery> SalesDeliveries => Set<SalesDelivery>();
    public DbSet<SalesDeliveryItem> SalesDeliveryItems => Set<SalesDeliveryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var encryptedConverter = new EncryptedValueConverter(_encryptionService);
        var nullableEncryptedConverter = new NullableEncryptedValueConverter(_encryptionService);
        modelBuilder.ApplyConfiguration(new TenantConfiguration(encryptedConverter, nullableEncryptedConverter));
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new DepartmentConfiguration());
        modelBuilder.ApplyConfiguration(new ItemConfiguration());
        modelBuilder.ApplyConfiguration(new PartnerConfiguration(encryptedConverter, nullableEncryptedConverter));
        modelBuilder.ApplyConfiguration(new EmployeeConfiguration(encryptedConverter, nullableEncryptedConverter));
        modelBuilder.ApplyConfiguration(new WarehouseConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderItemConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseReceiptItemConfiguration());
        modelBuilder.ApplyConfiguration(new SalesOrderConfiguration());
        modelBuilder.ApplyConfiguration(new SalesOrderItemConfiguration());
        modelBuilder.ApplyConfiguration(new QuotationConfiguration());
        modelBuilder.ApplyConfiguration(new QuotationItemConfiguration());
        modelBuilder.ApplyConfiguration(new SalesDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new SalesDeliveryItemConfiguration());

        modelBuilder.Entity<WorkflowSetting>(builder =>
        {
            builder.ToTable("workflow_settings");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id).HasColumnName("setting_id").HasMaxLength(36);
            builder.Ignore(e => e.SettingId);
            builder.Ignore(e => e.CreatedAt);
            builder.Ignore(e => e.CreatedBy);
            builder.Ignore(e => e.UpdatedAt);
            builder.Ignore(e => e.UpdatedBy);
            builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
            builder.Property(e => e.SettingKey).HasColumnName("setting_key").HasMaxLength(60).IsRequired();
            builder.Property(e => e.SettingValue).HasColumnName("setting_value").HasMaxLength(200).IsRequired();
            builder.Property(e => e.ValueType).HasColumnName("value_type").HasMaxLength(20).IsRequired();
            builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
            builder.HasIndex(e => new { e.TenantId, e.SettingKey }).IsUnique().HasDatabaseName("uq_tenant_key");
        });

        modelBuilder.Entity<StockLedger>(builder =>
        {
            builder.ToTable("stock_ledger");
            builder.HasKey(e => e.LedgerId);
            builder.Ignore(e => e.Id);
            builder.Ignore(e => e.CreatedAt);
            builder.Ignore(e => e.CreatedBy);
            builder.Ignore(e => e.UpdatedAt);
            builder.Ignore(e => e.UpdatedBy);
            builder.Property(e => e.LedgerId).HasColumnName("ledger_id").ValueGeneratedOnAdd();
            builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
            builder.Property(e => e.ItemId).HasColumnName("item_id").HasMaxLength(36).IsRequired();
            builder.Property(e => e.WarehouseId).HasColumnName("warehouse_id").HasMaxLength(36).IsRequired();
            builder.Property(e => e.PartnerId).HasColumnName("partner_id").HasMaxLength(36);
            builder.Property(e => e.EmployeeId).HasColumnName("employee_id").HasMaxLength(36);
            builder.Property(e => e.LedgerDate).HasColumnName("ledger_date").HasColumnType("date").IsRequired();
            builder.Property(e => e.Ym).HasColumnName("ym").HasMaxLength(7).IsRequired();
            builder.Property(e => e.MoveType).HasColumnName("move_type").HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => ParseStockMoveType(v)).IsRequired();
            builder.Property(e => e.SourceType).HasColumnName("source_type").HasMaxLength(30).IsRequired();
            builder.Property(e => e.SourceId).HasColumnName("source_id").HasMaxLength(36).IsRequired();
            builder.Property(e => e.DocNo).HasColumnName("doc_no").HasMaxLength(20);
            builder.Property(e => e.QtyIn).HasColumnName("qty_in").HasColumnType("decimal(15,3)").IsRequired();
            builder.Property(e => e.QtyOut).HasColumnName("qty_out").HasColumnType("decimal(15,3)").IsRequired();
            builder.Property(e => e.UnitCost).HasColumnName("unit_cost").HasColumnType("decimal(15,4)");
            builder.Property(e => e.SupplyAmount).HasColumnName("supply_amount").HasColumnType("decimal(15,2)");
            builder.Property(e => e.Memo).HasColumnName("memo").HasMaxLength(200);
        });

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

    public async Task<User?> FindUserByEmailAsync(string email)
    {
        return await Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive == true);
    }

    public async Task<User?> FindUserByIdAsync(string userId)
    {
        return await Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive == true);
    }

    // EF Core가 내부에서 호출하는 정식 시그니처 (acceptAllChangesOnSuccess + CancellationToken).
    // 단일 인자 시그니처(SaveChangesAsync(CancellationToken))는 base가 이 메서드를 호출하지 않으므로
    // 단일 인자만 override하면 우회되어 타임스탬프가 박히지 않는다 (MariaDB strict mode에서 INSERT 실패).
    // → 사장님 헌법: "어 어젠 됐는데?" 사고 패턴. 정공으로 두 시그니처 모두 처리.
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditTimestamps();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void ApplyAuditTimestamps()
    {
        var utcNow = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = utcNow;
                if (entry.Entity.UpdatedAt == default) entry.Entity.UpdatedAt = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = utcNow;
            }
        }
    }

    private static HitPan.Domain.Enums.StockMoveType ParseStockMoveType(string value)
    {
        return value switch
        {
            "in" => HitPan.Domain.Enums.StockMoveType.In,
            "out" => HitPan.Domain.Enums.StockMoveType.Out,
            "adjust" => HitPan.Domain.Enums.StockMoveType.Adjust,
            _ => HitPan.Domain.Enums.StockMoveType.In
        };
    }
}
