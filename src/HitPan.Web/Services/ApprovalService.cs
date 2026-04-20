using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>결재 API 클라이언트 서비스</summary>
public sealed class ApprovalService(HttpClient http)
{
    // ── 결재 설정 ──

    public async Task<List<ApprovalSettingModel>> GetSettingsAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalSettingModel>>("api/approval/settings", ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> SaveSettingAsync(SaveApprovalSettingModel model, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/approval/settings", model, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    // ── 결재 라인 ──

    public async Task<List<ApprovalLineModel>> GetLinesAsync(string docType, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalLineModel>>($"api/approval/lines/{Uri.EscapeDataString(docType)}", ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> SaveLinesAsync(SaveApprovalLinesModel model, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/approval/lines", model, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    // ── 결재 문서 ──

    public async Task<List<ApprovalDocumentModel>> GetPendingAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalDocumentModel>>("api/approval/pending", ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<List<ApprovalDocumentModel>> GetSentAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalDocumentModel>>("api/approval/sent", ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<List<ApprovalDocumentModel>> GetCompletedAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalDocumentModel>>("api/approval/completed", ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<ApprovalDetailModel?> GetDetailAsync(string approvalId, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<ApprovalDetailModel>($"api/approval/documents/{Uri.EscapeDataString(approvalId)}", ct); }
        catch { return null; }
    }

    public async Task<bool> ProcessAsync(string approvalId, string action, string? comment = null, CancellationToken ct = default)
    {
        try
        {
            using var r = await http.PostAsJsonAsync($"api/approval/documents/{Uri.EscapeDataString(approvalId)}/process",
                new { action, comment }, ct);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
