using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Sync;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 백오피스 Pull 복사본 조회 (사장님 결재 2026-06-01)
/// 헌법 #18·#22: snapshot 테이블만, 조회 전용.
/// </summary>
public class TenantSnapshotService : ITenantSnapshotService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<TenantSnapshotService> _logger;

    public TenantSnapshotService(IUnitOfWork uow, ILogger<TenantSnapshotService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<IEnumerable<SyncEmployeeDto>> GetEmployeesAsync(string tenantId, CancellationToken ct = default)
    {
        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        const string sql = @"
            SELECT
                CAST(employee_id AS CHAR) AS EmployeeId,
                name AS Name,
                email AS Email,
                position AS Position,
                CASE WHEN is_active = 1 THEN 1 ELSE 0 END AS IsActive
            FROM tenant_employees_snapshot
            WHERE tenant_id = @TenantId
            ORDER BY synced_at DESC, name";

        return await db.QueryAsync<SyncEmployeeDto>(sql, new { TenantId = tenantId });
    }

    public async Task<IEnumerable<SyncDeviceDto>> GetDevicesAsync(string tenantId, CancellationToken ct = default)
    {
        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        const string sql = @"
            SELECT
                CAST(device_id AS CHAR) AS DeviceId,
                device_name AS DeviceName,
                registered_at AS RegisteredAt
            FROM tenant_devices_snapshot
            WHERE tenant_id = @TenantId
            ORDER BY registered_at DESC";

        return await db.QueryAsync<SyncDeviceDto>(sql, new { TenantId = tenantId });
    }

    public async Task<DateTime?> GetLastSyncAtAsync(string tenantId, CancellationToken ct = default)
    {
        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        // 두 테이블 중 더 최근의 synced_at
        const string sql = @"
            SELECT MAX(synced_at) FROM (
                SELECT synced_at FROM tenant_employees_snapshot WHERE tenant_id = @TenantId
                UNION ALL
                SELECT synced_at FROM tenant_devices_snapshot WHERE tenant_id = @TenantId
            ) t";

        return await db.ExecuteScalarAsync<DateTime?>(sql, new { TenantId = tenantId });
    }

    private static async Task EnsureOpenAsync(IDbConnection db, CancellationToken ct)
    {
        if (db.State == ConnectionState.Open) return;
        if (db is DbConnection c)
            await c.OpenAsync(ct).ConfigureAwait(false);
        else
            db.Open();
    }
}
