using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

// ⬛ [낡은 줄 · 화면오류기록띠] public sealed class EmailClientService(HttpClient http)
public sealed class EmailClientService(HttpClient http, ILogger<EmailClientService> logger)
{
    // 화면오류기록띠 넓힘X1(작업지시서 §8-3 대표 ④) — onLoadFailed 를 더했다. 메일 설정 화면이 실패를 알아
    //   빈 SMTP(설정 안 된 것처럼) 만 남기지 않고 쉬운 알림을 띄우게 예외만 넘긴다 · 돌려주는 값은 그대로.
    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<EmailSettingsModel> GetSettingsAsync(CancellationToken ct = default)
    public async Task<EmailSettingsModel> GetSettingsAsync(CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try { return await http.GetFromJsonAsync<EmailSettingsModel>("api/email/settings", ct).ConfigureAwait(false) ?? new(); }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Email.GetSettings] {ex.Message}"); return new(); }
        // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] catch (Exception ex) { logger.LogWarning(ex, "[Email.GetSettings] 조회 실패"); return new(); }
        catch (Exception ex) { logger.LogWarning(ex, "[Email.GetSettings] 조회 실패"); onLoadFailed?.Invoke(ex); return new(); }
    }

    public async Task<bool> UpdateSettingsAsync(UpdateEmailSettingsModel req, CancellationToken ct = default)
    {
        try { using var res = await http.PutAsJsonAsync("api/email/settings", req, ct).ConfigureAwait(false); return res.IsSuccessStatusCode; }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Email.UpdateSettings] {ex.Message}"); return false; }
        catch (Exception ex) { logger.LogWarning(ex, "[Email.UpdateSettings] 저장 실패"); return false; }
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
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Email.GetHistory] {ex.Message}"); return new(); }
        catch (Exception ex) { logger.LogWarning(ex, "[Email.GetHistory] 조회 실패"); return new(); }
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
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Email.DownloadPdf] {ex.Message}"); return null; }
        catch (Exception ex) { logger.LogWarning(ex, "[Email.DownloadPdf] 내려받기 실패"); return null; }
    }
}
