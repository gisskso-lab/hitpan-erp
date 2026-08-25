namespace HitPan.Web.Models;

public enum WorkDocumentKind
{
    Quotation,
    SalesDelivery,
    SalesOrder,
    PurchaseOrder,
    PurchaseReceipt,
    Return,

    /// <summary>매출반품(반품확인서) — 고객사가 반품한 품목 (20260825작6).</summary>
    /// <remarks>매입반품(<see cref="Return"/>)과 방향이 반대인 별개 업무라 탭도 따로 둔다.</remarks>
    SalesReturn
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
