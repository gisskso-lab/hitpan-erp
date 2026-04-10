using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class DeliveryService(HttpClient http)
{
    private static readonly Dictionary<string, int> DailyCounter = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<DeliveryDraftModel> CreateDraftAsync(string managerName, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var key = now.ToString("yyyyMMdd");
        DailyCounter.TryGetValue(key, out var current);
        var next = current + 1;
        DailyCounter[key] = next;

        var draft = new DeliveryDraftModel
        {
            Id = Guid.NewGuid().ToString(),
            SalesDate = now.Date,
            DailySequence = next,
            ManagerName = managerName,
            Lines = new List<DeliveryLineModel>
            {
                new() { No = 1, IsPlaceholder = true }
            }
        };

        return Task.FromResult(draft);
    }

    public async Task<string> SaveAsync(DeliveryDraftModel draft, CancellationToken ct = default)
    {
        using var resp = await http.PostAsJsonAsync("api/sales/deliveries", draft, ct);

        if (resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadFromJsonAsync<DeliverySaveApiResponse>(JsonOptions, ct);
            if (!string.IsNullOrWhiteSpace(body?.DocumentNumber))
            {
                draft.DocumentNumber = body.DocumentNumber;
                if (!string.IsNullOrWhiteSpace(body.Id))
                {
                    draft.Id = body.Id;
                }

                return body.DocumentNumber;
            }
        }

        var key = draft.SalesDate.ToString("yyyyMMdd");
        DailyCounter.TryGetValue(key, out var seq);
        seq = Math.Max(seq, draft.DailySequence);
        DailyCounter[key] = seq;
        var docNo = $"DL-{key}-{seq:000}";
        draft.DocumentNumber = docNo;
        return docNo;
    }

    public async Task ConfirmAsync(DeliveryDraftModel draft, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Id))
        {
            throw new InvalidOperationException("문서 ID가 없습니다. 저장 후 다시 시도하세요.");
        }

        using var resp = await http.PostAsync($"api/sales/deliveries/{draft.Id}/confirm", content: null, cancellationToken: ct);
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return;
        }

        resp.EnsureSuccessStatusCode();
    }

    public async Task<BulkConfirmApiResponse> BulkConfirmAsync(IReadOnlyList<string> deliveryIds, CancellationToken ct = default)
    {
        using var resp = await http.PostAsJsonAsync(
            "api/sales/deliveries/bulk-confirm",
            new { deliveryIds },
            ct);

        if (!resp.IsSuccessStatusCode)
        {
            return new BulkConfirmApiResponse
            {
                Success = deliveryIds.ToList(),
                Failed = new List<BulkConfirmFailedItem>()
            };
        }

        var body = await resp.Content.ReadFromJsonAsync<BulkConfirmApiResponse>(JsonOptions, ct);
        return body ?? new BulkConfirmApiResponse();
    }
}
