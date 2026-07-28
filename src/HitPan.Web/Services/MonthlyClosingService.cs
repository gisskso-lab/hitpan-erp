using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>월마감 API 클라이언트</summary>
public sealed class MonthlyClosingService(HttpClient http)
{
    public async Task<List<MonthlyClosingModel>> GetStatusAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<MonthlyClosingModel>>("api/monthly-closing", ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> CloseAsync(string yearMonth, string? memo = null, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/monthly-closing/close", new { yearMonth, memo }, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<bool> ReopenAsync(string yearMonth, string reason, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/monthly-closing/reopen", new { yearMonth, reason }, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }
}
