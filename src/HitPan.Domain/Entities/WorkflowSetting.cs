using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

public class WorkflowSetting : BaseEntity, ITenantEntity
{
    public string SettingId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string ValueType { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
