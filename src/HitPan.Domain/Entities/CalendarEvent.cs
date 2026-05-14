using HitPan.Domain.Common;

namespace HitPan.Domain.Entities;

/// <summary>
/// POTHER.CALENDAR 일정/달력 (WS-11 축 5, 2026-05-14).
/// 클래스명은 .NET 예약어 'Event' 충돌 회피로 CalendarEvent. 테이블명은 events.
/// </summary>
public class CalendarEvent : BaseEntity, ITenantEntity
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? MigratedSourceHash { get; set; }
}
