using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class PurchaseReceiptItemConfiguration : IEntityTypeConfiguration<PurchaseReceiptItem>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptItem> builder)
    {
        builder.ToTable("purchase_receipt_items");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("receipt_item_id").HasMaxLength(36);
        builder.Ignore(e => e.ReceiptItemId);
        builder.Ignore(e => e.CreatedAt);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedAt);
        builder.Ignore(e => e.UpdatedBy);

        builder.Property(e => e.ReceiptId).HasColumnName("receipt_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.PoItemId).HasColumnName("po_item_id").HasMaxLength(36);
        builder.Property(e => e.ItemId).HasColumnName("item_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.WarehouseId).HasColumnName("warehouse_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.Qty).HasColumnName("qty").HasColumnType("decimal(15,3)").IsRequired();
        builder.Property(e => e.UnitPrice).HasColumnName("unit_price").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.SupplyAmount).HasColumnName("supply_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(15,2)").IsRequired();

        // Navigation 없이 shadow FK만 선언 — EF가 INSERT 순서(부모 먼저)를 정렬하게 한다.
        // Navigation을 두지 않는 이유: 엔티티는 Dapper 조회도 겸하므로 반영 자식 컬렉션을 두면
        // 양측 동기화 부담이 큼. FK 존재만 알려주면 EF는 부모→자식 순으로 INSERT한다.
        builder.HasOne<PurchaseReceipt>()
               .WithMany()
               .HasForeignKey(e => e.ReceiptId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
