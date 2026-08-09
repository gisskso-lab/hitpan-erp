using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

public sealed class PositionService(HttpClient http, ILogger<PositionService> logger)
{
    /// <summary>
    /// 직급 목록을 조회한다.
    /// </summary>
    /// <remarks>
    /// 🔴 실패 시 <c>null</c> 을 돌려준다. 빈 리스트가 아니다 — 호출부는 반드시 둘을 구분해야 한다.
    /// 종전에는 실패도 빈 리스트로 돌려줘 화면이 빈 표가 됐고,
    /// 직급이 등록된 회사에서 "직급이 다 지워졌다" 로 오해할 수 있었다.
    /// </remarks>
    public async Task<List<PositionListItemModel>?> GetListAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<PositionListItemModel>>("api/positions", ct).ConfigureAwait(false)
                   ?? new List<PositionListItemModel>();
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 진단 근거를 남긴다.
            logger.LogWarning(ex, "직급 목록 조회 실패");
            return null;
        }
    }

    public async Task<bool> CreateAsync(PositionEditModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/positions", request, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                logger.LogWarning("직급 추가 실패 (status={Status}, body={Body})", (int)res.StatusCode, body);
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "직급 추가 실패");
            return false;
        }
    }

    public async Task<bool> UpdateAsync(string id, PositionEditModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/positions/{Uri.EscapeDataString(id)}", request, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "직급 수정 실패 (positionId={PositionId})", id);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync($"api/positions/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "직급 삭제 실패 (positionId={PositionId})", id);
            return false;
        }
    }
}
