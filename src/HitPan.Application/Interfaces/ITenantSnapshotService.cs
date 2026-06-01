using HitPan.Application.DTOs.Sync;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 백오피스 화면용 — Pull 복사본 (snapshot) 조회 서비스
/// 사장님 결재 2026-06-01 / 헌법 #18·#22 정합
///
/// 본 서비스는 본사 백오피스 전용. tenant_employees_snapshot / tenant_devices_snapshot 조회만.
/// 수정 불가 (UI에서 편집 버튼 0건 강제).
/// </summary>
public interface ITenantSnapshotService
{
    Task<IEnumerable<SyncEmployeeDto>> GetEmployeesAsync(string tenantId, CancellationToken ct = default);
    Task<IEnumerable<SyncDeviceDto>> GetDevicesAsync(string tenantId, CancellationToken ct = default);
    Task<DateTime?> GetLastSyncAtAsync(string tenantId, CancellationToken ct = default);
}
