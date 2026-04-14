using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>
/// 견적서 API 연동 서비스다.
/// </summary>
public sealed class QuotationService(HttpClient http)
{
    // JSON 역직렬화 옵션이다.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // JSON 직렬화 옵션이다.
    private static readonly JsonSerializerOptions PostJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 새 견적 초안을 만든다.
    /// </summary>
    /// <param name="managerName">기본 담당자명이다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>초기 견적서 모델이다.</returns>
    public Task<QuotationDraftModel> CreateDraftAsync(string managerName, CancellationToken ct = default)
    {
        var draft = new QuotationDraftModel
        {
            Id = Guid.NewGuid().ToString(),
            QuoteDate = DateTime.Today,
            ValidUntil = DateTime.Today.AddDays(30),
            EmployeeId = managerName,
            Status = "draft",
            DocumentType = "견적서",
            Lines = new List<QuotationLineModel>
            {
                // 첫 라인을 플레이스홀더로 두어 입력 UX를 유지한다.
                new() { No = 1, SortOrder = 1, IsPlaceholder = true }
            }
        };

        return Task.FromResult(draft);
    }

    /// <summary>
    /// 견적서 목록을 조회한다.
    /// </summary>
    public async Task<List<QuotationListItem>> GetListAsync(
        DateTime? from = null,
        DateTime? to = null,
        string? partnerName = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var query = $"api/quotations?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&partner={Uri.EscapeDataString(partnerName ?? string.Empty)}&status={Uri.EscapeDataString(status ?? string.Empty)}";
        var rows = await http.GetFromJsonAsync<List<QuotationListResponse>>(query, JsonOptions, ct) ?? new List<QuotationListResponse>();
        return rows.Select(static x => new QuotationListItem
        {
            QuoteId = x.QuoteId ?? string.Empty,
            QuoteNo = x.QuoteNo ?? string.Empty,
            PartnerName = x.PartnerName ?? string.Empty,
            QuoteDate = x.QuoteDate,
            ValidUntil = x.ValidUntil,
            Status = x.Status ?? "draft",
            TotalAmount = x.TotalAmount,
            VatAmount = x.VatAmount
        }).ToList();
    }

    /// <summary>
    /// 견적서 상세를 조회한다.
    /// </summary>
    public async Task<QuotationDraftModel?> GetAsync(string id, CancellationToken ct = default)
    {
        var body = await http.GetFromJsonAsync<QuotationDetailResponse>($"api/quotations/{Uri.EscapeDataString(id)}", JsonOptions, ct);
        if (body is null)
        {
            return null;
        }

        var lines = (body.Items ?? new List<QuotationItemResponse>())
            .Select((x, index) =>
            {
                var line = new QuotationLineModel
                {
                    No = index + 1,
                    SortOrder = x.SortOrder <= 0 ? index + 1 : x.SortOrder,
                    IsPlaceholder = false,
                    ItemId = x.ItemId ?? string.Empty,
                    ItemName = x.ItemName ?? string.Empty,
                    Spec = x.Spec ?? string.Empty,
                    Unit = x.Unit ?? "EA",
                    Qty = x.Qty,
                    UnitPrice = x.UnitPrice,
                    DiscountRate = x.DiscountRate,
                    Note = x.Memo ?? string.Empty
                };
                line.RecalculateAmount();
                return line;
            }).ToList();

        if (lines.Count == 0)
        {
            lines.Add(new QuotationLineModel { No = 1, SortOrder = 1, IsPlaceholder = true });
        }

        return new QuotationDraftModel
        {
            Id = body.QuoteId,
            DocumentNumber = body.QuoteNo,
            QuoteDate = body.QuoteDate,
            ValidUntil = body.ValidUntil,
            PartnerId = body.PartnerId,
            SalesCompany = body.PartnerName ?? string.Empty,
            EmployeeId = body.EmployeeId,
            Memo = body.Memo,
            Status = body.Status ?? "draft",
            DocumentType = "견적서",
            Lines = lines,
            Density = QuotationGridDensity.Normal
        };
    }

