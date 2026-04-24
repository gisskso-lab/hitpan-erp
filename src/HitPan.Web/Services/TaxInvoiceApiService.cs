using System.Net.Http.Json;
using HitPan.Contracts.Idempotency;
using HitPan.Contracts.Sales;

namespace HitPan.Web.Services;

// 3계층 세금계산서 1계층 발행/취소 — TaxInvoiceController 연동 (P0-A #4 + P0-B N5).
// Idempotency-Key 헤더는 [IdempotencyKey] 어트리뷰트 붙은 엔드포인트(발행/취소)에 필수.
public sealed class TaxInvoiceApiService(HttpClient http)
{
    public async Task<IssueResult> IssueAsync(string deliveryId, string? memo, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/sales/tax-invoices")
        {
            Content = JsonContent.Create(new IssueTaxInvoiceRequest(deliveryId, memo))
        };
        req.Headers.Add(IdempotencyConstants.HeaderName, Guid.NewGuid().ToString("N"));

        using var resp = await http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var ok = await resp.Content.ReadFromJsonAsync<TaxInvoiceResponse>(cancellationToken: ct);
            return new IssueResult(true, ok?.InvoiceNo, null);
        }

        var err = await resp.Content.ReadFromJsonAsync<TaxInvoiceErrorResponse>(cancellationToken: ct);
        var msg = err?.Message ?? $"HTTP {(int)resp.StatusCode}";
        return new IssueResult(false, null, msg);
    }

    public async Task<BulkIssueResult> BulkIssueAsync(IEnumerable<string> deliveryIds, CancellationToken ct = default)
    {
        var ids = deliveryIds.ToList();
        var successes = new List<(string DeliveryId, string InvoiceNo)>();
        var failures = new List<(string DeliveryId, string Reason)>();

        foreach (var id in ids)
        {
            var r = await IssueAsync(id, null, ct);
            if (r.Success && !string.IsNullOrEmpty(r.InvoiceNo))
                successes.Add((id, r.InvoiceNo));
            else
                failures.Add((id, r.ErrorMessage ?? "알 수 없는 오류"));
        }

        return new BulkIssueResult(successes, failures);
    }

    public sealed record IssueResult(bool Success, string? InvoiceNo, string? ErrorMessage);

    public sealed record BulkIssueResult(
        IReadOnlyList<(string DeliveryId, string InvoiceNo)> Successes,
        IReadOnlyList<(string DeliveryId, string Reason)> Failures);
}
