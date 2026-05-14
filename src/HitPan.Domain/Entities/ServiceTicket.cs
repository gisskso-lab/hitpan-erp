using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

/// <summary>
/// POTHER.DOCAS AS 티켓 (WS-11 축 5, 2026-05-14).
/// </summary>
public class ServiceTicket : BaseEntity, ITenantEntity
{
    public string TicketId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public DateTime ServiceDate { get; set; }
    public string? PartnerId { get; set; }
    public string? ItemId { get; set; }
    public string? ProblemDesc { get; set; }
    public string? FixDesc { get; set; }
    public decimal Fee { get; set; }
    public string? Memo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? MigratedSourceHash { get; set; }
}
