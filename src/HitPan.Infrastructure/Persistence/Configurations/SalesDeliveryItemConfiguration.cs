using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class SalesDeliveryItemConfiguration : IEntityTypeConfiguration<SalesDeliveryItem>
{
    public void Configure(EntityTypeBuilder<SalesDeliveryItem> builder)
    {
        builder.ToTable("sales_delivery_items");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("delivery_item_id").HasMaxLength(36);
        builder.Ignore(e => e.DeliveryItemId);
        builder.Ignore(e => e.CreatedAt);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedAt);
        builder.Ignore(e => e.UpdatedBy);
        builder.Property(e => e.DeliveryId).HasColumnName("delivery_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.OrderItemId).HasColumnName("order_item_id").HasMaxLength(36);
        builder.Property(e => e.ItemId).HasColumnName("item_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.WarehouseId).HasColumnName("warehouse_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.Qty).HasColumnName("qty").HasColumnType("decimal(15,3)").IsRequired();
        builder.Property(e => e.UnitPrice).HasColumnName("unit_price").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.SupplyAmount).HasColumnName("supply_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(15,2)").IsRequired();
    }
}
