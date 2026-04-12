using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class DeliveryService(HttpClient http)
{
    private static readonly Dictionary<string, int> DailyCounter = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions PostJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync($"api/sales/deliveries/{draft.Id}/confirm", content, cancellationToken: ct);
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

    /// <summary>목록 조회 (쿼리 문자열 날짜).</summary>
    public async Task<List<DeliveryListDto>> GetListAsync(
        string? from = null,
        string? to = null,
        string? partner = null,
        string? status = null,
        CancellationToken ct = default)
    {
        try
        {
            var query = new StringBuilder("api/sales/deliveries?");
            query.Append($"from={Uri.EscapeDataString(from ?? "")}");
            query.Append($"&to={Uri.EscapeDataString(to ?? "")}");
            query.Append($"&partner={Uri.EscapeDataString(partner ?? "")}");
            query.Append($"&status={Uri.EscapeDataString(status ?? "")}");
            return await http.GetFromJsonAsync<List<DeliveryListDto>>(query.ToString(), JsonOptions, ct)
                   ?? new List<DeliveryListDto>();
        }
        catch
        {
            return new List<DeliveryListDto>();
        }
    }

    /// <summary>판매 목록 다이얼로그용 — API DTO를 <see cref="SalesListItem"/>으로 변환.</summary>
    public async Task<List<SalesListItem>> GetSalesListItemsAsync(
        string listType,
        DateTime? from,
        DateTime? to,
        string partnerName = "",
        CancellationToken ct = default)
    {
        var fromStr = from?.ToString("yyyy-MM-dd") ?? "";
        var toStr = to?.ToString("yyyy-MM-dd") ?? "";
        var list = await GetListAsync(fromStr, toStr, partnerName, listType, ct);
        return list.Select(static d => new SalesListItem
        {
            OrderId = d.DeliveryId,
            OrderDate = d.OrderDate,
            OrderNo = d.DeliveryNo,
            PartnerId = d.PartnerId,
            PartnerName = d.PartnerName,
            TotalAmount = d.TotalAmount,
            VatAmount = d.VatAmount,
            Status = d.Status
        }).ToList();
    }

    public async Task<DeliveryDetailDto?> GetAsync(string deliveryId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<DeliveryDetailDto>(
                $"api/sales/deliveries/{Uri.EscapeDataString(deliveryId)}",
                JsonOptions,
                ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UpdateAsync(
        string deliveryId,
        UpdateDeliveryRequest req,
        CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync(
                $"api/sales/deliveries/{Uri.EscapeDataString(deliveryId)}",
                req,
                ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string deliveryId, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync(
                $"api/sales/deliveries/{Uri.EscapeDataString(deliveryId)}",
                ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<PartnerSearchResult>> SearchPartnersAsync(string keyword, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<PartnerSearchResult>>(
                       $"api/partners/search?q={Uri.EscapeDataString(keyword)}",
                       JsonOptions,
                       ct)
                   ?? new List<PartnerSearchResult>();
        }
        catch
        {
            return new List<PartnerSearchResult>();
        }
    }
}
