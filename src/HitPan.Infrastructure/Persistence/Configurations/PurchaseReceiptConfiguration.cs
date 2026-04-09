using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class PurchaseReceiptConfiguration : IEntityTypeConfiguration<PurchaseReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseReceipt> builder)
    {
        builder.ToTable("purchase_receipts");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("receipt_id").HasMaxLength(36);
        builder.Ignore(e => e.ReceiptId);
        builder.Ignore(e => e.UpdatedAt);
        builder.Ignore(e => e.UpdatedBy);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.ReceiptNo).HasColumnName("receipt_no").HasMaxLength(20).IsRequired();
        builder.Property(e => e.PoId).HasColumnName("po_id").HasMaxLength(36);
        builder.Property(e => e.PartnerId).HasColumnName("partner_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.ReceiptDate).HasColumnName("receipt_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.SourceType).HasColumnName("source_type").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion(
            v => v.ToString().ToLowerInvariant(),
            v => ParsePurchaseReceiptStatus(v)).IsRequired();
        builder.Property(e => e.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.Memo).HasColumnName("memo").HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);

        builder.HasIndex(e => new { e.TenantId, e.ReceiptNo }).IsUnique().HasDatabaseName("uq_receipt_no");
    }

    private static HitPan.Domain.Enums.PurchaseReceiptStatus ParsePurchaseReceiptStatus(string value)
    {
        return value switch
        {
            "draft" => HitPan.Domain.Enums.PurchaseReceiptStatus.Draft,
            "confirmed" => HitPan.Domain.Enums.PurchaseReceiptStatus.Confirmed,
            "cancelled" => HitPan.Domain.Enums.PurchaseReceiptStatus.Cancelled,
            _ => HitPan.Domain.Enums.PurchaseReceiptStatus.Draft
        };
    }
}
