namespace HitPan.Web.Models;

public enum WorkDocumentKind
{
    SalesDelivery,
    SalesOrder,
    PurchaseOrder,
    PurchaseReceipt,
    Return
}

public sealed class WorkTabState
{
    public required int Id { get; init; }
    public required WorkDocumentKind Kind { get; init; }
    public string? DocumentNumber { get; set; }
    public bool IsDirty { get; set; }

    public string DisplayTitle =>
        string.IsNullOrEmpty(DocumentNumber) ? "새 작업창" : DocumentNumber!;
}
