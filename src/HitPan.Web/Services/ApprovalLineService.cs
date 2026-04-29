using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class ApprovalLineService(HttpClient http)
{
    public async Task<List<ApprovalLineListItemModel>> GetListAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<ApprovalLineListItemModel>>("api/approval-lines", ct).ConfigureAwait(false)
                   ?? new List<ApprovalLineListItemModel>();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ApprovalLineService.GetListAsync] {ex.GetType().Name}: {ex.Message}");
            return new List<ApprovalLineListItemModel>();
        }
    }

    public async Task<ApprovalLineDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<ApprovalLineDetailModel>($"api/approval-lines/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ApprovalLineService.GetAsync] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> CreateAsync(SaveApprovalLineModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/approval-lines", request, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Console.Error.WriteLine($"[ApprovalLineService.CreateAsync] {(int)res.StatusCode}: {body}");
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ApprovalLineService.CreateAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UpdateAsync(string id, SaveApprovalLineModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/approval-lines/{Uri.EscapeDataString(id)}", request, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ApprovalLineService.UpdateAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync($"api/approval-lines/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ApprovalLineService.DeleteAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
