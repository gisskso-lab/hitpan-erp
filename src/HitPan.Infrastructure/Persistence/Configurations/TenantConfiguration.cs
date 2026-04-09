using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("tenant_id").HasMaxLength(36);
        builder.Ignore(e => e.TenantId);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);

        builder.Property(e => e.TenantCode).HasColumnName("tenant_code").HasMaxLength(20).IsRequired();
        builder.Property(e => e.CompanyName).HasColumnName("company_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.BizNo).HasColumnName("biz_no").HasMaxLength(12).IsRequired();
        builder.Property(e => e.CeoName).HasColumnName("ceo_name").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Tel).HasColumnName("tel").HasMaxLength(20);
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(200);
        builder.Property(e => e.ResellerId).HasColumnName("reseller_id").HasMaxLength(36);
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(e => e.TrialEndsAt).HasColumnName("trial_ends_at");
        builder.Property(e => e.DbHost).HasColumnName("db_host").HasMaxLength(100).IsRequired();
        builder.Property(e => e.DbName).HasColumnName("db_name").HasMaxLength(50).IsRequired();
        builder.Property(e => e.LicenseKeyHash).HasColumnName("license_key_hash").HasMaxLength(256).IsRequired();
        builder.Property(e => e.ResellerTier).HasColumnName("reseller_tier").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => e.TenantCode).IsUnique().HasDatabaseName("uq_tenant_code");
    }
}
