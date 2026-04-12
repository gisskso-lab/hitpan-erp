using ClosedXML.Excel;
using HitPan.Application.DTOs.Document;

namespace HitPan.Application.Services;

public class ExcelExportService
{
    public byte[] GenerateDeliveryExcel(DocumentDto data)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("거래명세서");

        var titleRange = ws.Range("A1:I1").Merge();
        titleRange.Value = "거 래 명 세 서";
        titleRange.Style.Font.FontSize = 20;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;

        ws.Range("A2:D2").Merge().Value =
            $"공급자: {data.Tenant.CompanyName} ({data.Tenant.BizNo})";
        ws.Range("A3:D3").Merge().Value =
            $"대표: {data.Tenant.CeoName}  TEL: {data.Tenant.Tel}";
        ws.Range("A4:D4").Merge().Value =
            $"주소: {data.Tenant.Address}";

        ws.Range("E2:I2").Merge().Value =
            $"수신: {data.Partner.PartnerName} 귀하";
        ws.Range("E3:I3").Merge().Value =
            $"대표: {data.Partner.CeoName}  TEL: {data.Partner.Tel}";

        ws.Range("A5:I5").Merge().Value =
            $"문서번호: {data.Header.DocNo}   " +
            $"발행일: {data.Header.OrderDate:yyyy-MM-dd}   " +
            $"담당: {data.Header.EmployeeName}";

        string[] headers =
        {
            "No", "품명", "규격", "단위", "수량", "단가", "금액", "부가세", "비고"
        };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(7, i + 1);
            cell.Value = headers[i];
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Font.Bold = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Alignment.Horizontal =
                XLAlignmentHorizontalValues.Center;
        }

        var row = 8;
        for (var i = 0; i < 20; i++)
        {
            var item = i < data.Items.Count ? data.Items[i] : null;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = item?.ItemName ?? "";
            ws.Cell(row, 3).Value = item?.Spec ?? "";
            ws.Cell(row, 4).Value = item?.Unit ?? "";
            ws.Cell(row, 5).Value = item?.Qty ?? 0;
            ws.Cell(row, 6).Value = item?.UnitPrice ?? 0;
            ws.Cell(row, 7).Value = item?.Amount ?? 0;
            ws.Cell(row, 8).Value = item?.VatAmount ?? 0;
            ws.Cell(row, 9).Value = item?.Memo ?? "";
            ws.Range(row, 1, row, 9).Style
                .Border.OutsideBorder = XLBorderStyleValues.Thin;
            row++;
        }

        var sumRow = row + 1;
        ws.Cell(sumRow, 6).Value = "공급가액";
        ws.Cell(sumRow, 7).Value = data.Header.SupplyAmount;
        ws.Cell(sumRow + 1, 6).Value = "부가세액";
        ws.Cell(sumRow + 1, 7).Value = data.Header.VatAmount;
        ws.Cell(sumRow + 2, 6).Value = "합계금액";
        ws.Cell(sumRow + 2, 7).Value = data.Header.TotalAmount;
        ws.Cell(sumRow + 2, 7).Style.Font.Bold = true;
        ws.Cell(sumRow + 3, 6).Value = "현금";
        ws.Cell(sumRow + 3, 7).Value = data.Header.CashAmount;
        ws.Cell(sumRow + 4, 6).Value = "카드";
        ws.Cell(sumRow + 4, 7).Value = data.Header.CardAmount;

        ws.Cell("Z1").Value = "HITPAN_DOC";
        ws.Cell("Z2").Value = "delivery";
        ws.Cell("Z3").Value = data.Tenant.TenantId;
        ws.Cell("Z4").Value = data.Header.DocumentId;
        ws.Cell("Z5").Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Range("Z1:Z5").Style.Font.FontColor = XLColor.White;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateExcel(DocumentDto data, string docType)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("문서");

        var title = docType switch
        {
            "quotation" => "견 적 서",
            "po" => "발 주 서",
            "so" => "수 주 서",
            "po_receipt" => "매 입 명 세 서",
            "po_tax" => "매 입 계 산 서",
            _ => "거 래 문 서"
        };

        ws.Range("A1:I1").Merge().Value = title;
        ws.Range("A1:I1").Style.Font.FontSize = 20;
        ws.Range("A1:I1").Style.Font.Bold = true;
        ws.Range("A1:I1").Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;

        ws.Range("A2:D2").Merge().Value =
            $"공급자: {data.Tenant.CompanyName} ({data.Tenant.BizNo})";
        ws.Range("E2:I2").Merge().Value =
            $"수신: {data.Partner.PartnerName} 귀하";

        if (docType == "quotation" && data.Header.ValidUntil.HasValue)
        {
            ws.Cell("A6").Value =
                $"유효기간: {data.Header.ValidUntil.Value:yyyy-MM-dd}까지";
            ws.Cell("A6").Style.Font.FontColor = XLColor.Red;
        }

        string[] headers =
        {
            "No", "품명", "규격", "단위", "수량", "단가", "금액", "부가세", "비고"
        };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(7, i + 1);
            cell.Value = headers[i];
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Font.Bold = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var dataRow = 8;
        for (var i = 0; i < 20; i++)
        {
            var item = i < data.Items.Count ? data.Items[i] : null;
            ws.Cell(dataRow, 1).Value = i + 1;
            ws.Cell(dataRow, 2).Value = item?.ItemName ?? "";
            ws.Cell(dataRow, 3).Value = item?.Spec ?? "";
            ws.Cell(dataRow, 4).Value = item?.Unit ?? "";
            ws.Cell(dataRow, 5).Value = item?.Qty ?? 0;
            ws.Cell(dataRow, 6).Value = item?.UnitPrice ?? 0;
            ws.Cell(dataRow, 7).Value = item?.Amount ?? 0;
            ws.Cell(dataRow, 8).Value = item?.VatAmount ?? 0;
            ws.Cell(dataRow, 9).Value = item?.Memo ?? "";
            ws.Range(dataRow, 1, dataRow, 9).Style
                .Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRow++;
        }

        ws.Cell("Z1").Value = "HITPAN_DOC";
        ws.Cell("Z2").Value = docType;
        ws.Cell("Z3").Value = data.Tenant.TenantId;
        ws.Cell("Z4").Value = data.Header.DocumentId;
        ws.Cell("Z5").Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Range("Z1:Z5").Style.Font.FontColor = XLColor.White;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
