using HitPan.Application.DTOs.Backup;

namespace HitPan.Application.Interfaces;

/// <summary>자료 백업·복원 서비스 (사장님 결재 2026-04-29 — 로컬 미러 백업).</summary>
public interface IBackupService
{
    Task<BackupSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default);
    Task UpdateSettingsAsync(string tenantId, UpdateBackupSettingsRequest req, CancellationToken ct = default);
    Task<List<BackupHistoryDto>> GetHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default);
    Task<RunBackupResponse> RunBackupAsync(string tenantId, string triggeredBy = "manual", CancellationToken ct = default);
    Task<RestoreResponse> RestoreAsync(string tenantId, string? userId, RestoreRequest req, CancellationToken ct = default);
    Task<List<RestoreHistoryDto>> GetRestoreHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default);
}
