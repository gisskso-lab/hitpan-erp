using HitPan.Domain.Entities;
using HitPan.Infrastructure.Security.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    private readonly EncryptedValueConverter _encryptedConverter;
    private readonly NullableEncryptedValueConverter _nullableEncryptedConverter;

    public PartnerConfiguration(
        EncryptedValueConverter encryptedConverter,
        NullableEncryptedValueConverter nullableEncryptedConverter)
    {
        _encryptedConverter = encryptedConverter;
        _nullableEncryptedConverter = nullableEncryptedConverter;
    }

    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("partners");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("partner_id").HasMaxLength(36);
        builder.Ignore(e => e.PartnerId);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.PartnerCode).HasColumnName("partner_code").HasMaxLength(20).IsRequired();
        builder.Property(e => e.PartnerName).HasColumnName("partner_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.PartnerType).HasColumnName("partner_type").HasConversion<string>().IsRequired();
        builder.Property(e => e.BizNo).HasColumnName("biz_no").HasMaxLength(100).HasConversion(_nullableEncryptedConverter);
        builder.Property(e => e.CeoName).HasColumnName("ceo_name").HasMaxLength(50);
        builder.Property(e => e.BizType).HasColumnName("biz_type").HasMaxLength(50);
        builder.Property(e => e.BizItem).HasColumnName("biz_item").HasMaxLength(50);
        builder.Property(e => e.Tel).HasColumnName("tel").HasMaxLength(100).HasConversion(_nullableEncryptedConverter);
        builder.Property(e => e.Fax).HasColumnName("fax").HasMaxLength(100).HasConversion(_nullableEncryptedConverter);
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(200).HasConversion(_nullableEncryptedConverter);
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(200);
        builder.Property(e => e.CreditLimit).HasColumnName("credit_limit").HasColumnType("decimal(15,2)");
        builder.Property(e => e.PaymentTerms).HasColumnName("payment_terms");
        builder.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(30);
        builder.Property(e => e.BankAccount).HasColumnName("bank_account").HasMaxLength(200).HasConversion(_nullableEncryptedConverter);
        builder.Property(e => e.AccountHolder).HasColumnName("account_holder").HasMaxLength(30);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(e => e.Memo).HasColumnName("memo").HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(36);

        builder.HasIndex(e => new { e.TenantId, e.PartnerCode }).IsUnique().HasDatabaseName("uq_tenant_code");
    }
}
