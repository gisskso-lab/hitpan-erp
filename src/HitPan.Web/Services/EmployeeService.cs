using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 사원 CRUD API 클라이언트 서비스이다.
/// </summary>
public sealed class EmployeeService(HttpClient http, ILogger<EmployeeService> logger)
{
    public async Task<List<EmployeeListItemModel>> GetListAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<EmployeeListItemModel>>("api/employees", ct).ConfigureAwait(false)
                   ?? new List<EmployeeListItemModel>();
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 진단 근거를 남긴다.
            logger.LogWarning(ex, "사원 목록 조회 실패");
            return new List<EmployeeListItemModel>();
        }
    }

    /// <summary>
    /// 사원 목록을 조회한다. 실패 시 <c>null</c> 을 돌려준다.
    /// </summary>
    /// <remarks>
    /// 🔴 위 <see cref="GetListAsync"/> 는 실패를 빈 목록으로 뭉갠다.
    /// 결재 설정 화면처럼 "사원이 0명인가, 못 불러온 건가" 를 구분해야 하는 곳에서는 이쪽을 쓴다.
    /// 기존 메서드는 호출부가 여러 화면에 걸쳐 있어 그대로 둔다(헌법 #1 덮어쓰기 금지).
    /// </remarks>
    public async Task<List<EmployeeListItemModel>?> GetListOrNullAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<EmployeeListItemModel>>("api/employees", ct).ConfigureAwait(false)
                   ?? new List<EmployeeListItemModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사원 목록 조회 실패");
            return null;
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
            // §원칙 #15 빈 catch 금지 — 진단 근거를 남기고 빈 목록 반환(화면은 부서 미선택으로 동작).
            logger.LogWarning(ex, "부서 목록 조회 실패");
            return new List<DepartmentModel>();
        }
    }

    public async Task<EmployeeDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<EmployeeDetailModel>($"api/employees/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사원 조회 실패 (id={EmployeeId})", id);
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
                // 작20260428이7 §원칙 #15 "빈 catch 금지" — 진단 가능한 근거를 남긴다.
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                logger.LogWarning("사원 등록 실패 ({Status}): {Body}", (int)res.StatusCode, body);
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사원 등록 실패");
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사원 수정 실패 (id={EmployeeId})", id);
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사원 삭제 실패 (id={EmployeeId})", id);
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
                logger.LogWarning("연차 저장 실패 ({Status}): {Body}", (int)res.StatusCode, bodyText);
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "연차 저장 실패 (id={EmployeeId})", id);
            return false;
        }
    }
}
