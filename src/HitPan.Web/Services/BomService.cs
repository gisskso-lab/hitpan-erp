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

    public async Task<BomDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<BomDetailModel>($"api/bom/{Uri.EscapeDataString(id)}", ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool ok, string? bomId)> CreateAsync(CreateBomModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/bom", model, ct);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[BomService.CreateAsync] {(int)res.StatusCode}: {err}");
                return (false, null);
            }
            var body = await res.Content.ReadFromJsonAsync<BomCreateResult>(cancellationToken: ct);
            return (true, body?.Id);
        }
        catch
        {
            return (false, null);
        }
    }

    public async Task<bool> UpdateAsync(string id, CreateBomModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/bom/{Uri.EscapeDataString(id)}", model, ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync($"api/bom/{Uri.EscapeDataString(id)}", ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private class BomCreateResult { public string Id { get; set; } = ""; }

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

    public async Task<bool> RegisterBomAsItemAsync(string bomId, string itemType, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(
                $"api/bom/{Uri.EscapeDataString(bomId)}/register-item",
                new { itemType }, ct);
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
