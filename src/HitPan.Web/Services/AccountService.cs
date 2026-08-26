using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class AccountService(HttpClient http)
{
    public async Task<List<AccountModel>> GetListAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<AccountModel>>("api/finance/accounts", ct).ConfigureAwait(false)
                   ?? new List<AccountModel>();
        }
        catch (Exception)
        {
            return new List<AccountModel>();
        }
    }

    /// <summary>
    /// 🔴 20260827작4 — <b>합계잔액시산표</b> 조회 (회계장부 1차).
    /// </summary>
    /// <remarks>
    /// ⚠️ 실패 시 <c>null</c> 을 준다 — <b>빈 목록과 구분해야 한다.</b>
    /// 종전 관례처럼 <c>catch { return new(); }</c> 로 삼키면 401·500 이
    /// <b>"조회 0건" 으로 위장</b>돼, 장부가 비었는지 고장인지 알 수 없다
    /// (20260825작18 에서 같은 자리를 봉합했다). 회계장부는 숫자가 근거라 더더욱 안 된다.
    /// </remarks>
    public async Task<TrialBalanceModel?> GetTrialBalanceAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (from.HasValue) qs.Add($"from={from:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to:yyyy-MM-dd}");
            var path = "api/finance/trial-balance" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            return await http.GetFromJsonAsync<TrialBalanceModel>(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지.
            Console.Error.WriteLine("[작4] 시산표 조회 실패: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    public async Task<bool> CreateAsync(CreateAccountModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/finance/accounts", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateAsync(string code, UpdateAccountModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/finance/accounts/{Uri.EscapeDataString(code)}", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteAsync(string code, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync($"api/finance/accounts/{Uri.EscapeDataString(code)}", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
