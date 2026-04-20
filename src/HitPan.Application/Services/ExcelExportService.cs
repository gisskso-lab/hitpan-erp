using ClosedXML.Excel;
using HitPan.Application.DTOs.Document;

namespace HitPan.Application.Services;

/// <summary>
/// 엑셀 문서 생성 서비스 — 거래명세서, 견적서, 세금계산서
/// PDF 디자인과 동일한 한국 비즈니스 문서 양식을 엑셀로 출력한다.
/// </summary>
public class ExcelExportService
{
    // ─── 공통 스타일 상수 ───
    private const string FontName = "맑은 고딕";
    private const double TitleFontSize = 18;
    private const double LabelFontSize = 9;
    private const double DataFontSize = 9;
    private const string NumberFormat = "#,##0";
    private static readonly XLColor LabelBg = XLColor.FromHtml("#F5F5F5");

    // ─── 공개 라우팅 메서드 (하위 호환) ───

    /// <summary>
    /// 문서 타입에 따라 적절한 엑셀 생성 메서드로 분기한다.
    /// </summary>
    public byte[] GenerateExcel(DocumentDto data, string docType)
    {
        return docType switch
        {
            "delivery" => GenerateDeliveryExcel(data),
            "quotation" => GenerateQuotationExcel(data),
            "tax-invoice" => GenerateTaxInvoiceExcel(data),
            _ => GenerateDeliveryExcel(data)
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  거래명세서
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 거래명세서 엑셀을 생성한다.
    /// </summary>
    public byte[] GenerateDeliveryExcel(DocumentDto data)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("거래명세서");

        // ── 열 너비 설정 ──
        SetDeliveryColumnWidths(ws);

        // ── Row 1: 제목 ──
        var titleRange = ws.Range("A1:I1").Merge();
        titleRange.Value = "거 래 명 세 서";
        ApplyTitleStyle(titleRange);

        // ── Row 2: 문서번호 ──
        ws.Range("F2:I2").Merge().Value = $"문서번호: {data.Header.DocNo}";
        ws.Range("F2:I2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range("F2:I2").Style.Font.FontName = FontName;
        ws.Range("F2:I2").Style.Font.FontSize = DataFontSize;

        // ── Row 3~8: 공급자 / 공급받는자 정보 박스 ──
        BuildDeliveryCompanyInfo(ws, data, 3);

        // ── Row 9: 작성일 + 담당자 ──
        ws.Range("A9:D9").Merge().Value = $"작성일: {data.Header.OrderDate:yyyy-MM-dd}";
        ws.Range("A9:D9").Style.Font.FontName = FontName;
        ws.Range("A9:D9").Style.Font.FontSize = DataFontSize;
        ws.Range("F9:I9").Merge().Value = $"담당자: {data.Header.EmployeeName}";
        ws.Range("F9:I9").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range("F9:I9").Style.Font.FontName = FontName;
        ws.Range("F9:I9").Style.Font.FontSize = DataFontSize;

        // ── Row 10: 빈 줄 ──

        // ── Row 11: 테이블 헤더 ──
        string[] headers = { "No", "품명", "규격", "단위", "수량", "단가", "금액", "부가세", "비고" };
        BuildTableHeader(ws, 11, headers);

        // ── Row 12+: 데이터 행 (최소 20행) ──
        var dataRowCount = Math.Max(data.Items.Count, 20);
        var currentRow = 12;
        for (var i = 0; i < dataRowCount; i++)
        {
            var item = i < data.Items.Count ? data.Items[i] : null;
            ws.Cell(currentRow, 1).Value = i + 1;
            ws.Cell(currentRow, 2).Value = item?.ItemName ?? "";
            ws.Cell(currentRow, 3).Value = item?.Spec ?? "";
            ws.Cell(currentRow, 4).Value = item?.Unit ?? "";
            SetDecimalCell(ws.Cell(currentRow, 5), item?.Qty ?? 0);
            SetAmountCell(ws.Cell(currentRow, 6), item?.UnitPrice ?? 0);
            SetAmountCell(ws.Cell(currentRow, 7), item?.Amount ?? 0);
            SetAmountCell(ws.Cell(currentRow, 8), item?.VatAmount ?? 0);
            ws.Cell(currentRow, 9).Value = item?.Memo ?? "";

            // 행 스타일
            var rowRange = ws.Range(currentRow, 1, currentRow, 9);
            ApplyDataRowStyle(rowRange);
            // No 열 가운데 정렬
            ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            // 단위 열 가운데 정렬
            ws.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            currentRow++;
        }

        // ── 합계 행 ──
        var totalRow = currentRow;
        ws.Range(totalRow, 1, totalRow, 6).Merge().Value = "합   계";
        ws.Range(totalRow, 1, totalRow, 6).Style.Font.Bold = true;
        ws.Range(totalRow, 1, totalRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        SetAmountCell(ws.Cell(totalRow, 7), data.Header.SupplyAmount);
        ws.Cell(totalRow, 7).Style.Font.Bold = true;
        SetAmountCell(ws.Cell(totalRow, 8), data.Header.VatAmount);
        ws.Cell(totalRow, 8).Style.Font.Bold = true;
        ws.Cell(totalRow, 9).Value = "";
        ApplyDataRowStyle(ws.Range(totalRow, 1, totalRow, 9));
        // 합계 행 외곽 두꺼운 테두리
        ws.Range(totalRow, 1, totalRow, 9).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        // ── 요약 박스: 공급가액 / 부가세 / 합계 ──
        currentRow = totalRow + 2;
        BuildSummaryBox(ws, currentRow, data, showCashCard: true);

        // ── 푸터: 청구 문구 ──
        currentRow += 6;
        ws.Range(currentRow, 1, currentRow, 9).Merge().Value = "위 금액을 청구합니다.";
        ws.Range(currentRow, 1, currentRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(currentRow, 1, currentRow, 9).Style.Font.FontName = FontName;
        ws.Range(currentRow, 1, currentRow, 9).Style.Font.FontSize = 11;
        ws.Range(currentRow, 1, currentRow, 9).Style.Font.Bold = true;

        // ── 히든 메타데이터 (Z열) ──
        WriteMetadata(ws, "delivery", data);

        // ── 인쇄 설정 ──
        SetPrintArea(ws, currentRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════
    //  견적서
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 견적서 엑셀을 생성한다.
    /// </summary>
    public byte[] GenerateQuotationExcel(DocumentDto data)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("견적서");

        // ── 열 너비 설정 (할인% 열 추가로 J열까지) ──
        SetQuotationColumnWidths(ws);

        // ── Row 1: 제목 ──
        var titleRange = ws.Range("A1:J1").Merge();
        titleRange.Value = "견 적 서";
        ApplyTitleStyle(titleRange);

        // ── Row 2: 문서번호 ──
        ws.Range("G2:J2").Merge().Value = $"문서번호: {data.Header.DocNo}";
        ws.Range("G2:J2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range("G2:J2").Style.Font.FontName = FontName;
        ws.Range("G2:J2").Style.Font.FontSize = DataFontSize;

        // ── Row 3~8: 공급자 / 수신 정보 박스 ──
        BuildQuotationCompanyInfo(ws, data, 3);

        // ── Row 9: 유효기한 ──
        if (data.Header.ValidUntil.HasValue)
        {
            ws.Range("A9:E9").Merge().Value = $"유효기한: {data.Header.ValidUntil.Value:yyyy-MM-dd}까지";
            ws.Range("A9:E9").Style.Font.FontName = FontName;
            ws.Range("A9:E9").Style.Font.FontSize = DataFontSize;
            ws.Range("A9:E9").Style.Font.FontColor = XLColor.Red;
        }
        ws.Range("F9:J9").Merge().Value = $"담당자: {data.Header.EmployeeName}";
        ws.Range("F9:J9").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range("F9:J9").Style.Font.FontName = FontName;
        ws.Range("F9:J9").Style.Font.FontSize = DataFontSize;

        // ── Row 10: 빈 줄 ──

        // ── Row 11: 테이블 헤더 (할인% 추가) ──
        string[] headers = { "No", "품명", "규격", "단위", "수량", "단가", "할인(%)", "금액", "부가세", "비고" };
        BuildTableHeader(ws, 11, headers, colCount: 10);

        // ── Row 12+: 데이터 행 ──
        var dataRowCount = Math.Max(data.Items.Count, 20);
        var currentRow = 12;
        for (var i = 0; i < dataRowCount; i++)
        {
            var item = i < data.Items.Count ? data.Items[i] : null;
            ws.Cell(currentRow, 1).Value = i + 1;
            ws.Cell(currentRow, 2).Value = item?.ItemName ?? "";
            ws.Cell(currentRow, 3).Value = item?.Spec ?? "";
            ws.Cell(currentRow, 4).Value = item?.Unit ?? "";
            SetDecimalCell(ws.Cell(currentRow, 5), item?.Qty ?? 0);
            SetAmountCell(ws.Cell(currentRow, 6), item?.UnitPrice ?? 0);
            // 할인율
            if (item != null && item.DiscountRate > 0)
            {
                ws.Cell(currentRow, 7).Value = (double)item.DiscountRate;
                ws.Cell(currentRow, 7).Style.NumberFormat.Format = "0.#";
            }
            else
            {
                ws.Cell(currentRow, 7).Value = "";
            }
            ws.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            SetAmountCell(ws.Cell(currentRow, 8), item?.Amount ?? 0);
            SetAmountCell(ws.Cell(currentRow, 9), item?.VatAmount ?? 0);
            ws.Cell(currentRow, 10).Value = item?.Memo ?? "";

            var rowRange = ws.Range(currentRow, 1, currentRow, 10);
            ApplyDataRowStyle(rowRange);
            ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            currentRow++;
        }

        // ── 합계 행 ──
        var totalRow = currentRow;
        ws.Range(totalRow, 1, totalRow, 7).Merge().Value = "합   계";
        ws.Range(totalRow, 1, totalRow, 7).Style.Font.Bold = true;
        ws.Range(totalRow, 1, totalRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        SetAmountCell(ws.Cell(totalRow, 8), data.Header.SupplyAmount);
        ws.Cell(totalRow, 8).Style.Font.Bold = true;
        SetAmountCell(ws.Cell(totalRow, 9), data.Header.VatAmount);
        ws.Cell(totalRow, 9).Style.Font.Bold = true;
        ws.Cell(totalRow, 10).Value = "";
        ApplyDataRowStyle(ws.Range(totalRow, 1, totalRow, 10));
        ws.Range(totalRow, 1, totalRow, 10).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        // ── 요약 박스 ──
        currentRow = totalRow + 2;
        BuildSummaryBox(ws, currentRow, data, showCashCard: false, lastCol: 10);

        // ── 비고 사항 (유효기간, 납기, 결제조건) ──
        currentRow += 5;
        ws.Range(currentRow, 1, currentRow, 2).Merge().Value = "비고사항";
        ws.Range(currentRow, 1, currentRow, 2).Style.Font.Bold = true;
        ws.Range(currentRow, 1, currentRow, 2).Style.Font.FontName = FontName;
        ws.Range(currentRow, 1, currentRow, 2).Style.Font.FontSize = DataFontSize;
        ws.Range(currentRow, 1, currentRow, 2).Style.Fill.BackgroundColor = LabelBg;
        ApplyBorder(ws.Range(currentRow, 1, currentRow, 2));

        ws.Range(currentRow, 3, currentRow, 10).Merge().Value =
            $"유효기간: {(data.Header.ValidUntil.HasValue ? data.Header.ValidUntil.Value.ToString("yyyy-MM-dd") : "-")}  |  납기: 협의  |  결제조건: 협의";
        ws.Range(currentRow, 3, currentRow, 10).Style.Font.FontName = FontName;
        ws.Range(currentRow, 3, currentRow, 10).Style.Font.FontSize = DataFontSize;
        ApplyBorder(ws.Range(currentRow, 3, currentRow, 10));

        // ── 푸터 ──
        currentRow += 2;
        ws.Range(currentRow, 1, currentRow, 10).Merge().Value = "위 금액을 견적합니다.";
        ws.Range(currentRow, 1, currentRow, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(currentRow, 1, currentRow, 10).Style.Font.FontName = FontName;
        ws.Range(currentRow, 1, currentRow, 10).Style.Font.FontSize = 11;
        ws.Range(currentRow, 1, currentRow, 10).Style.Font.Bold = true;

        // ── 히든 메타데이터 ──
        WriteMetadata(ws, "quotation", data);

        // ── 인쇄 설정 ──
        SetPrintArea(ws, currentRow, lastCol: "J");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════
    //  세금계산서
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 세금계산서 엑셀을 생성한다.
    /// </summary>
    public byte[] GenerateTaxInvoiceExcel(DocumentDto data)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("세금계산서");

        // ── 열 너비 설정 ──
        SetTaxInvoiceColumnWidths(ws);

        // ── Row 1: 제목 ──
        var titleRange = ws.Range("A1:I1").Merge();
        titleRange.Value = "세 금 계 산 서 [전자]";
        ApplyTitleStyle(titleRange);

        // ── Row 2: 승인번호 ──
        ws.Range("A2:I2").Merge().Value = $"승인번호: {data.Header.DocNo}";
        ws.Range("A2:I2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A2:I2").Style.Font.FontName = FontName;
        ws.Range("A2:I2").Style.Font.FontSize = DataFontSize;

        // ── Row 3: 합계금액 표시 ──
        SetLabelCell(ws.Cell(3, 1), "합계금액");
        ws.Range(3, 2, 3, 9).Merge();
        SetAmountCell(ws.Cell(3, 2), data.Header.TotalAmount);
        ws.Cell(3, 2).Style.Font.Bold = true;
        ws.Cell(3, 2).Style.Font.FontSize = 12;
        ApplyBorder(ws.Range(3, 1, 3, 9));

        // ── Row 4~9: 공급자 / 공급받는자 정보 (세로 라벨) ──
        BuildTaxInvoiceCompanyInfo(ws, data, 4);

        // ── Row 10: 작성일자 ──
        ws.Range("A10:D10").Merge().Value = $"작성일자: {data.Header.OrderDate:yyyy-MM-dd}";
        ws.Range("A10:D10").Style.Font.FontName = FontName;
        ws.Range("A10:D10").Style.Font.FontSize = DataFontSize;
        ws.Range("E10:I10").Merge().Value = $"담당자: {data.Header.EmployeeName}";
        ws.Range("E10:I10").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range("E10:I10").Style.Font.FontName = FontName;
        ws.Range("E10:I10").Style.Font.FontSize = DataFontSize;

        // ── Row 11: 빈 줄 ──

        // ── Row 12: 테이블 헤더 ──
        string[] headers = { "월", "일", "품목", "규격", "수량", "단가", "공급가액", "세액", "비고" };
        BuildTableHeader(ws, 12, headers);

        // ── Row 13+: 데이터 행 ──
        var dataRowCount = Math.Max(data.Items.Count, 20);
        var currentRow = 13;
        for (var i = 0; i < dataRowCount; i++)
        {
            var item = i < data.Items.Count ? data.Items[i] : null;
            // 월/일은 문서 작성일 기준
            if (item != null)
            {
                ws.Cell(currentRow, 1).Value = data.Header.OrderDate.Month;
                ws.Cell(currentRow, 2).Value = data.Header.OrderDate.Day;
            }
            else
            {
                ws.Cell(currentRow, 1).Value = "";
                ws.Cell(currentRow, 2).Value = "";
            }
            ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 3).Value = item?.ItemName ?? "";
            ws.Cell(currentRow, 4).Value = item?.Spec ?? "";
            SetDecimalCell(ws.Cell(currentRow, 5), item?.Qty ?? 0);
            SetAmountCell(ws.Cell(currentRow, 6), item?.UnitPrice ?? 0);
            SetAmountCell(ws.Cell(currentRow, 7), item?.Amount ?? 0);
            SetAmountCell(ws.Cell(currentRow, 8), item?.VatAmount ?? 0);
            ws.Cell(currentRow, 9).Value = item?.Memo ?? "";

            var rowRange = ws.Range(currentRow, 1, currentRow, 9);
            ApplyDataRowStyle(rowRange);
            currentRow++;
        }

        // ── 합계 행 ──
        var totalRow = currentRow;
        ws.Range(totalRow, 1, totalRow, 6).Merge().Value = "합   계";
        ws.Range(totalRow, 1, totalRow, 6).Style.Font.Bold = true;
        ws.Range(totalRow, 1, totalRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        SetAmountCell(ws.Cell(totalRow, 7), data.Header.SupplyAmount);
        ws.Cell(totalRow, 7).Style.Font.Bold = true;
        SetAmountCell(ws.Cell(totalRow, 8), data.Header.VatAmount);
        ws.Cell(totalRow, 8).Style.Font.Bold = true;
        ws.Cell(totalRow, 9).Value = "";
        ApplyDataRowStyle(ws.Range(totalRow, 1, totalRow, 9));
        ws.Range(totalRow, 1, totalRow, 9).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        // ── 세금계산서 요약: 합계금액 / 공급가액 / 세액 ──
        currentRow = totalRow + 2;
        // 합계금액
        SetLabelCell(ws.Cell(currentRow, 5), "합계금액");
        ws.Range(currentRow, 6, currentRow, 9).Merge();
        SetAmountCell(ws.Cell(currentRow, 6), data.Header.TotalAmount);
        ws.Cell(currentRow, 6).Style.Font.Bold = true;
        ApplyBorder(ws.Range(currentRow, 5, currentRow, 9));
        // 공급가액
        SetLabelCell(ws.Cell(currentRow + 1, 5), "공급가액");
        ws.Range(currentRow + 1, 6, currentRow + 1, 9).Merge();
        SetAmountCell(ws.Cell(currentRow + 1, 6), data.Header.SupplyAmount);
        ApplyBorder(ws.Range(currentRow + 1, 5, currentRow + 1, 9));
        // 세액
        SetLabelCell(ws.Cell(currentRow + 2, 5), "세    액");
        ws.Range(currentRow + 2, 6, currentRow + 2, 9).Merge();
        SetAmountCell(ws.Cell(currentRow + 2, 6), data.Header.VatAmount);
        ApplyBorder(ws.Range(currentRow + 2, 5, currentRow + 2, 9));

        // 외곽 Medium 테두리
        ws.Range(currentRow, 5, currentRow + 2, 9).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        // ── 히든 메타데이터 ──
        WriteMetadata(ws, "tax-invoice", data);

        // ── 인쇄 설정 ──
        SetPrintArea(ws, currentRow + 2);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════
    //  공통 헬퍼 메서드
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 제목 셀 스타일을 적용한다. (18pt 볼드 가운데 정렬, 맑은 고딕)
    /// </summary>
    private static void ApplyTitleStyle(IXLRange range)
    {
        range.Style.Font.FontSize = TitleFontSize;
        range.Style.Font.Bold = true;
        range.Style.Font.FontName = FontName;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    /// <summary>
    /// 라벨 셀을 설정한다. (회색 배경, 9pt, 가운데 정렬)
    /// </summary>
    private static void SetLabelCell(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Fill.BackgroundColor = LabelBg;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = LabelFontSize;
        cell.Style.Font.Bold = true;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    /// <summary>
    /// 데이터 셀의 기본 스타일을 설정한다.
    /// </summary>
    private static void SetDataCell(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = DataFontSize;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    /// <summary>
    /// 금액 셀을 설정한다. (우측 정렬, #,##0 포맷)
    /// </summary>
    private static void SetAmountCell(IXLCell cell, decimal amount)
    {
        cell.Value = (double)amount;
        cell.Style.NumberFormat.Format = NumberFormat;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = DataFontSize;
    }

    /// <summary>
    /// 수량 등 소수점이 가능한 숫자 셀을 설정한다.
    /// </summary>
    private static void SetDecimalCell(IXLCell cell, decimal value)
    {
        cell.Value = (double)value;
        cell.Style.NumberFormat.Format = "#,##0.##";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = DataFontSize;
    }

    /// <summary>
    /// 테이블 헤더 행을 구성한다. (회색 배경, 볼드, 가운데 정렬, 테두리)
    /// </summary>
    private static void BuildTableHeader(IXLWorksheet ws, int row, string[] headers, int colCount = 0)
    {
        var cols = colCount > 0 ? colCount : headers.Length;
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Fill.BackgroundColor = LabelBg;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontSize = LabelFontSize;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        // 헤더 행 외곽 Medium 테두리
        ws.Range(row, 1, row, cols).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
    }

    /// <summary>
    /// 데이터 행에 기본 스타일을 적용한다. (Thin 테두리, 맑은 고딕 9pt)
    /// </summary>
    private static void ApplyDataRowStyle(IXLRange range)
    {
        range.Style.Font.FontName = FontName;
        range.Style.Font.FontSize = DataFontSize;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    /// <summary>
    /// 셀 범위에 Thin 테두리를 적용한다.
    /// </summary>
    private static void ApplyBorder(IXLRange range)
    {
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    // ─── 거래명세서 회사 정보 박스 ───

    /// <summary>
    /// 거래명세서 공급자/공급받는자 정보 영역을 구성한다. (Row 3~8)
    /// </summary>
    private static void BuildDeliveryCompanyInfo(IXLWorksheet ws, DocumentDto data, int startRow)
    {
        // 공급자 (A~D열)
        SetLabelCell(ws.Cell(startRow, 1), "공급자");
        ws.Range(startRow, 1, startRow, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        // 등록번호
        SetLabelCell(ws.Cell(startRow, 2), "등록번호");
        ws.Range(startRow, 3, startRow, 4).Merge();
        SetDataCell(ws.Cell(startRow, 3), data.Tenant.BizNo);

        // 상호
        SetLabelCell(ws.Cell(startRow + 1, 2), "상  호");
        SetDataCell(ws.Cell(startRow + 1, 3), data.Tenant.CompanyName);
        SetLabelCell(ws.Cell(startRow + 1, 4), "대표자");

        // 대표자 (다음 행에 값)
        SetLabelCell(ws.Cell(startRow + 2, 2), "대표자");
        ws.Range(startRow + 2, 3, startRow + 2, 4).Merge();
        SetDataCell(ws.Cell(startRow + 2, 3), data.Tenant.CeoName);

        // 주소
        SetLabelCell(ws.Cell(startRow + 3, 2), "주  소");
        ws.Range(startRow + 3, 3, startRow + 3, 4).Merge();
        SetDataCell(ws.Cell(startRow + 3, 3), data.Tenant.Address);

        // 업태
        SetLabelCell(ws.Cell(startRow + 4, 2), "업  태");
        ws.Range(startRow + 4, 3, startRow + 4, 4).Merge();
        SetDataCell(ws.Cell(startRow + 4, 3), data.Tenant.BizType);

        // 종목
        SetLabelCell(ws.Cell(startRow + 5, 2), "종  목");
        ws.Range(startRow + 5, 3, startRow + 5, 4).Merge();
        SetDataCell(ws.Cell(startRow + 5, 3), data.Tenant.BizItem);

        // 공급받는자 (E~I열)
        SetLabelCell(ws.Cell(startRow, 5), "공급받는자");
        ws.Range(startRow, 5, startRow, 5).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        SetLabelCell(ws.Cell(startRow, 6), "등록번호");
        ws.Range(startRow, 7, startRow, 9).Merge();
        SetDataCell(ws.Cell(startRow, 7), data.Partner.BizNo);

        SetLabelCell(ws.Cell(startRow + 1, 6), "상  호");
        SetDataCell(ws.Cell(startRow + 1, 7), data.Partner.PartnerName);
        SetLabelCell(ws.Cell(startRow + 1, 8), "대표자");

        SetLabelCell(ws.Cell(startRow + 2, 6), "대표자");
        ws.Range(startRow + 2, 7, startRow + 2, 9).Merge();
        SetDataCell(ws.Cell(startRow + 2, 7), data.Partner.CeoName);

        SetLabelCell(ws.Cell(startRow + 3, 6), "주  소");
        ws.Range(startRow + 3, 7, startRow + 3, 9).Merge();
        SetDataCell(ws.Cell(startRow + 3, 7), data.Partner.Address);

        SetLabelCell(ws.Cell(startRow + 4, 6), "업  태");
        ws.Range(startRow + 4, 7, startRow + 4, 9).Merge();
        SetDataCell(ws.Cell(startRow + 4, 7), data.Partner.BizType);

        SetLabelCell(ws.Cell(startRow + 5, 6), "종  목");
        ws.Range(startRow + 5, 7, startRow + 5, 9).Merge();
        SetDataCell(ws.Cell(startRow + 5, 7), data.Partner.BizItem);

        // 전체 공급자 영역 외곽 Medium 테두리
        ws.Range(startRow, 1, startRow + 5, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        // 전체 공급받는자 영역 외곽 Medium 테두리
        ws.Range(startRow, 5, startRow + 5, 9).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
    }

    // ─── 견적서 회사 정보 박스 ───

    /// <summary>
    /// 견적서 공급자/수신 정보 영역을 구성한다.
    /// </summary>
    private static void BuildQuotationCompanyInfo(IXLWorksheet ws, DocumentDto data, int startRow)
    {
        // 공급자 (A~E열)
        SetLabelCell(ws.Cell(startRow, 1), "공급자");

        SetLabelCell(ws.Cell(startRow, 2), "등록번호");
        ws.Range(startRow, 3, startRow, 5).Merge();
        SetDataCell(ws.Cell(startRow, 3), data.Tenant.BizNo);

        SetLabelCell(ws.Cell(startRow + 1, 2), "상  호");
        ws.Range(startRow + 1, 3, startRow + 1, 5).Merge();
        SetDataCell(ws.Cell(startRow + 1, 3), data.Tenant.CompanyName);

        SetLabelCell(ws.Cell(startRow + 2, 2), "대표자");
        ws.Range(startRow + 2, 3, startRow + 2, 5).Merge();
        SetDataCell(ws.Cell(startRow + 2, 3), data.Tenant.CeoName);

        SetLabelCell(ws.Cell(startRow + 3, 2), "주  소");
        ws.Range(startRow + 3, 3, startRow + 3, 5).Merge();
        SetDataCell(ws.Cell(startRow + 3, 3), data.Tenant.Address);

        SetLabelCell(ws.Cell(startRow + 4, 2), "업  태");
        ws.Range(startRow + 4, 3, startRow + 4, 5).Merge();
        SetDataCell(ws.Cell(startRow + 4, 3), data.Tenant.BizType);

        SetLabelCell(ws.Cell(startRow + 5, 2), "종  목");
        ws.Range(startRow + 5, 3, startRow + 5, 5).Merge();
        SetDataCell(ws.Cell(startRow + 5, 3), data.Tenant.BizItem);

        // 수신 (F~J열)
        SetLabelCell(ws.Cell(startRow, 6), "수  신");

        SetLabelCell(ws.Cell(startRow, 7), "등록번호");
        ws.Range(startRow, 8, startRow, 10).Merge();
        SetDataCell(ws.Cell(startRow, 8), data.Partner.BizNo);

        SetLabelCell(ws.Cell(startRow + 1, 7), "상  호");
        ws.Range(startRow + 1, 8, startRow + 1, 10).Merge();
        SetDataCell(ws.Cell(startRow + 1, 8), data.Partner.PartnerName);

        SetLabelCell(ws.Cell(startRow + 2, 7), "대표자");
        ws.Range(startRow + 2, 8, startRow + 2, 10).Merge();
        SetDataCell(ws.Cell(startRow + 2, 8), data.Partner.CeoName);

        SetLabelCell(ws.Cell(startRow + 3, 7), "주  소");
        ws.Range(startRow + 3, 8, startRow + 3, 10).Merge();
        SetDataCell(ws.Cell(startRow + 3, 8), data.Partner.Address);

        SetLabelCell(ws.Cell(startRow + 4, 7), "업  태");
        ws.Range(startRow + 4, 8, startRow + 4, 10).Merge();
        SetDataCell(ws.Cell(startRow + 4, 8), data.Partner.BizType);

        SetLabelCell(ws.Cell(startRow + 5, 7), "종  목");
        ws.Range(startRow + 5, 8, startRow + 5, 10).Merge();
        SetDataCell(ws.Cell(startRow + 5, 8), data.Partner.BizItem);

        // 외곽 Medium 테두리
        ws.Range(startRow, 1, startRow + 5, 5).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        ws.Range(startRow, 6, startRow + 5, 10).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
    }

    // ─── 세금계산서 회사 정보 박스 (세로 라벨) ───

    /// <summary>
    /// 세금계산서 공급자/공급받는자 정보를 세로 라벨 형식으로 구성한다.
    /// </summary>
    private static void BuildTaxInvoiceCompanyInfo(IXLWorksheet ws, DocumentDto data, int startRow)
    {
        // 공급자 (A열 세로 병합 라벨)
        ws.Range(startRow, 1, startRow + 5, 1).Merge();
        var supplierLabel = ws.Cell(startRow, 1);
        supplierLabel.Value = "공\n급\n자";
        supplierLabel.Style.Fill.BackgroundColor = LabelBg;
        supplierLabel.Style.Font.FontName = FontName;
        supplierLabel.Style.Font.FontSize = LabelFontSize;
        supplierLabel.Style.Font.Bold = true;
        supplierLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        supplierLabel.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        supplierLabel.Style.Alignment.WrapText = true;

        // 공급자 상세
        SetLabelCell(ws.Cell(startRow, 2), "등록번호");
        ws.Range(startRow, 3, startRow, 4).Merge();
        SetDataCell(ws.Cell(startRow, 3), data.Tenant.BizNo);

        SetLabelCell(ws.Cell(startRow + 1, 2), "상  호");
        SetDataCell(ws.Cell(startRow + 1, 3), data.Tenant.CompanyName);
        SetLabelCell(ws.Cell(startRow + 1, 4), "대표자");

        SetLabelCell(ws.Cell(startRow + 2, 2), "대표자");
        ws.Range(startRow + 2, 3, startRow + 2, 4).Merge();
        SetDataCell(ws.Cell(startRow + 2, 3), data.Tenant.CeoName);

        SetLabelCell(ws.Cell(startRow + 3, 2), "주  소");
        ws.Range(startRow + 3, 3, startRow + 3, 4).Merge();
        SetDataCell(ws.Cell(startRow + 3, 3), data.Tenant.Address);

        SetLabelCell(ws.Cell(startRow + 4, 2), "업  태");
        ws.Range(startRow + 4, 3, startRow + 4, 4).Merge();
        SetDataCell(ws.Cell(startRow + 4, 3), data.Tenant.BizType);

        SetLabelCell(ws.Cell(startRow + 5, 2), "종  목");
        ws.Range(startRow + 5, 3, startRow + 5, 4).Merge();
        SetDataCell(ws.Cell(startRow + 5, 3), data.Tenant.BizItem);

        // 공급받는자 (E열 세로 병합 라벨)
        ws.Range(startRow, 5, startRow + 5, 5).Merge();
        var receiverLabel = ws.Cell(startRow, 5);
        receiverLabel.Value = "공\n급\n받\n는\n자";
        receiverLabel.Style.Fill.BackgroundColor = LabelBg;
        receiverLabel.Style.Font.FontName = FontName;
        receiverLabel.Style.Font.FontSize = LabelFontSize;
        receiverLabel.Style.Font.Bold = true;
        receiverLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        receiverLabel.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        receiverLabel.Style.Alignment.WrapText = true;

        // 공급받는자 상세
        SetLabelCell(ws.Cell(startRow, 6), "등록번호");
        ws.Range(startRow, 7, startRow, 9).Merge();
        SetDataCell(ws.Cell(startRow, 7), data.Partner.BizNo);

        SetLabelCell(ws.Cell(startRow + 1, 6), "상  호");
        SetDataCell(ws.Cell(startRow + 1, 7), data.Partner.PartnerName);
        SetLabelCell(ws.Cell(startRow + 1, 8), "대표자");

        SetLabelCell(ws.Cell(startRow + 2, 6), "대표자");
        ws.Range(startRow + 2, 7, startRow + 2, 9).Merge();
        SetDataCell(ws.Cell(startRow + 2, 7), data.Partner.CeoName);

        SetLabelCell(ws.Cell(startRow + 3, 6), "주  소");
        ws.Range(startRow + 3, 7, startRow + 3, 9).Merge();
        SetDataCell(ws.Cell(startRow + 3, 7), data.Partner.Address);

        SetLabelCell(ws.Cell(startRow + 4, 6), "업  태");
        ws.Range(startRow + 4, 7, startRow + 4, 9).Merge();
        SetDataCell(ws.Cell(startRow + 4, 7), data.Partner.BizType);

        SetLabelCell(ws.Cell(startRow + 5, 6), "종  목");
        ws.Range(startRow + 5, 7, startRow + 5, 9).Merge();
        SetDataCell(ws.Cell(startRow + 5, 7), data.Partner.BizItem);

        // 외곽 Medium 테두리
        ws.Range(startRow, 1, startRow + 5, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        ws.Range(startRow, 5, startRow + 5, 9).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
    }

    // ─── 요약 박스 (공급가액 / 부가세 / 합계 / 현금 / 카드) ───

    /// <summary>
    /// 공급가액, 부가세, 합계 요약 박스를 구성한다.
    /// </summary>
    private static void BuildSummaryBox(IXLWorksheet ws, int startRow, DocumentDto data,
        bool showCashCard = false, int lastCol = 9)
    {
        var labelCol = lastCol - 4;
        var valStartCol = labelCol + 1;

        // 공급가액
        SetLabelCell(ws.Cell(startRow, labelCol), "공급가액");
        ws.Range(startRow, valStartCol, startRow, lastCol).Merge();
        SetAmountCell(ws.Cell(startRow, valStartCol), data.Header.SupplyAmount);
        ApplyBorder(ws.Range(startRow, labelCol, startRow, lastCol));

        // 부가세액
        SetLabelCell(ws.Cell(startRow + 1, labelCol), "부가세액");
        ws.Range(startRow + 1, valStartCol, startRow + 1, lastCol).Merge();
        SetAmountCell(ws.Cell(startRow + 1, valStartCol), data.Header.VatAmount);
        ApplyBorder(ws.Range(startRow + 1, labelCol, startRow + 1, lastCol));

        // 합계금액
        SetLabelCell(ws.Cell(startRow + 2, labelCol), "합계금액");
        ws.Range(startRow + 2, valStartCol, startRow + 2, lastCol).Merge();
        SetAmountCell(ws.Cell(startRow + 2, valStartCol), data.Header.TotalAmount);
        ws.Cell(startRow + 2, valStartCol).Style.Font.Bold = true;
        ApplyBorder(ws.Range(startRow + 2, labelCol, startRow + 2, lastCol));

        if (showCashCard)
        {
            // 현금
            SetLabelCell(ws.Cell(startRow + 3, labelCol), "현    금");
            ws.Range(startRow + 3, valStartCol, startRow + 3, lastCol).Merge();
            SetAmountCell(ws.Cell(startRow + 3, valStartCol), data.Header.CashAmount);
            ApplyBorder(ws.Range(startRow + 3, labelCol, startRow + 3, lastCol));

            // 카드
            SetLabelCell(ws.Cell(startRow + 4, labelCol), "카    드");
            ws.Range(startRow + 4, valStartCol, startRow + 4, lastCol).Merge();
            SetAmountCell(ws.Cell(startRow + 4, valStartCol), data.Header.CardAmount);
            ApplyBorder(ws.Range(startRow + 4, labelCol, startRow + 4, lastCol));

            // 외곽 Medium 테두리
            ws.Range(startRow, labelCol, startRow + 4, lastCol).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        }
        else
        {
            ws.Range(startRow, labelCol, startRow + 2, lastCol).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        }
    }

    // ─── Z열 히든 메타데이터 ───

    /// <summary>
    /// Z열에 히든 메타데이터를 기록한다.
    /// </summary>
    private static void WriteMetadata(IXLWorksheet ws, string docType, DocumentDto data)
    {
        ws.Cell("Z1").Value = "HITPAN_DOC";
        ws.Cell("Z2").Value = docType;
        ws.Cell("Z3").Value = data.Tenant.TenantId;
        ws.Cell("Z4").Value = data.Header.DocumentId;
        ws.Cell("Z5").Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Range("Z1:Z5").Style.Font.FontColor = XLColor.White;
    }

    // ─── 인쇄 영역 설정 ───

    /// <summary>
    /// A4 용지에 맞는 인쇄 영역을 설정한다.
    /// </summary>
    private static void SetPrintArea(IXLWorksheet ws, int lastRow, string lastCol = "I")
    {
        ws.PageSetup.PrintAreas.Add($"A1:{lastCol}{lastRow}");
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 1);
        ws.PageSetup.Margins.SetLeft(0.5);
        ws.PageSetup.Margins.SetRight(0.5);
        ws.PageSetup.Margins.SetTop(0.5);
        ws.PageSetup.Margins.SetBottom(0.5);
    }

    // ─── 열 너비 설정 ───

    /// <summary>
    /// 거래명세서 열 너비를 설정한다. (A~I, 9열)
    /// </summary>
    private static void SetDeliveryColumnWidths(IXLWorksheet ws)
    {
        ws.Column(1).Width = 5;   // No
        ws.Column(2).Width = 22;  // 품명
        ws.Column(3).Width = 12;  // 규격
        ws.Column(4).Width = 7;   // 단위
        ws.Column(5).Width = 10;  // 수량
        ws.Column(6).Width = 12;  // 단가
        ws.Column(7).Width = 14;  // 금액
        ws.Column(8).Width = 12;  // 부가세
        ws.Column(9).Width = 12;  // 비고
    }

    /// <summary>
    /// 견적서 열 너비를 설정한다. (A~J, 10열 — 할인% 추가)
    /// </summary>
    private static void SetQuotationColumnWidths(IXLWorksheet ws)
    {
        ws.Column(1).Width = 5;   // No
        ws.Column(2).Width = 20;  // 품명
        ws.Column(3).Width = 11;  // 규격
        ws.Column(4).Width = 7;   // 단위
        ws.Column(5).Width = 9;   // 수량
        ws.Column(6).Width = 11;  // 단가
        ws.Column(7).Width = 8;   // 할인(%)
        ws.Column(8).Width = 13;  // 금액
        ws.Column(9).Width = 11;  // 부가세
        ws.Column(10).Width = 11; // 비고
    }

    /// <summary>
    /// 세금계산서 열 너비를 설정한다. (A~I, 9열)
    /// </summary>
    private static void SetTaxInvoiceColumnWidths(IXLWorksheet ws)
    {
        ws.Column(1).Width = 5;   // 월
        ws.Column(2).Width = 5;   // 일
        ws.Column(3).Width = 20;  // 품목
        ws.Column(4).Width = 12;  // 규격
        ws.Column(5).Width = 10;  // 수량
        ws.Column(6).Width = 12;  // 단가
        ws.Column(7).Width = 14;  // 공급가액
        ws.Column(8).Width = 12;  // 세액
        ws.Column(9).Width = 12;  // 비고
    }
}
