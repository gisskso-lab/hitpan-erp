using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 전자서명 API 클라이언트 서비스이다.
/// - GET  /api/esign?documentType=&documentId= : 서명 이력 조회
/// - POST /api/esign/sign                      : 서명 기록
/// - POST /api/esign/{id}/void                 : 서명 무효화
/// </summary>
public sealed class ESignService(HttpClient http, ILogger<ESignService> logger)
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
        catch (Exception ex)
        {
            // 작(2026-08-12) 단계0: 빈 catch 봉합(헌법 #15).
            // 🔴 빈 목록을 돌려주면 화면에서 "서버 오류"와 "서명 이력이 없음"이 똑같아 보인다.
            //    최소한 로그에는 남겨야 원인을 추적할 수 있다.
            logger.LogWarning(ex, "전자서명 이력 조회 실패 (documentType={DocumentType}, documentId={DocumentId})",
                documentType, documentId);
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
        catch (Exception ex)
        {
            // 작(2026-08-12) 단계0: 빈 catch 봉합(헌법 #15).
            logger.LogWarning(ex, "전자서명 기록 실패 (documentType={DocumentType})", request.DocumentType);
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
        catch (Exception ex)
        {
            // 작(2026-08-12) 단계0: 빈 catch 봉합(헌법 #15).
            // 조용히 false 만 돌려주면 화면은 "실패"라고만 뜨고 원인이 어디에도 안 남는다.
            logger.LogWarning(ex, "전자서명 무효화 실패 (esignId={EsignId})", esignId);
            return false;
        }
    }
}
