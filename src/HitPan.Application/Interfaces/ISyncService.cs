using HitPan.Application.DTOs.Sync;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 백오피스 Pull 동기화 데이터 제공 (사장님 결재 2026-06-01)
/// 헌법 #18·#22 정합: 직원 5컬럼, 기기 3컬럼만
/// </summary>
public interface ISyncService
{
    Task<IEnumerable<SyncEmployeeDto>> GetEmployeesAsync(string tenantId, CancellationToken ct = default);
    Task<IEnumerable<SyncDeviceDto>> GetDevicesAsync(string tenantId, CancellationToken ct = default);
}
