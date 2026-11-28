using System.Net.Http.Json;
using static HitPan.Web.Pages.SettingsUi.TaxInvoiceCertPage;

namespace HitPan.Web.Services;

/// <summary>
/// 전자세금계산서 인증서 등록·관리 API 클라이언트 — 작B v3.0 방식 A
/// </summary>
// ⬛ [낡은 줄 · 화면오류기록띠] public sealed class TaxInvoiceCertClientService(HttpClient http)
public sealed class TaxInvoiceCertClientService(HttpClient http, ILogger<TaxInvoiceCertClientService> logger)
{
    // 화면오류기록띠 넓힘X1(작업지시서 §8-3 대표 ④) — 인증서 화면 조회 3자리에 onLoadFailed 를 더했다.
    //   돌려주는 값은 그대로 · 화면이 실패를 알아 「신규 등록」 꼴 · 거짓 초록 보안 상태 대신 쉬운 알림을 띄우게 예외만 넘긴다.
    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<CertInfoDto?> GetCertInfoAsync(CancellationToken ct = default)
    public async Task<CertInfoDto?> GetCertInfoAsync(CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] return await http.GetFromJsonAsync<CertInfoDto>("api/settings/tax-invoice-cert", ct).ConfigureAwait(false);
            // 인증서가 없으면 서버가 Ok(null) = 204(빈 본문)를 준다(TaxInvoiceCertController.GetInfo). 그건 실패가 아니라 「없음」 이다 —
            //   종전 꼴(GetFromJsonAsync)은 빈 본문을 못 읽어 예외 → catch 로 갔으므로, 실패 알림이 「없음」 에 뜨지 않게 204 를 먼저 가른다.
            //   200 · 그 밖 실패는 종전과 같은 결과(200 = 읽은 값 · 실패 = 예외 → catch).
            using var res = await http.GetAsync("api/settings/tax-invoice-cert", ct).ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<CertInfoDto>(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[TaxInvoiceCert.GetInfo] {ex.Message}");
            logger.LogWarning(ex, "[TaxInvoiceCert.GetInfo] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return null;
        }
    }

    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<SecurityStatusDto> GetSecurityStatusAsync(CancellationToken ct = default)
    public async Task<SecurityStatusDto> GetSecurityStatusAsync(CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            return await http.GetFromJsonAsync<SecurityStatusDto>("api/settings/tax-invoice-cert/security-status", ct)
                .ConfigureAwait(false) ?? new();
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[TaxInvoiceCert.GetSecurityStatus] {ex.Message}");
            logger.LogWarning(ex, "[TaxInvoiceCert.GetSecurityStatus] 조회 실패");
            onLoadFailed?.Invoke(ex);
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

    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<WatchdogReportDto?> GetWatchdogReportAsync(CancellationToken ct = default)
    public async Task<WatchdogReportDto?> GetWatchdogReportAsync(CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            return await http.GetFromJsonAsync<WatchdogReportDto>("api/settings/tax-invoice-cert/watchdog", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[TaxInvoiceCert.Watchdog] {ex.Message}");
            logger.LogWarning(ex, "[TaxInvoiceCert.Watchdog] 조회 실패");
            onLoadFailed?.Invoke(ex);
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
