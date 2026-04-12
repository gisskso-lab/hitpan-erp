using System.Net.Http;
using Microsoft.JSInterop;

namespace HitPan.Web.Services;

public class DocumentService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;

    public DocumentService(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    public async Task DownloadExcelAsync(string docType, string docId)
    {
        var url = new Uri(_http.BaseAddress!, $"api/documents/{docType}/{docId}/excel").AbsoluteUri;
        await _js.InvokeVoidAsync("open", url, "_blank");
    }

    public async Task DownloadPdfAsync(string docType, string docId)
    {
        var url = new Uri(_http.BaseAddress!, $"api/documents/{docType}/{docId}/pdf").AbsoluteUri;
        await _js.InvokeVoidAsync("open", url, "_blank");
    }
}