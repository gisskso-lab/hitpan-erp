using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class TenantProfileService(HttpClient http)
{
    public string? CompanyName { get; private set; }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var me = await http.GetFromJsonAsync<TenantMeClientDto>("api/tenants/me", cancellationToken: ct);
            CompanyName = string.IsNullOrWhiteSpace(me?.CompanyName) ? null : me!.CompanyName;
        }
        catch
        {
            CompanyName = null;
        }
    }

    public void Clear() => CompanyName = null;
}
