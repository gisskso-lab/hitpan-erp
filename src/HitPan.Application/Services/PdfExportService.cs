using HitPan.Application.DTOs.Document;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HitPan.Application.Services;

public class PdfExportService
{
    public byte[] GenerateDeliveryPdf(DocumentDto data)
        => GeneratePdf(data, "거 래 명 세 서");

    public byte[] GeneratePdf(DocumentDto data, string title)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x =>
                    x.FontFamily("Malgun Gothic").FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(title)
                        .FontSize(22).Bold().AlignCenter();
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(
                            $"공급자: {data.Tenant.CompanyName}");
                        row.RelativeItem().Text(
                                $"수신: {data.Partner.PartnerName} 귀하")
                            .AlignRight();
                    });
                    if (title == "견 적 서" &&
                        data.Header.ValidUntil.HasValue)
                    {
                        col.Item().Text(
                                $"유효기간: " +
                                $"{data.Header.ValidUntil.Value:yyyy-MM-dd}까지")
                            .FontColor(Colors.Red.Medium).AlignRight();
                    }
                });

                page.Content().PaddingVertical(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(25);
                        columns.RelativeColumn(3);
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        foreach (var h in new[]
                                 {
                                     "No", "품명", "규격", "단위",
                                     "수량", "단가", "금액", "부가세", "비고"
                                 })
                        {
                            header.Cell().Element(CellStyle)
                                .Text(h).Bold();
                        }
                    });

                    for (var i = 0; i < data.Items.Count; i++)
                    {
                        var item = data.Items[i];
                        table.Cell().Element(CellStyle)
                            .Text($"{i + 1}");
                        table.Cell().Element(CellStyle)
                            .Text(item.ItemName);
                        table.Cell().Element(CellStyle)
                            .Text(item.Spec);
                        table.Cell().Element(CellStyle)
                            .Text(item.Unit);
                        table.Cell().Element(CellStyle)
                            .AlignRight().Text($"{item.Qty:N2}");
                        table.Cell().Element(CellStyle)
                            .AlignRight().Text($"{item.UnitPrice:N0}");
                        table.Cell().Element(CellStyle)
                            .AlignRight().Text($"{item.Amount:N0}");
                        table.Cell().Element(CellStyle)
                            .AlignRight().Text($"{item.VatAmount:N0}");
                        table.Cell().Element(CellStyle)
                            .Text(item.Memo);
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(
                            $"공급가액: {data.Header.SupplyAmount:N0}");
                        col.Item().Text(
                            $"부가세액: {data.Header.VatAmount:N0}");
                        col.Item().Text(
                                $"합계금액: {data.Header.TotalAmount:N0}")
                            .Bold();
                    });
                });
            });
        });
        return document.GeneratePdf();
    }

    private static IContainer CellStyle(IContainer container)
        => container.Border(1).Padding(4).AlignCenter();
}
