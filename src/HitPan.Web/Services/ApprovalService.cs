using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>결재 API 클라이언트 서비스. 결재 = 권한 상승 경로이므로 빈 catch 금지 (헌법 #15).</summary>
public sealed class ApprovalService(HttpClient http, ILogger<ApprovalService> logger)
{
    // ── 결재 설정 ──

    public async Task<List<ApprovalSettingModel>> GetSettingsAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalSettingModel>>("api/approval/settings", ct) ?? new(); }
        catch (Exception ex) { logger.LogWarning(ex, "GetSettingsAsync failed"); return new(); }
    }

    /// <summary>
    /// 결재 설정을 조회한다. 실패 시 <c>null</c> 을 돌려준다.
    /// </summary>
    /// <remarks>
    /// 🔴 위 <see cref="GetSettingsAsync"/> 는 실패를 빈 목록으로 뭉갠다.
    /// 그래서 화면이 "헤더만 있는 빈 표" 를 그렸고, 사장님은 결재 설정이 지워진 줄 아셨다.
    /// "없다" 와 "못 불러왔다" 는 다른 사실이므로 실패를 <c>null</c> 로 분리해 알린다.
    /// 기존 메서드는 다른 호출부가 있을 수 있어 그대로 둔다(헌법 #1 덮어쓰기 금지).
    /// </remarks>
    public async Task<List<ApprovalSettingModel>?> GetSettingsOrNullAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalSettingModel>>("api/approval/settings", ct) ?? new(); }
        catch (Exception ex) { logger.LogWarning(ex, "결재 설정 조회 실패"); return null; }
    }

    public async Task<bool> SaveSettingAsync(SaveApprovalSettingModel model, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/approval/settings", model, ct); return r.IsSuccessStatusCode; }
        catch (Exception ex) { logger.LogWarning(ex, "SaveSettingAsync failed"); return false; }
    }

    // ── 결재 라인 ──

    public async Task<List<ApprovalLineModel>> GetLinesAsync(string docType, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalLineModel>>($"api/approval/lines/{Uri.EscapeDataString(docType)}", ct) ?? new(); }
        catch (Exception ex) { logger.LogWarning(ex, "GetLinesAsync failed docType={DocType}", docType); return new(); }
    }

    public async Task<bool> SaveLinesAsync(SaveApprovalLinesModel model, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/approval/lines", model, ct); return r.IsSuccessStatusCode; }
        catch (Exception ex) { logger.LogWarning(ex, "SaveLinesAsync failed"); return false; }
    }

    // ── 결재 문서 ──

    public async Task<List<ApprovalDocumentModel>> GetPendingAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalDocumentModel>>("api/approval/pending", ct) ?? new(); }
        catch (Exception ex) { logger.LogWarning(ex, "GetPendingAsync failed"); return new(); }
    }

    public async Task<List<ApprovalDocumentModel>> GetSentAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalDocumentModel>>("api/approval/sent", ct) ?? new(); }
        catch (Exception ex) { logger.LogWarning(ex, "GetSentAsync failed"); return new(); }
    }

    public async Task<List<ApprovalDocumentModel>> GetCompletedAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalDocumentModel>>("api/approval/completed", ct) ?? new(); }
        catch (Exception ex) { logger.LogWarning(ex, "GetCompletedAsync failed"); return new(); }
    }

    public async Task<ApprovalDetailModel?> GetDetailAsync(string approvalId, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<ApprovalDetailModel>($"api/approval/documents/{Uri.EscapeDataString(approvalId)}", ct); }
        catch (Exception ex) { logger.LogWarning(ex, "GetDetailAsync failed approvalId={ApprovalId}", approvalId); return null; }
    }

    public async Task<bool> ProcessAsync(string approvalId, string action, string? comment = null, CancellationToken ct = default)
    {
        try
        {
            using var r = await http.PostAsJsonAsync($"api/approval/documents/{Uri.EscapeDataString(approvalId)}/process",
                new { action, comment }, ct);
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) { logger.LogWarning(ex, "ProcessAsync failed approvalId={ApprovalId} action={Action}", approvalId, action); return false; }
    }
}
