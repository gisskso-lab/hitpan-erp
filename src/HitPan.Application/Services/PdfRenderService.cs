using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HitPan.Application.Services;

/// <summary>
/// 6종 문서 PDF 렌더링 서비스 (사장님 결재 2026-04-29).
/// 동일 레이아웃(회사 헤더 / 거래처 / 라인 / 합계)에 문서타입만 다르게 표시.
/// QuestPDF Community License (회사 매출 1M$ 이하 무료) — 베타 단계 적합.
/// </summary>
public sealed class PdfRenderService : IPdfRenderService
{
    private readonly IDbConnection _db;
    private readonly ILogger<PdfRenderService> _logger;
    private readonly IFormTemplateService? _formTemplateService;

    public PdfRenderService(IDbConnection db, ILogger<PdfRenderService> logger,
        IFormTemplateService? formTemplateService = null)
    {
        _db = db; _logger = logger; _formTemplateService = formTemplateService;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType, string documentId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 🔴 20260826작6 — 급여명세서는 거래문서와 **구조가 아예 다르다.**
        //   DocumentSnapshot 은 거래처·품목·수량·단가·부가세 축인데, 급여명세서는
        //   지급항목·공제항목·실수령액 축이다. 억지로 끼우면 모든 칸이 뜻과 어긋난다
        //   (거래처 칸에 사원명, 단가 칸에 공제액이 들어가는 식).
        //   ⇒ **전용 경로로 가른다.** 아래 거래문서 흐름은 한 줄도 건드리지 않는다(#1).
        if (string.Equals(documentType, PayslipDocType, StringComparison.Ordinal))
        {
            return await RenderPayslipAsync(tenantId, documentId, ct).ConfigureAwait(false);
        }

        var data = await LoadDocumentAsync(tenantId, documentType, documentId, ct).ConfigureAwait(false);
        var company = await LoadCompanyAsync(tenantId, ct).ConfigureAwait(false);

        // 작지② 양식 분기 (사장님 작업지시 2026-05-31)
        // form_templates에서 paper_mode + 여백 + 토글 로드 → plain·preprint 분기
        var template = await TryLoadTemplateAsync(tenantId, documentType, ct).ConfigureAwait(false);
        var isPreprint = template?.PaperMode == "preprint";
        // 🔴 봉합 2026-08-12 (사장님 지시 "양식정보 설정의 양식용지만 해")
        //   고객이 맞춘 좌표가 있으면 그대로 찍는다. 종전에는 이 값을 읽지도 않아
        //   코드 고정 위치로만 나갔고, 시판 양식지 칸과 안 맞았다.
        var useCoords = isPreprint && (template?.FieldCoords.Count ?? 0) > 0;
        var showHeader = template?.ShowCompanyLogo ?? true;
        var showBorder = template?.ShowBorder ?? true;
        var marginTop = (float)(template?.MarginTopMm ?? 20);
        var marginLeft = (float)(template?.MarginLeftMm ?? 20);
        var marginRight = (float)(template?.MarginRightMm ?? 20);
        var marginBottom = (float)(template?.MarginBottomMm ?? 20);

        var bytes = Document.Create(c =>
        {
            c.Page(page =>
            {
                page.Size(PageSizes.A4);
                // 좌표 인쇄는 원점이 **종이 왼쪽 위 모서리**다(고객이 자를 대는 기준).
                //   여백을 주면 좌표가 그만큼 통째로 밀린다 ⇒ 이 경로만 여백 0.
                if (useCoords)
                {
                    page.MarginTop(0); page.MarginLeft(0); page.MarginRight(0); page.MarginBottom(0);
                }
                else
                {
                    page.MarginTop(marginTop, Unit.Millimetre);
                    page.MarginLeft(marginLeft, Unit.Millimetre);
                    page.MarginRight(marginRight, Unit.Millimetre);
                    page.MarginBottom(marginBottom, Unit.Millimetre);
                }
                page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(10));
                page.PageColor(Colors.White);

                // preprint(양식용지): 헤더·테두리 미렌더, 필드값만 저장
                if (!isPreprint && showHeader)
                {
                    page.Header().Element(e => ComposeHeader(e, data, company));
                }
                if (useCoords)
                    page.Content().Element(e => ComposeBodyCoords(e, data, template!.FieldCoords));
                else
                    page.Content().Element(e => ComposeBody(e, data, isPreprint, showBorder));
                if (!isPreprint)
                {
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("Page ").FontSize(8); t.CurrentPageNumber().FontSize(8);
                        t.Span(" / ").FontSize(8); t.TotalPages().FontSize(8);
                    });
                }
            });
        }).GeneratePdf();

        var fileName = $"{KoLabel(documentType)}_{data.DocNo}.pdf";
        return (bytes, fileName);
    }

    private static string KoLabel(string docType) => docType switch
    {
        "quotation" => "견적서",
        "sales_order" => "수주서",
        "delivery" => "거래명세서",
        "tax_invoice" => "세금계산서",
        "purchase_order" => "발주서",
        "purchase_receipt" => "매입명세서",
        _ => "문서"
    };

    private static void ComposeHeader(IContainer e, DocumentSnapshot d, CompanyInfo c)
    {
        e.Column(col =>
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().Column(left =>
                {
                    left.Item().Text(KoLabel(d.DocType)).FontSize(20).Bold();
                    left.Item().PaddingTop(2).Text($"문서번호: {d.DocNo}").FontSize(9);
                    left.Item().Text($"일자: {d.DocDate:yyyy-MM-dd}").FontSize(9);
                });
                r.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text(c.CompanyName).FontSize(12).Bold();
                    if (!string.IsNullOrWhiteSpace(c.BusinessNo))
                        right.Item().Text($"사업자: {c.BusinessNo}").FontSize(9);
                    if (!string.IsNullOrWhiteSpace(c.RepresentativeName))
                        right.Item().Text($"대표: {c.RepresentativeName}").FontSize(9);
                    if (!string.IsNullOrWhiteSpace(c.Address))
                        right.Item().Text(c.Address).FontSize(8);
                    if (!string.IsNullOrWhiteSpace(c.Phone))
                        right.Item().Text($"TEL: {c.Phone}").FontSize(8);
                });
            });
            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Darken2);
        });
    }

    private static void ComposeBody(IContainer e, DocumentSnapshot d, bool isPreprint = false, bool showBorder = true)
    {
        // preprint(양식용지): 거래처·합계 박스 테두리·헤더 미렌더, 필드값만 좌표 저장
        if (isPreprint)
        {
            ComposeBodyPreprint(e, d);
            return;
        }
        var borderWidth = showBorder ? 0.5f : 0f;
        e.PaddingTop(10).Column(col =>
        {
            // 거래처 박스
            col.Item().PaddingBottom(8).Border(borderWidth).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(p =>
            {
                p.Item().Text("[ 거래처 정보 ]").FontSize(9).SemiBold();
                p.Item().PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().Text($"상호: {d.PartnerName}").FontSize(10);
                    if (!string.IsNullOrWhiteSpace(d.PartnerBusinessNo))
                        r.RelativeItem().AlignRight().Text($"사업자: {d.PartnerBusinessNo}").FontSize(9);
                });
                if (!string.IsNullOrWhiteSpace(d.PartnerAddress))
                    p.Item().Text($"주소: {d.PartnerAddress}").FontSize(9);
                if (!string.IsNullOrWhiteSpace(d.PartnerContact))
                    p.Item().Text($"담당: {d.PartnerContact}").FontSize(9);
            });

            // 품목 라인 표
            col.Item().Table(tbl =>
            {
                tbl.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(30);          // 순번
                    c.RelativeColumn(3);           // 품명
                    c.RelativeColumn(2);           // 규격
                    c.ConstantColumn(60);          // 수량
                    c.ConstantColumn(80);          // 단가
                    c.ConstantColumn(90);          // 공급가
                    c.ConstantColumn(70);          // 부가세
                    c.ConstantColumn(90);          // 합계
                });
                tbl.Header(h =>
                {
                    h.Cell().Element(CellHead).Text("순");
                    h.Cell().Element(CellHead).Text("품명");
                    h.Cell().Element(CellHead).Text("규격");
                    h.Cell().Element(CellHead).AlignRight().Text("수량");
                    h.Cell().Element(CellHead).AlignRight().Text("단가");
                    h.Cell().Element(CellHead).AlignRight().Text("공급가액");
                    h.Cell().Element(CellHead).AlignRight().Text("부가세");
                    h.Cell().Element(CellHead).AlignRight().Text("합계");
                });
                int seq = 1;
                foreach (var line in d.Lines)
                {
                    tbl.Cell().Element(CellBody).AlignCenter().Text($"{seq++}");
                    tbl.Cell().Element(CellBody).Text(line.ItemName);
                    tbl.Cell().Element(CellBody).Text(line.Spec ?? "");
                    tbl.Cell().Element(CellBody).AlignRight().Text(line.Qty.ToString("N1"));
                    tbl.Cell().Element(CellBody).AlignRight().Text(line.UnitPrice.ToString("N0"));
                    tbl.Cell().Element(CellBody).AlignRight().Text(line.Supply.ToString("N0"));
                    tbl.Cell().Element(CellBody).AlignRight().Text(line.Vat.ToString("N0"));
                    tbl.Cell().Element(CellBody).AlignRight().Text(line.Total.ToString("N0"));
                }
                if (d.Lines.Count == 0)
                {
                    tbl.Cell().ColumnSpan(8).Element(CellBody).AlignCenter().Text("(품목 정보 없음)").FontColor(Colors.Grey.Darken1);
                }
            });

            // 합계 박스
            col.Item().PaddingTop(8).AlignRight().Column(p =>
            {
                p.Item().Text($"공급가액 합계: {d.TotalSupply:N0}").FontSize(10);
                p.Item().Text($"부가세 합계  : {d.TotalVat:N0}").FontSize(10);
                p.Item().PaddingTop(4).Text($"총 합계: {d.TotalAmount:N0} 원").FontSize(13).Bold();
            });

            if (!string.IsNullOrWhiteSpace(d.Remark))
            {
                col.Item().PaddingTop(10).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(p =>
                {
                    p.Item().Text("비고").FontSize(9).SemiBold();
                    p.Item().Text(d.Remark).FontSize(9);
                });
            }
        });

        static IContainer CellHead(IContainer x) => x.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4);
        static IContainer CellBody(IContainer x) => x.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4);
    }

    // ─── DB lookup ─────────────────────────────────────
    private async Task<DocumentSnapshot> LoadDocumentAsync(string tenantId, string docType, string docId, CancellationToken ct)
    {
        // (문서별 SQL 다름 — 핵심 6종만 분기)
        return docType switch
        {
            "quotation" => await LoadQuotationAsync(tenantId, docId, ct).ConfigureAwait(false),
            "sales_order" => await LoadSalesOrderAsync(tenantId, docId, ct).ConfigureAwait(false),
            "delivery" => await LoadDeliveryAsync(tenantId, docId, ct).ConfigureAwait(false),
            "tax_invoice" => await LoadTaxInvoiceAsync(tenantId, docId, ct).ConfigureAwait(false),
            "purchase_order" => await LoadPurchaseOrderAsync(tenantId, docId, ct).ConfigureAwait(false),
            "purchase_receipt" => await LoadPurchaseReceiptAsync(tenantId, docId, ct).ConfigureAwait(false),
            _ => new DocumentSnapshot { DocType = docType, DocNo = docId }
        };
    }

    private async Task<DocumentSnapshot> LoadQuotationAsync(string tenantId, string id, CancellationToken ct)
    {
        // drift 봉합(13차 후순위→봉합): quotations 컬럼 DDL 정합(헌법 #13·#36).
        // quotation_no→quote_no, quotation_date→quote_date, quotation_id→quote_id,
        // total_supply→total_amount(공급가), total_vat→vat_amount, total_amount=공급가+부가세, remark→memo.
        // quotation_items: item_name 없음→items JOIN, seq 없음→sort_order, supply는 amount, total=amount+vat.
        const string head = """
            SELECT q.quote_no AS DocNo, q.quote_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(q.total_amount,0) AS TotalSupply, COALESCE(q.vat_amount,0) AS TotalVat,
                   COALESCE(q.total_amount,0) + COALESCE(q.vat_amount,0) AS TotalAmount,
                   q.memo AS Remark
            FROM quotations q LEFT JOIN partners p ON p.partner_id = q.partner_id AND p.tenant_id = q.tenant_id
            WHERE q.tenant_id = @TenantId AND q.quote_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(qi.qty,0) AS Qty, COALESCE(qi.unit_price,0) AS UnitPrice,
                   COALESCE(qi.amount,0) AS Supply, COALESCE(qi.vat_amount,0) AS Vat,
                   COALESCE(qi.amount,0) + COALESCE(qi.vat_amount,0) AS Total
            FROM quotation_items qi LEFT JOIN items i ON i.item_id = qi.item_id
            WHERE qi.quote_id = @Id ORDER BY qi.sort_order
            """;
        return await LoadByQueriesAsync(tenantId, id, "quotation", head, lines, ct).ConfigureAwait(false);
    }

    private async Task<DocumentSnapshot> LoadSalesOrderAsync(string tenantId, string id, CancellationToken ct)
    {
        // drift 봉합: sales_orders 컬럼 DDL 정합. so_no→order_no, so_date→order_date, so_id→order_id,
        // total_supply→total_amount(공급가), total_vat→vat_amount, remark→memo.
        // sales_order_items: item_name 없음→items JOIN, qty→ordered_qty, supply_amount 존재, seq 없음→정렬 PK.
        const string head = """
            SELECT s.order_no AS DocNo, s.order_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(s.total_amount,0) AS TotalSupply, COALESCE(s.vat_amount,0) AS TotalVat,
                   COALESCE(s.total_amount,0) + COALESCE(s.vat_amount,0) AS TotalAmount,
                   s.memo AS Remark
            FROM sales_orders s LEFT JOIN partners p ON p.partner_id = s.partner_id AND p.tenant_id = s.tenant_id
            WHERE s.tenant_id = @TenantId AND s.order_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(soi.ordered_qty,0) AS Qty, COALESCE(soi.unit_price,0) AS UnitPrice,
                   COALESCE(soi.supply_amount,0) AS Supply, COALESCE(soi.vat_amount,0) AS Vat,
                   COALESCE(soi.supply_amount,0) + COALESCE(soi.vat_amount,0) AS Total
            FROM sales_order_items soi LEFT JOIN items i ON i.item_id = soi.item_id
            WHERE soi.tenant_id = @TenantId AND soi.order_id = @Id ORDER BY soi.order_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "sales_order", head, lines, ct).ConfigureAwait(false);
    }

    private async Task<DocumentSnapshot> LoadDeliveryAsync(string tenantId, string id, CancellationToken ct)
    {
        // drift 봉합: sales_deliveries total_supply→total_amount(공급가), total_vat→vat_amount, remark→memo.
        // sales_delivery_items: item_name 없음→items JOIN, qty 존재, supply_amount 존재, seq 없음→정렬 PK.
        const string head = """
            SELECT d.delivery_no AS DocNo, d.delivery_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(d.total_amount,0) AS TotalSupply, COALESCE(d.vat_amount,0) AS TotalVat,
                   COALESCE(d.total_amount,0) + COALESCE(d.vat_amount,0) AS TotalAmount,
                   d.memo AS Remark
            FROM sales_deliveries d LEFT JOIN partners p ON p.partner_id = d.partner_id AND p.tenant_id = d.tenant_id
            WHERE d.tenant_id = @TenantId AND d.delivery_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(di.qty,0) AS Qty, COALESCE(di.unit_price,0) AS UnitPrice,
                   COALESCE(di.supply_amount,0) AS Supply, COALESCE(di.vat_amount,0) AS Vat,
                   COALESCE(di.supply_amount,0) + COALESCE(di.vat_amount,0) AS Total
            FROM sales_delivery_items di LEFT JOIN items i ON i.item_id = di.item_id
            WHERE di.tenant_id = @TenantId AND di.delivery_id = @Id ORDER BY di.delivery_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "delivery", head, lines, ct).ConfigureAwait(false);
    }

    private async Task<DocumentSnapshot> LoadTaxInvoiceAsync(string tenantId, string id, CancellationToken ct)
    {
        // tax_invoices drift 봉합(13차 후순위→봉합): 컬럼명 DDL 정합(헌법 #13·#36).
        // invoice_date→issued_at, supply_amount→amount_total, vat_amount→vat_total,
        // total_amount=amount_total+vat_total 계산, remark→remark1, tax_invoice_id→invoice_id.
        // tax_invoices에 partner_id 없음 → delivery_id 경유 sales_deliveries.partner_id로 거래처 조회.
        const string head = """
            SELECT t.invoice_no AS DocNo, t.issued_at AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(t.amount_total,0) AS TotalSupply, COALESCE(t.vat_total,0) AS TotalVat,
                   COALESCE(t.amount_total,0) + COALESCE(t.vat_total,0) AS TotalAmount,
                   t.remark1 AS Remark
            FROM tax_invoices t
            LEFT JOIN sales_deliveries sd ON sd.delivery_id = t.delivery_id AND sd.tenant_id = t.tenant_id
            LEFT JOIN partners p ON p.partner_id = sd.partner_id AND p.tenant_id = t.tenant_id
            WHERE t.tenant_id = @TenantId AND t.invoice_id = @Id
            """;
        // tax_invoices는 라인 별도 미사용 — 합계만 표시
        var d = new DocumentSnapshot { DocType = "tax_invoice" };
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var h = await _db.QuerySingleOrDefaultAsync<DocumentSnapshot>(
            new CommandDefinition(head, new { TenantId = tenantId, Id = id }, cancellationToken: ct))
            .ConfigureAwait(false);
        if (h is not null) { h.DocType = "tax_invoice"; return h; }
        d.DocNo = id; d.DocDate = DateTime.Today; d.PartnerName = "(거래처 정보 없음)";
        return d;
    }

    private async Task<DocumentSnapshot> LoadPurchaseOrderAsync(string tenantId, string id, CancellationToken ct)
    {
        // drift 봉합: purchase_orders total_supply→total_amount(공급가), total_vat→vat_amount, remark→memo.
        // purchase_order_items: item_name 없음→items JOIN, qty→ordered_qty, supply_amount 존재, seq 없음→정렬 PK.
        const string head = """
            SELECT po.po_no AS DocNo, po.po_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(po.total_amount,0) AS TotalSupply, COALESCE(po.vat_amount,0) AS TotalVat,
                   COALESCE(po.total_amount,0) + COALESCE(po.vat_amount,0) AS TotalAmount,
                   po.memo AS Remark
            FROM purchase_orders po LEFT JOIN partners p ON p.partner_id = po.partner_id AND p.tenant_id = po.tenant_id
            WHERE po.tenant_id = @TenantId AND po.po_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(poi.ordered_qty,0) AS Qty, COALESCE(poi.unit_price,0) AS UnitPrice,
                   COALESCE(poi.supply_amount,0) AS Supply, COALESCE(poi.vat_amount,0) AS Vat,
                   COALESCE(poi.supply_amount,0) + COALESCE(poi.vat_amount,0) AS Total
            FROM purchase_order_items poi LEFT JOIN items i ON i.item_id = poi.item_id
            WHERE poi.tenant_id = @TenantId AND poi.po_id = @Id ORDER BY poi.po_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "purchase_order", head, lines, ct).ConfigureAwait(false);
    }

    private async Task<DocumentSnapshot> LoadPurchaseReceiptAsync(string tenantId, string id, CancellationToken ct)
    {
        // drift 봉합: purchase_receipts total_supply→total_amount(공급가), total_vat→vat_amount, remark→memo.
        // purchase_receipt_items: item_name 없음→items JOIN, qty 존재, supply_amount 존재, seq 없음→정렬 PK.
        const string head = """
            SELECT pr.receipt_no AS DocNo, pr.receipt_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(pr.total_amount,0) AS TotalSupply, COALESCE(pr.vat_amount,0) AS TotalVat,
                   COALESCE(pr.total_amount,0) + COALESCE(pr.vat_amount,0) AS TotalAmount,
                   pr.memo AS Remark
            FROM purchase_receipts pr LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.receipt_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(pri.qty,0) AS Qty, COALESCE(pri.unit_price,0) AS UnitPrice,
                   COALESCE(pri.supply_amount,0) AS Supply, COALESCE(pri.vat_amount,0) AS Vat,
                   COALESCE(pri.supply_amount,0) + COALESCE(pri.vat_amount,0) AS Total
            FROM purchase_receipt_items pri LEFT JOIN items i ON i.item_id = pri.item_id
            WHERE pri.tenant_id = @TenantId AND pri.receipt_id = @Id ORDER BY pri.receipt_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "purchase_receipt", head, lines, ct).ConfigureAwait(false);
    }

    private async Task<DocumentSnapshot> LoadByQueriesAsync(string tenantId, string id, string docType, string headSql, string lineSql, CancellationToken ct)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        DocumentSnapshot? h = null;
        try
        {
            h = await _db.QuerySingleOrDefaultAsync<DocumentSnapshot>(
                new CommandDefinition(headSql, new { TenantId = tenantId, Id = id }, cancellationToken: ct))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 신규 ERP 테이블/컬럼명이 가정과 다르면 스키마 차이일 수 있음 — 안내문서 fallback
            _logger.LogWarning(ex, "[PDF] {Type} head 조회 실패 — fallback. id={Id}", docType, id);
        }
        if (h is null)
        {
            return new DocumentSnapshot { DocType = docType, DocNo = id, DocDate = DateTime.Today, PartnerName = "(원본 문서를 찾을 수 없습니다)" };
        }
        h.DocType = docType;
        try
        {
            var ls = await _db.QueryAsync<DocumentLine>(
                new CommandDefinition(lineSql, new { TenantId = tenantId, Id = id }, cancellationToken: ct))
                .ConfigureAwait(false);
            h.Lines = ls.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] {Type} 라인 조회 실패 — 빈 라인. id={Id}", docType, id);
            h.Lines = new();
        }
        return h;
    }

    private async Task<CompanyInfo> LoadCompanyAsync(string tenantId, CancellationToken ct)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = """
            SELECT company_name AS CompanyName, biz_no AS BusinessNo,
                   ceo_name AS RepresentativeName, address AS Address, tel AS Phone
            FROM local_company WHERE tenant_id = @TenantId
            """;
        try
        {
            var c = await _db.QuerySingleOrDefaultAsync<CompanyInfo>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct))
                .ConfigureAwait(false);
            return c ?? new CompanyInfo { CompanyName = "(회사 정보 없음)" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] local_company 조회 실패");
            return new CompanyInfo { CompanyName = "(회사 정보 없음)" };
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State != ConnectionState.Open && _db is DbConnection dc)
            await dc.OpenAsync(ct).ConfigureAwait(false);
    }

    // ─── 내부 모델 ─────────────────────────────────────
    private sealed class DocumentSnapshot
    {
        public string DocType { get; set; } = "";
        public string DocNo { get; set; } = "";
        public DateTime DocDate { get; set; } = DateTime.Today;
        public string PartnerName { get; set; } = "";
        public string? PartnerBusinessNo { get; set; }
        public string? PartnerAddress { get; set; }
        public string? PartnerContact { get; set; }
        public decimal TotalSupply { get; set; }
        public decimal TotalVat { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Remark { get; set; }
        public List<DocumentLine> Lines { get; set; } = new();
    }

    private sealed class DocumentLine
    {
        public string ItemName { get; set; } = "";
        public string? Spec { get; set; }
        public decimal Qty { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Supply { get; set; }
        public decimal Vat { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class CompanyInfo
    {
        public string CompanyName { get; set; } = "";
        public string? BusinessNo { get; set; }
        public string? RepresentativeName { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════
    // 급여명세서 (20260826작6)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>급여명세서 문서타입. 거래문서 8종과 <b>구조가 달라</b> 전용 경로로 간다.</summary>
    public const string PayslipDocType = "payslip";

    private sealed class PayslipSnapshot
    {
        public string SlipId { get; set; } = "";
        public string EmpName { get; set; } = "";
        public string? EmpNo { get; set; }
        public string? DeptName { get; set; }
        public int PayYear { get; set; }
        public int PayMonth { get; set; }
        public DateTime? PayDate { get; set; }
        public decimal TotalPayment { get; set; }
        public decimal TotalDeduct { get; set; }
        public decimal NetPayment { get; set; }
        public string? Memo { get; set; }
        public List<PayslipLine> Payments { get; set; } = new();
        public List<PayslipLine> Deducts { get; set; } = new();
    }

    private sealed class PayslipLine
    {
        public string ItemName { get; set; } = "";
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 급여명세서 PDF. <b>금액은 저장된 값을 그대로 찍는다 — 계산하지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 사장님 수동입력 원칙(2026-08-13): <i>"급여는 자동계산하지 말고 수동으로 int값 직접
    /// 받아서 입력하는게 가장 깔끔함"</i>. 여기서도 <b>합계를 다시 구하지 않는다</b> —
    /// 화면에서 본 숫자와 종이의 숫자가 다르면 그 자체로 사고다.
    /// </remarks>
    private async Task<(byte[] Bytes, string FileName)> RenderPayslipAsync(
        string tenantId, string slipId, CancellationToken ct)
    {
        var data = await LoadPayslipAsync(tenantId, slipId, ct).ConfigureAwait(false);
        var company = await LoadCompanyAsync(tenantId, ct).ConfigureAwait(false);

        // ① 결재 — 양식을 자유롭게 붙일 수 있게 한다. 미설정 고객은 기본 모양으로 나온다.
        //   ⚠️ TryLoadTemplateAsync 의 documentType→form_type 표에 payslip 을 넣어야
        //     고객이 설정한 양식이 실제로 먹는다(안 넣으면 조용히 기본 모양 — 8/12 적발 사례).
        var template = await TryLoadTemplateAsync(tenantId, PayslipDocType, ct).ConfigureAwait(false);
        var showHeader = template?.ShowCompanyLogo ?? true;
        var showBorder = template?.ShowBorder ?? true;
        var marginTop = (float)(template?.MarginTopMm ?? 20);
        var marginLeft = (float)(template?.MarginLeftMm ?? 20);
        var marginRight = (float)(template?.MarginRightMm ?? 20);
        var marginBottom = (float)(template?.MarginBottomMm ?? 20);

        var bytes = Document.Create(c =>
        {
            c.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(marginTop, Unit.Millimetre);
                page.MarginLeft(marginLeft, Unit.Millimetre);
                page.MarginRight(marginRight, Unit.Millimetre);
                page.MarginBottom(marginBottom, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(10));
                page.PageColor(Colors.White);

                if (showHeader)
                {
                    page.Header().Element(e => ComposePayslipHeader(e, data, company));
                }
                page.Content().Element(e => ComposePayslipBody(e, data, showBorder));
            });
        }).GeneratePdf();

        var fileName = $"급여명세서_{data.EmpName}_{data.PayYear}-{data.PayMonth:00}.pdf";
        return (bytes, fileName);
    }

    private async Task<PayslipSnapshot> LoadPayslipAsync(string tenantId, string slipId, CancellationToken ct)
    {
        var head = await _db.QueryFirstOrDefaultAsync<PayslipSnapshot>(new CommandDefinition(
            """
            SELECT s.slip_id       AS SlipId,
                   e.emp_name      AS EmpName,
                   e.emp_no        AS EmpNo,
                   d.dept_name     AS DeptName,
                   s.pay_year      AS PayYear,
                   s.pay_month     AS PayMonth,
                   s.pay_date      AS PayDate,
                   s.total_payment AS TotalPayment,
                   s.total_deduct  AS TotalDeduct,
                   s.net_payment   AS NetPayment,
                   s.memo          AS Memo
            FROM payroll_slips s
            LEFT JOIN employees   e ON e.employee_id = s.employee_id AND e.tenant_id = s.tenant_id
            LEFT JOIN departments d ON d.dept_id     = e.dept_id     AND d.tenant_id = s.tenant_id
            WHERE s.tenant_id = @TenantId AND s.slip_id = @SlipId
            """,
            new { TenantId = tenantId, SlipId = slipId }, cancellationToken: ct)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("급여명세서를 찾을 수 없습니다.");

        var lines = (await _db.QueryAsync<(string LineType, string ItemName, decimal Amount)>(new CommandDefinition(
            """
            SELECT line_type AS LineType, item_name AS ItemName, amount AS Amount
            FROM payroll_slip_lines
            WHERE tenant_id = @TenantId AND slip_id = @SlipId
            ORDER BY sort_order, item_name
            """,
            new { TenantId = tenantId, SlipId = slipId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        foreach (var (lineType, itemName, amount) in lines)
        {
            var row = new PayslipLine { ItemName = itemName, Amount = amount };
            // 공제(deduct) 외에는 전부 지급으로 본다 — 새 line_type 이 생겨도 항목이 사라지지 않게.
            if (string.Equals(lineType, "deduct", StringComparison.OrdinalIgnoreCase))
                head.Deducts.Add(row);
            else
                head.Payments.Add(row);
        }

        return head;
    }

    private static void ComposePayslipHeader(IContainer e, PayslipSnapshot d, CompanyInfo c)
    {
        e.Column(col =>
        {
            col.Item().AlignCenter().Text($"{d.PayYear}년 {d.PayMonth}월 급여명세서")
                .FontSize(16).Bold();
            col.Item().PaddingTop(4).AlignCenter().Text(c.CompanyName).FontSize(10);
            col.Item().PaddingBottom(6);
        });
    }

    private static void ComposePayslipBody(IContainer e, PayslipSnapshot d, bool showBorder)
    {
        e.Column(col =>
        {
            // ── 사원 정보 ──
            col.Item().PaddingBottom(8).Row(r =>
            {
                r.RelativeItem().Text($"성명: {d.EmpName}");
                r.RelativeItem().Text($"사번: {d.EmpNo ?? "-"}");
                r.RelativeItem().Text($"부서: {d.DeptName ?? "-"}");
                r.RelativeItem().Text($"지급일: {(d.PayDate.HasValue ? d.PayDate.Value.ToString("yyyy-MM-dd") : "-")}");
            });

            // ── 지급 / 공제 2단 ──
            col.Item().Row(r =>
            {
                r.RelativeItem().Element(x => ComposePayslipSection(x, "지급 내역", d.Payments, d.TotalPayment, showBorder));
                r.ConstantItem(12);
                r.RelativeItem().Element(x => ComposePayslipSection(x, "공제 내역", d.Deducts, d.TotalDeduct, showBorder));
            });

            // ── 실수령액 ──
            col.Item().PaddingTop(12).Background(Colors.Grey.Lighten3).Padding(8).Row(r =>
            {
                r.RelativeItem().Text("실수령액").FontSize(12).Bold();
                r.ConstantItem(160).AlignRight().Text($"{d.NetPayment:N0} 원").FontSize(14).Bold();
            });

            if (!string.IsNullOrWhiteSpace(d.Memo))
            {
                col.Item().PaddingTop(8).Text($"비고: {d.Memo}").FontSize(9);
            }
        });
    }

    private static void ComposePayslipSection(
        IContainer e, string title, List<PayslipLine> lines, decimal total, bool showBorder)
    {
        e.Column(col =>
        {
            col.Item().Background(Colors.Grey.Lighten2).Padding(4).Text(title).Bold();

            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.ConstantColumn(90); });

                foreach (var line in lines)
                {
                    t.Cell().Element(x => PayslipCell(x, showBorder)).Text(line.ItemName);
                    t.Cell().Element(x => PayslipCell(x, showBorder)).AlignRight().Text($"{line.Amount:N0}");
                }

                if (lines.Count == 0)
                {
                    t.Cell().ColumnSpan(2).Element(x => PayslipCell(x, showBorder))
                        .AlignCenter().Text("-").FontSize(9);
                }

                t.Cell().Element(x => PayslipCell(x, showBorder)).Text("합계").Bold();
                t.Cell().Element(x => PayslipCell(x, showBorder)).AlignRight().Text($"{total:N0}").Bold();
            });
        });
    }

    private static IContainer PayslipCell(IContainer c, bool showBorder) =>
        showBorder ? c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4) : c.Padding(4);

    /// <summary>
    /// 고객이 맞춘 좌표대로 양식용지에 값만 얹는다 (사장님 지시 2026-08-12 "양식용지만 해").
    /// </summary>
    /// <remarks>
    /// 시판 양식지는 칸 위치가 종이마다 다르다. 종전에는 좌표를 <b>읽지도 않아</b> 코드에 고정된
    /// 자리로만 인쇄됐고, 그래서 칸이 안 맞아 이 기능을 사실상 못 썼다.
    ///
    /// 원점은 <b>종이 왼쪽 위 모서리</b>다. 그래서 이 경로만 페이지 여백을 0 으로 두고
    /// 값 하나하나를 절대 위치에 올린다(고객이 자로 잰 값이 그대로 통해야 한다).
    ///
    /// 품목 줄은 <c>line.품목명</c> 처럼 적으면 첫 줄 위치가 되고, 그 아래로
    /// <c>row_pitch_mm</c> 간격(기본 6mm)으로 반복한다 — 줄마다 좌표를 적게 하면 못 쓴다.
    /// </remarks>
    private static void ComposeBodyCoords(IContainer e, DocumentSnapshot d, List<FieldCoord> coords)
    {
        // 줄 간격: 고객이 row_pitch 라는 이름으로 한 번만 지정한다(값 없으면 6mm).
        var pitch = coords.FirstOrDefault(c =>
            c.Key.Equals("row_pitch", StringComparison.OrdinalIgnoreCase))?.YMm ?? 0f;
        if (pitch <= 0f) pitch = 6f;

        // 몇 줄까지 찍을 수 있나 — 넘치면 종이 밖으로 나가 조용히 사라진다.
        var maxRows = coords.FirstOrDefault(c =>
            c.Key.Equals("max_rows", StringComparison.OrdinalIgnoreCase))?.XMm ?? 0f;

        e.Layers(layers =>
        {
            // 기준 층 — 좌표 인쇄는 이 위에 얹는다.
            layers.PrimaryLayer().Height(297, Unit.Millimetre);

            foreach (var c in coords)
            {
                if (c.Key.Equals("row_pitch", StringComparison.OrdinalIgnoreCase)) continue;
                if (c.Key.Equals("max_rows", StringComparison.OrdinalIgnoreCase)) continue;

                // 품목 줄: 첫 줄 좌표에서 아래로 반복
                if (c.Key.StartsWith("line.", StringComparison.OrdinalIgnoreCase))
                {
                    var field = c.Key.Substring(5);
                    var limit = maxRows > 0 ? (int)maxRows : d.Lines.Count;
                    for (int i = 0; i < d.Lines.Count && i < limit; i++)
                    {
                        var text = LineValue(d.Lines[i], field, i + 1);
                        if (string.IsNullOrEmpty(text)) continue;
                        PlaceText(layers, c, c.YMm + pitch * i, text);
                    }
                    continue;
                }

                var val = HeaderValue(d, c.Key);
                if (string.IsNullOrEmpty(val)) continue;
                PlaceText(layers, c, c.YMm, val);
            }
        });
    }

    /// <summary>값 하나를 종이 위 절대 위치(mm)에 올린다.</summary>
    private static void PlaceText(LayersDescriptor layers, FieldCoord c, float yMm, string text)
    {
        var width = c.WidthMm > 0 ? c.WidthMm : 30f;

        // TranslateX/Y 로 종이 왼쪽 위 기준 위치를 잡는다.
        var cell = layers.Layer()
            .TranslateX(c.XMm, Unit.Millimetre)
            .TranslateY(yMm, Unit.Millimetre)
            .Width(width, Unit.Millimetre);

        // 금액 칸은 오른쪽 정렬이라야 자릿수가 맞는다.
        var aligned = c.Align switch
        {
            "right" => cell.AlignRight(),
            "center" => cell.AlignCenter(),
            _ => cell
        };

        aligned.Text(text).FontSize(c.FontPt > 0 ? c.FontPt : 10f);
    }

    /// <summary>문서 머리 칸 이름 → 값. 고객이 한글로 적으므로 한글 이름을 먼저 받는다.</summary>
    private static string HeaderValue(DocumentSnapshot d, string key) => key.ToLowerInvariant() switch
    {
        "문서번호" or "doc_no" or "docno" => d.DocNo,
        "일자" or "doc_date" or "docdate" => d.DocDate.ToString("yyyy-MM-dd"),
        "년" or "year" => d.DocDate.ToString("yyyy"),
        "월" or "month" => d.DocDate.ToString("MM"),
        "일" or "day" => d.DocDate.ToString("dd"),
        "거래처" or "거래처명" or "상호" or "partner_name" => d.PartnerName,
        "사업자번호" or "등록번호" or "partner_business_no" => d.PartnerBusinessNo ?? "",
        "주소" or "partner_address" => d.PartnerAddress ?? "",
        "연락처" or "전화" or "partner_contact" => d.PartnerContact ?? "",
        "공급가액" or "total_supply" => d.TotalSupply.ToString("N0"),
        "세액" or "부가세" or "total_vat" => d.TotalVat.ToString("N0"),
        "합계" or "합계금액" or "total_amount" or "total" => d.TotalAmount.ToString("N0"),
        "비고" or "remark" => d.Remark ?? "",
        _ => ""
    };

    /// <summary>품목 줄 칸 이름 → 값.</summary>
    private static string LineValue(DocumentLine l, string field, int seq) => field.ToLowerInvariant() switch
    {
        "번호" or "순번" or "seq" or "no" => seq.ToString(),
        "품목" or "품목명" or "item_name" or "item" => l.ItemName,
        "규격" or "spec" => l.Spec ?? "",
        "수량" or "qty" => l.Qty.ToString("N1"),
        "단가" or "unit_price" or "price" => l.UnitPrice.ToString("N0"),
        "공급가액" or "supply" => l.Supply.ToString("N0"),
        "세액" or "부가세" or "vat" => l.Vat.ToString("N0"),
        "금액" or "합계" or "total" => l.Total.ToString("N0"),
        _ => ""
    };

    // 작지② paper_mode preprint 렌더링 — 시판 양식용지에 필드값만 좌표 저장
    // 테두리·헤더·푸터·합계 박스 모두 미렌더. 라인 데이터만 표 형태로 저장.
    //   ⚠️ 좌표가 없을 때만 쓰는 기본 배치다. 좌표가 있으면 ComposeBodyCoords 로 간다.
    private static void ComposeBodyPreprint(IContainer e, DocumentSnapshot d)
    {
        e.Column(col =>
        {
            // 거래처명 + 일자 — 양식용지 상단 좌표 (라벨 없음, 값만)
            col.Item().Row(r =>
            {
                r.RelativeItem().Text(d.PartnerName).FontSize(11);
                r.RelativeItem().AlignRight().Text(d.DocDate.ToString("yyyy-MM-dd")).FontSize(10);
            });

            // 라인 — 양식용지 본문 영역 (테두리 0, 좌표 저장)
            col.Item().PaddingTop(40).Table(tbl =>
            {
                tbl.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(25);
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                    c.ConstantColumn(50);
                    c.ConstantColumn(70);
                    c.ConstantColumn(80);
                    c.ConstantColumn(60);
                    c.ConstantColumn(80);
                });
                int seq = 1;
                foreach (var line in d.Lines)
                {
                    tbl.Cell().Padding(2).AlignCenter().Text($"{seq++}").FontSize(9);
                    tbl.Cell().Padding(2).Text(line.ItemName).FontSize(9);
                    tbl.Cell().Padding(2).Text(line.Spec ?? "").FontSize(9);
                    tbl.Cell().Padding(2).AlignRight().Text(line.Qty.ToString("N1")).FontSize(9);
                    tbl.Cell().Padding(2).AlignRight().Text(line.UnitPrice.ToString("N0")).FontSize(9);
                    tbl.Cell().Padding(2).AlignRight().Text(line.Supply.ToString("N0")).FontSize(9);
                    tbl.Cell().Padding(2).AlignRight().Text(line.Vat.ToString("N0")).FontSize(9);
                    tbl.Cell().Padding(2).AlignRight().Text(line.Total.ToString("N0")).FontSize(9);
                }
            });

            // 합계만 (라벨 없음, 양식용지 합계 박스 좌표)
            col.Item().PaddingTop(20).AlignRight().Text(d.TotalAmount.ToString("N0")).FontSize(11).Bold();
        });
    }

    /// <summary>고객이 저장한 좌표 JSON 을 읽는다. 잘못 적혀 있어도 인쇄는 되게 한다.</summary>
    /// <remarks>
    /// 🔴 여기서 예외를 던지면 <b>인쇄 자체가 막힌다.</b> 좌표는 거들 뿐이고 문서는 나가야 한다.
    ///   ⇒ 깨진 항목은 건너뛰고, 전부 깨졌으면 빈 목록(=종전 기본 배치)으로 되돌린다.
    /// 키 이름은 고객이 손으로 적으므로 <c>x_mm</c>·<c>xMm</c>·<c>x</c> 를 모두 받는다.
    /// </remarks>
    private List<FieldCoord> ParseFieldCoords(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<FieldCoord>();

        var list = new List<FieldCoord>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                _logger.LogWarning("양식용지 좌표가 목록 형태가 아니다 — 기본 배치로 인쇄한다.");
                return list;
            }

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                var key = Str(el, "key") ?? Str(el, "name");
                if (string.IsNullOrWhiteSpace(key)) continue;   // 어느 칸인지 모르면 못 찍는다

                var x = Num(el, "x_mm", "xMm", "x");
                var y = Num(el, "y_mm", "yMm", "y");
                if (x is null || y is null) continue;           // 위치 없으면 찍을 자리가 없다

                // 종이 밖 좌표는 조용히 사라진다 — 고객이 왜 안 나오는지 모르므로 남긴다.
                if (x < 0 || y < 0 || x > 420 || y > 594)
                {
                    _logger.LogWarning("양식용지 좌표가 종이 밖이다 — key={Key} x={X}mm y={Y}mm, 이 칸은 건너뛴다.", key, x, y);
                    continue;
                }

                list.Add(new FieldCoord
                {
                    Key = key!.Trim(),
                    XMm = (float)x.Value,
                    YMm = (float)y.Value,
                    FontPt = (float)(Num(el, "font_pt", "fontPt", "font") ?? 10d),
                    Align = (Str(el, "align") ?? "left").Trim().ToLowerInvariant(),
                    WidthMm = (float)(Num(el, "width_mm", "widthMm", "width") ?? 0d)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "양식용지 좌표를 읽지 못했다 — 기본 배치로 인쇄한다.");
            return new List<FieldCoord>();
        }
        return list;

        static string? Str(System.Text.Json.JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String
                ? p.GetString() : null;

        static double? Num(System.Text.Json.JsonElement el, params string[] names)
        {
            foreach (var n in names)
            {
                if (!el.TryGetProperty(n, out var p)) continue;
                if (p.ValueKind == System.Text.Json.JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
                // 고객이 "12.5" 처럼 따옴표를 붙여 적는 일이 잦다 — 그것도 받는다.
                if (p.ValueKind == System.Text.Json.JsonValueKind.String
                    && double.TryParse(p.GetString(), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
            }
            return null;
        }
    }

    // form_templates 조회 — 미존재 시 null (기존 plain 동작 정합)
    private async Task<FormTemplateInfo?> TryLoadTemplateAsync(string tenantId, string documentType, CancellationToken ct)
    {
        if (_formTemplateService is null) return null;

        // documentType → form_type 매핑
        //   ⚠️ 여기 빠진 문서는 **템플릿을 못 찾아 조용히 기본 모양으로 인쇄된다.**
        //   오류가 안 나서 더 위험하다 — 고객은 양식을 설정했는데 왜 안 바뀌는지 알 수 없다.
        //   ⇒ 양식(form_type)을 늘릴 때 이 표를 같이 늘려야 한다(2026-08-12 조사에서 적발).
        var formType = documentType switch
        {
            "quotation" => "estimate",
            "sales_order" => "sales_order",
            "delivery" => "delivery",
            "purchase_order" => "purchase_order",
            "purchase_receipt" => "receipt",
            "purchase_return" => "purchase_return",
            // 🔴 봉합 2026-08-12 — 종전 누락분.
            //   sales_return: 양식 시드는 됐는데 이 표에 없어 판매반품만 설정이 안 먹었다.
            "sales_return" => "sales_return",
            "tax_invoice" => "tax_invoice",
            // 사장님 결재 2026-08-11 (10종 확장) — 계산서·입금표
            "invoice_exempt" => "invoice_exempt",
            "payment_receipt" => "payment_receipt",
            // 🔴 20260826작6 — 급여명세서. 사장님 결재: "양식은 자유롭게 붙일 수 있도록 하되,
            //   기본 템플릿 제공". 이 표에 안 넣으면 고객이 양식을 설정해도 **안 먹는다**
            //   (위 주석의 8/12 적발 사례와 같은 자리 — 오류가 안 나서 더 위험하다).
            PayslipDocType => PayslipDocType,
            _ => null
        };
        if (formType is null) return null;

        try
        {
            var dto = await _formTemplateService.GetDefaultAsync(tenantId, formType, ct).ConfigureAwait(false);
            if (dto is null) return null;
            return new FormTemplateInfo
            {
                PaperMode = dto.PaperMode,
                MarginTopMm = dto.MarginTopMm,
                MarginLeftMm = dto.MarginLeftMm,
                MarginRightMm = dto.MarginRightMm,
                MarginBottomMm = dto.MarginBottomMm,
                ShowCompanyLogo = dto.ShowCompanyLogo,
                ShowBorder = dto.ShowBorder,
                // 🔴 봉합 2026-08-12 (사장님 지시 "양식정보 설정의 양식용지만 해")
                //   종전에는 이 값을 **읽어오지도 않았다.** 고객이 양식용지 좌표를 아무리
                //   맞춰도 인쇄물은 코드에 고정된 위치로만 나왔다.
                //   ⇒ 시판 양식지 칸과 안 맞아 양식용지 모드가 사실상 못 쓰는 기능이었다.
                FieldCoords = ParseFieldCoords(dto.FieldCoordsJson)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "form_templates 조회 실패 - tenant={Tenant} type={Type}, 기본 plain 가도", tenantId, formType);
            return null;
        }
    }

    private sealed class FormTemplateInfo
    {
        public string PaperMode { get; set; } = "plain";
        public decimal MarginTopMm { get; set; } = 20;
        public decimal MarginLeftMm { get; set; } = 20;
        public decimal MarginRightMm { get; set; } = 20;
        public decimal MarginBottomMm { get; set; } = 20;
        public bool ShowCompanyLogo { get; set; } = true;
        public bool ShowBorder { get; set; } = true;

        /// <summary>고객이 맞춘 양식용지 필드 위치. 비어 있으면 종전 기본 배치로 인쇄한다.</summary>
        public List<FieldCoord> FieldCoords { get; set; } = new();
    }

    /// <summary>
    /// 양식용지 한 칸의 위치. 고객이 실제 종이에 자를 대고 잰 값(mm)이 그대로 들어온다.
    /// </summary>
    /// <remarks>
    /// 원점은 <b>종이 왼쪽 위 모서리</b>다 — 여백이 아니라. 고객이 자로 재는 기준이 종이 끝이기 때문이다.
    /// 그래서 좌표 인쇄 경로는 페이지 여백을 0 으로 두고 값만 절대 위치에 올린다.
    /// </remarks>
    private sealed class FieldCoord
    {
        public string Key { get; set; } = "";
        public float XMm { get; set; }
        public float YMm { get; set; }
        public float FontPt { get; set; } = 10f;
        /// <summary>left(기본)·right·center. 금액 칸은 오른쪽 정렬이라야 자릿수가 맞는다.</summary>
        public string Align { get; set; } = "left";
        /// <summary>칸 너비(mm). 오른쪽·가운데 정렬일 때 기준이 된다. 0 이면 30mm 로 본다.</summary>
        public float WidthMm { get; set; }
    }
}
