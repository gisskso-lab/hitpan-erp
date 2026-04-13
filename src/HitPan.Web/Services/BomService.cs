using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class BomService(HttpClient http)
{
    public async Task<List<BomListModel>?> GetListAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<BomListModel>>("api/bom", ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<StockAlertModel>?> GetAlertsAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<StockAlertModel>>("api/bom/alerts", ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DismissAlertAsync(string alertId, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsync($"api/bom/alerts/{alertId}/dismiss", null, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> OrderAlertAsync(string alertId, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsync($"api/bom/alerts/{alertId}/order", null, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AssembleAsync(string bomId, decimal produceQty, string? memo = null, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/bom/assemble", new { bomId, produceQty, memo }, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
