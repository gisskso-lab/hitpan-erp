using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Sync;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 백오피스 Pull 동기화 (사장님 결재 2026-06-01)
/// 헌법 #18·#22: 직원 5컬럼, 기기 3컬럼만 반환. 업무 데이터 절대 금지.
/// 헌법 #20: 활성 사용자/기기만
/// </summary>
public class SyncService : ISyncService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<SyncService> _logger;

    public SyncService(IUnitOfWork uow, ILogger<SyncService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<IEnumerable<SyncEmployeeDto>> GetEmployeesAsync(string tenantId, CancellationToken ct = default)
    {
        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        // 헌법 #18: 5개 컬럼만 (이름·이메일·직급·재직여부)
        // 헌법 #99 패턴 정합: char/varchar(36) → CAST AS CHAR 명시 (Guid 추론 회피)
        const string sql = @"
            SELECT
                CAST(employee_id AS CHAR) AS EmployeeId,
                emp_name AS Name,
                IFNULL(email, '') AS Email,
                position AS Position,
                CASE WHEN resign_date IS NULL THEN 1 ELSE 0 END AS IsActive
            FROM employees
            WHERE tenant_id = @TenantId";

        return await db.QueryAsync<SyncEmployeeDto>(sql, new { TenantId = tenantId });
    }

    public async Task<IEnumerable<SyncDeviceDto>> GetDevicesAsync(string tenantId, CancellationToken ct = default)
    {
        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        // 헌법 #18: 3개 컬럼만 (기기ID·기기명·등록일)
        const string sql = @"
            SELECT
                CAST(device_id AS CHAR) AS DeviceId,
                device_name AS DeviceName,
                registered_at AS RegisteredAt
            FROM tenant_devices
            WHERE tenant_id = @TenantId AND status = 'approved'";

        return await db.QueryAsync<SyncDeviceDto>(sql, new { TenantId = tenantId });
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
