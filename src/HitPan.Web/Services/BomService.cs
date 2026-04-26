using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class BomService(HttpClient http)
{
    public async Task<List<BomListModel>?> GetListAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<BomListModel>>("api/bom", ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<BomDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<BomDetailModel>($"api/bom/{Uri.EscapeDataString(id)}", ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>상품마스터 item_id 로 매핑된 BOM 의 bom_id 조회. 매핑 없으면 null.</summary>
    public async Task<string?> GetBomIdByItemAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.GetAsync($"api/bom/by-item/{Uri.EscapeDataString(itemId)}", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[BomService.GetBomIdByItemAsync] {(int)resp.StatusCode}: {err}");
                return null;
            }
            // ASP.NET 직렬화는 camelCase ("bomId") 로 내려간다.
            // 명시적으로 PropertyNameCaseInsensitive 옵션을 넘겨 안전하게 매칭.
            var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var body = await resp.Content.ReadFromJsonAsync<BomByItemResponse>(opts, ct);
            return body?.BomId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BomService.GetBomIdByItemAsync] Error: {ex.Message}");
            return null;
        }
    }

    private sealed class BomByItemResponse
    {
        public string? BomId { get; set; }
    }

    /// <summary>
    /// BOM 생산지시 전 재고 사전체크. 사장님 헌법 (2026-04-26):
    /// CanProduce=false 면 클라이언트가 부족분 분류 후 다이얼로그 분기 (반제품→반려, 자재→자동발주).
    /// </summary>
    public async Task<BomAssembleCheckModel?> CheckAssembleAsync(string bomId, decimal produceQty, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync($"api/bom/{Uri.EscapeDataString(bomId)}/check-assemble", produceQty, ct);
            if (!res.IsSuccessStatusCode)
            {
                Console.WriteLine($"[BomService.CheckAssembleAsync] {(int)res.StatusCode}");
                return null;
            }
            var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return await res.Content.ReadFromJsonAsync<BomAssembleCheckModel>(opts, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BomService.CheckAssembleAsync] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// BOM 조립 직후 자재 자동발주 후보 조회.
    /// 사장님 헌법 (2026-04-26): produceQty 전달 → 서버가 MAX(부족분, 자동발주수량)으로 계산.
    /// </summary>
    public async Task<List<AutoOrderCandidateModel>> GetAssembleAutoOrderCandidatesAsync(string bomId, decimal produceQty = 1, CancellationToken ct = default)
    {
        try
        {
            var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return await http.GetFromJsonAsync<List<AutoOrderCandidateModel>>(
                $"api/bom/{Uri.EscapeDataString(bomId)}/auto-order-candidates?produceQty={produceQty}", opts, ct) ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BomService.GetAssembleAutoOrderCandidatesAsync] {ex.Message}");
            return new();
        }
    }

    public async Task<(bool ok, string? bomId)> CreateAsync(CreateBomModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/bom", model, ct);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[BomService.CreateAsync] {(int)res.StatusCode}: {err}");
                return (false, null);
            }
            var body = await res.Content.ReadFromJsonAsync<BomCreateResult>(cancellationToken: ct);
            return (true, body?.Id);
        }
        catch
        {
            return (false, null);
        }
    }

    public async Task<bool> UpdateAsync(string id, CreateBomModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/bom/{Uri.EscapeDataString(id)}", model, ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync($"api/bom/{Uri.EscapeDataString(id)}", ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private class BomCreateResult { public string Id { get; set; } = ""; }

    public async Task<List<StockAlertModel>?> GetAlertsAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<StockAlertModel>>("api/bom/alerts", ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DismissAlertAsync(string alertId, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsync($"api/bom/alerts/{alertId}/dismiss", null, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> OrderAlertAsync(string alertId, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsync($"api/bom/alerts/{alertId}/order", null, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RegisterBomAsItemAsync(string bomId, string itemType, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(
                $"api/bom/{Uri.EscapeDataString(bomId)}/register-item",
                new { itemType }, ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AssembleAsync(string bomId, decimal produceQty, string? memo = null, CancellationToken ct = default)
    {
        var (ok, _) = await AssembleWithErrorAsync(bomId, produceQty, memo, ct).ConfigureAwait(false);
        return ok;
    }

    /// <summary>조립 실행. 실패 시 서버 응답 본문을 함께 반환.</summary>
    public async Task<(bool Ok, string? Error)> AssembleWithErrorAsync(string bomId, decimal produceQty, string? memo = null, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/bom/assemble", new { bomId, produceQty, memo }, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return (false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)res.StatusCode}" : body);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// 조립 해체 — 사장님 지시 (2026-04-26): 완제품 OUT + 자재 IN 으로 가격·재고 회귀.
    /// </summary>
    public async Task<(bool Ok, string? Error)> DisassembleWithErrorAsync(string bomId, decimal produceQty, string? memo = null, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/bom/disassemble", new { bomId, produceQty, memo }, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return (false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)res.StatusCode}" : body);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
