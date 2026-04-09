using HitPan.Domain.Entities;
using HitPan.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class SalesDeliveryConfiguration : IEntityTypeConfiguration<SalesDelivery>
{
    public void Configure(EntityTypeBuilder<SalesDelivery> builder)
    {
        builder.ToTable("sales_deliveries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("delivery_id").HasMaxLength(36);
        builder.Ignore(e => e.DeliveryId);
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.DeliveryNo).HasColumnName("delivery_no").HasMaxLength(20).IsRequired();
        builder.Property(e => e.OrderId).HasColumnName("order_id").HasMaxLength(36);
        builder.Property(e => e.PartnerId).HasColumnName("partner_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.EmployeeId).HasColumnName("employee_id").HasMaxLength(36);
        builder.Property(e => e.DeliveryDate).HasColumnName("delivery_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.SourceType).HasColumnName("source_type").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion(
            v => v.ToString().ToLowerInvariant(),
            v => ParseSalesDeliveryStatus(v)).IsRequired();
        builder.Property(e => e.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.Memo).HasColumnName("memo").HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(36);

        builder.HasIndex(e => new { e.TenantId, e.DeliveryNo }).IsUnique().HasDatabaseName("uq_delivery_no");
    }

    private static SalesDeliveryStatus ParseSalesDeliveryStatus(string value)
    {
        return value switch
        {
            "draft" => SalesDeliveryStatus.Draft,
            "confirmed" => SalesDeliveryStatus.Confirmed,
            "cancelled" => SalesDeliveryStatus.Cancelled,
            _ => SalesDeliveryStatus.Draft
        };
    }
}
