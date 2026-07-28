using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class BackupService(HttpClient http)
{
    public async Task<BackupSettingsModel> GetSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<BackupSettingsModel>("api/backup/settings", ct).ConfigureAwait(false)
                   ?? new BackupSettingsModel();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BackupService.GetSettingsAsync] {ex.GetType().Name}: {ex.Message}");
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
            Console.Error.WriteLine($"[BackupService.UpdateSettingsAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<List<BackupHistoryModel>> GetHistoryAsync(int limit = 50, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<BackupHistoryModel>>($"api/backup/history?limit={limit}", ct)
                   .ConfigureAwait(false) ?? new();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BackupService.GetHistoryAsync] {ex.GetType().Name}: {ex.Message}");
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
            Console.Error.WriteLine($"[BackupService.RunBackupAsync] {ex.GetType().Name}: {ex.Message}");
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
            Console.Error.WriteLine($"[BackupService.RestoreAsync] {ex.GetType().Name}: {ex.Message}");
            return new RestoreResultModel { Success = false, Error = ex.Message };
        }
    }

    public async Task<List<RestoreHistoryModel>> GetRestoreHistoryAsync(int limit = 50, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<RestoreHistoryModel>>($"api/backup/restore-history?limit={limit}", ct)
                   .ConfigureAwait(false) ?? new();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BackupService.GetRestoreHistoryAsync] {ex.GetType().Name}: {ex.Message}");
            return new();
        }
    }
}
