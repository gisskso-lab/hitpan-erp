using HitPan.Domain.Entities;
using HitPan.Domain.Enums;
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
        // W1-1 (작업지시서 20260707작2, 2026-07-01 emp_type 봉합과 동일 부류): users.role 관용 컨버터.
        // 진범: seed-parent(CompanyBootstrapProvisioner)가 'tenant_admin'(언더스코어) INSERT ↔
        // 종전 HasConversion<string>()은 멤버명 'TenantAdmin'만 역변환 가능 → 로그인 조회 시
        // EF materialize 폭발(계정은 DB에 있는데 읽는 순간 터짐). EF8 실측: 대소문자 무시 파싱은
        // 되고 언더스코어가 사인. 쓰기는 기존과 동일(멤버명 ToString), 읽기는 ParseUserRole로
        // 언더스코어 제거 재시도까지 — 기존 깨진 행(Sandbox·demo 시드)도 코드만으로 자가치유.
        builder.Property(e => e.Role).HasColumnName("role")
            .HasConversion(
                v => v.ToString(),
                v => ParseUserRole(v))
            .IsRequired();
        builder.Property(e => e.AccountType).HasColumnName("account_type").HasMaxLength(20).IsRequired();
        // reseller_id·platform_id 매핑 제거 (보안 격벽 2026-06-18): 본사·대리점 계층은 백오피스 전용.
        //   DB 컬럼은 흔적으로 남아도 ERP 엔티티(User)는 더 이상 매핑하지 않음(부모/자식만).
        builder.Property(e => e.DeptId).HasColumnName("dept_id").HasMaxLength(36);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(100);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        // 🔴 작(2026-08-14) — 사장님 지적 "계정은 죽여야지".
        //   DB 에는 이 칸이 원래 있었는데 **여기 매핑만 빠져 있었다.**
        //   그래서 계정 목록(Dapper 로 is_deleted 를 직접 읽음)에서는 지운 계정이 사라졌는데
        //   **로그인은 EF 라 이 칸을 몰라 그대로 뚫려 있었다.**
        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").IsRequired().HasDefaultValue(false);
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

    /// <summary>
    /// W1-1 (2026-07-07): users.role DB값 → UserRole 안전 변환 (EmployeeConfiguration.ParseEmpType 패턴).
    /// ① 대/소문자 무관 Parse('TenantAdmin'·'tenantadmin' 등) → ② 실패 시 언더스코어 제거 후 재시도
    /// ('tenant_admin'→'tenantadmin'→TenantAdmin 치유) → ③ 최종 실패 시 User 폴백.
    /// 이유: Enum.Parse 예외는 user materialize 자체를 막아 로그인이 500으로 죽는 P0였다.
    /// 폴백 시 경고로그는 정적 컨버터 컨텍스트라 불가(EmployeeConfiguration.ParseEmpType과 동일 사유) —
    /// 폴백 User는 최소권한이라 보안상 안전하며, 화면 권한 이상으로 운영자가 인지 가능.
    /// </summary>
    private static UserRole ParseUserRole(string value)
    {
        if (Enum.TryParse<UserRole>(value, ignoreCase: true, out var role))
            return role;

        // 언더스코어 어휘('tenant_admin' 등 snake_case로 저장된 기존 행) 자가치유.
        if (Enum.TryParse<UserRole>(value.Replace("_", ""), ignoreCase: true, out role))
            return role;

        return UserRole.User;
    }
}
