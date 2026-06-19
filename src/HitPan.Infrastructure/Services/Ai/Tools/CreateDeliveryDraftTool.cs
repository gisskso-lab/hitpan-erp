using System.Globalization;
using System.Text;
using System.Text.Json;
using HitPan.Application.DTOs.Chatbot;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Ai.Tools;

// ─────────────────────────────────────────────────────────────────
// [생성 Tool] 거래명세서 초안 작성 — draft 상태로만 만들고 승인 카드 반환.
//
//   사장님 비전(2026-06-20): "거래명세서 작성하고 수주도" → 초안 화면 + 승인/반려 → 승인 시 확정.
//   절대 규칙: 이 Tool 은 draft 까지만. 확정(confirm)·원장 반영은 사람이 승인 버튼으로(헌법 #6).
//   품목은 이름만 줘도 서비스가 마스터에서 매칭(ItemId 비우면 ItemName 사용).
//   거래처는 partnerId 필수 — 클로드가 partner_search 로 먼저 얻어서 넘긴다.
// ─────────────────────────────────────────────────────────────────
public sealed class CreateDeliveryDraftTool : IHitpanTool
{
    private readonly ISalesService _sales;
    private readonly ILogger<CreateDeliveryDraftTool> _logger;

    public CreateDeliveryDraftTool(ISalesService sales, ILogger<CreateDeliveryDraftTool> logger)
    {
        _sales = sales;
        _logger = logger;
    }

    public string Name => "create_delivery_draft";

    public string Description =>
        "거래명세서(납품) 초안을 작성한다. 초안(draft)만 만들고 사람의 승인을 받는다(자동 확정 절대 금지). " +
        "거래처ID는 먼저 partner_search 로 확인해 넘긴다. 품목은 이름으로 지정 가능. " +
        "사용자가 '거래명세서 작성/써줘', '납품서 만들어' 등을 요청할 때 사용.";

    public bool IsWrite => true;

    // 봉합 (2026-06-20, 3차 전수조사 AICHAT-SEC-01-F1): 거래명세서 작성은 판매 직무 기능 →
    //   정식 SalesController(SalesOnly) 와 동일 권한 요구. 엔진이 ctx.Policies 로 게이트.
    public string? RequiredPolicy => "SalesOnly";

    public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            partnerId = new { type = "string", description = "거래처ID(필수). partner_search 로 먼저 확인." },
            partnerName = new { type = "string", description = "거래처명(표시용, 선택)." },
            deliveryDate = new { type = "string", description = "납품일 YYYY-MM-DD(선택, 없으면 오늘)." },
            memo = new { type = "string", description = "메모(선택)." },
            items = new
            {
                type = "array",
                description = "품목 목록(1개 이상).",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        itemName = new { type = "string", description = "품목명(품목ID 모르면 이름으로)." },
                        qty = new { type = "number", description = "수량(0보다 큼)." },
                        unitPrice = new { type = "number", description = "단가(0보다 큼)." }
                    },
                    required = new[] { "itemName", "qty", "unitPrice" }
                }
            }
        },
        required = new[] { "partnerId", "items" }
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext ctx, CancellationToken ct = default)
    {
        var partnerId = GetString(input, "partnerId");
        if (string.IsNullOrWhiteSpace(partnerId))
        {
            return ToolResult.Fail("거래처ID가 필요합니다. 먼저 거래처를 검색해 주세요.");
        }
        var partnerName = GetString(input, "partnerName") ?? "거래처";

        if (!input.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array
            || itemsEl.GetArrayLength() == 0)
        {
            return ToolResult.Fail("품목이 최소 1개 필요합니다.");
        }

        var deliveryDate = GetDate(input, "deliveryDate") ?? DateTime.Today;
        var memo = GetString(input, "memo");

        var items = new List<CreateDeliveryItemRequest>();
        decimal total = 0;
        var summary = new StringBuilder();
        foreach (var it in itemsEl.EnumerateArray())
        {
            var itemName = GetString(it, "itemName") ?? "";
            var qty = GetDecimal(it, "qty");
            var unitPrice = GetDecimal(it, "unitPrice");
            if (string.IsNullOrWhiteSpace(itemName) || qty <= 0 || unitPrice <= 0)
            {
                return ToolResult.Fail("각 품목은 이름·수량(>0)·단가(>0)가 필요합니다.");
            }
            var supply = Math.Round(qty * unitPrice, 0);
            var vat = Math.Round(supply * 0.1m, 0);
            total += supply + vat;
            items.Add(new CreateDeliveryItemRequest
            {
                ItemName = itemName,    // ItemId 비우면 서비스가 이름으로 매칭.
                Qty = qty,
                UnitPrice = unitPrice,
                SupplyAmount = supply,
                VatAmount = vat
            });
            summary.AppendLine($"• {itemName} — {qty:0.##}개 × {unitPrice:N0}원 = 공급 {supply:N0}원 (부가세 {vat:N0}원)");
        }

        var request = new CreateDeliveryRequest
        {
            PartnerId = partnerId!,
            DeliveryDate = deliveryDate,
            Memo = memo,
            Items = items
        };

        // 초안 생성 — 서비스는 draft 상태로 만든다(확정은 별도 confirm 엔드포인트).
        var (id, documentNumber) = await _sales.CreateDeliveryAsync(request, ct).ConfigureAwait(false);

        var summaryText =
            $"거래처: {partnerName}\n납품일: {deliveryDate:yyyy-MM-dd}\n" +
            summary.ToString() +
            $"합계(부가세 포함): {total:N0}원";

        var pending = new PendingActionDto
        {
            Kind = "sales-delivery-draft",
            Title = $"거래명세서 초안 — {partnerName}",
            Summary = summaryText,
            DraftId = id,
            ApproveMethod = "POST",
            // Lv.3 연쇄 승인 엔드포인트(거래명세서 확정 + 수주 자동생성). 단순 confirm 아님.
            ApproveUrl = "/api/chatbot/approve-action",
            ChainNote = "승인하면 거래명세서가 확정되고, 워크플로우에 맞춰 수주서도 자동 생성됩니다."
        };

        // 클로드에게 줄 결과(승인 대기임을 알림 — 클로드가 "초안 만들었으니 승인하세요"라고 안내).
        var content = JsonSerializer.Serialize(new
        {
            status = "draft_created_pending_approval",
            documentNumber,
            partnerName,
            total,
            note = "초안만 생성됨. 확정은 사람 승인 필요(자동 확정 금지)."
        });

        return new ToolResult { Content = content, Succeeded = true, PendingApproval = pending };
    }

    private static string? GetString(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static decimal GetDecimal(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v)
           && (v.ValueKind == JsonValueKind.Number) && v.TryGetDecimal(out var d) ? d : 0m;

    private static DateTime? GetDate(JsonElement e, string key)
        => DateTime.TryParse(GetString(e, key), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
