using System.Net.Http.Json;
using System.Text.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class PartnerMasterService(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<PartnerListRow>> GetListAsync(
        string? search = null,
        string? type = null,
        CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
            {
                qs.Add("search=" + Uri.EscapeDataString(search.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                qs.Add("type=" + Uri.EscapeDataString(type.Trim()));
            }

            var path = "api/partners" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
            var list = await http.GetFromJsonAsync<List<PartnerListRow>>(path, JsonOptions, ct).ConfigureAwait(false);
            return list ?? new List<PartnerListRow>();
        }
        catch
        {
            return new List<PartnerListRow>();
        }
    }

    public async Task<PartnerDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<PartnerDetailModel>($"api/partners/{Uri.EscapeDataString(id)}", JsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> CreateAsync(PartnerDetailModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/partners", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateAsync(string id, PartnerDetailModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/partners/{Uri.EscapeDataString(id)}", model, ct).ConfigureAwait(false);
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
            using var res = await http.DeleteAsync($"api/partners/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<PartnerListRow>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<PartnerListRow>();
        }

        return await GetListAsync(query, null, ct).ConfigureAwait(false);
    }
}
