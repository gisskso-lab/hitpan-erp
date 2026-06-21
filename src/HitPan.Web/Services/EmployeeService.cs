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
        catch (Exception)
        {
            return new List<EmployeeListItemModel>();
        }
    }

    /// <summary>
    /// 봉합 (2026-06-22, 10차 P1-1): 부서 드롭다운 목록 조회 (읽기 전용).
    /// 사원 부서는 백엔드가 dept_id 로 저장하므로, 화면 선택지를 채우기 위해 부서 목록을 받는다.
    /// </summary>
    public async Task<List<DepartmentModel>> GetDepartmentsAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<DepartmentModel>>("api/employees/departments", ct).ConfigureAwait(false)
                   ?? new List<DepartmentModel>();
        }
        catch (Exception ex)
        {
            // §원칙 #15 빈 catch 금지 — 진단 메시지 출력 후 빈 목록 반환(화면은 부서 미선택으로 동작).
            Console.Error.WriteLine($"[EmployeeService.GetDepartmentsAsync] EXCEPTION: {ex.GetType().Name} {ex.Message}");
            return new List<DepartmentModel>();
        }
    }

    public async Task<EmployeeDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<EmployeeDetailModel>($"api/employees/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
        }
        catch (Exception)
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
        catch (Exception)
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
        catch (Exception)
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
