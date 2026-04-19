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

    // UI only
    public bool IsChecked { get; set; }
    public bool IsProcessed =>
        Status is "confirmed" or "invoiced" or "cancelled";
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
        Status == "confirmed" ||
        Status == "invoiced" ||
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
