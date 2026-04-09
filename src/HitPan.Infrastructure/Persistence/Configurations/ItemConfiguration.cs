using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("item_id").HasMaxLength(36);
        builder.Ignore(e => e.ItemId);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.ItemCode).HasColumnName("item_code").HasMaxLength(30).IsRequired();
        builder.Property(e => e.ItemName).HasColumnName("item_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ItemType).HasColumnName("item_type").HasConversion<string>().IsRequired();
        builder.Property(e => e.CategoryId).HasColumnName("category_id").HasMaxLength(36);
        builder.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(10).IsRequired();
        builder.Property(e => e.StdPrice).HasColumnName("std_price").HasColumnType("decimal(15,2)");
        builder.Property(e => e.CostPrice).HasColumnName("cost_price").HasColumnType("decimal(15,2)");
        builder.Property(e => e.SafeStock).HasColumnName("safe_stock").HasColumnType("decimal(15,3)");
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(e => e.Memo).HasColumnName("memo").HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(36);

        builder.HasIndex(e => new { e.TenantId, e.ItemCode }).IsUnique().HasDatabaseName("uq_tenant_code");
    }
}
