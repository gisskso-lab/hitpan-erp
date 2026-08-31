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

        // 🔴 20260831 — P0: 거래명세서 신규저장이 400 으로 죽어 있었다.
        //
        //   서버 POST /api/sales/deliveries 에 20260827작9 에서 [IdempotencyKey] 가 붙었는데
        //   (중복생성 절대금지 오더 — 생성이 두 번 타면 수주서까지 두 장 난다)
        //   **화면 저장 경로만 그 헤더를 안 보냈다.**
        //   같은 파일의 ConfirmAsync(:186) 는 진작 붙이고 있었다 — 생성만 빠진 비대칭이다.
        //
        //   증상: "거래명세서 저장 실패: idempotency_key_required" ⇒ 저장 자체가 불가.
        //   ⚠️ 서버가 잘못한 게 아니다. 헤더를 요구하는 게 맞고, 안 보내던 쪽이 틀렸다.
        //
        //   키는 draft.Id 를 쓴다(ConfirmAsync 와 동일 규칙) — 같은 초안을 두 번 눌러도
        //   서버가 같은 키로 보고 한 번만 반영한다. 신규(Id 없음)면 새 GUID 를 만든다.
        //   🔴 GUID 를 매번 새로 만들면 멱등이 성립하지 않지만, 신규 저장은 아직 식별자가
        //      없으므로 이 경로에선 서버 잔량 가드(SalesService W5)가 2차 방어를 맡는다.
        var idemKey = string.IsNullOrWhiteSpace(draft.Id) ? Guid.NewGuid().ToString("N") : draft.Id;

        using var content = JsonContent.Create(payload, options: PostJsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/sales/deliveries") { Content = content };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", idemKey);
        using var resp = await http.SendAsync(req, ct);

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

                // 20260825작5: 수주 자동생성된 건이면 서버가 준 실제 수주번호로 채운다.
                // 종전에는 화면이 "수-yyyyMMdd-001" 을 지어내 항상 -001 로 보였다.
                if (!string.IsNullOrWhiteSpace(body.AutoCreatedOrderNo))
                {
                    draft.LinkedSalesOrderDocumentNo = body.AutoCreatedOrderNo;
                }

                return new DeliverySaveResult(true, body.DocumentNumber, null, body.AutoCreatedOrderNo);
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

    // 봉합 (2026-06-22, 11차전 수주재편집): 종전 수주 수정이 UpdateAsync→PUT api/sales/deliveries 로 가서
    //   sales_order_id 로 거래명세서를 조회해 실패했다(저장 불가). 백엔드 신설 PUT api/sales/orders/{id}
    //   (UpdateOrderAsync, draft 만 수정)를 호출하는 수주 전용 수정 메서드를 추가한다. 성공 시 204 NoContent.
    public async Task<DeliverySaveResult> UpdateOrderAsync(string orderId, DeliveryDraftModel draft, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return new DeliverySaveResult(false, null, "문서 ID가 없습니다.");
        }

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

        using var resp = await http.PutAsJsonAsync($"api/sales/orders/{Uri.EscapeDataString(orderId)}", payload, PostJsonOptions, ct);
        if (resp.IsSuccessStatusCode)
        {
            return new DeliverySaveResult(true, draft.DocumentNumber ?? orderId, null);
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
                Status = d.Status,

                // 20260825작5: 작성자를 목록까지 실어 보낸다 — 여기서 빠뜨리면 그리드가 항상 공란이다.
                CreatedByName = d.CreatedByName
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
            Status = d.Status,

            // 20260825작5: 작성자를 목록까지 실어 보낸다 — 여기서 빠뜨리면 그리드가 항상 공란이다.
            CreatedByName = d.CreatedByName
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
        DateTime? from = null, DateTime? to = null, string? status = null, CancellationToken ct = default,
        bool includeReturns = false)
    {
        try
        {
            var qs = new List<string>();
            if (from.HasValue) qs.Add($"from={from:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to:yyyy-MM-dd}");
            if (!string.IsNullOrEmpty(status)) qs.Add($"status={Uri.EscapeDataString(status)}");
            // 🔴 20260827작1 §8-B — 켰을 때만 보낸다. 안 보내면 서버 기본값(false)이라 종전과 같다.
            if (includeReturns) qs.Add("includeReturns=true");
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
        catch (Exception ex)
        {
            // 🔴 20260825작18 (헌법 #15) — 종전 `catch { return new(); }` 는 예외 변수조차 없었다.
            //   401·500·역직렬화 실패가 전부 "조회 0건" 과 구별되지 않아, 고장이 빈 화면으로
            //   위장됐다. 오류를 오류로 보이게 하지 않으면 원인 규명 자체가 불가능하다.
            Console.Error.WriteLine("[작18] 매입반품 목록 조회 실패: " + ex.GetType().Name + ": " + ex.Message);
            return new();
        }
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

    /// <summary>
    /// 매입명세서를 반품으로 전환하고 <b>만들어진 반품서의 ID·번호를 돌려준다</b> (20260825작18).
    /// </summary>
    /// <remarks>
    /// 🔴 종전엔 <c>bool</c> 만 돌려줬다. 서버는 <c>{returnId, returnNo}</c> 를 정상 반환하는데
    /// 클라이언트가 그걸 <b>버렸다</b>. 그래서 담당자는 방금 만든 반품서의 번호도 위치도 모른 채
    /// 원래 화면에 서 있었다 — 사장님이 본 <i>"전환했는데 아무 데도 안 보인다"</i> 의 뿌리다.
    /// 값을 살려 호출부가 그 문서로 데려갈 수 있게 한다.
    /// <para>
    /// 실패 사유도 함께 돌려준다. 종전 <c>catch { return false; }</c> 는 원인을 통째로 삼켜
    /// 화면이 <i>"전환에 실패했습니다"</i> 한 줄만 띄웠다 (헌법 #15).
    /// </para>
    /// </remarks>
    public async Task<(bool Success, string? ReturnId, string? ReturnNo, string? ErrorMessage)>
        ConvertReceiptToReturnAsync(string receiptId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.PostAsync(
                $"api/purchase/receipts/{Uri.EscapeDataString(receiptId)}/convert-to-return",
                new StringContent("{}", Encoding.UTF8, "application/json"), ct);

            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return (false, null, null, body);

            var created = System.Text.Json.JsonSerializer.Deserialize<ConvertToReturnResult>(body, JsonOptions);
            return (true, created?.ReturnId, created?.ReturnNo, null);
        }
        catch (Exception ex)
        {
            return (false, null, null, ex.Message);
        }
    }

    /// <summary>전환 API 응답 — 서버 <c>PurchaseController.ConvertReceiptToReturn</c> 과 짝이다.</summary>
    private sealed class ConvertToReturnResult
    {
        public string? ReturnId { get; set; }
        public string? ReturnNo { get; set; }
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

    /// <summary>매입반품 취소 — status 'confirmed' → 'canceled' + 재고원장 Reverse IN(확정 OUT 되돌림). 15차 15-P1 봉합.</summary>
    public async Task<(bool Success, string? ErrorMessage)> CancelPurchaseReturnAsync(string returnId, CancellationToken ct = default)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(
                $"api/purchase/returns/{Uri.EscapeDataString(returnId)}/cancel", content, ct);
            if (resp.IsSuccessStatusCode) return (true, null);

            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 매출반품 — 14차 P0 봉합(2026-06-22, B안 풀 배선). 13차에 백엔드(api/sales/returns)는
    //   만들었으나 프론트 호출이 0건(DOA)이었고, ReturnPage "판매반품" 선택지가 매입반품으로
    //   둔갑 저장돼 재고·잔액·회계 3중 역방향 오염을 일으켰다. 매입반품 3메서드의 거울로 배선한다.
    //   상세 모델은 PurchaseReturnDetailModel 재사용(DeliveryId 자리만 JSON 미매핑, 무해).
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>매출반품 목록 — 기간 조회(PurchaseReturnListItem 모델 재사용).</summary>
    public async Task<List<PurchaseReturnListItem>> GetSalesReturnListAsync(
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (from.HasValue) qs.Add($"from={from:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to:yyyy-MM-dd}");
            var path = "api/sales/returns" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
            var list = await http.GetFromJsonAsync<List<PurchaseReturnListItem>>(path, JsonOptions, ct);
            return list ?? new();
        }
        catch { return new(); }
    }

    /// <summary>
    /// 🔴 <b>매출반품 생성</b> — 20260831작15 (사장님 실측 반려 1 봉합).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>없던 것이 이것 하나였다.</b> 목록(:817)·상세·확정·취소·삭제는 14차에 다 배선됐는데
    /// <b>생성만 없었다.</b> 그래서 20260828작14 의 [반품하기] 는 갈 곳이 없어
    /// <c>SaveAsync</c>(판매 생성)로 갔고, 사장님이 실측에서 잡으셨다 —
    /// <i>"반품이 아니라 추가로 수량 90개가 주문이 된 셈"</i>.
    /// </para>
    /// <para>
    /// 🔴 <b>수량은 양수로 보낸다.</b> 화면이 (−)로 들고 있는 값을 호출부에서 절대값으로 바꿔 담는다.
    /// 부호는 원장이 붙인다(<c>qty_in</c> · 역분개). 서버 DTO 가 <c>[Range(0.0001,…)]</c> 이라
    /// 음수를 보내면 400 이다.
    /// </para>
    /// <para>
    /// ⚠️ 멱등 헤더를 붙인다 — 생성 경로이므로 두 번 눌리면 반품이 두 장 난다.
    /// 20260827작9 가 판매 생성에서 겪은 사고와 같은 자리다.
    /// </para>
    /// </remarks>
    public async Task<DeliverySaveResult> CreateSalesReturnAsync(
        CreateSalesReturnPayload payload, CancellationToken ct = default)
    {
        try
        {
            using var content = JsonContent.Create(payload, options: PostJsonOptions);
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/sales/returns") { Content = content };
            req.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));

            using var resp = await http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadFromJsonAsync<SalesReturnCreateApiResponse>(JsonOptions, ct);
                if (!string.IsNullOrWhiteSpace(body?.ReturnNo))
                {
                    return new DeliverySaveResult(true, body.ReturnNo, null);
                }
                if (!string.IsNullOrWhiteSpace(body?.ReturnId))
                {
                    return new DeliverySaveResult(true, body.ReturnId, null);
                }
                return new DeliverySaveResult(false, null, "서버 응답에 반품번호가 없습니다.");
            }

            var err = await resp.Content.ReadAsStringAsync(ct);
            return new DeliverySaveResult(false, null,
                string.IsNullOrWhiteSpace(err) ? $"HTTP {(int)resp.StatusCode}" : err);
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지.
            return new DeliverySaveResult(false, null, ex.Message);
        }
    }

    /// <summary>매출반품 단건 상세 — 편집 화면 로드용.</summary>
    public async Task<PurchaseReturnDetailModel?> GetSalesReturnDetailAsync(string returnId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<PurchaseReturnDetailModel>(
                $"api/sales/returns/{Uri.EscapeDataString(returnId)}", JsonOptions, ct);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>매출반품 단건 상세 — 반품확인서 화면 전용 (20260825작6).</summary>
    /// <remarks>
    /// 🔴 기존 <see cref="GetSalesReturnDetailAsync"/> 를 고치지 않고 <b>따로 둔다.</b>
    /// 그 메서드는 매입관리 반품 화면도 쓰고 있어, 반환 타입을 바꾸면 매입이 깨진다.
    /// (실제로 한 번 바꿨다가 빌드가 잡았다 — 헌법 #1 · #12)
    /// 이쪽만 <see cref="SalesReturnDetailModel"/> 로 받아 <b>로스</b> 표시를 살려 온다.
    /// </remarks>
    public async Task<SalesReturnDetailModel?> GetSalesReturnDetailForSalesAsync(string returnId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<SalesReturnDetailModel>(
                $"api/sales/returns/{Uri.EscapeDataString(returnId)}", JsonOptions, ct);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>매출반품 삭제 (draft만). 성공 시 (true, null).</summary>
    public async Task<(bool Success, string? Error)> DeleteSalesReturnAsync(string returnId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.DeleteAsync(
                $"api/sales/returns/{Uri.EscapeDataString(returnId)}", ct);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// 매출반품 확정 — status 'draft' → 'confirmed' + 재고원장 Reverse IN 발행(재고 증가).
    /// 매입반품(Reverse OUT)의 정확한 거울 — 고객이 판매분을 돌려보낸 것이므로 재고가 증가한다.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> ConfirmSalesReturnAsync(string returnId, CancellationToken ct = default)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(
                $"api/sales/returns/{Uri.EscapeDataString(returnId)}/confirm", content, ct);
            if (resp.IsSuccessStatusCode) return (true, null);

            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>매출반품 취소 — status 'confirmed' → 'canceled' + 재고원장 Reverse OUT(확정 IN 되돌림). 15차 15-P1 봉합.</summary>
    public async Task<(bool Success, string? ErrorMessage)> CancelSalesReturnAsync(string returnId, CancellationToken ct = default)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(
                $"api/sales/returns/{Uri.EscapeDataString(returnId)}/cancel", content, ct);
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
