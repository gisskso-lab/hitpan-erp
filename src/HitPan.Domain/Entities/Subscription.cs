using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class Subscription : BaseEntity, ITenantEntity
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public PlanType PlanType { get; set; }
    public byte BaseUsers { get; set; }
    public byte ExtraUsers { get; set; }
    public int BaseFee { get; set; }
    public int ExtraFeePerUser { get; set; }
    public string BillingCycle { get; set; } = "monthly";
    public DateTime StartedAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime NextBillingAt { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
}
