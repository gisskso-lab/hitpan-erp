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

    public async Task<DeliverySaveResult> SaveAsync(DeliveryDraftModel draft, CancellationToken ct = default)
    {
        // 서버 CreateDeliveryRequest 스키마에 맞춰 명시적으로 매핑한다.
        // (draft 객체 직접 직렬화 시 Lines/Items, SalesDate/DeliveryDate 등 필드명 불일치로 서버가 품목을 빈 리스트로 받는다.)
        var payload = new CreateDeliveryPayload
        {
            PartnerId = draft.PartnerId ?? string.Empty,
            DeliveryDate = draft.SalesDate,
            Memo = draft.Memo,
            Items = draft.Lines
                .Where(x => !x.IsPlaceholder)
                .Select(x => new CreateDeliveryItemPayload
                {
                    ItemId = x.ItemId,
                    ItemName = string.IsNullOrWhiteSpace(x.ItemId) ? x.ItemName : null,
                    Qty = x.Qty,
                    UnitPrice = x.UnitPrice,
                    SupplyAmount = x.Amount,
                    VatAmount = x.VatAmount
                }).ToList()
        };

        using var resp = await http.PostAsJsonAsync("api/sales/deliveries", payload, PostJsonOptions, ct);

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

                return new DeliverySaveResult(true, body.DocumentNumber, null);
            }

            return new DeliverySaveResult(false, null, "서버 응답에 문서번호가 없습니다.");
        }

        var errBody = await resp.Content.ReadAsStringAsync(ct);
        return new DeliverySaveResult(false, null, string.IsNullOrWhiteSpace(errBody) ? $"HTTP {(int)resp.StatusCode}" : errBody);
    }

    // 봉합 (2026-06-22, 10차 전수조사 P0-1): 종전 SalesOrderPage 신규저장이 SaveAsync→api/sales/deliveries
    //   를 호출해 수주서가 거래명세서로 둔갑 저장됐다(헌법 #20 견적→수주→거래명세서 흐름이 수주 단계에서 끊김,
    //   OrderedQty 영구 유실). 백엔드 수주 전용 엔드포인트(SalesController CreateOrder + CreateSalesOrderRequest)
    //   는 이미 존재했으므로 그것을 호출하는 신규 생성 메서드를 추가한다. 거래명세서가 아니라 수주(sales_orders)로 저장된다.
    public async Task<DeliverySaveResult> CreateOrderAsync(DeliveryDraftModel draft, CancellationToken ct = default)
    {
        var payload = new CreateSalesOrderPayload
        {
            PartnerId = draft.PartnerId ?? string.Empty,
            OrderDate = draft.SalesDate,
            Memo = draft.Memo,
            Items = draft.Lines
                .Where(x => !x.IsPlaceholder)
                .Select(x => new CreateSalesOrderItemPayload
                {
                    ItemId = x.ItemId,
                    OrderedQty = x.Qty,
                    UnitPrice = x.UnitPrice,
                    SupplyAmount = x.Amount,
                    VatAmount = x.VatAmount
                }).ToList()
        };

        using var resp = await http.PostAsJsonAsync("api/sales/orders", payload, PostJsonOptions, ct);
        if (resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadFromJsonAsync<DeliverySaveApiResponse>(JsonOptions, ct);
            if (!string.IsNullOrWhiteSpace(body?.DocumentNumber))
            {
                draft.DocumentNumber = body.DocumentNumber;
                if (!string.IsNullOrWhiteSpace(body.Id)) draft.Id = body.Id;
                return new DeliverySaveResult(true, body.DocumentNumber, null);
            }
            // 문서번호 미반환이어도 Id 라도 있으면 성공으로 본다(서버 응답 변형 방어).
            if (!string.IsNullOrWhiteSpace(body?.Id))
            {
                draft.Id = body.Id;
                return new DeliverySaveResult(true, body.Id, null);
            }
        }

        var errBody = await resp.Content.ReadAsStringAsync(ct);
        return new DeliverySaveResult(false, null, string.IsNullOrWhiteSpace(errBody) ? $"HTTP {(int)resp.StatusCode}" : errBody);
    }

    public async Task ConfirmAsync(DeliveryDraftModel draft, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Id))
        {
            throw new InvalidOperationException("문서 ID가 없습니다. 저장 후 다시 시도하세요.");
        }

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/sales/deliveries/{draft.Id}/confirm") { Content = content };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", draft.Id);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            // §절대원칙 #20 — 실패를 성공으로 위장하면 재고 미차감 무결성 붕괴.
            var body = await resp.Content.ReadAsStringAsync(ct);
            var reason = string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body;
            throw new InvalidOperationException($"거래명세서 확정 실패 ({(int)resp.StatusCode}): {reason}");
        }
    }

    public async Task<BulkConfirmApiResponse> BulkConfirmAsync(IReadOnlyList<string> deliveryIds, CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.PostAsJsonAsync(
                "api/sales/deliveries/bulk-confirm",
                new { deliveryIds },
                ct);

            if (!resp.IsSuccessStatusCode)
            {
                // 실패 응답 — 전 건을 서버 응답 본문과 함께 실패로 기록한다.
                // (기존 코드는 실패 ID를 Success 리스트에 담아 위장해서 UI가 성공으로 착각하게 했다.)
                var err = await resp.Content.ReadAsStringAsync(ct);
                var reason = string.IsNullOrWhiteSpace(err) ? $"HTTP {(int)resp.StatusCode}" : err;
                return new BulkConfirmApiResponse
                {
                    Success = new List<string>(),
                    Failed = deliveryIds
                        .Select(id => new BulkConfirmFailedItem { Id = id, Reason = reason })
                        .ToList()
                };
            }

            var body = await resp.Content.ReadFromJsonAsync<BulkConfirmApiResponse>(JsonOptions, ct);
            return body ?? new BulkConfirmApiResponse();
        }
        catch (Exception ex)
        {
            return new BulkConfirmApiResponse
            {
                Success = new List<string>(),
                Failed = deliveryIds
                    .Select(id => new BulkConfirmFailedItem { Id = id, Reason = ex.Message })
                    .ToList()
            };
        }
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
        catch (Exception)
        {
            return new List<DeliveryListDto>();
        }
    }

    /// <summary>수주서 목록 조회 (수주 전용 API 호출).</summary>
    /// <remarks>
    /// 서버 SalesOrderListDto는 OrderId/OrderNo 필드를 반환한다.
    /// 과거에 DeliveryListDto로 역직렬화하면서 OrderId가 빈 문자열로 내려왔고,
    /// 이로 인해 "판매로 전환" 버튼이 무반응이었다(ids.Count == 0으로 즉시 return).
    /// </remarks>
    public async Task<List<SalesListItem>> GetOrderListAsync(
        DateTime? from = null, DateTime? to = null, string? status = null, CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (from.HasValue) qs.Add($"from={from:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to:yyyy-MM-dd}");
            if (!string.IsNullOrEmpty(status)) qs.Add($"status={Uri.EscapeDataString(status)}");
            var path = "api/sales/orders" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            var list = await http.GetFromJsonAsync<List<SalesOrderRow>>(path, JsonOptions, ct)
                       ?? new List<SalesOrderRow>();

            return list.Select(static d => new SalesListItem
            {
                OrderId = d.OrderId,
                OrderDate = d.OrderDate,
                OrderNo = d.OrderNo,
                PartnerId = d.PartnerId,
                PartnerName = d.PartnerName,
                TotalAmount = d.TotalAmount,
                VatAmount = d.VatAmount,
                Status = d.Status
            }).ToList();
        }
        catch (Exception)
        {
            return new List<SalesListItem>();
        }
    }

    /// <summary>수주서를 거래명세서(판매)로 전환한다.</summary>
    public async Task<ConvertToDeliveryResponse?> ConvertOrderToDeliveryAsync(string orderId, CancellationToken ct = default)
    {
        var (result, _) = await ConvertOrderToDeliveryWithErrorAsync(orderId, ct);
        return result;
    }

    /// <summary>수주서 → 판매 전환. 실패 시 서버 응답 본문을 함께 반환한다.</summary>
    public async Task<(ConvertToDeliveryResponse? Result, string? Error)> ConvertOrderToDeliveryWithErrorAsync(string orderId, CancellationToken ct = default)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(
                $"api/sales/orders/{Uri.EscapeDataString(orderId)}/convert-to-delivery", content, cancellationToken: ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                return (null, string.IsNullOrWhiteSpace(err) ? $"HTTP {(int)resp.StatusCode}" : err);
            }

            var body = await resp.Content.ReadFromJsonAsync<ConvertToDeliveryResponse>(JsonOptions, ct);
            return (body, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
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
        catch (Exception)
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
        catch (Exception)
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
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 확정된 거래명세서 취소 — Reverse 원장 발행으로 재고·잔액·수금 전체 복귀.
    /// draft 상태 전표는 DeleteAsync 사용.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> CancelConfirmedAsync(string deliveryId, CancellationToken ct = default)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var res = await http.PostAsync(
                $"api/sales/deliveries/{Uri.EscapeDataString(deliveryId)}/cancel", content, ct);
            if (res.IsSuccessStatusCode) return (true, null);

            var body = await res.Content.ReadAsStringAsync(ct);
            return (false, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
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
        catch (Exception)
        {
            return new List<PartnerSearchResult>();
        }
    }

    /// <summary>발주서 목록 조회 (발주 전용 API 호출).</summary>
    public async Task<List<PurchaseOrderListItem>> GetPurchaseOrderListAsync(
        DateTime? from = null, DateTime? to = null, string? status = null, CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (from.HasValue) qs.Add($"from={from:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to:yyyy-MM-dd}");
            if (!string.IsNullOrEmpty(status)) qs.Add($"status={Uri.EscapeDataString(status)}");
            var path = "api/purchase/orders" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            var list = await http.GetFromJsonAsync<List<PurchaseOrderListItem>>(path, JsonOptions, ct)
                       ?? new List<PurchaseOrderListItem>();

            return list;
        }
        catch (Exception)
        {
            return new List<PurchaseOrderListItem>();
        }
    }

    /// <summary>매입명세서 목록 조회 (매입명세 전용 API 호출).</summary>
    public async Task<List<PurchaseReceiptListItem>> GetPurchaseReceiptListAsync(
        DateTime? from = null, DateTime? to = null, string? status = null, CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (from.HasValue) qs.Add($"from={from:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to:yyyy-MM-dd}");
            if (!string.IsNullOrEmpty(status)) qs.Add($"status={Uri.EscapeDataString(status)}");
            var path = "api/purchase/receipts" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            var list = await http.GetFromJsonAsync<List<PurchaseReceiptListItem>>(path, JsonOptions, ct)
                       ?? new List<PurchaseReceiptListItem>();

            return list;
        }
        catch (Exception)
        {
            return new List<PurchaseReceiptListItem>();
        }
    }

    /// <summary>매입명세서 단건 상세 조회 (목록 → 편집 화면 로드용).</summary>
    public async Task<PurchaseReceiptDetailModel?> GetPurchaseReceiptDetailAsync(string receiptId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<PurchaseReceiptDetailModel>(
                $"api/purchase/receipts/{Uri.EscapeDataString(receiptId)}",
                JsonOptions,
                ct);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>매입명세서 draft 삭제. 성공 시 (true, null), 실패 시 (false, 서버응답).</summary>
    public async Task<(bool Success, string? Error)> DeletePurchaseReceiptAsync(string receiptId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.DeleteAsync(
                $"api/purchase/receipts/{Uri.EscapeDataString(receiptId)}", ct);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>발주서를 매입명세서(입고)로 전환한다.</summary>
    public async Task<ConvertToReceiptResponse?> ConvertOrderToReceiptAsync(string poId, CancellationToken ct = default)
    {
        var (result, _) = await ConvertOrderToReceiptWithErrorAsync(poId, ct);
        return result;
    }

    /// <summary>발주서 → 매입 전환. 실패 시 서버 응답 본문을 함께 반환한다.</summary>
    public async Task<(ConvertToReceiptResponse? Result, string? Error)> ConvertOrderToReceiptWithErrorAsync(string poId, CancellationToken ct = default)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(
                $"api/purchase/orders/{Uri.EscapeDataString(poId)}/convert-to-receipt", content, cancellationToken: ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                return (null, string.IsNullOrWhiteSpace(err) ? $"HTTP {(int)resp.StatusCode}" : err);
            }

            var body = await resp.Content.ReadFromJsonAsync<ConvertToReceiptResponse>(JsonOptions, ct);
            return (body, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<List<PurchaseReturnListItem>> GetPurchaseReturnListAsync(
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (from.HasValue) qs.Add($"from={from:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to:yyyy-MM-dd}");
            var path = "api/purchase/returns" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
            var list = await http.GetFromJsonAsync<List<PurchaseReturnListItem>>(path, JsonOptions, ct);
            return list ?? new();
        }
        catch { return new(); }
    }

    /// <summary>거래명세서 확정 직후 자동발주 후보 조회 (사장님 지시 2026-04-26).</summary>
    public async Task<List<AutoOrderCandidateModel>> GetAutoOrderCandidatesAsync(string deliveryId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<AutoOrderCandidateModel>>(
                $"api/sales/deliveries/{Uri.EscapeDataString(deliveryId)}/auto-order-candidates",
                JsonOptions, ct) ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    /// <summary>
    /// 자동발주 즉시 생성. 공급처별 발주서(draft)로 묶여 반환.
    /// autoReceive=true 면 발주 직후 매입전환 + 매입확정까지 원클릭 (사장님 지시 2026-04-26).
    /// </summary>
    public async Task<List<AutoOrderResultModel>> CreateAutoOrdersAsync(
        IReadOnlyList<AutoOrderCandidateModel> candidates,
        bool autoReceive = false,
        CancellationToken ct = default)
    {
        try
        {
            var path = $"api/sales/auto-orders?autoReceive={autoReceive.ToString().ToLowerInvariant()}";
            using var resp = await http.PostAsJsonAsync(path, candidates, ct);
            if (!resp.IsSuccessStatusCode) return new();
            return await resp.Content.ReadFromJsonAsync<List<AutoOrderResultModel>>(JsonOptions, ct) ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    /// <summary>발주서 단건 상세 조회.</summary>
    public async Task<PurchaseOrderDetailModel?> GetPurchaseOrderDetailAsync(string poId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<PurchaseOrderDetailModel>(
                $"api/purchase/orders/{Uri.EscapeDataString(poId)}", JsonOptions, ct);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>발주서 삭제(소프트). 성공 시 (true, null).</summary>
    public async Task<(bool Success, string? Error)> DeletePurchaseOrderAsync(string poId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.DeleteAsync(
                $"api/purchase/orders/{Uri.EscapeDataString(poId)}", ct);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>수주서 단건 상세 조회.</summary>
    public async Task<SalesOrderDetailModel?> GetSalesOrderDetailAsync(string orderId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<SalesOrderDetailModel>(
                $"api/sales/orders/{Uri.EscapeDataString(orderId)}", JsonOptions, ct);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>수주서 삭제(소프트). 성공 시 (true, null).</summary>
    public async Task<(bool Success, string? Error)> DeleteSalesOrderAsync(string orderId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.DeleteAsync(
                $"api/sales/orders/{Uri.EscapeDataString(orderId)}", ct);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>매입반품 단건 상세 조회.</summary>
    public async Task<PurchaseReturnDetailModel?> GetPurchaseReturnDetailAsync(string returnId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<PurchaseReturnDetailModel>(
                $"api/purchase/returns/{Uri.EscapeDataString(returnId)}", JsonOptions, ct);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>매입반품 삭제 (draft만). 성공 시 (true, null).</summary>
    public async Task<(bool Success, string? Error)> DeletePurchaseReturnAsync(string returnId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.DeleteAsync(
                $"api/purchase/returns/{Uri.EscapeDataString(returnId)}", ct);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<bool> ConvertReceiptToReturnAsync(string receiptId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.PostAsync(
                $"api/purchase/receipts/{Uri.EscapeDataString(receiptId)}/convert-to-return",
                new StringContent("{}", Encoding.UTF8, "application/json"), ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 매입반품 확정 — status 'draft' → 'confirmed' + 재고원장 Reverse OUT 발행.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> ConfirmPurchaseReturnAsync(string returnId, CancellationToken ct = default)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(
                $"api/purchase/returns/{Uri.EscapeDataString(returnId)}/confirm", content, ct);
            if (resp.IsSuccessStatusCode) return (true, null);

            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
