using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class AccountService(HttpClient http)
{
    public async Task<List<AccountModel>> GetListAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<AccountModel>>("api/finance/accounts", ct).ConfigureAwait(false)
                   ?? new List<AccountModel>();
        }
        catch
        {
            return new List<AccountModel>();
        }
    }

    public async Task<bool> CreateAsync(CreateAccountModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/finance/accounts", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateAsync(string code, UpdateAccountModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/finance/accounts/{Uri.EscapeDataString(code)}", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteAsync(string code, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync($"api/finance/accounts/{Uri.EscapeDataString(code)}", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
