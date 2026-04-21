using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class UserService(HttpClient http)
{
    public async Task<List<UserListModel>?> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<UserListModel>>("api/users", ct).ConfigureAwait(false);
        }
        catch { return null; }
    }

    public async Task<UserListModel?> GetAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<UserListModel>($"api/users/{userId}", ct).ConfigureAwait(false);
        }
        catch { return null; }
    }

    public async Task<(bool ok, string? error)> CreateAsync(CreateUserModel model, CancellationToken ct = default)
    {
        try
        {
            var res = await http.PostAsJsonAsync("api/users", model, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode) return (true, null);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (false, body);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<bool> UpdateAsync(string userId, UpdateUserModel model, CancellationToken ct = default)
    {
        try
        {
            var res = await http.PutAsJsonAsync($"api/users/{userId}", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeactivateAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var res = await http.DeleteAsync($"api/users/{userId}", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<ResetPasswordResponse?> ResetPasswordAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var res = await http.PostAsync($"api/users/{userId}/reset-password", null, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadFromJsonAsync<ResetPasswordResponse>(ct).ConfigureAwait(false);
        }
        catch { return null; }
    }
}
