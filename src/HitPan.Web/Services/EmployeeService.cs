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
            if (!res.IsSuccessStatusCode)
            {
                // 작20260428이7 §원칙 #15 "빈 catch 금지" — 진단 가능한 메시지 던짐.
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Console.Error.WriteLine($"[EmployeeService.CreateAsync] {(int)res.StatusCode} {res.StatusCode}: {body}");
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[EmployeeService.CreateAsync] EXCEPTION: {ex.GetType().Name} {ex.Message}");
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

    /// <summary>
    /// 작20260429 연차 관리 (사장님 결재): 부여·사용 일수만 단독 저장.
    /// 사원관리 그리드의 연차 컬럼 인라인 편집 후 호출.
    /// </summary>
    public async Task<bool> UpdateAnnualLeaveAsync(string id, decimal total, decimal used, CancellationToken ct = default)
    {
        try
        {
            var body = new UpdateAnnualLeaveModel { AnnualLeaveTotal = total, AnnualLeaveUsed = used };
            using var res = await http.PutAsJsonAsync(
                $"api/employees/{Uri.EscapeDataString(id)}/annual-leave", body, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var bodyText = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Console.Error.WriteLine($"[EmployeeService.UpdateAnnualLeaveAsync] {(int)res.StatusCode}: {bodyText}");
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[EmployeeService.UpdateAnnualLeaveAsync] EXCEPTION: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }
}
