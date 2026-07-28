namespace HitPan.Web.Models;

public enum DeliveryGridDensity
{
    Compact,
    Normal,
    Comfortable
}

public sealed class DeliveryLineModel
{
    public string ItemId { get; set; } = string.Empty;

    public int No { get; set; }
    public int RowNo
    {
        get => No;
        set => No = value;
    }

    public bool IsSelected { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string Unit { get; set; } = "EA";
    public decimal Quantity { get; set; }
    public decimal Qty
    {
        get => Quantity;
        set => Quantity = value;
    }

    public decimal UnitPrice { get; set; }
    public decimal Amount => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
    public decimal VatAmount => decimal.Round(Amount * 0.1m, 2, MidpointRounding.AwayFromZero);
    public decimal Total => Amount + VatAmount;
    public string Note { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    public string LineAssignee { get; set; } = string.Empty;
    public bool IsPlaceholder { get; set; }

    /// <summary>상품 타입 (product | assembly | promo). 라인에 1+1·조립 칩 표시용.</summary>
    public string? ItemType { get; set; }

    public void RecalculateAmount()
    {
        // Computed fields are derived from quantity and unit price.
    }

    public DeliveryLineModel CloneForRow()
    {
        return new DeliveryLineModel
        {
            ItemName = ItemName,
            Spec = Spec,
            Unit = Unit,
            Quantity = Quantity,
            UnitPrice = UnitPrice,
            Note = Note,
            Warehouse = Warehouse,
            LineAssignee = LineAssignee,
            IsPlaceholder = false
        };
    }
}

public sealed class DeliveryDraftModel
{
    public string? Id { get; set; }

    /// <summary>거래처 API 식별자 (자동완성 선택 시 설정).</summary>
    public string? PartnerId { get; set; }

    /// <summary>비고.</summary>
    public string? Memo { get; set; }

    public string DocumentType { get; set; } = "거래명세서";
    public DateTime SalesDate { get; set; }
    public int DailySequence { get; set; }
    public string? DocumentNumber { get; set; }
    public string SalesCompany { get; set; } = string.Empty;
    public string ManagerName { get; set; } = string.Empty;
    public List<DeliveryLineModel> Lines { get; set; } = new();
    public DeliveryGridDensity Density { get; set; } = DeliveryGridDensity.Normal;

    /// <summary>이전 단계 연결 문서 (예: 견적)</summary>
    public string? LinkedQuoteDocumentNo { get; set; }

    /// <summary>이전 단계 연결 문서 (예: 수주)</summary>
    public string? LinkedSalesOrderDocumentNo { get; set; }

    /// <summary>서버 상태 (draft | confirmed | cancelled). 확정 후 '매출취소' 버튼 노출 분기용.</summary>
    public string? Status { get; set; }

    /// <summary>이전 단계 연결 문서 (예: 발주)</summary>
    public string? LinkedPurchaseOrderDocumentNo { get; set; }
}

public sealed class DeliverySummaryModel
{
    public decimal PrevReceivable { get; set; } // 전미수금
    public decimal TodaySales { get; set; } // 당일판매
    public decimal TodayReceipt { get; set; } // 당일수금
    public decimal ClaimAmount { get; set; } // 청구금액
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal DiscountAmount { get; set; } // 전체할인
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public sealed class SalesListItem
{
    public string OrderId { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsAuto { get; set; }

    // UI only
    public bool IsChecked { get; set; }
    public bool IsProcessed =>
        Status is "cancelled";
}

public class DeliveryListDto
{
    public string DeliveryId { get; set; } = "";
    public string DeliveryNo { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public string PartnerId { get; set; } = "";
    public string PartnerName { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = "";
    public string? Memo { get; set; }

    public bool IsChecked { get; set; }
    public bool IsProcessed =>
        Status == "cancelled";
}

public sealed class DeliveryDetailDto : DeliveryListDto
{
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public List<DeliveryItemDto> Items { get; set; } = new();
    public decimal PrevReceivable { get; set; }
    public decimal TodaySales { get; set; }
    public decimal TodayReceipt { get; set; }
}

public sealed class DeliveryItemDto
{
    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public string? Memo { get; set; }
    public int RowNo { get; set; }
}

public sealed class UpdateDeliveryRequest
{
    public DateTime OrderDate { get; set; }
    public string PartnerId { get; set; } = "";
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? Memo { get; set; }
    public List<DeliveryItemDto> Items { get; set; } = new();
}

public sealed class PartnerSearchResult
{
    public string PartnerId { get; set; } = "";
    public string PartnerName { get; set; } = "";
    public string? BizNo { get; set; }
    public string? Tel { get; set; }
    public string? Address { get; set; }
}

public sealed class DeliveryWorkflowStepModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Href { get; set; } = "/";
    public WorkDocumentKind? OpenTabKind { get; set; }
    public string? LinkedDocumentNumber { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class DeliverySaveApiResponse
{
    public string? Id { get; set; }
    public string? DocumentNumber { get; set; }
}

/// <summary>거래명세서 저장 결과. 실패 시 Error에 서버 응답 본문을 담아 UI에서 원인 표시.</summary>
public sealed record DeliverySaveResult(bool Success, string? DocumentNumber, string? Error);

/// <summary>서버 CreateDeliveryRequest와 동일 스키마(web 쪽 명시 매핑용).</summary>
public sealed class CreateDeliveryPayload
{
    public string? OrderId { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public string? Memo { get; set; }
    public List<CreateDeliveryItemPayload> Items { get; set; } = new();
}

public sealed class CreateDeliveryItemPayload
{
    public string? OrderItemId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string? ItemName { get; set; }
    public string? WarehouseId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}

/// <summary>
/// 봉합 (2026-06-22, 10차 P0-1): 서버 CreateSalesOrderRequest와 동일 스키마(web 명시 매핑용).
/// 수주서 신규 저장이 거래명세서(CreateDeliveryPayload)가 아니라 수주로 저장되도록 별도 페이로드를 둔다.
/// </summary>
public sealed class CreateSalesOrderPayload
{
    public string PartnerId { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public DateTime OrderDate { get; set; }
    public string? Memo { get; set; }
    public List<CreateSalesOrderItemPayload> Items { get; set; } = new();
}

public sealed class CreateSalesOrderItemPayload
{
    public string ItemId { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}

/// <summary>
/// 서버 SalesOrderListDto (api/sales/orders 응답)과 1:1 매핑되는 웹 전용 모델.
/// DeliveryListDto와 필드명이 달라(OrderId vs DeliveryId) 별도 타입으로 분리한다.
/// </summary>
public sealed class SalesOrderRow
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public sealed class BulkConfirmApiResponse
{
    public List<string> Success { get; set; } = new();
    public List<BulkConfirmFailedItem> Failed { get; set; } = new();
}

public sealed class BulkConfirmFailedItem
{
    public string? Id { get; set; }
    public string? Reason { get; set; }
}

public sealed class ConvertToDeliveryResponse
{
    public string DeliveryId { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
}

/// <summary>
/// 발주 목록 조회 응답을 매핑하는 웹 전용 모델.
/// 서버 PurchaseOrderListDto 와 동일 필드를 JSON 역직렬화용으로 둔다.
/// </summary>
public sealed class PurchaseOrderListItem
{
    public string PoId { get; set; } = string.Empty;
    public string PoNo { get; set; } = string.Empty;
    public DateTime PoDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public bool IsChecked { get; set; }
}

/// <summary>
/// 매입명세 목록 조회 응답을 매핑하는 웹 전용 모델.
/// 서버 PurchaseReceiptListDto 와 동일 필드를 JSON 역직렬화용으로 둔다.
/// </summary>
public sealed class PurchaseReceiptListItem
{
    public string ReceiptId { get; set; } = string.Empty;
    public string ReceiptNo { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public bool IsChecked { get; set; }
}

/// <summary>매입명세서 단건 상세 (목록 → 편집 화면 로드용).</summary>
public sealed class PurchaseReceiptDetailModel
{
    public string ReceiptId { get; set; } = string.Empty;
    public string ReceiptNo { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public string? PoId { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public List<PurchaseReceiptDetailItem> Items { get; set; } = new();
}

public sealed class PurchaseReceiptDetailItem
{
    public string ReceiptItemId { get; set; } = string.Empty;
    public string? PoItemId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public string? WarehouseId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}

/// <summary>
/// 발주서 매입전환 응답.
/// </summary>
public sealed class ConvertToReceiptResponse
{
    public string ReceiptId { get; set; } = string.Empty;
    public string ReceiptNo { get; set; } = string.Empty;
}

/// <summary>거래명세서 확정 직후 자동발주 후보 (사장님 지시 2026-04-26).</summary>
public sealed class AutoOrderCandidateModel
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal CurrentQty { get; set; }
    public decimal SafetyQty { get; set; }
    public decimal SuggestedOrderQty { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>"out_of_stock" | "below_safety"</summary>
    public string Reason { get; set; } = string.Empty;
}

public sealed class AutoOrderResultModel
{
    public string? PoId { get; set; }
    public string? PoNo { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public List<string> ItemIds { get; set; } = new();
    public bool Success { get; set; }
    public string? Reason { get; set; }
}

/// <summary>발주서 단건 상세.</summary>
public sealed class PurchaseOrderDetailModel
{
    public string PoId { get; set; } = string.Empty;
    public string PoNo { get; set; } = string.Empty;
    public DateTime PoDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public List<PurchaseOrderDetailItem> Items { get; set; } = new();
}

public sealed class PurchaseOrderDetailItem
{
    public string PoItemId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public string? WarehouseId { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}

/// <summary>수주서 단건 상세.</summary>
public sealed class SalesOrderDetailModel
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public List<SalesOrderDetailItem> Items { get; set; } = new();
}

public sealed class SalesOrderDetailItem
{
    public string OrderItemId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}

/// <summary>매입반품 단건 상세.</summary>
public sealed class PurchaseReturnDetailModel
{
    public string ReturnId { get; set; } = string.Empty;
    public string ReturnNo { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string? ReceiptId { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public List<PurchaseReturnDetailItem> Items { get; set; } = new();
}

public sealed class PurchaseReturnDetailItem
{
    public string ReturnItemId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public string? WarehouseId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
}

public enum DeliveryContextAction
{
    CopyRow,
    InsertAbove,
    InsertBelow,
    DeleteRow,
    LinkSalesOrder,
    IssueTaxInvoice,
    PartnerHistory
}

public static class DeliveryWorkflowFactory
{
    public static IReadOnlyList<DeliveryWorkflowStepModel> Build(string documentType, DeliveryDraftModel draft)
    {
        return documentType switch
        {
            "견적" => Flow(
                currentKey: "quote",
                Step("quote", "견적", "/quotations", null, draft.DocumentNumber),
                Step("order", "수주", "/sales-orders", null, null),
                Step("delivery", "거래명세서", "/deliveries", null, null),
                Step("tax", "세금계산서", "/deliveries", null, null)),
            "수주" => Flow(
                currentKey: "order",
                Step("quote", "견적", "/quotations", null, draft.LinkedQuoteDocumentNo),
                Step("order", "수주", "/sales-orders", null, draft.DocumentNumber),
                Step("delivery", "거래명세서", "/deliveries", null, null),
                Step("tax", "세금계산서", "/deliveries", null, null)),
            "발주" => Flow(
                currentKey: "po",
                Step("po", "발주", "/purchase-orders", null, draft.DocumentNumber),
                Step("purchase", "매입", "/purchases", null, null),
                Step("return", "반품", "/returns", null, null)),
            "매입" => Flow(
                currentKey: "purchase",
                Step("po", "발주", "/purchase-orders", null, draft.LinkedPurchaseOrderDocumentNo),
                Step("purchase", "매입", "/purchases", null, draft.DocumentNumber),
                Step("return", "반품", "/returns", null, null)),
            "반품" => Flow(
                currentKey: "return",
                Step("po", "발주", "/purchase-orders", null, null),
                Step("purchase", "매입", "/purchases", null, null),
                Step("return", "반품", "/returns", null, draft.DocumentNumber)),
            _ => Flow(
                currentKey: "delivery",
                Step("quote", "견적", "/quotations", null, draft.LinkedQuoteDocumentNo),
                Step("order", "수주", "/sales-orders", null, draft.LinkedSalesOrderDocumentNo),
                Step("delivery", "거래명세서", "/deliveries", null, draft.DocumentNumber),
                Step("tax", "세금계산서", "/deliveries", null, null))
        };
    }

    private static DeliveryWorkflowStepModel[] Flow(string currentKey, params DeliveryWorkflowStepModel[] steps)
    {
        foreach (var s in steps)
        {
            s.IsCurrent = s.Key == currentKey;
        }

        return steps;
    }

    private static DeliveryWorkflowStepModel Step(
        string key,
        string label,
        string href,
        WorkDocumentKind? tabKind,
        string? linkedNo) =>
        new()
        {
            Key = key,
            Label = label,
            Href = href,
            OpenTabKind = tabKind,
            LinkedDocumentNumber = linkedNo
        };
}

public class ReportRow
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public decimal Qty { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    /// <summary>안전재고 미달 여부 — 재고현황에서 행 강조용</summary>
    public bool IsBelowSafety { get; set; }
}

public class ProfitReportRow
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
    public decimal ProfitRate { get; set; }
}

/// <summary>
/// 수불부(원장) 행 모델이다.
/// </summary>
public class StockLedgerRow
{
    public string Label { get; set; } = "";
    public decimal QtyIn { get; set; }
    public decimal QtyOut { get; set; }
    public decimal Balance { get; set; }
    public decimal AmountIn { get; set; }
    public decimal AmountOut { get; set; }
}

public sealed class PurchaseReturnListItem
{
    public string ReturnId { get; set; } = string.Empty;
    public string ReturnNo { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public bool IsChecked { get; set; }
}
