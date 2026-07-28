using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class PositionService(HttpClient http)
{
    public async Task<List<PositionListItemModel>> GetListAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<PositionListItemModel>>("api/positions", ct).ConfigureAwait(false)
                   ?? new List<PositionListItemModel>();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PositionService.GetListAsync] {ex.GetType().Name}: {ex.Message}");
            return new List<PositionListItemModel>();
        }
    }

    public async Task<bool> CreateAsync(PositionEditModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/positions", request, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Console.Error.WriteLine($"[PositionService.CreateAsync] {(int)res.StatusCode}: {body}");
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PositionService.CreateAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UpdateAsync(string id, PositionEditModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/positions/{Uri.EscapeDataString(id)}", request, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PositionService.UpdateAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync($"api/positions/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PositionService.DeleteAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
