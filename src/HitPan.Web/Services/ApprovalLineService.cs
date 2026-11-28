using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

// ⬛ [낡은 줄 · 화면오류기록띠] public sealed class ApprovalLineService(HttpClient http)
public sealed class ApprovalLineService(HttpClient http, ILogger<ApprovalLineService> logger)
{
    // 화면오류기록띠 넓힘X1(작업지시서 §8-3 대표 ④) — 결재선(보기 전용) 화면 조회 2자리에 onLoadFailed 를 더했다.
    //   빈 목록만 남지 않게 화면이 쉬운 알림을 띄운다 · 돌려주는 값은 그대로.
    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<List<ApprovalLineListItemModel>> GetListAsync(CancellationToken ct = default)
    public async Task<List<ApprovalLineListItemModel>> GetListAsync(CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            return await http.GetFromJsonAsync<List<ApprovalLineListItemModel>>("api/approval-lines", ct).ConfigureAwait(false)
                   ?? new List<ApprovalLineListItemModel>();
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[ApprovalLineService.GetListAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[ApprovalLineService.GetListAsync] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return new List<ApprovalLineListItemModel>();
        }
    }

    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<ApprovalLineDetailModel?> GetAsync(string id, CancellationToken ct = default)
    public async Task<ApprovalLineDetailModel?> GetAsync(string id, CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            return await http.GetFromJsonAsync<ApprovalLineDetailModel>($"api/approval-lines/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[ApprovalLineService.GetAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[ApprovalLineService.GetAsync] 조회 실패");
            onLoadFailed?.Invoke(ex);
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
                // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[ApprovalLineService.CreateAsync] {(int)res.StatusCode}: {body}");
                logger.LogWarning("[ApprovalLineService.CreateAsync] 서버 거절 ({Status}): {Body}", (int)res.StatusCode, body);
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[ApprovalLineService.CreateAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[ApprovalLineService.CreateAsync] 저장 실패");
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
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[ApprovalLineService.UpdateAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[ApprovalLineService.UpdateAsync] 저장 실패");
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
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[ApprovalLineService.DeleteAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[ApprovalLineService.DeleteAsync] 삭제 실패");
            return false;
        }
    }
}
