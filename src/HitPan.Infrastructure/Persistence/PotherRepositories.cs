using System.Data;
using Dapper;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;

namespace HitPan.Infrastructure.Persistence;

// WS-11 정공법 축 5 (사장님 명령 2026-05-14):
// POTHER 4 테이블 Dapper 리포지토리. hp/email은 VARBINARY AES (헌법 #5).
// 마이그 단계에서 INSERT는 MdbMigrationService가 직접 SQL로 처리하고,
// 본 리포지토리는 일반 ERP CRUD 경로(향후 컨트롤러/서비스용)에서 사용.

public sealed class PartnerContactRepository : IPartnerContactRepository
{
    private readonly IDbConnection _db;
    private readonly IBinaryCryptoService _crypto;

    public PartnerContactRepository(IDbConnection db, IBinaryCryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    private sealed class Row
    {
        public string contact_id { get; set; } = string.Empty;
        public string tenant_id { get; set; } = string.Empty;
        public string? partner_id { get; set; }
        public string contact_name { get; set; } = string.Empty;
        public string? company_name { get; set; }
        public string? tel { get; set; }
        public byte[]? hp_encrypted { get; set; }
        public byte[]? email_encrypted { get; set; }
        public string? address { get; set; }
        public string? memo { get; set; }
        public bool is_active { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public string? migrated_source_hash { get; set; }
    }

    private PartnerContact Map(Row r) => new()
    {
        ContactId = r.contact_id,
        TenantId = r.tenant_id,
        PartnerId = r.partner_id,
        ContactName = r.contact_name,
        CompanyName = r.company_name,
        Tel = r.tel,
        Hp = _crypto.DecryptFromBytes(r.hp_encrypted),
        Email = _crypto.DecryptFromBytes(r.email_encrypted),
        Address = r.address,
        Memo = r.memo,
        IsActive = r.is_active,
        CreatedAt = r.created_at,
        UpdatedAt = r.updated_at,
        MigratedSourceHash = r.migrated_source_hash,
    };

    public async Task<PartnerContact?> GetByIdAsync(string tenantId, string contactId, CancellationToken ct = default)
    {
        var row = await _db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT * FROM partner_contacts WHERE tenant_id=@TenantId AND contact_id=@Id LIMIT 1",
            new { TenantId = tenantId, Id = contactId }, cancellationToken: ct)).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<PartnerContact>> ListAsync(string tenantId, int take = 200, CancellationToken ct = default)
    {
        var rows = await _db.QueryAsync<Row>(new CommandDefinition(
            "SELECT * FROM partner_contacts WHERE tenant_id=@TenantId AND is_active=1 ORDER BY contact_name LIMIT @Take",
            new { TenantId = tenantId, Take = take }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public Task<int> InsertAsync(PartnerContact e, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO partner_contacts
              (contact_id, tenant_id, partner_id, contact_name, company_name,
               tel, hp_encrypted, email_encrypted, address, memo,
               is_active, created_at, updated_at, migrated_source_hash)
            VALUES
              (@ContactId, @TenantId, @PartnerId, @ContactName, @CompanyName,
               @Tel, @Hp, @Email, @Address, @Memo,
               @IsActive, @Now, @Now, @Hash)
            """;
        return _db.ExecuteAsync(new CommandDefinition(sql, new
        {
            e.ContactId, e.TenantId, e.PartnerId, e.ContactName, e.CompanyName,
            e.Tel,
            Hp = _crypto.EncryptToBytes(e.Hp),
            Email = _crypto.EncryptToBytes(e.Email),
            e.Address, e.Memo, e.IsActive,
            Now = DateTime.UtcNow,
            Hash = e.MigratedSourceHash,
        }, cancellationToken: ct));
    }

    public Task<int> UpdateAsync(PartnerContact e, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE partner_contacts SET
              partner_id=@PartnerId, contact_name=@ContactName, company_name=@CompanyName,
              tel=@Tel, hp_encrypted=@Hp, email_encrypted=@Email,
              address=@Address, memo=@Memo, is_active=@IsActive, updated_at=@Now
            WHERE tenant_id=@TenantId AND contact_id=@ContactId
            """;
        return _db.ExecuteAsync(new CommandDefinition(sql, new
        {
            e.ContactId, e.TenantId, e.PartnerId, e.ContactName, e.CompanyName,
            e.Tel,
            Hp = _crypto.EncryptToBytes(e.Hp),
            Email = _crypto.EncryptToBytes(e.Email),
            e.Address, e.Memo, e.IsActive,
            Now = DateTime.UtcNow,
        }, cancellationToken: ct));
    }

    public Task<int> DeleteAsync(string tenantId, string contactId, CancellationToken ct = default)
        => _db.ExecuteAsync(new CommandDefinition(
            "UPDATE partner_contacts SET is_active=0, updated_at=@Now WHERE tenant_id=@TenantId AND contact_id=@Id",
            new { TenantId = tenantId, Id = contactId, Now = DateTime.UtcNow }, cancellationToken: ct));
}

public sealed class ServiceTicketRepository : IServiceTicketRepository
{
    private readonly IDbConnection _db;
    public ServiceTicketRepository(IDbConnection db) { _db = db; }

    public Task<ServiceTicket?> GetByIdAsync(string tenantId, string ticketId, CancellationToken ct = default)
        => _db.QuerySingleOrDefaultAsync<ServiceTicket>(new CommandDefinition(
            """
            SELECT ticket_id AS TicketId, tenant_id AS TenantId, service_date AS ServiceDate,
                   partner_id AS PartnerId, item_id AS ItemId, problem_desc AS ProblemDesc,
                   fix_desc AS FixDesc, fee AS Fee, memo AS Memo, is_active AS IsActive,
                   created_at AS CreatedAt, updated_at AS UpdatedAt,
                   migrated_source_hash AS MigratedSourceHash
            FROM service_tickets WHERE tenant_id=@TenantId AND ticket_id=@Id LIMIT 1
            """,
            new { TenantId = tenantId, Id = ticketId }, cancellationToken: ct));

    public async Task<IReadOnlyList<ServiceTicket>> ListAsync(string tenantId, int take = 200, CancellationToken ct = default)
    {
        var rows = await _db.QueryAsync<ServiceTicket>(new CommandDefinition(
            """
            SELECT ticket_id AS TicketId, tenant_id AS TenantId, service_date AS ServiceDate,
                   partner_id AS PartnerId, item_id AS ItemId, problem_desc AS ProblemDesc,
                   fix_desc AS FixDesc, fee AS Fee, memo AS Memo, is_active AS IsActive,
                   created_at AS CreatedAt, updated_at AS UpdatedAt,
                   migrated_source_hash AS MigratedSourceHash
            FROM service_tickets WHERE tenant_id=@TenantId AND is_active=1
            ORDER BY service_date DESC LIMIT @Take
            """,
            new { TenantId = tenantId, Take = take }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public Task<int> InsertAsync(ServiceTicket e, CancellationToken ct = default)
        => _db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO service_tickets
              (ticket_id, tenant_id, service_date, partner_id, item_id,
               problem_desc, fix_desc, fee, memo, is_active,
               created_at, updated_at, migrated_source_hash)
            VALUES
              (@TicketId, @TenantId, @ServiceDate, @PartnerId, @ItemId,
               @ProblemDesc, @FixDesc, @Fee, @Memo, @IsActive,
               @Now, @Now, @MigratedSourceHash)
            """,
            new { e.TicketId, e.TenantId, e.ServiceDate, e.PartnerId, e.ItemId,
                  e.ProblemDesc, e.FixDesc, e.Fee, e.Memo, e.IsActive,
                  Now = DateTime.UtcNow, e.MigratedSourceHash },
            cancellationToken: ct));

    public Task<int> UpdateAsync(ServiceTicket e, CancellationToken ct = default)
        => _db.ExecuteAsync(new CommandDefinition("""
            UPDATE service_tickets SET
              service_date=@ServiceDate, partner_id=@PartnerId, item_id=@ItemId,
              problem_desc=@ProblemDesc, fix_desc=@FixDesc, fee=@Fee, memo=@Memo,
              is_active=@IsActive, updated_at=@Now
            WHERE tenant_id=@TenantId AND ticket_id=@TicketId
            """,
            new { e.TicketId, e.TenantId, e.ServiceDate, e.PartnerId, e.ItemId,
                  e.ProblemDesc, e.FixDesc, e.Fee, e.Memo, e.IsActive,
                  Now = DateTime.UtcNow },
            cancellationToken: ct));

    public Task<int> DeleteAsync(string tenantId, string ticketId, CancellationToken ct = default)
        => _db.ExecuteAsync(new CommandDefinition(
            "UPDATE service_tickets SET is_active=0, updated_at=@Now WHERE tenant_id=@TenantId AND ticket_id=@Id",
            new { TenantId = tenantId, Id = ticketId, Now = DateTime.UtcNow }, cancellationToken: ct));
}

public sealed class DeliveryTrackingRepository : IDeliveryTrackingRepository
{
    private readonly IDbConnection _db;
    public DeliveryTrackingRepository(IDbConnection db) { _db = db; }

    public Task<DeliveryTracking?> GetByIdAsync(string tenantId, string trackingId, CancellationToken ct = default)
        => _db.QuerySingleOrDefaultAsync<DeliveryTracking>(new CommandDefinition(
            """
            SELECT tracking_id AS TrackingId, tenant_id AS TenantId, delivery_date AS DeliveryDate,
                   partner_id AS PartnerId, address AS Address, status AS Status, memo AS Memo,
                   is_active AS IsActive, created_at AS CreatedAt, updated_at AS UpdatedAt,
                   migrated_source_hash AS MigratedSourceHash
            FROM delivery_tracking WHERE tenant_id=@TenantId AND tracking_id=@Id LIMIT 1
            """,
            new { TenantId = tenantId, Id = trackingId }, cancellationToken: ct));

    public async Task<IReadOnlyList<DeliveryTracking>> ListAsync(string tenantId, int take = 200, CancellationToken ct = default)
    {
        var rows = await _db.QueryAsync<DeliveryTracking>(new CommandDefinition(
            """
            SELECT tracking_id AS TrackingId, tenant_id AS TenantId, delivery_date AS DeliveryDate,
                   partner_id AS PartnerId, address AS Address, status AS Status, memo AS Memo,
                   is_active AS IsActive, created_at AS CreatedAt, updated_at AS UpdatedAt,
                   migrated_source_hash AS MigratedSourceHash
            FROM delivery_tracking WHERE tenant_id=@TenantId AND is_active=1
            ORDER BY delivery_date DESC LIMIT @Take
            """,
            new { TenantId = tenantId, Take = take }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public Task<int> InsertAsync(DeliveryTracking e, CancellationToken ct = default)
        => _db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO delivery_tracking
              (tracking_id, tenant_id, delivery_date, partner_id, address,
               status, memo, is_active, created_at, updated_at, migrated_source_hash)
            VALUES
              (@TrackingId, @TenantId, @DeliveryDate, @PartnerId, @Address,
               @Status, @Memo, @IsActive, @Now, @Now, @MigratedSourceHash)
            """,
            new { e.TrackingId, e.TenantId, e.DeliveryDate, e.PartnerId, e.Address,
                  e.Status, e.Memo, e.IsActive, Now = DateTime.UtcNow, e.MigratedSourceHash },
            cancellationToken: ct));

    public Task<int> UpdateAsync(DeliveryTracking e, CancellationToken ct = default)
        => _db.ExecuteAsync(new CommandDefinition("""
            UPDATE delivery_tracking SET
              delivery_date=@DeliveryDate, partner_id=@PartnerId, address=@Address,
              status=@Status, memo=@Memo, is_active=@IsActive, updated_at=@Now
            WHERE tenant_id=@TenantId AND tracking_id=@TrackingId
            """,
            new { e.TrackingId, e.TenantId, e.DeliveryDate, e.PartnerId, e.Address,
                  e.Status, e.Memo, e.IsActive, Now = DateTime.UtcNow },
            cancellationToken: ct));

    public Task<int> DeleteAsync(string tenantId, string trackingId, CancellationToken ct = default)
        => _db.ExecuteAsync(new CommandDefinition(
            "UPDATE delivery_tracking SET is_active=0, updated_at=@Now WHERE tenant_id=@TenantId AND tracking_id=@Id",
            new { TenantId = tenantId, Id = trackingId, Now = DateTime.UtcNow }, cancellationToken: ct));
}

public sealed class CalendarEventRepository : ICalendarEventRepository
{
    private readonly IDbConnection _db;
    public CalendarEventRepository(IDbConnection db) { _db = db; }

    public Task<CalendarEvent?> GetByIdAsync(string tenantId, string eventId, CancellationToken ct = default)
        => _db.QuerySingleOrDefaultAsync<CalendarEvent>(new CommandDefinition(
            """
            SELECT event_id AS EventId, tenant_id AS TenantId, event_date AS EventDate,
                   title AS Title, memo AS Memo, is_active AS IsActive,
                   created_at AS CreatedAt, updated_at AS UpdatedAt,
                   migrated_source_hash AS MigratedSourceHash
            FROM events WHERE tenant_id=@TenantId AND event_id=@Id LIMIT 1
            """,
            new { TenantId = tenantId, Id = eventId }, cancellationToken: ct));

    public async Task<IReadOnlyList<CalendarEvent>> ListAsync(string tenantId, int take = 200, CancellationToken ct = default)
    {
        var rows = await _db.QueryAsync<CalendarEvent>(new CommandDefinition(
            """
            SELECT event_id AS EventId, tenant_id AS TenantId, event_date AS EventDate,
                   title AS Title, memo AS Memo, is_active AS IsActive,
                   created_at AS CreatedAt, updated_at AS UpdatedAt,
                   migrated_source_hash AS MigratedSourceHash
            FROM events WHERE tenant_id=@TenantId AND is_active=1
            ORDER BY event_date DESC LIMIT @Take
            """,
            new { TenantId = tenantId, Take = take }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public Task<int> InsertAsync(CalendarEvent e, CancellationToken ct = default)
        => _db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO events
              (event_id, tenant_id, event_date, title, memo,
               is_active, created_at, updated_at, migrated_source_hash)
            VALUES
              (@EventId, @TenantId, @EventDate, @Title, @Memo,
               @IsActive, @Now, @Now, @MigratedSourceHash)
            """,
            new { e.EventId, e.TenantId, e.EventDate, e.Title, e.Memo,
                  e.IsActive, Now = DateTime.UtcNow, e.MigratedSourceHash },
            cancellationToken: ct));

    public Task<int> UpdateAsync(CalendarEvent e, CancellationToken ct = default)
        => _db.ExecuteAsync(new CommandDefinition("""
            UPDATE events SET
              event_date=@EventDate, title=@Title, memo=@Memo,
              is_active=@IsActive, updated_at=@Now
            WHERE tenant_id=@TenantId AND event_id=@EventId
            """,
            new { e.EventId, e.TenantId, e.EventDate, e.Title, e.Memo, e.IsActive,
                  Now = DateTime.UtcNow },
            cancellationToken: ct));

    public Task<int> DeleteAsync(string tenantId, string eventId, CancellationToken ct = default)
        => _db.ExecuteAsync(new CommandDefinition(
            "UPDATE events SET is_active=0, updated_at=@Now WHERE tenant_id=@TenantId AND event_id=@Id",
            new { TenantId = tenantId, Id = eventId, Now = DateTime.UtcNow }, cancellationToken: ct));
}
