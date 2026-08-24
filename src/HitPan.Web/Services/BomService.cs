using System.Net.Http.Json;
using HitPan.Contracts.Idempotency;
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
        catch (Exception)
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
        catch (Exception)
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
        catch (Exception)
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
        catch (Exception)
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
        catch (Exception)
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
        catch (Exception ex)
        {
            // 봉합 (2026-08-25, 20260825작1 W2-4): 빈 catch 였다(#15 위반).
            //   조회가 실패해도 null → 화면은 "미달 0건" 으로 읽는다 ⇒ 실패가 정상으로 위장된다.
            //   최소한 왜 실패했는지는 남긴다.
            Console.Error.WriteLine($"[WARN] 안전재고 알림 조회 실패 — {ex.GetType().Name}: {ex.Message}");
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
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 안전재고 알림 발주 (20260825작1 W2). <paramref name="autoReceive"/> 면 매입확정까지 시도한다.
    /// </summary>
    /// <returns>
    /// 결과. 실패하면 <c>null</c>. 🔴 <b>왜 실패했는지</b>를 화면이 보여줄 수 있게
    /// 서버 메시지를 <see cref="OrderAlertResultModel.ChainSkippedReason"/> 에 담아 돌려준다 —
    /// 종전엔 <c>bool</c> 이라 이유가 통째로 사라졌다.
    /// </returns>
    public async Task<OrderAlertResultModel?> OrderAlertAsync(
        string alertId, bool autoReceive = false, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsync(
                $"api/bom/alerts/{alertId}/order?autoReceive={(autoReceive ? "true" : "false")}",
                null, ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Console.Error.WriteLine($"[WARN] 자동발주 실패 — {(int)res.StatusCode}: {body}");
                return null;
            }

            return await res.Content
                            .ReadFromJsonAsync<OrderAlertResultModel>(cancellationToken: ct)
                            .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 봉합 (2026-08-25, 20260825작1 W2-4): 빈 catch 였다(#15 위반).
            //   서버가 "자동발주 공급처가 설정되지 않았습니다" 라고 알려줘도 통째로 버려져,
            //   화면엔 "1건 실패" 라는 노란 알림만 떴다 — 사장님이 이유를 알 방법이 없었다.
            Console.Error.WriteLine($"[WARN] 자동발주 호출 실패 — {ex.GetType().Name}: {ex.Message}");
            return null;
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
        catch (Exception)
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
            // 멱등 헤더 필수 (작6, 2026-06-26): assemble 엔드포인트가 [IdempotencyKey] 라 헤더 없으면 400.
            //   메서드 1회 호출 = 키 1개 → 같은 요청 재전송(타임아웃 재시도)은 같은 키 유지 → 중복 차단.
            //   새 생산 클릭 = 새 호출 = 새 키 → 정상 반복생산 보존(헌법 #20). TaxInvoiceApiService 패턴.
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/bom/assemble")
            {
                Content = JsonContent.Create(new { bomId, produceQty, memo })
            };
            req.Headers.Add(IdempotencyConstants.HeaderName, Guid.NewGuid().ToString("N"));
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
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
            // 멱등 헤더 필수 (작6, 2026-06-26): disassemble 도 [IdempotencyKey]. 생산과 동일 패턴.
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/bom/disassemble")
            {
                Content = JsonContent.Create(new { bomId, produceQty, memo })
            };
            req.Headers.Add(IdempotencyConstants.HeaderName, Guid.NewGuid().ToString("N"));
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
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
