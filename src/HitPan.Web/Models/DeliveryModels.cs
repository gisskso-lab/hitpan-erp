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

    /// <summary>파손 로스 여부 — 매출반품에서만 쓴다 (20260825작6).</summary>
    /// <remarks>
    /// 사장님 정의: <i>"파손이면 로스로 정의, 파손이 아니면 재입고(재고반영)"</i>.
    /// 체크하면 확정해도 <b>재고에 안 들어간다</b> — 팔 수 없는 물건이라서다.
    /// 매출·미수 차감은 그대로 간다(고객에게 돈은 돌려준다).
    /// </remarks>
    public bool IsLoss { get; set; }

    /// <summary>
    /// 이 줄이 어느 판매 줄에서 왔는지 (20260825작7). 직접 입력한 줄이면 null.
    /// </summary>
    /// <remarks>
    /// 「판매불러오기」로 채운 줄만 값이 있다. 저장 payload 에 그대로 실어 보내
    /// <c>sales_return_items.delivery_item_id</c> 로 남는다 — 원단가 추적의 근거다.
    /// </remarks>
    public string? DeliveryItemId { get; set; }

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
    /// <summary>전표를 작성한 사원 이름이다 (20260825작5).</summary>
    public string? CreatedByName { get; set; }

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
    /// <summary>전표를 작성한 사원 이름이다 (20260825작5).</summary>
    public string? CreatedByName { get; set; }

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

    /// <summary>연결된 수주서 번호 (20260825작5). 화면이 지어내던 가짜 번호를 대체한다.</summary>
    public string? LinkedOrderNo { get; set; }

    public List<DeliveryItemDto> Items { get; set; } = new();
    public decimal PrevReceivable { get; set; }
    public decimal TodaySales { get; set; }
    public decimal TodayReceipt { get; set; }
}

public sealed class DeliveryItemDto
{
    /// <summary>
    /// 이 판매 줄의 식별자다 (20260825작7). 반품확인서가 <b>어느 판매 줄에서 왔는지</b> 적을 때 쓴다.
    /// </summary>
    /// <remarks>
    /// <c>sales_return_items.delivery_item_id</c> 는 컬럼·FK·DTO 가 전부 있었는데
    /// <b>화면에 값이 안 와서</b> 채울 수가 없었다. 여기가 끊긴 자리였다.
    /// </remarks>
    public string? DeliveryItemId { get; set; }

    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }

    /// <summary>출고 창고 (20260825작7). 반품은 <b>나간 창고로 되돌아온다</b>.</summary>
    public string? WarehouseId { get; set; }

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

    /// <summary>
    /// 수주 없이 저장해 수주서가 자동 생성된 경우에만 그 번호가 담긴다 (20260825작5).
    /// 수주에서 전환된 정상 흐름이면 null.
    /// </summary>
    public string? AutoCreatedOrderNo { get; set; }
}

/// <summary>거래명세서 저장 결과. 실패 시 Error에 서버 응답 본문을 담아 UI에서 원인 표시.</summary>
public sealed record DeliverySaveResult(bool Success, string? DocumentNumber, string? Error, string? AutoCreatedOrderNo = null);

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
    /// <summary>전표를 작성한 사원 이름이다 (20260825작5).</summary>
    public string? CreatedByName { get; set; }

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
    /// <summary>전표를 작성한 사원 이름 (20260825작16).
    /// 서버가 employees.user_id = created_by 조인으로 채운다.</summary>
    public string? CreatedByName { get; set; }

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

    /// <summary>
    /// 반품 상태 (20260826작5) — <c>null</c>=반품 없음 · <c>"draft"</c>=반품 작성중 · <c>"confirmed"</c>=반품 확정.
    /// </summary>
    /// <remarks>
    /// 🔴 사장님 지시(2026-08-26): <i>"반품처리된 전표는 상태처리를 「반품」이라고 표기할것"</i>.
    /// ⚠️ 매입확정과 <b>다른 축</b>이라 <c>Status</c> 를 덮지 않고 별도 값으로 온다 —
    /// 반품했다고 매입이 취소된 게 아니다.
    /// </remarks>
    public string? ReturnStatus { get; set; }

    /// <summary>
    /// 🔴 20260827작3 (사장님 실측 반려) — 이 매입이 나간 <b>반품전표 번호</b>(쉼표 구분).
    /// </summary>
    /// <remarks>
    /// 종전엔 <see cref="ReturnStatus"/> 로 「반품」 <b>글자만</b> 보여줘서, 담당자가
    /// <b>어느 반품전표인지</b> 알 수 없어 반품목록에서 눈으로 찾아야 했다.
    /// ⚠️ 부분반품을 나눠서 하면 <b>둘 이상</b>이라 전부 온다.
    /// </remarks>
    public string? ReturnNos { get; set; }

    /// <summary>전표를 작성한 사원 이름 (20260825작16).
    /// 서버가 employees.user_id = created_by 조인으로 채운다.</summary>
    public string? CreatedByName { get; set; }

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


