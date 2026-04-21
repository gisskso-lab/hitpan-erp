using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>
/// 전자서명 API 클라이언트 서비스이다.
/// - GET  /api/esign?documentType=&documentId= : 서명 이력 조회
/// - POST /api/esign/sign                      : 서명 기록
/// - POST /api/esign/{id}/void                 : 서명 무효화
/// </summary>
public sealed class ESignService(HttpClient http)
{
    /// <summary>
    /// 전자서명 이력을 조회한다.
    /// </summary>
    public async Task<List<ESignHistoryModel>> GetHistoryAsync(
        string? documentType = null,
        string? documentId = null,
        CancellationToken ct = default)
    {
        try
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(documentType)) query.Add($"documentType={Uri.EscapeDataString(documentType)}");
            if (!string.IsNullOrWhiteSpace(documentId)) query.Add($"documentId={Uri.EscapeDataString(documentId)}");
            var url = "api/esign" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
            return await http.GetFromJsonAsync<List<ESignHistoryModel>>(url, ct).ConfigureAwait(false)
                   ?? new List<ESignHistoryModel>();
        }
        catch
        {
            return new List<ESignHistoryModel>();
        }
    }

    /// <summary>
    /// 서명을 기록하고 서명 ID를 반환한다.
    /// </summary>
    public async Task<ESignResponseModel?> SignAsync(ESignRequestModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/esign/sign", request, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadFromJsonAsync<ESignResponseModel>(cancellationToken: ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 서명을 무효화한다. (TenantAdmin)
    /// </summary>
    public async Task<bool> VoidAsync(string esignId, string? reason, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(
                $"api/esign/{Uri.EscapeDataString(esignId)}/void",
                new { reason },
                ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
