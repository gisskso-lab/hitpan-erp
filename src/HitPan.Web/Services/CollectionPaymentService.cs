using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>수금·지급 API 클라이언트 서비스</summary>
public sealed class CollectionPaymentService(HttpClient http)
{
    // ── 수금 ──

    public async Task<List<CollectionModel>> GetCollectionsAsync(DateTime? from = null, DateTime? to = null, string? partnerId = null, CancellationToken ct = default)
    {
        try
        {
            var q = "api/collections?";
            if (from.HasValue) q += $"from={from:yyyy-MM-dd}&";
            if (to.HasValue) q += $"to={to:yyyy-MM-dd}&";
            if (!string.IsNullOrEmpty(partnerId)) q += $"partnerId={Uri.EscapeDataString(partnerId)}&";
            return await http.GetFromJsonAsync<List<CollectionModel>>(q.TrimEnd('&', '?'), ct) ?? new();
        }
        catch { return new(); }
    }

    public async Task<bool> CreateCollectionAsync(CreateCollectionModel model, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/collections", model, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<bool> DeleteCollectionAsync(string id, CancellationToken ct = default)
    {
        try { using var r = await http.DeleteAsync($"api/collections/{Uri.EscapeDataString(id)}", ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    // ── 지급 ──

    public async Task<List<PaymentModel>> GetPaymentsAsync(DateTime? from = null, DateTime? to = null, string? partnerId = null, CancellationToken ct = default)
    {
        try
        {
            var q = "api/payments?";
            if (from.HasValue) q += $"from={from:yyyy-MM-dd}&";
            if (to.HasValue) q += $"to={to:yyyy-MM-dd}&";
            if (!string.IsNullOrEmpty(partnerId)) q += $"partnerId={Uri.EscapeDataString(partnerId)}&";
            return await http.GetFromJsonAsync<List<PaymentModel>>(q.TrimEnd('&', '?'), ct) ?? new();
        }
        catch { return new(); }
    }

    public async Task<bool> CreatePaymentAsync(CreatePaymentModel model, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/payments", model, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<bool> DeletePaymentAsync(string id, CancellationToken ct = default)
    {
        try { using var r = await http.DeleteAsync($"api/payments/{Uri.EscapeDataString(id)}", ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }
}
