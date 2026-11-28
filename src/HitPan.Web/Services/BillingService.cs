using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

// ⬛ [낡은 줄 · 화면오류기록띠] public sealed class BillingService(HttpClient http)
public sealed class BillingService(HttpClient http, ILogger<BillingService> logger)
{
    // 화면오류기록띠 넓힘X1(작업지시서 §8-3 대표 ④) — 구독 결제 화면 조회 4자리에 onLoadFailed 를 더했다.
    //   돌려주는 값은 그대로 · 화면이 실패를 알아 「활성 구독이 없습니다」 같은 거짓 안내 대신 쉬운 알림을 띄우게 예외만 넘긴다.
    //   (사용자정보 화면의 구독 조회는 넘기지 않음 — 그 화면은 null 을 「확인할 수 없습니다」 로 이미 보여 준다)
    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<BillingSettingsModel> GetSettingsAsync(CancellationToken ct = default)
    public async Task<BillingSettingsModel> GetSettingsAsync(CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            return await http.GetFromJsonAsync<BillingSettingsModel>("api/billing/settings", ct).ConfigureAwait(false)
                   ?? new BillingSettingsModel();
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BillingService.GetSettingsAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BillingService.GetSettingsAsync] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return new BillingSettingsModel();
        }
    }

    public async Task<bool> UpdateSettingsAsync(BillingSettingsModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync("api/billing/settings", request, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BillingService.UpdateSettingsAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BillingService.UpdateSettingsAsync] 저장 실패");
            return false;
        }
    }

    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<List<PaymentMethodModel>> GetPaymentMethodsAsync(CancellationToken ct = default)
    public async Task<List<PaymentMethodModel>> GetPaymentMethodsAsync(CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            return await http.GetFromJsonAsync<List<PaymentMethodModel>>("api/billing/payment-methods", ct).ConfigureAwait(false)
                   ?? new List<PaymentMethodModel>();
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BillingService.GetPaymentMethodsAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BillingService.GetPaymentMethodsAsync] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return new List<PaymentMethodModel>();
        }
    }

    public async Task<bool> RegisterPaymentMethodAsync(RegisterPaymentMethodModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/billing/payment-methods", request, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BillingService.RegisterPaymentMethodAsync] {(int)res.StatusCode}: {body}");
                logger.LogWarning("[BillingService.RegisterPaymentMethodAsync] 서버 거절 ({Status}): {Body}", (int)res.StatusCode, body);
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BillingService.RegisterPaymentMethodAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BillingService.RegisterPaymentMethodAsync] 등록 실패");
            return false;
        }
    }

    public async Task<bool> SetDefaultAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsync($"api/billing/payment-methods/{Uri.EscapeDataString(id)}/default", null, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BillingService.SetDefaultAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BillingService.SetDefaultAsync] 저장 실패");
            return false;
        }
    }

    public async Task<bool> DeletePaymentMethodAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync($"api/billing/payment-methods/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BillingService.DeletePaymentMethodAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BillingService.DeletePaymentMethodAsync] 삭제 실패");
            return false;
        }
    }

    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<SubscriptionModel?> GetSubscriptionAsync(CancellationToken ct = default)
    public async Task<SubscriptionModel?> GetSubscriptionAsync(CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            var res = await http.GetAsync("api/billing/subscription", ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
            return await res.Content.ReadFromJsonAsync<SubscriptionModel>(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BillingService.GetSubscriptionAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BillingService.GetSubscriptionAsync] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return null;
        }
    }

    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] public async Task<List<InvoiceListItemModel>> GetInvoicesAsync(CancellationToken ct = default)
    public async Task<List<InvoiceListItemModel>> GetInvoicesAsync(CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            return await http.GetFromJsonAsync<List<InvoiceListItemModel>>("api/billing/invoices", ct).ConfigureAwait(false)
                   ?? new List<InvoiceListItemModel>();
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[BillingService.GetInvoicesAsync] {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "[BillingService.GetInvoicesAsync] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return new List<InvoiceListItemModel>();
        }
    }
}
