using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace HitPan.Application.Services;

/// <summary>엑셀·PDF 문서 출력 (7종 문서 유형).</summary>
public sealed class DocumentService
{
    static DocumentService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // --- ClosedXML: 엑셀 7종 ---
    public byte[] CreateExcelSalesOrder(string tenantId)
        => CreateExcelWorkbook("판매수주", tenantId, new[] { ("주문번호", "SO-001"), ("거래처", "(예시)"), ("금액", "0") });

    public byte[] CreateExcelPurchaseOrder(string tenantId)
        => CreateExcelWorkbook("매입발주", tenantId, new[] { ("발주번호", "PO-001"), ("거래처", "(예시)"), ("금액", "0") });

    public byte[] CreateExcelDelivery(string tenantId)
        => CreateExcelWorkbook("거래명세", tenantId, new[] { ("명세번호", "DL-001"), ("거래처", "(예시)"), ("합계", "0") });

    public byte[] CreateExcelStock(string tenantId)
        => CreateExcelWorkbook("재고현황", tenantId, new[] { ("품목코드", ""), ("품명", ""), ("수량", "0") });

    public byte[] CreateExcelPartner(string tenantId)
        => CreateExcelWorkbook("거래처목록", tenantId, new[] { ("거래처명", ""), ("사업자번호", ""), ("연락처", "") });

    public byte[] CreateExcelItemCatalog(string tenantId)
        => CreateExcelWorkbook("품목목록", tenantId, new[] { ("품목코드", ""), ("품명", ""), ("단가", "0") });

    public byte[] CreateExcelQuotation(string tenantId)
        => CreateExcelWorkbook("견적서", tenantId, new[] { ("견적번호", "QT-001"), ("거래처", "(예시)"), ("합계", "0") });

    private static byte[] CreateExcelWorkbook(string title, string tenantId, (string Col, string Sample)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = title;
        ws.Cell(2, 1).Value = "tenant_id";
        ws.Cell(2, 2).Value = tenantId;
        var r = 4;
        foreach (var (col, sample) in rows)
        {
            ws.Cell(r, 1).Value = col;
            ws.Cell(r, 2).Value = sample;
            r++;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // --- QuestPDF: PDF 7종 ---
    public byte[] CreatePdfSalesOrder(string tenantId) => CreatePdfDocument("판매수주", tenantId);
    public byte[] CreatePdfPurchaseOrder(string tenantId) => CreatePdfDocument("매입발주", tenantId);
    public byte[] CreatePdfDelivery(string tenantId) => CreatePdfDocument("거래명세", tenantId);
    public byte[] CreatePdfStock(string tenantId) => CreatePdfDocument("재고현황", tenantId);
    public byte[] CreatePdfPartner(string tenantId) => CreatePdfDocument("거래처목록", tenantId);
    public byte[] CreatePdfItemCatalog(string tenantId) => CreatePdfDocument("품목목록", tenantId);
    public byte[] CreatePdfQuotation(string tenantId) => CreatePdfDocument("견적서", tenantId);

    private static byte[] CreatePdfDocument(string title, string tenantId)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Header().Text(title).SemiBold().FontSize(18);
                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text($"tenant_id: {tenantId}");
                    col.Item().Text("히트판 ERP 문서 출력 (준비 템플릿)");
                });
            });
        }).GeneratePdf();
    }
}
