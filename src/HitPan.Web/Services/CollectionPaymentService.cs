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

    /// <summary>
    /// 서버 페이지네이션 버전 (2026-05-13 야간 신규).
    /// 기존 GetCollectionsAsync 유지 — ServerData 전환 시 이 메서드 사용.
    /// </summary>
    public async Task<PagedResponse<CollectionModel>> GetCollectionsPagedAsync(
        int page, int pageSize,
        DateTime? from = null, DateTime? to = null, string? partnerId = null,
        CancellationToken ct = default)
    {
        try
        {
            var q = $"api/collections/paged?page={page}&pageSize={pageSize}";
            if (from.HasValue) q += $"&from={from:yyyy-MM-dd}";
            if (to.HasValue) q += $"&to={to:yyyy-MM-dd}";
            if (!string.IsNullOrEmpty(partnerId)) q += $"&partnerId={Uri.EscapeDataString(partnerId)}";
            return await http.GetFromJsonAsync<PagedResponse<CollectionModel>>(q, ct) ?? new();
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

    // ── 20260915작1 3판 R4 — 서버 안내 문구를 돌려주는 등록·삭제 (기존 bool 메서드는 그대로 둔다 · #1) ──
    //   🔴 막는 것 ≠ 알려주는 것: 서버는 이월잔액 초과·기준일까지·방향·이관 줄 삭제를 400 {error} 로 거절한다.
    //      bool 만 돌리면 그 문구가 화면에 닿지 않는다 → 문구를 그대로 담아 돌려준다.

    /// <summary>서버가 문구를 못 보냈을 때(연결 끊김 등) 쓰는 기본 안내.</summary>
    public const string FallbackSaveError = "처리하지 못했습니다. 인터넷 연결을 확인한 뒤 다시 시도해 주세요.";

    public Task<SaveResultModel> CreateCollectionWithMessageAsync(CreateCollectionModel model, CancellationToken ct = default)
        => SendWithMessageAsync(() => http.PostAsJsonAsync("api/collections", model, ct), nameof(CreateCollectionWithMessageAsync), ct);

    public Task<SaveResultModel> DeleteCollectionWithMessageAsync(string id, CancellationToken ct = default)
        => SendWithMessageAsync(() => http.DeleteAsync($"api/collections/{Uri.EscapeDataString(id)}", ct), nameof(DeleteCollectionWithMessageAsync), ct);

    public Task<SaveResultModel> CreatePaymentWithMessageAsync(CreatePaymentModel model, CancellationToken ct = default)
        => SendWithMessageAsync(() => http.PostAsJsonAsync("api/payments", model, ct), nameof(CreatePaymentWithMessageAsync), ct);

    public Task<SaveResultModel> DeletePaymentWithMessageAsync(string id, CancellationToken ct = default)
        => SendWithMessageAsync(() => http.DeleteAsync($"api/payments/{Uri.EscapeDataString(id)}", ct), nameof(DeletePaymentWithMessageAsync), ct);

    private static async Task<SaveResultModel> SendWithMessageAsync(Func<Task<HttpResponseMessage>> send, string caller, CancellationToken ct)
    {
        try
        {
            using var r = await send();
            if (r.IsSuccessStatusCode) return new SaveResultModel(true, null);
            var body = await r.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"[CollectionPaymentService.{caller}] {(int)r.StatusCode}: {body}");
            return new SaveResultModel(false, ExtractServerError(body) ?? FallbackSaveError);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[CollectionPaymentService.{caller}] 연결 오류: {ex.Message}");
            return new SaveResultModel(false, FallbackSaveError);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"[CollectionPaymentService.{caller}] 취소·시간 초과: {ex.Message}");
            return new SaveResultModel(false, FallbackSaveError);
        }
    }

    /// <summary>서버 응답 <c>{"error":"…"}</c>(GlobalExceptionMiddleware) 또는 <c>{"message":"…"}</c> 에서 문구를 꺼낸다.</summary>
    private static string? ExtractServerError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            foreach (var name in new[] { "error", "message" })
            {
                if (doc.RootElement.TryGetProperty(name, out var el)
                    && el.ValueKind == System.Text.Json.JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(el.GetString()))
                    return el.GetString();
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.WriteLine($"[CollectionPaymentService.ExtractServerError] JSON 아닌 응답: {ex.Message}");
        }
        return null;
    }

    // ── 미수/미지급 정공법 (WS-20260427-04, 사장님 헌법 §20) ──

    public async Task<ReceivablesResponseModel> GetReceivablesAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<ReceivablesResponseModel>("api/finance/receivables", ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<PayablesResponseModel> GetPayablesAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<PayablesResponseModel>("api/finance/payables", ct) ?? new(); }
        catch { return new(); }
    }
}
