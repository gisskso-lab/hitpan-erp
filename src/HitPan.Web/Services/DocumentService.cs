using System.Net.Http;
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

    private async Task<string> BuildDocumentUrlAsync(string docType, string docId, string format)
    {
        var relative = $"api/documents/{docType}/{docId}/{format}";
        var tokenResult = await _storage.GetAsync<string>(AuthStorageKeys.AccessToken);
        var token = tokenResult.Success ? tokenResult.Value : null;
        if (string.IsNullOrEmpty(token))
        {
            return new Uri(_http.BaseAddress!, relative).AbsoluteUri;
        }

        var withToken = $"{relative}?token={Uri.EscapeDataString(token)}";
        return new Uri(_http.BaseAddress!, withToken).AbsoluteUri;
    }
}