using HitPan.Domain.Entities;
using HitPan.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("sales_orders");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("order_id").HasMaxLength(36);
        builder.Ignore(e => e.OrderId);
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.OrderNo).HasColumnName("order_no").HasMaxLength(20).IsRequired();
        builder.Property(e => e.PartnerId).HasColumnName("partner_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.EmployeeId).HasColumnName("employee_id").HasMaxLength(36);
        builder.Property(e => e.OrderDate).HasColumnName("order_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.DeliveryDate).HasColumnName("delivery_date").HasColumnType("date");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion(
            v => v.ToString().ToLowerInvariant(),
            v => ParseSalesOrderStatus(v)).IsRequired();
        builder.Property(e => e.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.Memo).HasColumnName("memo").HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(36);

        builder.HasIndex(e => new { e.TenantId, e.OrderNo }).IsUnique().HasDatabaseName("uq_order_no");
    }

    private static SalesOrderStatus ParseSalesOrderStatus(string value)
    {
        return value switch
        {
            "draft" => SalesOrderStatus.Draft,
            "confirmed" => SalesOrderStatus.Confirmed,
            "partial" => SalesOrderStatus.Partial,
            "closed" => SalesOrderStatus.Closed,
            "cancelled" => SalesOrderStatus.Cancelled,
            _ => SalesOrderStatus.Draft
        };
    }
}
