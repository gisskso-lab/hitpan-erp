using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("warehouse_id").HasMaxLength(36);
        builder.Ignore(e => e.WarehouseId);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.WhCode).HasColumnName("wh_code").HasMaxLength(20).IsRequired();
        builder.Property(e => e.WhName).HasColumnName("wh_name").HasMaxLength(50).IsRequired();
        builder.Property(e => e.WhType).HasColumnName("wh_type").HasConversion<string>().IsRequired();
        builder.Property(e => e.Location).HasColumnName("location").HasMaxLength(100);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.WhCode }).IsUnique().HasDatabaseName("uq_tenant_code");
    }
}
