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

    // ── 결재관리 필터 (작20260824작1 ②) ──

    /// <summary>
    /// 결재관리 목록 — <b>필터1(scope) × 필터2(docType)</b>.
    /// 실패는 <c>null</c> 로 알린다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>빈 목록으로 뭉개지 않는다.</b> 위 <see cref="GetSettingsOrNullAsync"/> 주석과 같은
    /// 이유다 — "결재가 없다" 와 "못 불러왔다" 는 다른 사실인데, 둘 다 빈 표로 보이면
    /// 사장님은 결재가 사라진 줄 아신다.
    /// </remarks>
    public async Task<List<ApprovalDocumentModel>?> GetDocumentsAsync(
        string scope, string? docType = null, CancellationToken ct = default)
    {
        var url = $"api/approval/documents?scope={Uri.EscapeDataString(scope)}";
        if (!string.IsNullOrEmpty(docType)) url += $"&docType={Uri.EscapeDataString(docType)}";

        try { return await http.GetFromJsonAsync<List<ApprovalDocumentModel>>(url, ct) ?? new(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "결재관리 목록 조회 실패 scope={Scope} docType={DocType}", scope, docType);
            return null;
        }
    }

    /// <summary>필터2 콤보 항목 — 문서종류 목록(그룹웨어 것만).</summary>
    /// <remarks>
    /// 🔴 화면이 목록을 손으로 갖지 않는다. 서버가 라벨 사전을 순회해 내려주므로
    /// 문서종류가 늘어도 필터2가 <b>따라온다.</b>
    /// </remarks>
    public async Task<List<ApprovalDocTypeModel>> GetFilterDocTypesAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ApprovalDocTypeModel>>("api/approval/doc-types", ct) ?? new(); }
        catch (Exception ex) { logger.LogWarning(ex, "결재 문서종류 목록 조회 실패"); return new(); }
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
