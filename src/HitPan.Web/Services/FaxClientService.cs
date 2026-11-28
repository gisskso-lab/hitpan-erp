using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>팩스 API 클라이언트 (20260821작1 W3). EmailClientService 와 동일 골격.</summary>
// ⬛ [낡은 줄 · 화면오류기록띠] public sealed class FaxClientService(HttpClient http)
public sealed class FaxClientService(HttpClient http, ILogger<FaxClientService> logger)
{
    public async Task<FaxSettingsModel> GetSettingsAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<FaxSettingsModel>("api/fax/settings", ct).ConfigureAwait(false) ?? new(); }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Fax.GetSettings] {ex.Message}"); return new(); }
        catch (Exception ex) { logger.LogWarning(ex, "[Fax.GetSettings] 조회 실패"); return new(); }
    }

    public async Task<bool> UpdateSettingsAsync(UpdateFaxSettingsModel req, CancellationToken ct = default)
    {
        try { using var res = await http.PutAsJsonAsync("api/fax/settings", req, ct).ConfigureAwait(false); return res.IsSuccessStatusCode; }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Fax.UpdateSettings] {ex.Message}"); return false; }
        catch (Exception ex) { logger.LogWarning(ex, "[Fax.UpdateSettings] 저장 실패"); return false; }
    }

    public async Task<TestFaxResultModel> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsync("api/fax/settings/test", null, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return new TestFaxResultModel { Success = false, Error = $"HTTP {(int)res.StatusCode}" };
            return await res.Content.ReadFromJsonAsync<TestFaxResultModel>(cancellationToken: ct).ConfigureAwait(false) ?? new();
        }
        catch (Exception ex) { return new TestFaxResultModel { Success = false, Error = ex.Message }; }
    }

    public async Task<SendFaxResultModel> SendDocumentAsync(SendFaxModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/fax/send", req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return new SendFaxResultModel { Success = false, Error = $"HTTP {(int)res.StatusCode}" };
            return await res.Content.ReadFromJsonAsync<SendFaxResultModel>(cancellationToken: ct).ConfigureAwait(false) ?? new();
        }
        catch (Exception ex) { return new SendFaxResultModel { Success = false, Error = ex.Message }; }
    }

    public async Task<List<FaxHistoryModel>> GetHistoryAsync(string? documentType = null, int limit = 100, CancellationToken ct = default)
    {
        try
        {
            var url = $"api/fax/history?limit={limit}";
            if (!string.IsNullOrWhiteSpace(documentType)) url += $"&documentType={Uri.EscapeDataString(documentType)}";
            return await http.GetFromJsonAsync<List<FaxHistoryModel>>(url, ct).ConfigureAwait(false) ?? new();
        }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Fax.GetHistory] {ex.Message}"); return new(); }
        catch (Exception ex) { logger.LogWarning(ex, "[Fax.GetHistory] 조회 실패"); return new(); }
    }
}
