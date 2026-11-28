using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

// ⬛ [낡은 줄 · 화면오류기록띠] public sealed class BillingService(HttpClient http)
public sealed class BillingService(HttpClient http, ILogger<BillingService> logger)
{
    public async Task<BillingSettingsModel> GetSettingsAsync(CancellationToken ct = default)
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

    public async Task<List<PaymentMethodModel>> GetPaymentMethodsAsync(CancellationToken ct = default)
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

    public async Task<SubscriptionModel?> GetSubscriptionAsync(CancellationToken ct = default)
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
            return null;
        }
    }

    public async Task<List<InvoiceListItemModel>> GetInvoicesAsync(CancellationToken ct = default)
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
            return new List<InvoiceListItemModel>();
        }
    }
}
