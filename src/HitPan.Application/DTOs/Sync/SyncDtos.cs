namespace HitPan.Application.DTOs.Sync;

/// <summary>
/// 백오피스 Pull 동기화 — 직원 5컬럼 (헌법 #18 정합)
/// </summary>
public record SyncEmployeeDto(
    string EmployeeId,
    string Name,
    string Email,
    string? Position,
    bool IsActive
);

/// <summary>
/// 백오피스 Pull 동기화 — 기기 3컬럼 (헌법 #18 정합)
/// </summary>
public record SyncDeviceDto(
    string DeviceId,
    string? DeviceName,
    DateTime RegisteredAt
);
