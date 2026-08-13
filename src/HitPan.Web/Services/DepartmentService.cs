using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 부서 마스터 클라이언트. 작(2026-08-13) 그룹웨어 단계4 토대.
/// </summary>
/// <remarks>
/// 구조는 <see cref="PositionService"/>(직급)와 같다 — 새 방식을 만들지 않는다.
/// 다만 부서는 <b>서버가 주는 사유</b>(이미 있는 부서·상위 부서 없음·순환)를 화면까지 올려보낸다.
/// </remarks>
public sealed class DepartmentService(HttpClient http, ILogger<DepartmentService> logger)
{
    /// <summary>
    /// 부서 목록(관리 화면용 — 비활성 포함).
    /// </summary>
    /// <remarks>
    /// 🔴 실패 시 <c>null</c>. 빈 리스트가 아니다 — 호출부가 둘을 구분해야 한다.
    /// 실패를 빈 리스트로 돌리면 부서가 있는 회사에서 "부서가 다 지워졌다" 로 오해한다.
    /// </remarks>
    public async Task<List<DepartmentListItemModel>?> GetListAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<DepartmentListItemModel>>("api/departments", ct)
                       .ConfigureAwait(false)
                   ?? new List<DepartmentListItemModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "부서 목록 조회 실패");
            return null;
        }
    }

    public async Task<(bool Ok, string? Message)> CreateAsync(DepartmentEditModel request,
        CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/departments", request, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var message = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("부서 추가 실패 (status={Status})", (int)res.StatusCode);
            return (false, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "부서 추가 실패");
            return (false, "부서를 추가하지 못했습니다. 잠시 후 다시 시도해 주세요.");
        }
    }

    public async Task<(bool Ok, string? Message)> UpdateAsync(string id, DepartmentEditModel request,
        CancellationToken ct = default)
    {
        try
        {
            using var res = await http
                .PutAsJsonAsync($"api/departments/{Uri.EscapeDataString(id)}", request, ct)
                .ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var message = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("부서 수정 실패 (deptId={DeptId}, status={Status})", id, (int)res.StatusCode);
            return (false, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "부서 수정 실패 (deptId={DeptId})", id);
            return (false, "부서를 수정하지 못했습니다. 잠시 후 다시 시도해 주세요.");
        }
    }

    /// <summary>
    /// 부서를 지운다. 사원·하위 부서가 물려 있으면 <b>비활성으로 돌아간다</b>.
    /// </summary>
    public async Task<DepartmentDeleteResultModel> DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http
                .DeleteAsync($"api/departments/{Uri.EscapeDataString(id)}", ct)
                .ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                var message = await ReadMessageAsync(res, ct).ConfigureAwait(false);
                logger.LogWarning("부서 삭제 실패 (deptId={DeptId}, status={Status})", id, (int)res.StatusCode);
                return new DepartmentDeleteResultModel { Deleted = false, Message = message };
            }

            return await res.Content.ReadFromJsonAsync<DepartmentDeleteResultModel>(cancellationToken: ct)
                       .ConfigureAwait(false)
                   ?? new DepartmentDeleteResultModel { Deleted = false, Message = "결과를 확인하지 못했습니다." };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "부서 삭제 실패 (deptId={DeptId})", id);
            return new DepartmentDeleteResultModel
            {
                Deleted = false,
                Message = "부서를 삭제하지 못했습니다. 잠시 후 다시 시도해 주세요."
            };
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
            // 본문이 JSON 이 아닐 수 있다(프록시 오류 페이지 등). 아래 기본 문구로 간다.
        }

        return "요청을 처리하지 못했습니다.";
    }

    private sealed class ErrorResponse
    {
        public string? Message { get; set; }
    }
}
