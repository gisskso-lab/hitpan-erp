using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class Tenant : BaseEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string BizNo { get; set; } = string.Empty;
    public string CeoName { get; set; } = string.Empty;
    public string? Tel { get; set; }
    public string? Address { get; set; }
    public string? ResellerId { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public DateTime? TrialEndsAt { get; set; }
    public string DbHost { get; set; } = string.Empty;
    public string DbName { get; set; } = string.Empty;
    public string LicenseKeyHash { get; set; } = string.Empty;
    public byte ResellerTier { get; set; }
}
