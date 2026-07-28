using HitPan.Domain.Entities;
using HitPan.Domain.Enums;
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
        // W3-3 (작업지시서 20260707작2, 휴면 enum 지뢰 읽기 안전장치): 출하 DDL 의 status DEFAULT 'trial'
        //   및 백오피스 수신 어휘(suspended 등)가 enum 멤버명과 달라 읽는 순간 materialize 폭발하는 휴면
        //   지뢰였다(W1-1 users.role 과 동일 부류). enum 에 Trial 가산 + 읽기만 관용. 쓰기는 기존과 동일.
        builder.Property(e => e.Status).HasColumnName("status")
            .HasConversion(
                v => v.ToString(),
                v => ParseSubscriptionStatus(v))
            .IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }

    /// <summary>
    /// W3-3 (2026-07-12): subscriptions.status DB값 → SubscriptionStatus 안전 변환 (ParseUserRole 패턴).
    /// ① 대/소문자 무관 Parse('trial' 은 Trial 멤버 가산으로 여기서 해소) → ② 명시 맵(suspended→Paused)
    /// → ③ 최종 실패 시 Paused 폴백(과금 상태 불명은 보수적으로 일시정지 취급).
    /// 폴백 시 경고로그는 정적 컨버터 컨텍스트라 불가(EmployeeConfiguration.ParseEmpType 과 동일 사유).
    /// </summary>
    private static SubscriptionStatus ParseSubscriptionStatus(string value)
    {
        if (Enum.TryParse<SubscriptionStatus>(value, ignoreCase: true, out var status))
            return status;

        return value.Trim().ToLowerInvariant() switch
        {
            "suspended" => SubscriptionStatus.Paused,
            _ => SubscriptionStatus.Paused
        };
    }
}
