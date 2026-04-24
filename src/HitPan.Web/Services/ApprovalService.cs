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
