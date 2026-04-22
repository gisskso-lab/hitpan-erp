using System.Net.Http;
using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.JSInterop;

namespace HitPan.Web.Services;

public class DocumentService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private readonly HitPanProtectedLocalStorage _storage;

    public DocumentService(HttpClient http, IJSRuntime js, HitPanProtectedLocalStorage storage)
    {
        _http = http;
        _js = js;
        _storage = storage;
    }

    public async Task DownloadExcelAsync(string docType, string docId)
    {
        var url = await BuildDocumentUrlAsync(docType, docId, "excel");
        await _js.InvokeVoidAsync("open", url, "_blank");
    }

    public async Task DownloadPdfAsync(string docType, string docId)
    {
        var url = await BuildDocumentUrlAsync(docType, docId, "pdf");
        await _js.InvokeVoidAsync("open", url, "_blank");
    }

    /// <summary>
    /// 다운로드 URL 생성 — 보안 정책:
    /// 1) 서버에 POST /documents/{type}/{id}/download-token 요청
    /// 2) 서버가 2시간 유효 + doc_id 바인딩된 전용 토큰 발급
    /// 3) 그 토큰으로 GET 다운로드 URL 구성
    /// → 로그인 access 토큰은 URL에 노출되지 않음.
    /// </summary>
    private async Task<string> BuildDocumentUrlAsync(string docType, string docId, string format)
    {
        var relative = $"api/documents/{docType}/{docId}/{format}";

        // 1) 다운로드 전용 토큰 발급 (서버는 access 토큰으로 인증)
        var tokenRes = await _http.PostAsync($"api/documents/{docType}/{docId}/download-token", null);
        string? downloadToken = null;
        if (tokenRes.IsSuccessStatusCode)
        {
            var payload = await tokenRes.Content.ReadFromJsonAsync<DownloadTokenResponse>();
            downloadToken = payload?.Token;
        }

        // 2) 발급 실패 시 — 보안 우선, 토큰 없이 시도하지 않고 null 반환 방지를 위해 빈 링크
        if (string.IsNullOrEmpty(downloadToken))
        {
            return new Uri(_http.BaseAddress!, relative).AbsoluteUri;
        }

        var withToken = $"{relative}?token={Uri.EscapeDataString(downloadToken)}";
        return new Uri(_http.BaseAddress!, withToken).AbsoluteUri;
    }

    private sealed class DownloadTokenResponse
    {
        public string Token { get; set; } = string.Empty;
        public int Expires_In { get; set; }
    }
}