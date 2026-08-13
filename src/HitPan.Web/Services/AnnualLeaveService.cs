using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 연차 엔진 클라이언트. 작(2026-08-13) 그룹웨어 단계5.
/// </summary>
/// <remarks>
/// 🔴 반자동 3단 — <see cref="SuggestAsync"/> 는 <b>보여주기만</b> 하고
/// <see cref="ConfirmAsync"/> 를 불러야 잔여에 반영된다.
/// </remarks>
public sealed class AnnualLeaveService(HttpClient http, ILogger<AnnualLeaveService> logger)
{
    /// <summary>
    /// ① 제안 — 계산 결과를 받는다. 저장되지 않는다.
    /// </summary>
    /// <remarks>
    /// 🔴 실패 시 <c>null</c>. 빈 목록이 아니다 — 호출부가 둘을 구분해야 한다.
    /// 실패를 빈 목록으로 뭉개면 "직원이 없다" 로 보인다.
    /// </remarks>
    public async Task<List<AnnualLeaveSuggestionModel>?> SuggestAsync(int year,
        string? employeeId = null, CancellationToken ct = default)
    {
        try
        {
            var url = $"api/annual-leave/suggest?year={year}";
            if (!string.IsNullOrWhiteSpace(employeeId))
            {
                url += $"&employeeId={Uri.EscapeDataString(employeeId)}";
            }

            var list = await http.GetFromJsonAsync<List<AnnualLeaveSuggestionModel>>(url, ct)
                .ConfigureAwait(false) ?? new List<AnnualLeaveSuggestionModel>();

            // ② 사람이 고칠 값의 출발점 — 제안값으로 채워 둔다.
            foreach (var s in list)
            {
                s.EditDays = s.ExistingGrantedDays ?? s.SuggestedDays;
            }

            return list;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "연차 제안 조회 실패 (year={Year})", year);
            return null;
        }
    }

    /// <summary>②③ 수정 + 확정.</summary>
    public async Task<(bool Ok, string? Message)> ConfirmAsync(ConfirmAnnualLeaveModel request,
        CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/annual-leave/confirm", request, ct)
                .ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var message = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("연차 확정 실패 (empId={EmpId}, status={Status})",
                request.EmployeeId, (int)res.StatusCode);
            return (false, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "연차 확정 실패 (empId={EmpId})", request.EmployeeId);
            return (false, "확정하지 못했습니다. 잠시 후 다시 시도해 주세요.");
        }
    }

    /// <summary>부여 이력.</summary>
    public async Task<List<AnnualLeaveGrantModel>?> GetGrantsAsync(int? year = null,
        string? employeeId = null, CancellationToken ct = default)
    {
        try
        {
            var url = "api/annual-leave/grants";
            var q = new List<string>();
            if (year is not null) q.Add($"year={year}");
            if (!string.IsNullOrWhiteSpace(employeeId)) q.Add($"employeeId={Uri.EscapeDataString(employeeId)}");
            if (q.Count > 0) url += "?" + string.Join("&", q);

            return await http.GetFromJsonAsync<List<AnnualLeaveGrantModel>>(url, ct).ConfigureAwait(false)
                   ?? new List<AnnualLeaveGrantModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "연차 이력 조회 실패");
            return null;
        }
    }

    /// <summary>노무 기준값 — 법이 바뀌면 여기 값을 갈아끼운다.</summary>
    public async Task<List<LaborPolicyModel>?> GetPoliciesAsync(CancellationToken ct = default)
    {
        try
        {
            var list = await http.GetFromJsonAsync<List<LaborPolicyModel>>("api/annual-leave/policies", ct)
                .ConfigureAwait(false) ?? new List<LaborPolicyModel>();

            foreach (var p in list)
            {
                p.EditValue = p.PolicyValue;
            }

            return list;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "노무 기준값 조회 실패");
            return null;
        }
    }

    /// <summary>기준값을 고친다. 새 시행일로 행이 추가된다(옛 값은 남는다).</summary>
    public async Task<(bool Ok, string? Message)> SavePolicyAsync(SaveLaborPolicyModel request,
        CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/annual-leave/policies", request, ct)
                .ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var message = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("기준값 저장 실패 (key={Key}, status={Status})",
                request.PolicyKey, (int)res.StatusCode);
            return (false, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "기준값 저장 실패 (key={Key})", request.PolicyKey);
            return (false, "저장하지 못했습니다. 잠시 후 다시 시도해 주세요.");
        }
    }

    private static async Task<string> ReadMessageAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            var body = await res.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken: ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(body?.Message))
            {
                return body!.Message!;
            }
        }
        catch (Exception)
        {
            // 본문이 JSON 이 아닐 수 있다. 아래 기본 문구로 간다.
        }

        return "요청을 처리하지 못했습니다.";
    }

    private sealed class ErrorResponse
    {
        public string? Message { get; set; }
    }
}
