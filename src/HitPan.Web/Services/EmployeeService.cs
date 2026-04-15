using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>
/// 사원 CRUD API 클라이언트 서비스이다.
/// </summary>
public sealed class EmployeeService(HttpClient http)
{
    public async Task<List<EmployeeListItemModel>> GetListAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<EmployeeListItemModel>>("api/employees", ct).ConfigureAwait(false)
                   ?? new List<EmployeeListItemModel>();
        }
        catch
        {
            return new List<EmployeeListItemModel>();
        }
    }

    public async Task<EmployeeDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<EmployeeDetailModel>($"api/employees/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> CreateAsync(EmployeeEditModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/employees", request, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateAsync(string id, EmployeeEditModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/employees/{Uri.EscapeDataString(id)}", request, ct).ConfigureAwait(false);
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
            using var res = await http.DeleteAsync($"api/employees/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

}
