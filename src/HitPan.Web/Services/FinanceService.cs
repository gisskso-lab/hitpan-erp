using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>경리·세무 API 클라이언트</summary>
public sealed class FinanceClientService(HttpClient http)
{
    // 현금출납장
    public async Task<List<CashbookModel>> GetCashbookAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = "api/finance/cashbook?";
        if (from.HasValue) q += $"from={from:yyyy-MM-dd}&";
        if (to.HasValue) q += $"to={to:yyyy-MM-dd}&";
        try { return await http.GetFromJsonAsync<List<CashbookModel>>(q.TrimEnd('&', '?'), ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> CreateCashbookAsync(CreateCashbookModel m, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/finance/cashbook", m, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<bool> DeleteCashbookAsync(string id, CancellationToken ct = default)
    {
        try { using var r = await http.DeleteAsync($"api/finance/cashbook/{id}", ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    // 매입매출장
    public async Task<List<PurchaseSalesLedgerModel>> GetPurchaseSalesLedgerAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = "api/finance/purchase-sales-ledger?";
        if (from.HasValue) q += $"from={from:yyyy-MM-dd}&";
        if (to.HasValue) q += $"to={to:yyyy-MM-dd}&";
        try { return await http.GetFromJsonAsync<List<PurchaseSalesLedgerModel>>(q.TrimEnd('&', '?'), ct) ?? new(); }
        catch { return new(); }
    }

    // 부가세
    public async Task<VatSummaryModel?> GetVatAsync(int? year = null, int? half = null, CancellationToken ct = default)
    {
        var q = $"api/finance/vat?year={year ?? DateTime.Today.Year}&half={half ?? (DateTime.Today.Month <= 6 ? 1 : 2)}";
        try { return await http.GetFromJsonAsync<VatSummaryModel>(q, ct); }
        catch { return null; }
    }

    // 경비 — 헌법 #19·#25 정합 (5/26 진범 #4·#7 봉합)
    // default: 최근 30일 + limit 500. 호출자가 명시적으로 from/to 지정 시 그대로 전달.
    public async Task<List<ExpenseModel>> GetExpensesAsync(DateTime? from = null, DateTime? to = null, int limit = 500, CancellationToken ct = default)
    {
        var effFrom = from ?? DateTime.Today.AddDays(-30);
        var effTo = to ?? DateTime.Today;
        var q = $"api/finance/expenses?from={effFrom:yyyy-MM-dd}&to={effTo:yyyy-MM-dd}&limit={limit}";
        try { return await http.GetFromJsonAsync<List<ExpenseModel>>(q, ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> CreateExpenseAsync(CreateExpenseModel m, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/finance/expenses", m, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    // 경비 승인/반려
    public async Task<bool> ProcessExpenseAsync(string id, string action, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync($"api/finance/expenses/{Uri.EscapeDataString(id)}/process", new { action }, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    // 손익현황
    public async Task<List<ProfitSummaryModel>> GetProfitAsync(int? year = null, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ProfitSummaryModel>>($"api/finance/profit?year={year ?? DateTime.Today.Year}", ct) ?? new(); }
        catch { return new(); }
    }
}
