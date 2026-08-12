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

        var data = await LoadDocumentAsync(tenantId, documentType, documentId, ct).ConfigureAwait(false);
        var company = await LoadCompanyAsync(tenantId, ct).ConfigureAwait(false);

        // 작지② 양식 분기 (사장님 작업지시 2026-05-31)
        // form_templates에서 paper_mode + 여백 + 토글 로드 → plain·preprint 분기
        var template = await TryLoadTemplateAsync(tenantId, documentType, ct).ConfigureAwait(false);
        var isPreprint = template?.PaperMode == "preprint";
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

                // preprint(양식용지): 헤더·테두리 미렌더, 필드값만 저장
                if (!isPreprint && showHeader)
                {
                    page.Header().Element(e => ComposeHeader(e, data, company));
                }
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

    // 작지② paper_mode preprint 렌더링 — 시판 양식용지에 필드값만 좌표 저장
    // 테두리·헤더·푸터·합계 박스 모두 미렌더. 라인 데이터만 표 형태로 저장.
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
                ShowBorder = dto.ShowBorder
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
    }
}
