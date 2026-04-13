using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class SettingsService(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TenantSettingsModel?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<TenantSettingsModel>("api/settings", JsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveAsync(TenantSettingsModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync("api/settings", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> VerifyForcePasswordAsync(string password, CancellationToken ct = default)
    {
        try
        {
            using var res = await http
                .PostAsJsonAsync("api/settings/verify-force-edit-password", new { password }, ct)
                .ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await res.Content.ReadFromJsonAsync<VerifyForceEditPasswordResponse>(JsonOptions, ct)
                .ConfigureAwait(false);
            return body?.Valid == true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class VerifyForceEditPasswordResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
    }
}
