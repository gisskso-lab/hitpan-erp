using HitPan.Application.DTOs.Document;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HitPan.Application.Services;

/// <summary>
/// PDF 문서 생성 서비스 — 거래명세서, 견적서, 세금계산서를 QuestPDF로 생성한다.
/// </summary>
public class PdfExportService
{
    // 공통 색상 상수
    private static readonly string HeaderBgColor = "#F0F0F0";

    /// <summary>
    /// 거래명세서 PDF를 생성한다.
    /// </summary>
    /// <param name="data">문서 데이터다.</param>
    /// <returns>PDF 바이트 배열이다.</returns>
    public byte[] GenerateDeliveryPdf(DocumentDto data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                ConfigurePage(page);

                page.Content().Column(col =>
                {
                    // 제목: "거 래 명 세 서"
                    col.Item().PaddingBottom(5).Text("거 래 명 세 서")
                        .FontSize(20).Bold().AlignCenter();

                    // 문서번호 (우측 정렬)
                    if (!string.IsNullOrEmpty(data.Header.DocNo))
                    {
                        col.Item().PaddingBottom(5).Text($"문서번호: {data.Header.DocNo}")
                            .FontSize(9).AlignRight();
                    }

                    // 공급자 / 공급받는자 정보 박스
                    col.Item().PaddingBottom(5).Row(row =>
                    {
                        row.RelativeItem().Border(1.5f).Padding(6).Column(left =>
                        {
                            left.Item().Text("[ 공급자 ]").FontSize(9).Bold();
                            RenderCompanyInfoRows(left, "등록번호", data.Tenant.BizNo,
                                "상    호", data.Tenant.CompanyName, "대 표 자", data.Tenant.CeoName,
                                "주    소", data.Tenant.Address, "업    태", data.Tenant.BizType,
                                "종    목", data.Tenant.BizItem, "전    화", data.Tenant.Tel);
                        });
                        row.ConstantItem(8); // 간격
                        row.RelativeItem().Border(1.5f).Padding(6).Column(right =>
                        {
                            right.Item().Text("[ 공급받는자 ]").FontSize(9).Bold();
                            RenderCompanyInfoRows(right, "등록번호", data.Partner.BizNo,
                                "상    호", data.Partner.PartnerName, "대 표 자", data.Partner.CeoName,
                                "주    소", data.Partner.Address, "업    태", data.Partner.BizType,
                                "종    목", data.Partner.BizItem, "전    화", data.Partner.Tel);
                        });
                    });

                    // 작성일 + 담당자
                    col.Item().PaddingBottom(5).Row(row =>
                    {
                        row.RelativeItem().Text($"작성일: {data.Header.OrderDate:yyyy-MM-dd}").FontSize(9);
                        row.RelativeItem().Text($"담당자: {data.Header.EmployeeName}").FontSize(9).AlignRight();
                    });

                    // 품목 테이블
                    col.Item().PaddingBottom(5).Element(c => RenderDeliveryTable(c, data));

                    // 합계 바
                    col.Item().PaddingBottom(10).Element(c => RenderTotalBar(c, data));

                    // 하단 문구 + 직인
                    col.Item().PaddingTop(15).Row(row =>
                    {
                        row.RelativeItem().AlignBottom().Text("위 금액을 청구합니다.")
                            .FontSize(11).Bold();
                        row.ConstantItem(80).Height(80).Element(c =>
                            RenderStamp(c, data.Tenant.CompanyName));
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    /// <summary>
    /// 견적서 PDF를 생성한다.
    /// </summary>
    /// <param name="data">문서 데이터다.</param>
    /// <returns>PDF 바이트 배열이다.</returns>
    public byte[] GenerateQuotationPdf(DocumentDto data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                ConfigurePage(page);

                page.Content().Column(col =>
                {
                    // 제목: "견 적 서"
                    col.Item().PaddingBottom(5).Text("견 적 서")
                        .FontSize(20).Bold().AlignCenter();

                    // 문서번호 (우측 정렬)
                    if (!string.IsNullOrEmpty(data.Header.DocNo))
                    {
                        col.Item().PaddingBottom(5).Text($"문서번호: {data.Header.DocNo}")
                            .FontSize(9).AlignRight();
                    }

                    // 공급자 / 수신 정보 박스
                    col.Item().PaddingBottom(5).Row(row =>
                    {
                        row.RelativeItem().Border(1.5f).Padding(6).Column(left =>
                        {
                            left.Item().Text("[ 공급자 ]").FontSize(9).Bold();
                            RenderCompanyInfoRows(left, "등록번호", data.Tenant.BizNo,
                                "상    호", data.Tenant.CompanyName, "대 표 자", data.Tenant.CeoName,
                                "주    소", data.Tenant.Address, "업    태", data.Tenant.BizType,
                                "종    목", data.Tenant.BizItem, "전    화", data.Tenant.Tel);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1.5f).Padding(6).Column(right =>
                        {
                            right.Item().Text("[ 수신 ]").FontSize(9).Bold();
                            RenderCompanyInfoRows(right, "등록번호", data.Partner.BizNo,
                                "상    호", data.Partner.PartnerName, "대 표 자", data.Partner.CeoName,
                                "주    소", data.Partner.Address, "업    태", data.Partner.BizType,
                                "종    목", data.Partner.BizItem, "전    화", data.Partner.Tel);
                        });
                    });

                    // 작성일 + 유효기한 + 담당자
                    col.Item().PaddingBottom(5).Row(row =>
                    {
                        row.RelativeItem().Text($"작성일: {data.Header.OrderDate:yyyy-MM-dd}").FontSize(9);
                        if (data.Header.ValidUntil.HasValue)
                        {
                            row.RelativeItem().Text($"유효기한: {data.Header.ValidUntil.Value:yyyy-MM-dd}")
                                .FontSize(9).AlignCenter();
                        }
                        row.RelativeItem().Text($"담당자: {data.Header.EmployeeName}").FontSize(9).AlignRight();
                    });

                    // 품목 테이블 (할인율 포함)
                    col.Item().PaddingBottom(5).Element(c => RenderQuotationTable(c, data));

                    // 합계 바
                    col.Item().PaddingBottom(5).Element(c => RenderTotalBar(c, data));

                    // 비고 박스 (유효기한, 납품조건, 결제조건)
                    col.Item().PaddingBottom(5).Border(1).Padding(8).Column(note =>
                    {
                        note.Item().Text("[ 비고 ]").FontSize(9).Bold();
                        if (data.Header.ValidUntil.HasValue)
                        {
                            note.Item().PaddingTop(3).Text($"• 유효기간: {data.Header.ValidUntil.Value:yyyy-MM-dd}까지")
                                .FontSize(9);
                        }
                        note.Item().PaddingTop(2).Text("• 납품조건: 협의 후 결정").FontSize(9);
                        note.Item().PaddingTop(2).Text("• 결제조건: 협의 후 결정").FontSize(9);
                    });

                    // 하단 문구 + 직인
                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().AlignBottom().Text("위 금액을 견적합니다.")
                            .FontSize(11).Bold();
                        row.ConstantItem(80).Height(80).Element(c =>
                            RenderStamp(c, data.Tenant.CompanyName));
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    /// <summary>
    /// 세금계산서 PDF를 생성한다.
    /// </summary>
    /// <param name="data">문서 데이터다.</param>
    /// <returns>PDF 바이트 배열이다.</returns>
    public byte[] GenerateTaxInvoicePdf(DocumentDto data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                ConfigurePage(page);

                page.Content().Column(col =>
                {
                    // 제목: "세 금 계 산 서" + [전자] 뱃지
                    col.Item().PaddingBottom(3).Row(titleRow =>
                    {
                        titleRow.RelativeItem(); // 왼쪽 여백
                        titleRow.AutoItem().Text("세 금 계 산 서")
                            .FontSize(20).Bold();
                        titleRow.AutoItem().PaddingLeft(8).PaddingTop(5)
                            .Border(1).Padding(2, Unit.Point)
                            .Text("[전자]").FontSize(9).Bold();
                        titleRow.RelativeItem(); // 오른쪽 여백
                    });

                    // 승인번호
                    if (!string.IsNullOrEmpty(data.Header.DocNo))
                    {
                        col.Item().PaddingBottom(5).Text($"승인번호: {data.Header.DocNo}")
                            .FontSize(9).AlignRight();
                    }

                    // 공급자 / 공급받는자 정보 (세금계산서 레이아웃)
                    col.Item().PaddingBottom(5).Border(1.5f).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(20);  // 세로 레이블
                            columns.RelativeColumn();    // 공급자 정보
                            columns.ConstantColumn(20);  // 세로 레이블
                            columns.RelativeColumn();    // 공급받는자 정보
                        });

                        // 공급자 세로 레이블
                        table.Cell().RowSpan(4).Border(0.5f).Background(HeaderBgColor)
                            .AlignCenter().AlignMiddle()
                            .Text("공\n급\n자").FontSize(8).Bold();

                        // 공급자 정보
                        table.Cell().RowSpan(4).Border(0.5f).Padding(4).Column(c =>
                        {
                            RenderTaxInfoRow(c, "등록번호", data.Tenant.BizNo);
                            RenderTaxInfoRow(c, "상    호", data.Tenant.CompanyName);
                            RenderTaxInfoRow(c, "대 표 자", data.Tenant.CeoName);
                            RenderTaxInfoRow(c, "주    소", data.Tenant.Address);
                            RenderTaxInfoRow(c, "업    태", data.Tenant.BizType);
                            RenderTaxInfoRow(c, "종    목", data.Tenant.BizItem);
                        });

                        // 공급받는자 세로 레이블
                        table.Cell().RowSpan(4).Border(0.5f).Background(HeaderBgColor)
                            .AlignCenter().AlignMiddle()
                            .Text("공\n급\n받\n는\n자").FontSize(8).Bold();

                        // 공급받는자 정보
                        table.Cell().RowSpan(4).Border(0.5f).Padding(4).Column(c =>
                        {
                            RenderTaxInfoRow(c, "등록번호", data.Partner.BizNo);
                            RenderTaxInfoRow(c, "상    호", data.Partner.PartnerName);
                            RenderTaxInfoRow(c, "대 표 자", data.Partner.CeoName);
                            RenderTaxInfoRow(c, "주    소", data.Partner.Address);
                            RenderTaxInfoRow(c, "업    태", data.Partner.BizType);
                            RenderTaxInfoRow(c, "종    목", data.Partner.BizItem);
                        });
                    });

                    // 작성일자
                    col.Item().PaddingBottom(5).Text($"작성일자: {data.Header.OrderDate:yyyy-MM-dd}")
                        .FontSize(9);

                    // 합계금액 | 공급가액 | 세액 상단 표
                    col.Item().PaddingBottom(5).Border(1.5f).Table(totalTable =>
                    {
                        totalTable.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        totalTable.Cell().Border(0.5f).Background(HeaderBgColor)
                            .Padding(4).AlignCenter().Text("합계금액").FontSize(9).Bold();
                        totalTable.Cell().Border(0.5f).Background(HeaderBgColor)
                            .Padding(4).AlignCenter().Text("공급가액").FontSize(9).Bold();
                        totalTable.Cell().Border(0.5f).Background(HeaderBgColor)
                            .Padding(4).AlignCenter().Text("세액").FontSize(9).Bold();

                        totalTable.Cell().Border(0.5f).Padding(4).AlignRight()
                            .Text($"{data.Header.TotalAmount:N0}").FontSize(10).Bold();
                        totalTable.Cell().Border(0.5f).Padding(4).AlignRight()
                            .Text($"{data.Header.SupplyAmount:N0}").FontSize(10);
                        totalTable.Cell().Border(0.5f).Padding(4).AlignRight()
                            .Text($"{data.Header.VatAmount:N0}").FontSize(10);
                    });

                    // 품목 테이블: 월, 일, 품목, 규격, 수량, 단가, 공급가액, 세액
                    col.Item().PaddingBottom(5).Element(c => RenderTaxInvoiceTable(c, data));

                    // 하단 문구 + 직인
                    col.Item().PaddingTop(15).Row(row =>
                    {
                        row.RelativeItem().AlignBottom().Text("이 금액을 청구함")
                            .FontSize(11).Bold();
                        row.ConstantItem(80).Height(80).Element(c =>
                            RenderStamp(c, data.Tenant.CompanyName));
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    /// <summary>
    /// 기존 호환용 — 타이틀 문자열로 PDF를 생성한다. (레거시 지원)
    /// </summary>
    public byte[] GeneratePdf(DocumentDto data, string title)
    {
        return title switch
        {
            "견적서" or "견 적 서" => GenerateQuotationPdf(data),
            "세금계산서" or "세 금 계 산 서" => GenerateTaxInvoicePdf(data),
            _ => GenerateDeliveryPdf(data)
        };
    }

    // ─────────────────────────────────────────────
    // 공통 헬퍼 메서드
    // ─────────────────────────────────────────────

    /// <summary>
    /// A4 페이지 공통 설정을 적용한다.
    /// </summary>
    private static void ConfigurePage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(15, Unit.Millimetre);
        page.DefaultTextStyle(x =>
            x.FontFamily("Malgun Gothic").FontSize(9));
    }

    /// <summary>
    /// 회사 정보 행을 렌더링한다 (거래명세서, 견적서 공통).
    /// </summary>
    private static void RenderCompanyInfoRows(ColumnDescriptor col,
        params string[] labelValuePairs)
    {
        for (var i = 0; i < labelValuePairs.Length - 1; i += 2)
        {
            var label = labelValuePairs[i];
            var value = labelValuePairs[i + 1] ?? "";
            col.Item().PaddingTop(2).Row(r =>
            {
                r.ConstantItem(55).Text(label).FontSize(8);
                r.RelativeItem().Text(value).FontSize(9);
            });
        }
    }

    /// <summary>
    /// 세금계산서 정보 행을 렌더링한다.
    /// </summary>
    private static void RenderTaxInfoRow(ColumnDescriptor col, string label, string? value)
    {
        col.Item().PaddingTop(1).Row(r =>
        {
            r.ConstantItem(55).Text(label).FontSize(8);
            r.RelativeItem().Text(value ?? "").FontSize(9);
        });
    }

    /// <summary>
    /// 거래명세서 품목 테이블을 렌더링한다.
    /// </summary>
    private static void RenderDeliveryTable(IContainer container, DocumentDto data)
    {
        container.Border(1.5f).Table(table =>
        {
            // 컬럼 정의: No, 품명, 규격, 단위, 수량, 단가, 금액, 비고
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(28);   // No
                columns.RelativeColumn(3);     // 품명
                columns.RelativeColumn(1.5f);  // 규격
                columns.RelativeColumn(1);     // 단위
                columns.RelativeColumn(1.2f);  // 수량
                columns.RelativeColumn(1.5f);  // 단가
                columns.RelativeColumn(1.5f);  // 금액
                columns.RelativeColumn(1.5f);  // 비고
            });

            // 헤더 행
            foreach (var h in new[] { "No", "품명", "규격", "단위", "수량", "단가", "금액", "비고" })
            {
                table.Cell().Element(HeaderCellStyle).Text(h).FontSize(8).Bold();
            }

            // 데이터 행
            for (var i = 0; i < data.Items.Count; i++)
            {
                var item = data.Items[i];
                table.Cell().Element(DataCellStyle).AlignCenter().Text($"{i + 1}").FontSize(8);
                table.Cell().Element(DataCellStyle).Text(item.ItemName).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(item.Spec).FontSize(8);
                table.Cell().Element(DataCellStyle).AlignCenter().Text(item.Unit).FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight().Text($"{item.Qty:N0}").FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight().Text($"{item.UnitPrice:N0}").FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight().Text($"{item.Amount:N0}").FontSize(8);
                table.Cell().Element(DataCellStyle).Text(item.Memo).FontSize(8);
            }

            // 합계 행
            table.Cell().ColumnSpan(4).Element(HeaderCellStyle).AlignCenter()
                .Text("합  계").FontSize(9).Bold();
            table.Cell().Element(DataCellStyle).AlignRight()
                .Text($"{data.Items.Sum(x => x.Qty):N0}").FontSize(8).Bold();
            table.Cell().Element(DataCellStyle).Text("").FontSize(8);
            table.Cell().Element(DataCellStyle).AlignRight()
                .Text($"{data.Header.SupplyAmount:N0}").FontSize(8).Bold();
            table.Cell().Element(DataCellStyle).Text("").FontSize(8);
        });
    }

    /// <summary>
    /// 견적서 품목 테이블을 렌더링한다 (할인율 컬럼 포함).
    /// </summary>
    private static void RenderQuotationTable(IContainer container, DocumentDto data)
    {
        container.Border(1.5f).Table(table =>
        {
            // 컬럼 정의: No, 품명, 규격, 단위, 수량, 단가, 할인(%), 금액, 비고
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(28);    // No
                columns.RelativeColumn(3);      // 품명
                columns.RelativeColumn(1.2f);   // 규격
                columns.RelativeColumn(0.8f);   // 단위
                columns.RelativeColumn(1);      // 수량
                columns.RelativeColumn(1.3f);   // 단가
                columns.RelativeColumn(0.8f);   // 할인(%)
                columns.RelativeColumn(1.3f);   // 금액
                columns.RelativeColumn(1.3f);   // 비고
            });

            // 헤더 행
            foreach (var h in new[] { "No", "품명", "규격", "단위", "수량", "단가", "할인(%)", "금액", "비고" })
            {
                table.Cell().Element(HeaderCellStyle).Text(h).FontSize(8).Bold();
            }

            // 데이터 행
            for (var i = 0; i < data.Items.Count; i++)
            {
                var item = data.Items[i];
                table.Cell().Element(DataCellStyle).AlignCenter().Text($"{i + 1}").FontSize(8);
                table.Cell().Element(DataCellStyle).Text(item.ItemName).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(item.Spec).FontSize(8);
                table.Cell().Element(DataCellStyle).AlignCenter().Text(item.Unit).FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight().Text($"{item.Qty:N0}").FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight().Text($"{item.UnitPrice:N0}").FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight()
                    .Text(item.DiscountRate > 0 ? $"{item.DiscountRate:N1}" : "").FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight().Text($"{item.Amount:N0}").FontSize(8);
                table.Cell().Element(DataCellStyle).Text(item.Memo).FontSize(8);
            }

            // 합계 행
            table.Cell().ColumnSpan(5).Element(HeaderCellStyle).AlignCenter()
                .Text("합  계").FontSize(9).Bold();
            table.Cell().Element(DataCellStyle).Text("").FontSize(8);
            table.Cell().Element(DataCellStyle).Text("").FontSize(8);
            table.Cell().Element(DataCellStyle).AlignRight()
                .Text($"{data.Header.SupplyAmount:N0}").FontSize(8).Bold();
            table.Cell().Element(DataCellStyle).Text("").FontSize(8);
        });
    }

    /// <summary>
    /// 세금계산서 품목 테이블을 렌더링한다 (월, 일, 품목, 규격, 수량, 단가, 공급가액, 세액).
    /// </summary>
    private static void RenderTaxInvoiceTable(IContainer container, DocumentDto data)
    {
        container.Border(1.5f).Table(table =>
        {
            // 컬럼 정의: 월, 일, 품목, 규격, 수량, 단가, 공급가액, 세액
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(28);    // 월
                columns.ConstantColumn(28);    // 일
                columns.RelativeColumn(3);      // 품목
                columns.RelativeColumn(1.2f);   // 규격
                columns.RelativeColumn(1);      // 수량
                columns.RelativeColumn(1.3f);   // 단가
                columns.RelativeColumn(1.5f);   // 공급가액
                columns.RelativeColumn(1.3f);   // 세액
            });

            // 헤더 행
            foreach (var h in new[] { "월", "일", "품목", "규격", "수량", "단가", "공급가액", "세액" })
            {
                table.Cell().Element(HeaderCellStyle).Text(h).FontSize(8).Bold();
            }

            // 데이터 행
            foreach (var item in data.Items)
            {
                var month = data.Header.OrderDate.Month.ToString();
                var day = data.Header.OrderDate.Day.ToString();
                table.Cell().Element(DataCellStyle).AlignCenter().Text(month).FontSize(8);
                table.Cell().Element(DataCellStyle).AlignCenter().Text(day).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(item.ItemName).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(item.Spec).FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight().Text($"{item.Qty:N0}").FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight().Text($"{item.UnitPrice:N0}").FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight().Text($"{item.Amount:N0}").FontSize(8);
                table.Cell().Element(DataCellStyle).AlignRight().Text($"{item.VatAmount:N0}").FontSize(8);
            }

            // 합계 행
            table.Cell().ColumnSpan(4).Element(HeaderCellStyle).AlignCenter()
                .Text("합  계").FontSize(9).Bold();
            table.Cell().Element(DataCellStyle).AlignRight()
                .Text($"{data.Items.Sum(x => x.Qty):N0}").FontSize(8).Bold();
            table.Cell().Element(DataCellStyle).Text("").FontSize(8);
            table.Cell().Element(DataCellStyle).AlignRight()
                .Text($"{data.Header.SupplyAmount:N0}").FontSize(8).Bold();
            table.Cell().Element(DataCellStyle).AlignRight()
                .Text($"{data.Header.VatAmount:N0}").FontSize(8).Bold();
        });
    }

    /// <summary>
    /// 합계 바 (공급가액, 부가세, 합계)를 렌더링한다.
    /// </summary>
    private static void RenderTotalBar(IContainer container, DocumentDto data)
    {
        container.Border(1.5f).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            table.Cell().Border(0.5f).Background(HeaderBgColor).Padding(5).AlignCenter()
                .Text("공급가액").FontSize(9).Bold();
            table.Cell().Border(0.5f).Background(HeaderBgColor).Padding(5).AlignCenter()
                .Text("부가세").FontSize(9).Bold();
            table.Cell().Border(0.5f).Background(HeaderBgColor).Padding(5).AlignCenter()
                .Text("합  계").FontSize(9).Bold();

            table.Cell().Border(0.5f).Padding(5).AlignRight()
                .Text($"{data.Header.SupplyAmount:N0}").FontSize(10);
            table.Cell().Border(0.5f).Padding(5).AlignRight()
                .Text($"{data.Header.VatAmount:N0}").FontSize(10);
            table.Cell().Border(0.5f).Padding(5).AlignRight()
                .Text($"{data.Header.TotalAmount:N0}").FontSize(10).Bold();
        });
    }

    /// <summary>
    /// 직인(도장) 영역을 렌더링한다 — 사각 테두리 안에 회사명 표시.
    /// </summary>
    private static void RenderStamp(IContainer container, string companyName)
    {
        // 직인 영역: 빨간 테두리 사각형 + 회사명 중앙 배치
        container.AlignRight().AlignBottom()
            .Width(70).Height(70)
            .Border(2).BorderColor(Colors.Red.Medium)
            .AlignCenter().AlignMiddle()
            .Padding(4)
            .Text(text =>
            {
                // 회사명이 길 경우 줄 바꿈 처리
                var displayName = companyName.Length > 4
                    ? companyName[..4] + "\n" + (companyName.Length > 8 ? companyName[4..8] : companyName[4..])
                    : companyName;
                text.AlignCenter();
                text.Span(displayName).FontSize(9).FontColor(Colors.Red.Medium).Bold();
            });
    }

    /// <summary>
    /// 헤더 셀 스타일 — 연한 회색 배경 + 내부 테두리.
    /// </summary>
    private static IContainer HeaderCellStyle(IContainer container)
        => container.Border(0.5f).Background(HeaderBgColor)
            .Padding(4).AlignCenter();

    /// <summary>
    /// 데이터 셀 스타일 — 내부 테두리.
    /// </summary>
    private static IContainer DataCellStyle(IContainer container)
        => container.Border(0.5f).Padding(3);
}