    /// <summary>
    /// 견적서를 생성한다.
    /// </summary>
    public async Task<string> CreateAsync(QuotationDraftModel draft, CancellationToken ct = default)
    {
        var request = new QuotationSaveRequest
        {
            PartnerId = draft.PartnerId ?? string.Empty,
            EmployeeId = draft.EmployeeId,
            QuoteDate = draft.QuoteDate,
            ValidUntil = draft.ValidUntil,
            Memo = draft.Memo,
            Items = BuildItems(draft.Lines)
        };

        using var response = await http.PostAsJsonAsync("api/quotations", request, PostJsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateResponse>(JsonOptions, ct);
        return created?.Id ?? string.Empty;
    }

    /// <summary>
    /// 견적서를 수정한다.
    /// </summary>
    public async Task<bool> UpdateAsync(string id, QuotationDraftModel draft, CancellationToken ct = default)
    {
        var request = new QuotationSaveRequest
        {
            PartnerId = draft.PartnerId ?? string.Empty,
            EmployeeId = draft.EmployeeId,
            QuoteDate = draft.QuoteDate,
            ValidUntil = draft.ValidUntil,
            Memo = draft.Memo,
            Status = draft.Status,
            Items = BuildItems(draft.Lines)
        };

        using var response = await http.PutAsJsonAsync($"api/quotations/{Uri.EscapeDataString(id)}", request, PostJsonOptions, ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// 견적서를 삭제한다.
    /// </summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/quotations/{Uri.EscapeDataString(id)}", ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// 견적서를 수주서로 전환한다.
    /// </summary>
    public async Task<string?> ConvertToSalesOrderAsync(string id, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"api/quotations/{Uri.EscapeDataString(id)}/convert", new StringContent("{}"), ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<ConvertResponse>(JsonOptions, ct);
        return body?.ConvertedOrderId;
    }

    /// <summary>
    /// 저장 요청용 라인 목록을 만든다.
    /// </summary>
    private static List<QuotationItemRequest> BuildItems(List<QuotationLineModel> lines)
    {
        return lines
            .Where(x => !x.IsPlaceholder)
            .Select((x, index) => new QuotationItemRequest
            {
                ItemId = x.ItemId,
                ItemName = x.ItemName,
                Spec = x.Spec,
                Unit = x.Unit,
                Qty = x.Qty,
                UnitPrice = x.UnitPrice,
                DiscountRate = x.DiscountRate,
                Amount = x.Amount,
                VatAmount = x.VatAmount,
                Memo = x.Note,
                SortOrder = x.SortOrder <= 0 ? index + 1 : x.SortOrder
            }).ToList();
    }

    /// <summary>
    /// 생성 응답 DTO다.
    /// </summary>
    private sealed class CreateResponse
    {
        /// <summary>견적 식별자다.</summary>
        public string? Id { get; set; }
    }

    /// <summary>
    /// 전환 응답 DTO다.
    /// </summary>
    private sealed class ConvertResponse
    {
        /// <summary>전환 수주 식별자다.</summary>
        public string? ConvertedOrderId { get; set; }
    }

    /// <summary>
    /// 견적서 목록 응답 DTO다.
    /// </summary>
    private sealed class QuotationListResponse
    {
        public string? QuoteId { get; set; }
        public string? QuoteNo { get; set; }
        public string? PartnerName { get; set; }
        public DateTime QuoteDate { get; set; }
        public DateTime? ValidUntil { get; set; }
        public string? Status { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal VatAmount { get; set; }
    }

    /// <summary>
    /// 견적서 상세 응답 DTO다.
    /// </summary>
    private sealed class QuotationDetailResponse
    {
        public string? QuoteId { get; set; }
        public string? QuoteNo { get; set; }
        public string? PartnerId { get; set; }
        public string? PartnerName { get; set; }
        public string? EmployeeId { get; set; }
        public DateTime QuoteDate { get; set; }
        public DateTime? ValidUntil { get; set; }
        public string? Status { get; set; }
        public string? Memo { get; set; }
        public List<QuotationItemResponse>? Items { get; set; }
    }

    /// <summary>
    /// 견적서 품목 응답 DTO다.
    /// </summary>
    private sealed class QuotationItemResponse
    {
        public string? ItemId { get; set; }
        public string? ItemName { get; set; }
        public string? Spec { get; set; }
        public string? Unit { get; set; }
        public decimal Qty { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountRate { get; set; }
        public decimal Amount { get; set; }
        public decimal VatAmount { get; set; }
        public string? Memo { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// 견적 저장 요청 DTO다.
    /// </summary>
    private sealed class QuotationSaveRequest
    {
        public string PartnerId { get; set; } = string.Empty;
        public string? EmployeeId { get; set; }
        public DateTime QuoteDate { get; set; }
        public DateTime? ValidUntil { get; set; }
        public string? Status { get; set; }
        public string? Memo { get; set; }
        public List<QuotationItemRequest> Items { get; set; } = new();
    }

    /// <summary>
    /// 견적 저장 요청 품목 DTO다.
    /// </summary>
    private sealed class QuotationItemRequest
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? Spec { get; set; }
        public string? Unit { get; set; }
        public decimal Qty { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountRate { get; set; }
        public decimal Amount { get; set; }
        public decimal VatAmount { get; set; }
        public string? Memo { get; set; }
        public int SortOrder { get; set; }
    }
}
