using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("user_id").HasMaxLength(36);
        builder.Ignore(e => e.UserId);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        // Email은 평문 유지 — FindUserByEmailAsync 직접 비교 필요 (향후 email_hash 기반 로그인으로 개편 시 암호화 전환)
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(200).IsRequired();
        builder.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(256).IsRequired();
        builder.Property(e => e.UserName).HasColumnName("user_name").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Role).HasColumnName("role").HasConversion<string>().IsRequired();
        builder.Property(e => e.AccountType).HasColumnName("account_type").HasMaxLength(20).IsRequired();
        // reseller_id·platform_id 매핑 제거 (보안 격벽 2026-06-18): 본사·대리점 계층은 백오피스 전용.
        //   DB 컬럼은 흔적으로 남아도 ERP 엔티티(User)는 더 이상 매핑하지 않음(부모/자식만).
        builder.Property(e => e.DeptId).HasColumnName("dept_id").HasMaxLength(36);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(100);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(e => e.FailedLoginCount).HasColumnName("failed_login_count").IsRequired().HasDefaultValue(0);
        builder.Property(e => e.LockoutEnd).HasColumnName("lockout_end");
        builder.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(e => e.PasswordChangedAt).HasColumnName("password_changed_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(36);

        builder.HasIndex(e => new { e.TenantId, e.Email }).IsUnique().HasDatabaseName("uq_tenant_email");
    }
}
