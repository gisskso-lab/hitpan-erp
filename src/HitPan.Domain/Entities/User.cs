using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class User : BaseEntity, ITenantEntity
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    // AccountType: ERP는 부모/자식 둘뿐 — tenant_admin(부모)/tenant_user(자식)만 사용.
    //   본사(platform)·대리점(reseller) 계층은 백오피스 전용이라 PlatformId/ResellerId 제거 (보안 격벽, 2026-06-18).
    public string AccountType { get; set; } = "tenant_user";
    public string? DeptId { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
}
