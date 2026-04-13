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
}
