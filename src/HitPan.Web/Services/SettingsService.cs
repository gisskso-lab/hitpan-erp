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
        catch (Exception)
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
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 사업장 기본정보를 tenants 테이블에 반영한다.
    /// </summary>
    public async Task<bool> SaveCompanyAsync(TenantCompanyModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync("api/settings/company", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 사업장 기본정보를 조회한다.
    /// </summary>
    public async Task<TenantCompanyModel?> GetCompanyAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<TenantCompanyModel>("api/settings/company", JsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
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
        catch (Exception)
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
