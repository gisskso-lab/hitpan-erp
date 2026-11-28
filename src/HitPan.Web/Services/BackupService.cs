using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

// ⬛ [낡은 줄 · 화면오류기록띠] public sealed class BackupService(HttpClient http)
public sealed class BackupService(HttpClient http, ILogger<BackupService> logger)
{
    // 화면오류기록띠 넓힘X1(작업지시서 §8-3 대표 ④) — 조회 3자리에 onLoadFailed 를 더했다.
    //   실패를 빈 값으로 돌려주는 것은 그대로 · 화면이 「실패」 와 「0건」 을 가를 수 있게 예외만 넘긴다(알림은 화면 몫 · LoadFailedNotice).
    //   백업 화면은 설정 조회가 실패하면 [설정 저장] 을 막는다(빈 값으로 실제 설정 덮어쓰기 방지).
    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<BackupSettingsModel> GetSettingsAsync(CancellationToken ct = default)
    public async Task<BackupSettingsModel> GetSettingsAsync(CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            return await http.GetFromJsonAsync<BackupSettingsModel>("api/backup/settings", ct).ConfigureAwait(false)
                   ?? new BackupSettingsModel();
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BackupService.GetSettingsAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BackupService.GetSettingsAsync] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return new BackupSettingsModel();
        }
    }

    public async Task<bool> UpdateSettingsAsync(BackupSettingsModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync("api/backup/settings", req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BackupService.UpdateSettingsAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BackupService.UpdateSettingsAsync] 저장 실패");
            return false;
        }
    }

    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<List<BackupHistoryModel>> GetHistoryAsync(int limit = 50, CancellationToken ct = default)
    public async Task<List<BackupHistoryModel>> GetHistoryAsync(int limit = 50, CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            return await http.GetFromJsonAsync<List<BackupHistoryModel>>($"api/backup/history?limit={limit}", ct)
                   .ConfigureAwait(false) ?? new();
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BackupService.GetHistoryAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BackupService.GetHistoryAsync] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return new();
        }
    }

    public async Task<RunBackupResultModel> RunBackupAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsync("api/backup/run", null, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return new RunBackupResultModel { Success = false, Error = $"HTTP {(int)res.StatusCode}" };
            return await res.Content.ReadFromJsonAsync<RunBackupResultModel>(cancellationToken: ct).ConfigureAwait(false)
                   ?? new RunBackupResultModel { Success = false, Error = "응답 파싱 실패" };
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BackupService.RunBackupAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BackupService.RunBackupAsync] 실행 실패");
            return new RunBackupResultModel { Success = false, Error = ex.Message };
        }
    }

    public async Task<RestoreResultModel> RestoreAsync(RestoreRequestModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/backup/restore", req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return new RestoreResultModel { Success = false, Error = $"HTTP {(int)res.StatusCode}" };
            return await res.Content.ReadFromJsonAsync<RestoreResultModel>(cancellationToken: ct).ConfigureAwait(false)
                   ?? new RestoreResultModel { Success = false, Error = "응답 파싱 실패" };
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BackupService.RestoreAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BackupService.RestoreAsync] 복원 실패");
            return new RestoreResultModel { Success = false, Error = ex.Message };
        }
    }

    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<List<RestoreHistoryModel>> GetRestoreHistoryAsync(int limit = 50, CancellationToken ct = default)
    public async Task<List<RestoreHistoryModel>> GetRestoreHistoryAsync(int limit = 50, CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            return await http.GetFromJsonAsync<List<RestoreHistoryModel>>($"api/backup/restore-history?limit={limit}", ct)
                   .ConfigureAwait(false) ?? new();
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BackupService.GetRestoreHistoryAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BackupService.GetRestoreHistoryAsync] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return new();
        }
    }
}
