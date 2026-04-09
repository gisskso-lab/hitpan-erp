using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class SalesOrderItemConfiguration : IEntityTypeConfiguration<SalesOrderItem>
{
    public void Configure(EntityTypeBuilder<SalesOrderItem> builder)
    {
        builder.ToTable("sales_order_items");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("order_item_id").HasMaxLength(36);
        builder.Ignore(e => e.OrderItemId);
        builder.Ignore(e => e.CreatedAt);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedAt);
        builder.Ignore(e => e.UpdatedBy);
        builder.Property(e => e.OrderId).HasColumnName("order_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.ItemId).HasColumnName("item_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.OrderedQty).HasColumnName("ordered_qty").HasColumnType("decimal(15,3)").IsRequired();
        builder.Property(e => e.DeliveredQty).HasColumnName("delivered_qty").HasColumnType("decimal(15,3)").IsRequired();
        builder.Property(e => e.UnitPrice).HasColumnName("unit_price").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.SupplyAmount).HasColumnName("supply_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.ItemStatus).HasColumnName("item_status").HasMaxLength(20).IsRequired();
    }
}
