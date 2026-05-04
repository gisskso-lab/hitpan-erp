using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("po_id").HasMaxLength(36);
        builder.Ignore(e => e.PoId);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.PoNo).HasColumnName("po_no").HasMaxLength(20).IsRequired();
        builder.Property(e => e.PartnerId).HasColumnName("partner_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.EmployeeId).HasColumnName("employee_id").HasMaxLength(36);
        builder.Property(e => e.PoDate).HasColumnName("po_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.ExpectedDate).HasColumnName("expected_date").HasColumnType("date");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion(
            v => v.ToString().ToLowerInvariant(),
            v => ParsePurchaseOrderStatus(v)).IsRequired();
        builder.Property(e => e.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.Memo).HasColumnName("memo").HasMaxLength(500);
        builder.Property(e => e.IsAuto).HasColumnName("is_auto").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(36);

        builder.HasIndex(e => new { e.TenantId, e.PoNo }).IsUnique().HasDatabaseName("uq_po_no");
    }

    private static HitPan.Domain.Enums.PurchaseOrderStatus ParsePurchaseOrderStatus(string value)
    {
        return value switch
        {
            "draft" => HitPan.Domain.Enums.PurchaseOrderStatus.Draft,
            "confirmed" => HitPan.Domain.Enums.PurchaseOrderStatus.Confirmed,
            "partial" => HitPan.Domain.Enums.PurchaseOrderStatus.Partial,
            "closed" => HitPan.Domain.Enums.PurchaseOrderStatus.Closed,
            "cancelled" => HitPan.Domain.Enums.PurchaseOrderStatus.Cancelled,
            _ => HitPan.Domain.Enums.PurchaseOrderStatus.Draft
        };
    }
}
