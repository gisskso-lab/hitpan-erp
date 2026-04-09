using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> builder)
    {
        builder.ToTable("purchase_order_items");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("po_item_id").HasMaxLength(36);
        builder.Ignore(e => e.PoItemId);
        builder.Ignore(e => e.CreatedAt);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedAt);
        builder.Ignore(e => e.UpdatedBy);

        builder.Property(e => e.PoId).HasColumnName("po_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.ItemId).HasColumnName("item_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.OrderedQty).HasColumnName("ordered_qty").HasColumnType("decimal(15,3)").IsRequired();
        builder.Property(e => e.ReceivedQty).HasColumnName("received_qty").HasColumnType("decimal(15,3)").IsRequired();
        builder.Property(e => e.UnitPrice).HasColumnName("unit_price").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.SupplyAmount).HasColumnName("supply_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.WarehouseId).HasColumnName("warehouse_id").HasMaxLength(36);
        builder.Property(e => e.ItemStatus).HasColumnName("item_status").HasMaxLength(20).IsRequired();
    }
}
