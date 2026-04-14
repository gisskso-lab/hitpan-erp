using HitPan.Domain.Entities;
using HitPan.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

/// <summary>
/// 견적서 헤더 엔티티 매핑을 구성한다.
/// </summary>
public sealed class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    /// <summary>
    /// 엔티티-테이블 매핑을 설정한다.
    /// </summary>
    /// <param name="builder">엔티티 빌더다.</param>
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("quotations");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("quote_id").HasMaxLength(36);
        builder.Ignore(e => e.QuoteId);
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.QuoteNo).HasColumnName("quote_no").HasMaxLength(20).IsRequired();
        builder.Property(e => e.PartnerId).HasColumnName("partner_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.EmployeeId).HasColumnName("employee_id").HasMaxLength(36);
        builder.Property(e => e.QuoteDate).HasColumnName("quote_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.ValidUntil).HasColumnName("valid_until").HasColumnType("date");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion(
            v => v.ToString().ToLowerInvariant(),
            v => ParseQuotationStatus(v)).IsRequired();
        builder.Property(e => e.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.Memo).HasColumnName("memo").HasMaxLength(500);
        builder.Property(e => e.ConvertedOrderId).HasColumnName("converted_order_id").HasMaxLength(36);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(36);
        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(e => new { e.TenantId, e.QuoteNo }).IsUnique().HasDatabaseName("uq_quote_no");
        builder.HasMany(e => e.Items).WithOne(e => e.Quotation).HasForeignKey(e => e.QuoteId);
    }

    /// <summary>
    /// 문자열 상태값을 열거형으로 파싱한다.
    /// </summary>
    /// <param name="value">문자열 상태값이다.</param>
    /// <returns>견적서 상태 열거형이다.</returns>
    private static QuotationStatus ParseQuotationStatus(string value)
    {
        return value switch
        {
            "draft" => QuotationStatus.Draft,
            "submitted" => QuotationStatus.Submitted,
            "accepted" => QuotationStatus.Accepted,
            "rejected" => QuotationStatus.Rejected,
            "expired" => QuotationStatus.Expired,
            "converted" => QuotationStatus.Converted,
            _ => QuotationStatus.Draft
        };
    }
}
