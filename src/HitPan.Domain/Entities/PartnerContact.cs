using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

/// <summary>
/// POTHER.DOCNM 명함/연락처 (WS-11 축 5, 2026-05-14).
/// hp/email은 VARBINARY AES-256 컬럼으로 저장 (헌법 #5).
/// </summary>
public class PartnerContact : BaseEntity, ITenantEntity
{
    public string ContactId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string? PartnerId { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? Tel { get; set; }
    public string? Hp { get; set; }       // 평문 (Application 계층에서 AES 복호화 후)
    public string? Email { get; set; }    // 평문 (Application 계층에서 AES 복호화 후)
    public string? Address { get; set; }
    public string? Memo { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>WS-11 축 2: SHA256 멱등 키 (uppercase hex 64자).</summary>
    public string? MigratedSourceHash { get; set; }
}
