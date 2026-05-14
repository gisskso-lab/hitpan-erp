using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

/// <summary>
/// POTHER.DELIVERY 배송 추적 (WS-11 축 5, 2026-05-14).
/// </summary>
public class DeliveryTracking : BaseEntity, ITenantEntity
{
    public string TrackingId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public DateTime DeliveryDate { get; set; }
    public string? PartnerId { get; set; }
    public string? Address { get; set; }
    public string Status { get; set; } = "pending";
    public string? Memo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? MigratedSourceHash { get; set; }
}
