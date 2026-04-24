using System.Net.Http.Json;
using System.Text.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class ItemMasterService(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<ItemListModel>?> GetListAsync(
        string? search = null,
        string? group = null,
        string? type = null,
        bool excludeBom = false,
        CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
            {
                qs.Add("search=" + Uri.EscapeDataString(search.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(group))
            {
                qs.Add("group=" + Uri.EscapeDataString(group.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                qs.Add("type=" + Uri.EscapeDataString(type.Trim()));
            }

            if (excludeBom)
            {
                qs.Add("excludeBom=true");
            }

            var path = "api/items" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
            return await http.GetFromJsonAsync<List<ItemListModel>>(path, JsonOptions, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemMasterService.GetListAsync] Error: {ex.Message}");
            return null;
        }
    }

    public async Task<ItemDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<ItemDetailModel>($"api/items/{Uri.EscapeDataString(id)}", JsonOptions, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemMasterService.GetAsync] Error: {ex.Message}");
            return null;
        }
    }

    public async Task<(bool ok, string? errorMessage)> CreateAsync(ItemDetailModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/items", model, ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[ItemMasterService.CreateAsync] {(int)res.StatusCode}: {body}");
                var msg = ExtractErrorMessage(body);
                return (false, msg);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemMasterService.CreateAsync] Error: {ex.Message}");
            return (false, null);
        }
    }

    public async Task<(bool ok, string? errorMessage)> UpdateAsync(string id, ItemDetailModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/items/{Uri.EscapeDataString(id)}", model, ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[ItemMasterService.UpdateAsync] {(int)res.StatusCode}: {body}");
                var msg = ExtractErrorMessage(body);
                return (false, msg);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemMasterService.UpdateAsync] Error: {ex.Message}");
            return (false, null);
        }
    }

    private static string? ExtractErrorMessage(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msgEl))
                return msgEl.GetString();
        }
        catch { /* JSON이 아닌 응답 — 의도된 무시, 호출자가 기본 메시지 사용 */ }
        return null;
    }

    public async Task<List<ItemSpecialPriceModel>> GetSpecialPricesAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            var list = await http.GetFromJsonAsync<List<ItemSpecialPriceModel>>(
                $"api/items/{Uri.EscapeDataString(itemId)}/special-prices", JsonOptions, ct);
            return list ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemMasterService.GetSpecialPricesAsync] Error: {ex.Message}");
            return new();
        }
    }

    public async Task<bool> UpsertSpecialPriceAsync(string itemId, ItemSpecialPriceModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync(
                $"api/items/{Uri.EscapeDataString(itemId)}/special-prices", model, ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemMasterService.UpsertSpecialPriceAsync] Error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteSpecialPriceAsync(string itemId, string priceId, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync(
                $"api/items/{Uri.EscapeDataString(itemId)}/special-prices/{Uri.EscapeDataString(priceId)}", ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemMasterService.DeleteSpecialPriceAsync] Error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync($"api/items/{Uri.EscapeDataString(id)}", ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[ItemMasterService.DeleteAsync] {(int)res.StatusCode}: {body}");
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemMasterService.DeleteAsync] Error: {ex.Message}");
            return false;
        }
    }
}
