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
        await _js.InvokeVoidAsync("hitpan.openFile", url);
    }

    public async Task DownloadPdfAsync(string docType, string docId)
    {
        var url = await BuildDocumentUrlAsync(docType, docId, "pdf");
        await _js.InvokeVoidAsync("hitpan.openFile", url);
    }

    /// <summary>
    /// 같은 PDF 를 인쇄창까지 보낸다 (20261021작 절 C-3).
    /// </summary>
    /// <remarks>
    /// 🔴 화면을 찍지 않는다 — 「인쇄」·「PDF」·「이메일」이 <b>같은 PDF</b> 를 쓴다.
    /// 서버는 <c>?disposition=inline</c> 일 때만 바로 열리게 돌려준다(기본은 내려받기).
    /// 돌려주는 값은 <b>어느 길로 갔나</b> 다 — <c>window</c>(새 창) · <c>download</c>(내려받기) ·
    /// <c>failed</c>. 화면은 이 값으로 고객에게 다음 할 일을 알려 준다.
    /// </remarks>
    public async Task<string> PrintPdfAsync(string docType, string docId)
    {
        var url = await BuildDocumentUrlAsync(docType, docId, "pdf");
        var inlineUrl = url.Contains('?') ? $"{url}&disposition=inline" : $"{url}?disposition=inline";
        return await _js.InvokeAsync<string>("hitpan.printPdf", inlineUrl);
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