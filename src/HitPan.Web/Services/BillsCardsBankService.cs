using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class BillsCardsBankService(HttpClient http)
{
    // ─── Bills ───────────────────────────────────────────
    public async Task<List<BillModel>> ListBillsAsync(string? type, string? status, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (!string.IsNullOrEmpty(type)) qs.Add($"type={Uri.EscapeDataString(type)}");
            if (!string.IsNullOrEmpty(status)) qs.Add($"status={Uri.EscapeDataString(status)}");
            if (from.HasValue) qs.Add($"from={from:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to:yyyy-MM-dd}");
            var url = "api/finance/bills" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
            return await http.GetFromJsonAsync<List<BillModel>>(url, ct).ConfigureAwait(false) ?? new();
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Bills.List] {ex.Message}"); return new(); }
    }

    public async Task<bool> CreateBillAsync(CreateBillModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/finance/bills", req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Bills.Create] {ex.Message}"); return false; }
    }

    public async Task<bool> UpdateBillStatusAsync(string billId, UpdateBillStatusModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/finance/bills/{billId}/status", req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Bills.UpdateStatus] {ex.Message}"); return false; }
    }

    // ─── Card Payments ──────────────────────────────────
    public async Task<List<CardPaymentModel>> ListCardPaymentsAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (from.HasValue) qs.Add($"from={from:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to:yyyy-MM-dd}");
            var url = "api/finance/card-payments" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
            return await http.GetFromJsonAsync<List<CardPaymentModel>>(url, ct).ConfigureAwait(false) ?? new();
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Cards.List] {ex.Message}"); return new(); }
    }

    public async Task<CardPaymentModel?> GetCardPaymentAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<CardPaymentModel>($"api/finance/card-payments/{id}", ct).ConfigureAwait(false);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Cards.Get] {ex.Message}"); return null; }
    }

    public async Task<bool> CreateCardPaymentAsync(CreateCardPaymentModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/finance/card-payments", req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Cards.Create] {ex.Message}"); return false; }
    }

    // ─── Bank Tx ────────────────────────────────────────
    public async Task<List<BankTxModel>> ListBankTxAsync(string? accountNo, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (!string.IsNullOrEmpty(accountNo)) qs.Add($"accountNo={Uri.EscapeDataString(accountNo)}");
            if (from.HasValue) qs.Add($"from={from:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to:yyyy-MM-dd}");
            var url = "api/finance/bank-transactions" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
            return await http.GetFromJsonAsync<List<BankTxModel>>(url, ct).ConfigureAwait(false) ?? new();
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Bank.List] {ex.Message}"); return new(); }
    }

    public async Task<bool> CreateBankTxAsync(CreateBankTxModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/finance/bank-transactions", req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Bank.Create] {ex.Message}"); return false; }
    }
}
