using HitPan.Domain.Entities;

namespace HitPan.Application.Interfaces;

// WS-11 정공법 축 5 (사장님 명령 2026-05-14):
// POTHER 4 테이블 풀스택 리포지토리. 기본 CRUD만 — 멱등 INSERT는 INSERT IGNORE + migrated_source_hash로.

public interface IPartnerContactRepository
{
    Task<PartnerContact?> GetByIdAsync(string tenantId, string contactId, CancellationToken ct = default);
    Task<IReadOnlyList<PartnerContact>> ListAsync(string tenantId, int take = 200, CancellationToken ct = default);
    Task<int> InsertAsync(PartnerContact entity, CancellationToken ct = default);
    Task<int> UpdateAsync(PartnerContact entity, CancellationToken ct = default);
    Task<int> DeleteAsync(string tenantId, string contactId, CancellationToken ct = default);
}

public interface IServiceTicketRepository
{
    Task<ServiceTicket?> GetByIdAsync(string tenantId, string ticketId, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceTicket>> ListAsync(string tenantId, int take = 200, CancellationToken ct = default);
    Task<int> InsertAsync(ServiceTicket entity, CancellationToken ct = default);
    Task<int> UpdateAsync(ServiceTicket entity, CancellationToken ct = default);
    Task<int> DeleteAsync(string tenantId, string ticketId, CancellationToken ct = default);
}

public interface IDeliveryTrackingRepository
{
    Task<DeliveryTracking?> GetByIdAsync(string tenantId, string trackingId, CancellationToken ct = default);
    Task<IReadOnlyList<DeliveryTracking>> ListAsync(string tenantId, int take = 200, CancellationToken ct = default);
    Task<int> InsertAsync(DeliveryTracking entity, CancellationToken ct = default);
    Task<int> UpdateAsync(DeliveryTracking entity, CancellationToken ct = default);
    Task<int> DeleteAsync(string tenantId, string trackingId, CancellationToken ct = default);
}

public interface ICalendarEventRepository
{
    Task<CalendarEvent?> GetByIdAsync(string tenantId, string eventId, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEvent>> ListAsync(string tenantId, int take = 200, CancellationToken ct = default);
    Task<int> InsertAsync(CalendarEvent entity, CancellationToken ct = default);
    Task<int> UpdateAsync(CalendarEvent entity, CancellationToken ct = default);
    Task<int> DeleteAsync(string tenantId, string eventId, CancellationToken ct = default);
}
