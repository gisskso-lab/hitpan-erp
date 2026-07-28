using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.User;

// 작10 자식 계정 발급 (W2 매니저 가도용 스켈레톤)
// 헌법 #2 tenant_id JWT · #7 SaaS↔ERP 권한 혼용 0 · #11 권한 어드민 직접 설정

public class CreateChildUserRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string EmpName { get; set; } = string.Empty;
    [Required, StringLength(64, MinimumLength = 8)] public string Password { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = "User"; // User · hr_manager · accountant 등
    public string? Department { get; set; }
    public string? Position { get; set; }
    public string? Phone { get; set; }
    public List<ChildPermissionInit>? Permissions { get; set; }
}

public class ChildPermissionInit
{
    [Required] public string MenuKey { get; set; } = string.Empty;
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
    public bool CanExport { get; set; }
}

public class UpdateChildUserStatusRequest
{
    [Required] public string Status { get; set; } = "active"; // active · suspended · deleted
    public string? Reason { get; set; }
}

public class ChildUserResponse
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ParentUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
