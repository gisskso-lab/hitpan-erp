using System.Net.Http.Json;
using static HitPan.Web.Pages.SettingsUi.TaxInvoiceCertPage;

namespace HitPan.Web.Services;

/// <summary>
/// 전자세금계산서 인증서 등록·관리 API 클라이언트 — 작B v3.0 방식 A
/// </summary>
public sealed class TaxInvoiceCertClientService(HttpClient http)
{
    public async Task<CertInfoDto?> GetCertInfoAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<CertInfoDto>("api/settings/tax-invoice-cert", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TaxInvoiceCert.GetInfo] {ex.Message}");
            return null;
        }
    }

    public async Task<SecurityStatusDto> GetSecurityStatusAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<SecurityStatusDto>("api/settings/tax-invoice-cert/security-status", ct)
                .ConfigureAwait(false) ?? new();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TaxInvoiceCert.GetSecurityStatus] {ex.Message}");
            return new();
        }
    }

    public async Task<RegisterResult> RegisterCertAsync(byte[] pfxBytes, string userPin, CancellationToken ct = default)
    {
        try
        {
            var req = new { PfxBytes = pfxBytes, UserPin = userPin };
            using var res = await http.PostAsJsonAsync("api/settings/tax-invoice-cert/register", req, ct)
                .ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                var error = await res.Content.ReadFromJsonAsync<RegisterResult>(cancellationToken: ct).ConfigureAwait(false);
                return error ?? new RegisterResult { Success = false, ErrorMessage = $"HTTP {(int)res.StatusCode}" };
            }
            return await res.Content.ReadFromJsonAsync<RegisterResult>(cancellationToken: ct)
                .ConfigureAwait(false) ?? new() { Success = true };
        }
        catch (Exception ex)
        {
            return new RegisterResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<WatchdogReportDto?> GetWatchdogReportAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<WatchdogReportDto>("api/settings/tax-invoice-cert/watchdog", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TaxInvoiceCert.Watchdog] {ex.Message}");
            return null;
        }
    }

    public async Task<RegisterResult> RemoveCertAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync("api/settings/tax-invoice-cert", ct).ConfigureAwait(false);
            return await res.Content.ReadFromJsonAsync<RegisterResult>(cancellationToken: ct)
                .ConfigureAwait(false) ?? new() { Success = res.IsSuccessStatusCode };
        }
        catch (Exception ex)
        {
            return new RegisterResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public sealed record RegisterResult
    {
        public bool Success { get; init; }
        public string? Message { get; init; }
        public string? ErrorMessage { get; init; }
    }
}
