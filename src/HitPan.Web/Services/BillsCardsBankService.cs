using System.Net.Http.Json;
using HitPan.Web.Models;
using MudBlazor;

namespace HitPan.Web.Services;

// ⬛ [낡은 줄 · 화면오류기록띠] public sealed class BillsCardsBankService(HttpClient http)
public sealed class BillsCardsBankService(HttpClient http, ILogger<BillsCardsBankService> logger, ISnackbar snackbar)
{
    // 화면오류기록띠 대표 ③ — 어음 · 카드 · 통장 화면은 목록이 비면 실패인지 0건인지 말하지 않는다(띠가 유일한 신호였다).
    //   목록 조회(연 화면) 실패만 쉬운 알림 한 번 · 등록 · 상태 바꿈은 화면이 이미 「실패」 를 띄움.
    private void NotifyLoadFailed(Exception ex) => LoadFailedNotice.Show(snackbar, ex);

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
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Bills.List] {ex.Message}"); return new(); }
        catch (Exception ex) { logger.LogWarning(ex, "[Bills.List] 어음 목록 조회 실패"); return new(); }
    }

    public async Task<bool> CreateBillAsync(CreateBillModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/finance/bills", req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Bills.Create] {ex.Message}"); return false; }
        catch (Exception ex) { logger.LogWarning(ex, "[Bills.Create] 어음 등록 실패"); return false; }
    }

    public async Task<bool> UpdateBillStatusAsync(string billId, UpdateBillStatusModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/finance/bills/{billId}/status", req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Bills.UpdateStatus] {ex.Message}"); return false; }
        catch (Exception ex) { logger.LogWarning(ex, "[Bills.UpdateStatus] 어음 상태 저장 실패"); return false; }
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
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Cards.List] {ex.Message}"); return new(); }
        catch (Exception ex) { logger.LogWarning(ex, "[Cards.List] 카드결제 목록 조회 실패"); NotifyLoadFailed(ex); return new(); }
    }

    public async Task<CardPaymentModel?> GetCardPaymentAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<CardPaymentModel>($"api/finance/card-payments/{id}", ct).ConfigureAwait(false);
        }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Cards.Get] {ex.Message}"); return null; }
        catch (Exception ex) { logger.LogWarning(ex, "[Cards.Get] 카드결제 조회 실패"); return null; }
    }

    public async Task<bool> CreateCardPaymentAsync(CreateCardPaymentModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/finance/card-payments", req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Cards.Create] {ex.Message}"); return false; }
        catch (Exception ex) { logger.LogWarning(ex, "[Cards.Create] 카드결제 등록 실패"); return false; }
    }

    // ─── Bank Tx ────────────────────────────────────────
    // 헌법 #19·#25 정합 — default from=Today-30, to=Today, limit=500 (5/27 P1-4 봉합)
    public async Task<List<BankTxModel>> ListBankTxAsync(string? accountNo, DateTime? from, DateTime? to, int limit = 500, CancellationToken ct = default)
    {
        try
        {
            var effFrom = from ?? DateTime.Today.AddDays(-30);
            var effTo = to ?? DateTime.Today;
            var qs = new List<string>
            {
                $"from={effFrom:yyyy-MM-dd}",
                $"to={effTo:yyyy-MM-dd}",
                $"limit={limit}"
            };
            if (!string.IsNullOrEmpty(accountNo)) qs.Add($"accountNo={Uri.EscapeDataString(accountNo)}");
            var url = "api/finance/bank-transactions?" + string.Join("&", qs);
            return await http.GetFromJsonAsync<List<BankTxModel>>(url, ct).ConfigureAwait(false) ?? new();
        }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Bank.List] {ex.Message}"); return new(); }
        catch (Exception ex) { logger.LogWarning(ex, "[Bank.List] 통장 거래 목록 조회 실패"); NotifyLoadFailed(ex); return new(); }
    }

    public async Task<bool> CreateBankTxAsync(CreateBankTxModel req, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/finance/bank-transactions", req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        // ⬛ [낡은 줄 · 화면오류기록띠] catch (Exception ex) { Console.Error.WriteLine($"[Bank.Create] {ex.Message}"); return false; }
        catch (Exception ex) { logger.LogWarning(ex, "[Bank.Create] 통장 거래 등록 실패"); return false; }
    }
}
