using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class PermissionService(HttpClient http)
{
    public async Task<List<UserPermissionModel>?> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
            return await http
                .GetFromJsonAsync<List<UserPermissionModel>>("api/permissions", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<UserPermissionModel?> GetAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            return await http
                .GetFromJsonAsync<UserPermissionModel>($"api/permissions/{Uri.EscapeDataString(userId)}", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveAsync(SavePermissionsModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http
                .PostAsJsonAsync("api/permissions", model, ct)
                .ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
