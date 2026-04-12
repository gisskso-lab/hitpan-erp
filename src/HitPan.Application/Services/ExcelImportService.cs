using ClosedXML.Excel;
using HitPan.Application.DTOs.Document;

namespace HitPan.Application.Services;

public class ExcelImportService
{
    public ImportPreviewDto ParseAnyExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet(1);

        if (ws.Cell("Z1").GetString() != "HITPAN_DOC")
        {
            return new ImportPreviewDto
            {
                IsValid = false,
                ErrorMessage = "히트판 전용 양식이 아닙니다."
            };
        }

        var dto = new ImportPreviewDto
        {
            DocumentType = ws.Cell("Z2").GetString(),
            TenantId = ws.Cell("Z3").GetString(),
            DocumentId = ws.Cell("Z4").GetString(),
            IsValid = true
        };

        for (var row = 8; row < 28; row++)
        {
            var itemName = ws.Cell(row, 2).GetString();
            if (string.IsNullOrWhiteSpace(itemName))
            {
                continue;
            }

            dto.ParsedItems.Add(new DocumentItem
            {
                ItemName = itemName,
                Spec = ws.Cell(row, 3).GetString(),
                Unit = ws.Cell(row, 4).GetString(),
                Qty = ws.Cell(row, 5).GetValue<decimal>(),
                UnitPrice = ws.Cell(row, 6).GetValue<decimal>(),
                Amount = ws.Cell(row, 7).GetValue<decimal>(),
                VatAmount = ws.Cell(row, 8).GetValue<decimal>(),
                Memo = ws.Cell(row, 9).GetString()
            });
        }

        return dto;
    }
}