/// <summary>매출반품(반품확인서) 상세 — 매입반품과 별개 모델이다 (20260825작6).</summary>
/// <remarks>
/// 사장님 정의: <i>"매출에 있는 반품 = 사용자의 고객사가 반품처리한 품목관리"</i>.
/// 매입반품 모델(<see cref="PurchaseReturnDetailModel"/>)을 재사용하지 않는다 —
/// 매출에만 있는 <b>로스</b> 개념이 매입 화면으로 새어 들어가면 안 되기 때문이다.
/// </remarks>
public sealed class SalesReturnDetailModel
{
    public string ReturnId { get; set; } = string.Empty;
    public string ReturnNo { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string? DeliveryId { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public string? ReturnReason { get; set; }
    public string? ReturnReasonMemo { get; set; }
    public List<SalesReturnDetailItem> Items { get; set; } = new();
}

/// <summary>매출반품 품목줄 — 로스 표시가 있다 (20260825작6).</summary>
public sealed class SalesReturnDetailItem
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

    /// <summary>원 판매 줄 연결 (20260825작7) — 다시 열어 고쳐도 링크가 살아 있게 한다.</summary>
    public string? DeliveryItemId { get; set; }

    /// <summary>파손 로스 여부 — true 면 확정해도 재고에 안 들어간다.</summary>
    public bool IsLoss { get; set; }
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
    /// <summary>전표를 작성한 사원 이름이다 (20260825작5).</summary>
    public string? CreatedByName { get; set; }

    public string ReturnId { get; set; } = string.Empty;
    public string ReturnNo { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }

    /// <summary>
    /// 🔴 20260827작3 — 이 반품이 나온 <b>원 매입전표</b>. 서버 <c>PurchaseReturnListDto</c> 와 짝.
    /// ⚠️ NULL 가능(매입명세서 없이 직접 작성한 반품) ⇒ 화면은 「직접작성」으로 표기한다.
    /// </summary>
    public string? ReceiptId { get; set; }

    /// <summary>원 매입전표 번호(사람이 읽는 값).</summary>
    public string? ReceiptNo { get; set; }

    public bool IsChecked { get; set; }
}

/// <summary>
/// 단가 참고값 4종 — 명세서 화면에서 <b>커서를 올리면 보여주는 값</b> (20260820작4 · 설계2 C안).
/// </summary>
/// <remarks>
/// 🔴 <b>이 값들은 "적용된 단가" 가 아니다.</b> 사람이 보고 고르라고 주는 <b>참고 자료</b>다.
/// 문서에 실제로 들어가는 값은 입력칸에 있는 것이고, 사람이 언제든 고칠 수 있다.
///
/// <para>
/// ⚠️ <b>값이 없으면 <c>null</c> 이다. 0 이 아니다.</b> 0 으로 그리면 화면에서
/// <b>진짜 0원과 구별이 안 된다</b>(게이트 G-8). 없는 줄은 <b>빼고 그린다.</b>
/// </para>
/// </remarks>
public sealed class PriceHint
{
    public string ItemId { get; set; } = string.Empty;

    /// <summary>업체특별단가 — 🔴 <b>자동 채움에 쓰이는 유일한 값</b>(C안).</summary>
    public decimal? PartnerSpecialPrice { get; set; }

    /// <summary>최종단가 — 그 업체와 마지막으로 거래한 단가(판매/매입이 서로 다르다).</summary>
    public decimal? LastPrice { get; set; }

    /// <summary>최종단가가 언제 거래분인지.</summary>
    public DateTime? LastPriceDate { get; set; }

    /// <summary>표준단가 — 상품 마스터의 기준 금액.</summary>
    public decimal? StdPrice { get; set; }

    /// <summary>
    /// 상품특별단가 — 🔴 <b>표시 전용. 자동 채움에 끼지 않는다</b>(설계2 §4-4).
    /// 사장님 판정: <i>"상품 특별단가는 존재 자체가 큰 의미가 없네"</i>
    /// </summary>
    public decimal? ItemSpecialPrice { get; set; }

    /// <summary>보여줄 값이 하나라도 있나 — 없으면 말풍선을 아예 띄우지 않는다.</summary>
    public bool HasAny =>
        PartnerSpecialPrice.HasValue || LastPrice.HasValue
        || StdPrice.HasValue || ItemSpecialPrice.HasValue;
}
