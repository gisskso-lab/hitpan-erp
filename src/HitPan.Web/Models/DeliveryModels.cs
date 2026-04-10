namespace HitPan.Web.Models;

public enum DeliveryGridDensity
{
    Compact,
    Normal,
    Comfortable
}

public sealed class DeliveryLineModel
{
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
    public bool IsProcessed => Status is "invoiced" or "cancelled";
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
            "수주" => Flow(
                currentKey: "order",
                Step("quote", "견적", "/quotes", WorkDocumentKind.SalesOrder, draft.LinkedQuoteDocumentNo),
                Step("order", "수주", "/sales", WorkDocumentKind.SalesOrder, draft.DocumentNumber),
                Step("delivery", "거래명세서", "/sales", WorkDocumentKind.SalesDelivery, null),
                Step("tax", "세금계산서", "/accounting", null, null)),
            "발주" => Flow(
                currentKey: "po",
                Step("po", "발주", "/purchase", WorkDocumentKind.PurchaseOrder, draft.DocumentNumber),
                Step("purchase", "매입", "/purchase", WorkDocumentKind.PurchaseReceipt, draft.LinkedPurchaseOrderDocumentNo),
                Step("receipt", "입고", "/purchase", WorkDocumentKind.PurchaseReceipt, null)),
            "매입" => Flow(
                currentKey: "purchase",
                Step("po", "발주", "/purchase", WorkDocumentKind.PurchaseOrder, draft.LinkedPurchaseOrderDocumentNo),
                Step("purchase", "매입", "/purchase", WorkDocumentKind.PurchaseReceipt, draft.DocumentNumber),
                Step("receipt", "입고", "/purchase", WorkDocumentKind.PurchaseReceipt, null)),
            "반품" => Flow(
                currentKey: "return",
                Step("return", "반품", "/sales", WorkDocumentKind.Return, draft.DocumentNumber),
                Step("stock", "재고", "/stock", null, null)),
            _ => Flow(
                currentKey: "delivery",
                Step("quote", "견적", "/quotes", WorkDocumentKind.SalesOrder, draft.LinkedQuoteDocumentNo),
                Step("order", "수주", "/quotes", WorkDocumentKind.SalesOrder, draft.LinkedSalesOrderDocumentNo),
                Step("delivery", "거래명세서", "/sales", WorkDocumentKind.SalesDelivery, draft.DocumentNumber),
                Step("tax", "세금계산서", "/accounting", null, null))
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
