using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class EmailClientService(HttpClient http)
{
    public async Task<EmailSettingsModel> GetSettingsAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<EmailSettingsModel>("api/email/settings", ct).ConfigureAwait(false) ?? new(); }
        catch (Exception ex) { Console.Error.WriteLine($"[Email.GetSettings] {ex.Message}"); return new(); }
    }

    public async Task<bool> UpdateSettingsAsync(UpdateEmailSettingsModel req, CancellationToken ct = default)
    {
        try { using var res = await http.PutAsJsonAsync("api/email/settings", req, ct).ConfigureAwait(false); return res.IsSuccessStatusCode; }
        catch (Exception ex) { Console.Error.WriteLine($"[Email.UpdateSettings] {ex.Message}"); return false; }
    }

    public async Task<TestSmtpResultModel> TestSmtpAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsync("api/email/settings/test", null, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return new TestSmtpResultModel { Success = false, Error = $"HTTP {(int)res.StatusCode}" };
            return await res.Content.ReadFromJsonAsync<TestSmtpResultModel>(cancellationToken: ct).ConfigureAwait(false) ?? new();
        }
        catch (Exception ex) { return new TestSmtpResultModel { Success = false, Error = ex.Message }; }
    }

    public async Task<SendEmailResultModel> SendDocumentAsync(SendDocumentEmailModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/email/send", req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return new SendEmailResultModel { Success = false, Error = $"HTTP {(int)res.StatusCode}" };
            return await res.Content.ReadFromJsonAsync<SendEmailResultModel>(cancellationToken: ct).ConfigureAwait(false) ?? new();
        }
        catch (Exception ex) { return new SendEmailResultModel { Success = false, Error = ex.Message }; }
    }

    public async Task<List<EmailHistoryRowModel>> GetHistoryAsync(string? documentType, int limit = 100, CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string> { $"limit={limit}" };
            if (!string.IsNullOrEmpty(documentType)) qs.Add($"documentType={Uri.EscapeDataString(documentType)}");
            var url = "api/email/history?" + string.Join("&", qs);
            return await http.GetFromJsonAsync<List<EmailHistoryRowModel>>(url, ct).ConfigureAwait(false) ?? new();
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Email.GetHistory] {ex.Message}"); return new(); }
    }

    /// <summary>거래처 이메일 lookup (Dialog 자동채움).</summary>
    public async Task<string> GetPartnerEmailAsync(string partnerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(partnerId)) return "";
        try
        {
            var resp = await http.GetFromJsonAsync<PartnerEmailLookup>($"api/email/partner-email?partnerId={Uri.EscapeDataString(partnerId)}", ct).ConfigureAwait(false);
            return resp?.Email ?? "";
        }
        catch { return ""; }
    }

    private sealed class PartnerEmailLookup { public string Email { get; set; } = ""; public string PartnerName { get; set; } = ""; }

    /// <summary>PDF 미리보기 다운로드 — Blob URL 또는 byte[] 반환.</summary>
    public async Task<byte[]?> DownloadPdfAsync(string documentType, string documentId, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.GetAsync($"api/email/preview-pdf?documentType={Uri.EscapeDataString(documentType)}&documentId={Uri.EscapeDataString(documentId)}", ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Email.DownloadPdf] {ex.Message}"); return null; }
    }
}
