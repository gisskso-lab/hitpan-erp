using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 사원 CRUD API 클라이언트 서비스이다.
/// </summary>
public sealed class EmployeeService(HttpClient http, ILogger<EmployeeService> logger)
{
    /// <summary>
    /// 사원 목록을 조회한다. <b>기본은 재직자만</b>(사장님 지시 2026-08-14 — 퇴사자 숨김).
    /// </summary>
    /// <param name="includeResigned">
    /// 퇴사자까지 볼지 여부. 사원관리의 "퇴사자 포함" 스위치만 이 값을 켠다.
    /// 🔴 기본이 <c>false</c> 라, 이 값을 안 넘기는 다른 화면들(근로계약서·결재선 등)은
    /// 자동으로 재직자만 보게 된다 — 퇴사자를 새 계약 상대로 고르는 사고를 함께 막는다.
    /// </param>
    public async Task<List<EmployeeListItemModel>> GetListAsync(
        bool includeResigned = false, CancellationToken ct = default)
    {
        try
        {
            var url = includeResigned ? "api/employees?includeResigned=true" : "api/employees";
            return await http.GetFromJsonAsync<List<EmployeeListItemModel>>(url, ct).ConfigureAwait(false)
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
    /// 작(2026-08-12) 그룹웨어 단계0 P0-B: 퇴사 처리 전 사전 점검.
    /// 결재선에 걸려 있거나 진행 중 결재가 있으면 알린다. 막지는 않는다(반자동 원칙).
    /// </summary>
    public async Task<EmployeeResignPrecheckModel?> GetResignPrecheckAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<EmployeeResignPrecheckModel>(
                $"api/employees/{Uri.EscapeDataString(id)}/resign-precheck", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "퇴사 사전점검 조회 실패 (id={EmployeeId})", id);
            return null;
        }
    }

    /// <summary>
    /// 작(2026-08-12) 그룹웨어 단계0 P0-A·C: 퇴사 처리.
    /// </summary>
    /// <remarks>
    /// 기존 <see cref="DeleteAsync"/> 는 사원만 비활성화하고 <b>로그인 계정을 그대로 두었다.</b>
    /// 이 경로는 계정 차단 + 사유·실제 퇴사일까지 기록한다.
    /// </remarks>
    /// <returns>
    /// 성공 여부와, <b>로그인 계정을 실제로 차단했는지</b>. 실패면 null.
    /// 계정 없는 사원(실측 12명 중 11명)에게 "계정도 차단했다"고 안내하지 않기 위해 결과를 받는다.
    /// </returns>
    public async Task<EmployeeResignResultModel?> ResignAsync(string id, DateTime? resignDate,
        string? resignReason, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(
                $"api/employees/{Uri.EscapeDataString(id)}/resign",
                new { ResignDate = resignDate, ResignReason = resignReason },
                ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("퇴사 처리 실패 (id={EmployeeId}, status={Status})", id, res.StatusCode);
                return null;
            }

            return await res.Content
                .ReadFromJsonAsync<EmployeeResignResultModel>(cancellationToken: ct)
                .ConfigureAwait(false)
                ?? new EmployeeResignResultModel();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "퇴사 처리 실패 (id={EmployeeId})", id);
            return null;
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
