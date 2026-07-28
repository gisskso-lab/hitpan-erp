using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

/// <summary>
/// 견적서 품목 엔티티 매핑을 구성한다.
/// </summary>
public sealed class QuotationItemConfiguration : IEntityTypeConfiguration<QuotationItem>
{
    /// <summary>
    /// 엔티티-테이블 매핑을 설정한다.
    /// </summary>
    /// <param name="builder">엔티티 빌더다.</param>
    public void Configure(EntityTypeBuilder<QuotationItem> builder)
    {
        builder.ToTable("quotation_items");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
        builder.Ignore(e => e.QuotationItemId);
        builder.Ignore(e => e.CreatedAt);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedAt);
        builder.Ignore(e => e.UpdatedBy);
        builder.Property(e => e.QuoteId).HasColumnName("quote_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.ItemId).HasColumnName("item_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.Spec).HasColumnName("spec").HasMaxLength(100);
        builder.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(10);
        builder.Property(e => e.Qty).HasColumnName("qty").HasColumnType("decimal(15,3)").IsRequired();
        builder.Property(e => e.UnitPrice).HasColumnName("unit_price").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.DiscountRate).HasColumnName("discount_rate").HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(15,2)").IsRequired();
        builder.Property(e => e.Memo).HasColumnName("memo").HasMaxLength(200);
        builder.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();

        builder.HasOne<Quotation>()
               .WithMany()
               .HasForeignKey(e => e.QuoteId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
