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
    public string Title { get; set; } = "";
    public string? SubTitle { get; set; }
    public string Url { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsDirty { get; set; }

    public string DisplayTitle =>
        string.IsNullOrEmpty(SubTitle) ? Title : $"{Title} ({SubTitle})";
}
