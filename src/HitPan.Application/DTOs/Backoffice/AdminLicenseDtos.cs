using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Backoffice;

// 작8 백오피스 P0 — 라이선스 발급·갱신·만료 (W2 매니저 가도용 스켈레톤)
// 헌법 #7 PlatformOnly · #22 본사 메타만 보유

public class AdminLicenseListItem
{
    public string LicenseId { get; set; } = string.Empty;
    public string LicenseKey { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // active · expired · revoked
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int DeviceCount { get; set; }
    public int DeviceLimit { get; set; }
}

public class AdminLicenseDetail : AdminLicenseListItem
{
    public string? IssuedBy { get; set; }
    public string? RevokedReason { get; set; }
    public DateTime? RevokedAt { get; set; }
    public List<AdminLicenseDeviceItem> Devices { get; set; } = new();
}

public class AdminLicenseDeviceItem
{
    public string DeviceId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

public class AdminIssueLicenseRequest
{
    [Required] public string TenantId { get; set; } = string.Empty;
    [Required] public string PlanType { get; set; } = string.Empty; // basic · pro · enterprise
    [Range(1, 999)] public int DeviceLimit { get; set; } = 1;
    public DateTime? ExpiresAt { get; set; }
    public string? Memo { get; set; }
}

public class AdminRenewLicenseRequest
{
    [Required] public DateTime ExpiresAt { get; set; }
    public string? Memo { get; set; }
}

public class AdminRevokeLicenseRequest
{
    [Required] public string Reason { get; set; } = string.Empty;
}

public class AdminLicenseListResponse
{
    public List<AdminLicenseListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int TotalPages { get; set; }
}
