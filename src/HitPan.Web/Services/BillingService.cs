using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class BillingService(HttpClient http)
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
            Console.Error.WriteLine($"[BillingService.GetSettingsAsync] {ex.GetType().Name}: {ex.Message}");
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
            Console.Error.WriteLine($"[BillingService.UpdateSettingsAsync] {ex.GetType().Name}: {ex.Message}");
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
            Console.Error.WriteLine($"[BillingService.GetPaymentMethodsAsync] {ex.GetType().Name}: {ex.Message}");
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
                Console.Error.WriteLine($"[BillingService.RegisterPaymentMethodAsync] {(int)res.StatusCode}: {body}");
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BillingService.RegisterPaymentMethodAsync] {ex.GetType().Name}: {ex.Message}");
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
            Console.Error.WriteLine($"[BillingService.SetDefaultAsync] {ex.GetType().Name}: {ex.Message}");
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
            Console.Error.WriteLine($"[BillingService.DeletePaymentMethodAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<SubscriptionModel?> GetSubscriptionAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<SubscriptionModel>("api/billing/subscription", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BillingService.GetSubscriptionAsync] {ex.GetType().Name}: {ex.Message}");
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
            Console.Error.WriteLine($"[BillingService.GetInvoicesAsync] {ex.GetType().Name}: {ex.Message}");
            return new List<InvoiceListItemModel>();
        }
    }
}
