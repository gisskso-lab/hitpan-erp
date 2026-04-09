using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("subscription_id").HasMaxLength(36);
        builder.Ignore(e => e.SubscriptionId);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.PlanType).HasColumnName("plan_type").HasConversion<string>().IsRequired();
        builder.Property(e => e.BaseUsers).HasColumnName("base_users").IsRequired();
        builder.Property(e => e.ExtraUsers).HasColumnName("extra_users").IsRequired();
        builder.Property(e => e.BaseFee).HasColumnName("base_fee").IsRequired();
        builder.Property(e => e.ExtraFeePerUser).HasColumnName("extra_fee_per_user").IsRequired();
        builder.Property(e => e.BillingCycle).HasColumnName("billing_cycle").HasMaxLength(20).IsRequired();
        builder.Property(e => e.StartedAt).HasColumnName("started_at").HasColumnType("date").IsRequired();
        builder.Property(e => e.EndsAt).HasColumnName("ends_at").HasColumnType("date");
        builder.Property(e => e.NextBillingAt).HasColumnName("next_billing_at").HasColumnType("date").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
