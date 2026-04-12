using ClosedXML.Excel;

namespace HitPan.Application.Services;

/// <summary>엑셀 역수입 파서 (7종).</summary>
public sealed class ExcelImportService
{
    public IReadOnlyList<IReadOnlyDictionary<string, string>> ParseSalesOrderImport(Stream stream)
        => ParseFirstSheet(stream, "판매수주");

    public IReadOnlyList<IReadOnlyDictionary<string, string>> ParsePurchaseOrderImport(Stream stream)
        => ParseFirstSheet(stream, "매입발주");

    public IReadOnlyList<IReadOnlyDictionary<string, string>> ParseDeliveryImport(Stream stream)
        => ParseFirstSheet(stream, "거래명세");

    public IReadOnlyList<IReadOnlyDictionary<string, string>> ParseStockImport(Stream stream)
        => ParseFirstSheet(stream, "재고");

    public IReadOnlyList<IReadOnlyDictionary<string, string>> ParsePartnerImport(Stream stream)
        => ParseFirstSheet(stream, "거래처");

    public IReadOnlyList<IReadOnlyDictionary<string, string>> ParseItemCatalogImport(Stream stream)
        => ParseFirstSheet(stream, "품목");

    public IReadOnlyList<IReadOnlyDictionary<string, string>> ParseQuotationImport(Stream stream)
        => ParseFirstSheet(stream, "견적");

    /// <summary>첫 시트에서 1행을 헤더로 간주하고 2행부터 키-값 행으로 파싱합니다.</summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ParseFirstSheet(Stream stream, string contextHint)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var range = ws.RangeUsed();
        if (range is null)
        {
            return Array.Empty<IReadOnlyDictionary<string, string>>();
        }

        var firstRow = range.FirstRow().RowNumber();
        var lastRow = range.LastRow().RowNumber();
        var firstCol = range.FirstColumn().ColumnNumber();
        var lastCol = range.LastColumn().ColumnNumber();

        var headers = new List<string>();
        for (var c = firstCol; c <= lastCol; c++)
        {
            var h = ws.Cell(firstRow, c).GetString().Trim();
            headers.Add(string.IsNullOrEmpty(h) ? $"col_{c}" : h);
        }

        var rows = new List<IReadOnlyDictionary<string, string>>();
        for (var r = firstRow + 1; r <= lastRow; r++)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var empty = true;
            for (var i = 0; i < headers.Count; i++)
            {
                var col = firstCol + i;
                var v = ws.Cell(r, col).GetString().Trim();
                if (!string.IsNullOrEmpty(v))
                {
                    empty = false;
                }

                dict[headers[i]] = v;
            }

            if (!empty)
            {
                dict["_context"] = contextHint;
                rows.Add(dict);
            }
        }

        return rows;
    }
}
