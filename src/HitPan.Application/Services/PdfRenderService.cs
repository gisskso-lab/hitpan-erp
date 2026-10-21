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

        // 🔴 20261021작 절 B-3 — 세금계산서 전용(법정) 작성기.
        //   양식용지(preprint)·좌표 경로는 **종전 그대로** 간다(아래 공용 흐름).
        //   나머지 9종도 아래 공용 흐름 그대로 — 이 분기 안에서만 갈라진다(#1).
        if (string.Equals(documentType, TaxInvoiceDocType, StringComparison.Ordinal) && !isPreprint)
        {
            // 양식 행이 없으면 법정 2매가 기본이다(FormTemplateService 의 TwoCopyFormTypes).
            var copyMode = template?.PrintCopyMode ?? "both";
            var taxBytes = RenderTaxInvoiceDocument(data, company, copyMode,
                marginTop, marginLeft, marginRight, marginBottom);
            return (taxBytes, $"{KoLabel(documentType)}_{data.DocNo}.pdf");
        }

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
        // 🔴 20261021작 절 B-2 — 열쇠 교정.
        //   종전: WHERE t.invoice_id = @Id. 그런데 화면·모바일·이메일이 주는 값은 모두
        //   **거래명세서 번호(delivery_id)** 다(설계 §3-1) ⇒ 영영 못 찾아 「(거래처 정보 없음)」
        //   빈 종이가 메일로 나갔다. 이제 delivery_id 로 찾는다.
        //   🔴 품목 정본은 거래명세서다. tax_invoice_items 는 발행이 채우지 않는다(설계 §13 D-3).
        //
        //   컬럼명 DDL 정합(헌법 #13·#36) — 2026-10-20 information_schema 실측:
        //   tax_invoices(invoice_no·issued_at·amendment_reason·hometax_approval_no·remark1·status),
        //   sales_deliveries(delivery_no·delivery_date·total_amount·vat_amount·memo·is_deleted),
        //   partners(partner_name·biz_no·ceo_name·biz_type·biz_item·tel·address·manager_name).
        const string head = """
            SELECT d.delivery_no AS DocNo, d.delivery_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   p.ceo_name AS PartnerCeoName, p.biz_type AS PartnerBizType,
                   p.biz_item AS PartnerBizItem, p.tel AS PartnerPhone,
                   COALESCE(d.total_amount,0) AS TotalSupply, COALESCE(d.vat_amount,0) AS TotalVat,
                   COALESCE(d.total_amount,0) + COALESCE(d.vat_amount,0) AS TotalAmount,
                   d.memo AS Remark
            FROM sales_deliveries d
            LEFT JOIN partners p ON p.partner_id = d.partner_id AND p.tenant_id = d.tenant_id
            WHERE d.tenant_id = @TenantId AND d.delivery_id = @Id AND d.is_deleted = 0
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(di.qty,0) AS Qty, COALESCE(di.unit_price,0) AS UnitPrice,
                   COALESCE(di.supply_amount,0) AS Supply, COALESCE(di.vat_amount,0) AS Vat,
                   COALESCE(di.supply_amount,0) + COALESCE(di.vat_amount,0) AS Total
            FROM sales_delivery_items di LEFT JOIN items i ON i.item_id = di.item_id
            WHERE di.tenant_id = @TenantId AND di.delivery_id = @Id ORDER BY di.delivery_item_id
            """;

        var snap = await LoadByQueriesAsync(tenantId, id, "tax_invoice", head, lines, ct)
            .ConfigureAwait(false);

        // 발행된 계산서가 있으면 머리값만 덮는다.
        //   🔴 status='issued' 만 — 취소본(canceled)이 종이로 나가면 안 된다(설계 §13 D-6).
        //   🔴 미발행이면 행이 없다 ⇒ 거래명세서 값 그대로 그린다(승인번호 빈칸). 스텁으로 안 빠진다.
        const string invoiceHead = """
            SELECT t.invoice_no AS InvoiceNo, t.issued_at AS IssuedAt,
                   t.amendment_reason AS AmendmentReason,
                   t.hometax_approval_no AS HometaxApprovalNo,
                   t.remark1 AS Remark1
            FROM tax_invoices t
            WHERE t.tenant_id = @TenantId AND t.delivery_id = @Id AND t.status = 'issued'
            ORDER BY t.issued_at DESC
            LIMIT 1
            """;
        // 🔴 유일성이 없다 — delivery_id 는 UNIQUE 가 아니라 그냥 KEY 다(출하 DDL).
        //   더블클릭 한 번으로 issued 2행이 되면 QuerySingleOrDefault 는 그 전표를
        //   **영구 500** 으로 만든다 ⇒ QueryFirstOrDefault + ORDER BY ... LIMIT 1.
        try
        {
            var inv = await _db.QueryFirstOrDefaultAsync<TaxInvoiceHead>(
                new CommandDefinition(invoiceHead, new { TenantId = tenantId, Id = id }, cancellationToken: ct))
                .ConfigureAwait(false);
            if (inv is not null)
            {
                if (!string.IsNullOrWhiteSpace(inv.InvoiceNo)) snap.DocNo = inv.InvoiceNo;
                snap.DocDate = inv.IssuedAt;
                snap.AmendmentReason = inv.AmendmentReason;
                snap.HometaxApprovalNo = inv.HometaxApprovalNo;
                if (!string.IsNullOrWhiteSpace(inv.Remark1)) snap.Remark = inv.Remark1;
            }
            else
            {
                // 🔴 발행본이 없는데 취소본이 있으면, 그 전표는 **취소된 계산서**다.
                //   취소행은 목록에 남아 체크된다 ⇒ 막지 않으면 취소본이 「유효한 법정 서식」
                //   모양으로 거래처에 나간다. 🔴 인쇄 경로에서만 막는다 —
                //   발행·취소 로직(TaxInvoiceService)은 한 줄도 안 건드린다.
                const string canceledSql = """
                    SELECT 1 FROM tax_invoices
                    WHERE tenant_id = @TenantId AND delivery_id = @Id AND status = 'canceled'
                    LIMIT 1
                    """;
                var canceled = await _db.QueryFirstOrDefaultAsync<int?>(
                    new CommandDefinition(canceledSql, new { TenantId = tenantId, Id = id }, cancellationToken: ct))
                    .ConfigureAwait(false);
                snap.IsCanceledInvoice = canceled.HasValue;
            }
        }
        catch (Exception ex)
        {
            // 빈 catch 금지(#15). 계산서 머리를 못 덮어도 거래명세서 값으로는 그린다.
            _logger.LogWarning(ex, "[PDF] 세금계산서 머리 조회 실패 — 거래명세서 값으로 그린다. id={Id}", id);
        }

        return snap;
    }

    /// <summary>발행된 세금계산서의 머리값 — 거래명세서 위에 덮는다(20261021작 절 B-2).</summary>
    private sealed class TaxInvoiceHead
    {
        public string? InvoiceNo { get; set; }
        public DateTime IssuedAt { get; set; }
        public string? AmendmentReason { get; set; }
        public string? HometaxApprovalNo { get; set; }
        public string? Remark1 { get; set; }
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
                   ceo_name AS RepresentativeName, address AS Address, tel AS Phone,
                   -- 🔴 20261021작 절 B-2 — 세금계산서 법정 서식 칸. 칸 추가만, 삭제 0.
                   biz_type AS BizType, biz_item AS BizItem, seal_url AS SealUrl
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

        // ── 20261021작 절 B-2 — 세금계산서 법정 서식이 요구하는 칸. **더하기만**(#1).
        //    다른 9종은 이 칸들을 안 읽는다 ⇒ 종이가 안 바뀐다.
        /// <summary>공급받는 자 대표자명.</summary>
        public string? PartnerCeoName { get; set; }
        /// <summary>공급받는 자 업태.</summary>
        public string? PartnerBizType { get; set; }
        /// <summary>공급받는 자 종목.</summary>
        public string? PartnerBizItem { get; set; }
        /// <summary>공급받는 자 전화번호.</summary>
        public string? PartnerPhone { get; set; }
        /// <summary>수정사유 — 수정세금계산서일 때만 값이 있다.</summary>
        public string? AmendmentReason { get; set; }
        /// <summary>국세청 승인번호 — 전자발행이 끝난 건에만 값이 있다.</summary>
        public string? HometaxApprovalNo { get; set; }
        /// <summary>취소된 계산서인가 — 참이면 법정 서식 대신 안내문 한 장을 낸다.</summary>
        public bool IsCanceledInvoice { get; set; }
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

        // ── 20261021작 절 B-2 — 공급자 업태·종목. 법정 서식의 칸이다. **더하기만**(#1).
        /// <summary>공급자 업태.</summary>
        public string? BizType { get; set; }
        /// <summary>공급자 종목.</summary>
        public string? BizItem { get; set; }
        /// <summary>
        /// 인장 경로. 🔴 값이 있으면 이미지를 찍고, <b>비어 있으면 지금의 글자 도장</b>을 그린다.
        /// 지금은 보관소가 없어 항상 비어 있다(설계 §8-2) ⇒ 합격선 ④ 는 이번 차수 <b>미달</b>이다.
        /// 보관소가 생기는 날 <b>코드 수정 0</b> 으로 켜진다.
        /// </summary>
        public string? SealUrl { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════
    // 세금계산서 전용 작성기 (20261021작 절 B-3)
    //   배치는 PdfExportService.GenerateTaxInvoicePdf 의 법정 배치를 **바탕으로 옮겨 적고**
    //   6요소(수정사유·서명란·비고란·절취선·결제구분·영수/청구)를 보탰다.
    //   🔴 PdfExportService 는 한 줄도 안 지웠다(#1). 나머지 9종은 이 코드를 안 탄다.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>세금계산서 문서타입 키.</summary>
    private const string TaxInvoiceDocType = "tax_invoice";

    /// <summary>격자 머리칸 바탕색.</summary>
    private const string TaxGridBg = "#F0F0F0";

    /// <summary>
    /// 세금계산서 한 벌(또는 두 벌)을 그린다.
    /// </summary>
    /// <remarks>
    /// 🔴 <paramref name="copyMode"/> 는 <c>form_templates.print_copy_mode</c> 다 —
    /// <c>supplier</c>=공급자 보관용 1벌 · <c>recipient</c>=공급받는 자 보관용 1벌 ·
    /// <c>both</c>=두 벌 + 사이 절취선. 🔴 <b>두 벌은 같은 자료 객체 하나로 그린다</b> —
    /// 벌마다 따로 계산하면 양쪽 값이 어긋난다.
    /// 🔴 <b>줄을 자르는 코드는 0건</b>이다. 품목이 넘치면 QuestPDF 가 다음 장으로 이어 찍고,
    /// 열 제목은 <c>table.Header(</c> 로 2쪽에도 다시 나온다.
    /// </remarks>
    private byte[] RenderTaxInvoiceDocument(DocumentSnapshot d, CompanyInfo c, string copyMode,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        var sealImage = TryLoadSealImage(c.SealUrl);
        var drawSupplier = !string.Equals(copyMode, "recipient", StringComparison.Ordinal);
        var drawRecipient = !string.Equals(copyMode, "supplier", StringComparison.Ordinal);

        // 🔴 20261021작 §9-1 봉합(R-1) — 쪽 나눔 판정.
        //   반려 사유: 두 벌을 한 Column 에 그냥 쌓아 두면 QuestPDF 가 **벌 한가운데에서** 쪽을
        //   끊었다. 1쪽 한가운데 절취선 + 그 아래 미완성 종이 ⇒ 자르면 반 토막 계산서.
        //   집행: **두 벌이 한 장에 실제로 들어가는지 먼저 재고**(MeasureBothCopiesFitOnePage),
        //     · 들어가면 → 한 장에 두 벌 + 절취선 (기본 · 사장님 결재)
        //     · 안 들어가면 → 절취선을 빼고 벌마다 제 쪽에서 시작한다(PageBreak)
        //   🔴 품목을 줄이는 코드는 여전히 0건이다.
        var bothCopies = drawSupplier && drawRecipient;
        var fitsOnePage = !bothCopies || MeasureBothCopiesFitOnePage(
            d, c, sealImage, marginTop, marginLeft, marginRight, marginBottom);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(marginTop, Unit.Millimetre);
                page.MarginLeft(marginLeft, Unit.Millimetre);
                page.MarginRight(marginRight, Unit.Millimetre);
                page.MarginBottom(marginBottom, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(9));
                page.PageColor(Colors.White);

                page.Content().Column(col =>
                {
                    // 🔴 취소된 계산서는 법정 서식으로 내지 않는다 — 안내문 한 장만 낸다.
                    if (d.IsCanceledInvoice)
                    {
                        col.Item().Element(ComposeCanceledNotice);
                        return;
                    }

                    if (drawSupplier)
                    {
                        col.Item().Element(e => ComposeTaxInvoiceCopy(e, d, c, "공급자 보관용", sealImage));
                    }
                    if (bothCopies)
                    {
                        // 🔴 들어갈 때만 절취선. 안 들어가면 절취선 0건 + 쪽을 나눈다.
                        if (fitsOnePage) col.Item().Element(ComposeTearLine);
                        else col.Item().PageBreak();
                    }
                    if (drawRecipient)
                    {
                        col.Item().Element(e => ComposeTaxInvoiceCopy(e, d, c, "공급받는 자 보관용", sealImage));
                    }
                });

                // 한글 꼬리 — 한 장이면 「1 / 1 쪽」.
                page.Footer().AlignCenter().Text(t =>
                {
                    t.CurrentPageNumber().FontSize(8);
                    t.Span(" / ").FontSize(8);
                    t.TotalPages().FontSize(8);
                    t.Span(" 쪽").FontSize(8);
                });
            });
        }).GeneratePdf();
    }

    /// <summary>취소된 계산서 안내문 — 고객이 읽는 말로만 적는다.</summary>
    private static void ComposeCanceledNotice(IContainer e)
    {
        e.PaddingTop(40).AlignCenter().Column(col =>
        {
            col.Item().AlignCenter().Text("취소된 세금계산서입니다").FontSize(16).Bold();
            col.Item().PaddingTop(10).AlignCenter()
                .Text("취소된 계산서는 인쇄하거나 보낼 수 없습니다.").FontSize(11);
            col.Item().PaddingTop(3).AlignCenter()
                .Text("필요하시면 계산서를 다시 발행한 뒤 인쇄해 주세요.").FontSize(11);
        });
    }

    /// <summary>두 벌 사이 절취선.</summary>
    private static void ComposeTearLine(IContainer e)
    {
        e.PaddingVertical(7).Column(col =>
        {
            col.Item().LineHorizontal(0.8f).LineColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(2).AlignCenter().Text("절취선").FontSize(8);
        });
    }

    /// <summary>
    /// 🔴 20261021작 §9-1(R-1) — 두 벌 + 절취선이 A4 한 장에 <b>실제로</b> 들어가는지 잰다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>품목 줄 수로 눈대중하지 않는다.</b> 같은 종이·같은 여백·같은 글꼴로 한 장을 깔게 한 뒤
    /// QuestPDF 가 <b>그때 잰 높이</b>를 받는다 — 공급자 벌 높이 · 절취선 높이 · 공급받는 자 벌 높이,
    /// 그리고 머리·꼬리를 뺀 <b>실제 남은 높이</b>. 넷을 견주어 판정한다.
    /// </para>
    /// <para>
    /// 설계 §7 의 「136mm + 7mm + 136mm = 279mm ≤ 297mm」는 설계 당시의 손계산이다.
    /// 여기서는 그 숫자를 <b>쓰지 않는다</b> — 여백 설정·거래처 주소 길이·품목 줄 수에 따라
    /// 벌 높이가 달라지므로, 고정 숫자로 판정하면 또 틀린다.
    /// </para>
    /// <para>
    /// 🔴 재다가 실패하면 <b>「안 들어간다」로 본다.</b> 쪽을 나누면 종이가 두 장 나올 뿐이지만,
    /// 잘못 「들어간다」로 보면 <b>반 토막 계산서</b>가 고객에게 나간다.
    /// </para>
    /// </remarks>
    private bool MeasureBothCopiesFitOnePage(DocumentSnapshot d, CompanyInfo c, byte[]? sealImage,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        var probe = new TaxInvoiceFitProbe(d, c, sealImage);
        try
        {
            Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginTop(marginTop, Unit.Millimetre);
                    page.MarginLeft(marginLeft, Unit.Millimetre);
                    page.MarginRight(marginRight, Unit.Millimetre);
                    page.MarginBottom(marginBottom, Unit.Millimetre);
                    page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(9));
                    page.PageColor(Colors.White);
                    page.Content().Dynamic(probe);

                    // 꼬리를 똑같이 달아야 「남은 높이」가 실제와 같아진다.
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.CurrentPageNumber().FontSize(8);
                        t.Span(" / ").FontSize(8);
                        t.TotalPages().FontSize(8);
                        t.Span(" 쪽").FontSize(8);
                    });
                });
            }).GeneratePdf();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "세금계산서 쪽 나눔을 재지 못했다 — 안전하게 두 쪽으로 나눠 낸다. 문서번호 {DocNo}", d.DocNo);
            return false;
        }

        if (!probe.Measured)
        {
            _logger.LogWarning(
                "세금계산서 쪽 나눔 높이를 받지 못했다 — 안전하게 두 쪽으로 나눠 낸다. 문서번호 {DocNo}", d.DocNo);
            return false;
        }

        var needed = probe.SupplierHeight + probe.TearHeight + probe.RecipientHeight;

        // 판정 근거를 남긴다 — 「왜 두 쪽이 나왔냐」는 문의가 오면 이 줄이 답이다.
        _logger.LogDebug(
            "세금계산서 쪽 나눔 판정 — 공급자 {Supplier}pt · 절취선 {Tear}pt · 공급받는자 {Recipient}pt " +
            "= {Needed}pt / 남은 높이 {Available}pt (문서번호 {DocNo})",
            probe.SupplierHeight, probe.TearHeight, probe.RecipientHeight,
            needed, probe.AvailableHeight, d.DocNo);

        return needed <= probe.AvailableHeight;
    }

    /// <summary>
    /// 쪽 나눔 판정을 위한 자 — 종이는 버리고 <b>높이만</b> 가져온다(20261021작 §9-1).
    /// </summary>
    private sealed class TaxInvoiceFitProbe : IDynamicComponent
    {
        private readonly DocumentSnapshot _d;
        private readonly CompanyInfo _c;
        private readonly byte[]? _seal;

        public TaxInvoiceFitProbe(DocumentSnapshot d, CompanyInfo c, byte[]? seal)
        {
            _d = d;
            _c = c;
            _seal = seal;
        }

        /// <summary>높이를 한 번이라도 받았나.</summary>
        public bool Measured { get; private set; }
        public float SupplierHeight { get; private set; }
        public float TearHeight { get; private set; }
        public float RecipientHeight { get; private set; }
        public float AvailableHeight { get; private set; }

        public DynamicComponentComposeResult Compose(QuestPDF.Elements.DynamicContext context)
        {
            if (!Measured)
            {
                var supplier = context.CreateElement(
                    x => ComposeTaxInvoiceCopy(x, _d, _c, "공급자 보관용", _seal));
                var tear = context.CreateElement(ComposeTearLine);
                var recipient = context.CreateElement(
                    x => ComposeTaxInvoiceCopy(x, _d, _c, "공급받는 자 보관용", _seal));

                SupplierHeight = supplier.Size.Height;
                TearHeight = tear.Size.Height;
                RecipientHeight = recipient.Size.Height;
                AvailableHeight = context.AvailableSize.Height;
                Measured = true;
            }

            // 잰 뒤에는 빈 자리만 돌려준다 — 이 종이는 쓰지 않고 버린다.
            return new DynamicComponentComposeResult
            {
                Content = context.CreateElement(x => x.Height(0)),
                HasMoreContent = false
            };
        }
    }

    /// <summary>세금계산서 한 벌.</summary>
    private static void ComposeTaxInvoiceCopy(IContainer e, DocumentSnapshot d, CompanyInfo c,
        string copyLabel, byte[]? sealImage)
    {
        e.Column(col =>
        {
            // 제목줄 — 왼쪽 보관용 라벨 · 가운데 제목 · 오른쪽 전자 표시
            col.Item().Row(r =>
            {
                r.RelativeItem().AlignMiddle().Text(copyLabel).FontSize(9).Bold();
                r.RelativeItem(2).AlignCenter().Text("세 금 계 산 서").FontSize(18).Bold();
                r.RelativeItem().AlignMiddle().AlignRight().Text("전자").FontSize(9).Bold();
            });

            // 작성일자 · 승인번호
            col.Item().PaddingTop(2).Row(r =>
            {
                r.RelativeItem().Text($"작성일자: {d.DocDate:yyyy-MM-dd}").FontSize(9);
                r.RelativeItem().AlignRight().Text($"승인번호: {d.DocNo}").FontSize(9);
            });
            if (!string.IsNullOrWhiteSpace(d.HometaxApprovalNo))
            {
                col.Item().AlignRight().Text($"국세청승인번호: {d.HometaxApprovalNo}").FontSize(8);
            }
            // 수정사유 — 칸은 늘 그리고, 값이 있으면 채운다.
            col.Item().PaddingTop(1).Text($"수정사유: {d.AmendmentReason ?? ""}").FontSize(9);

            // 공급자 / 공급받는 자 — 2단 대칭 격자
            col.Item().PaddingTop(4).Border(1.2f).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(18);
                    columns.RelativeColumn();
                    columns.ConstantColumn(18);
                    columns.RelativeColumn();
                });

                table.Cell().Border(0.5f).Background(TaxGridBg).AlignCenter().AlignMiddle()
                    .Text("공\n급\n자").FontSize(8).Bold();
                table.Cell().Border(0.5f).Padding(4).Column(x =>
                {
                    TaxInfoRow(x, "등록번호", c.BusinessNo);
                    TaxInfoRow(x, "상    호", c.CompanyName);
                    TaxInfoRow(x, "대 표 자", c.RepresentativeName);
                    TaxInfoRow(x, "주    소", c.Address);
                    TaxInfoRow(x, "업    태", c.BizType);
                    TaxInfoRow(x, "종    목", c.BizItem);
                    TaxInfoRow(x, "전    화", c.Phone);
                });

                table.Cell().Border(0.5f).Background(TaxGridBg).AlignCenter().AlignMiddle()
                    .Text("공\n급\n받\n는\n자").FontSize(8).Bold();
                table.Cell().Border(0.5f).Padding(4).Column(x =>
                {
                    TaxInfoRow(x, "등록번호", d.PartnerBusinessNo);
                    TaxInfoRow(x, "상    호", d.PartnerName);
                    TaxInfoRow(x, "대 표 자", d.PartnerCeoName);
                    TaxInfoRow(x, "주    소", d.PartnerAddress);
                    TaxInfoRow(x, "업    태", d.PartnerBizType);
                    TaxInfoRow(x, "종    목", d.PartnerBizItem);
                    TaxInfoRow(x, "전    화", d.PartnerPhone);
                });
            });

            // 합계금액 · 공급가액 · 세액
            col.Item().PaddingTop(4).Border(1.2f).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                table.Cell().Element(TaxHeaderCell).AlignCenter().Text("합계금액").FontSize(9).Bold();
                table.Cell().Element(TaxHeaderCell).AlignCenter().Text("공급가액").FontSize(9).Bold();
                table.Cell().Element(TaxHeaderCell).AlignCenter().Text("세액").FontSize(9).Bold();
                table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalAmount:N0}").FontSize(10).Bold();
                table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalSupply:N0}").FontSize(10);
                table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalVat:N0}").FontSize(10);
            });

            // 품목 — 🔴 자르는 코드 0건. 넘치면 다음 장으로 이어 찍는다.
            col.Item().PaddingTop(4).Border(1.2f).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(24);     // 월
                    columns.ConstantColumn(24);     // 일
                    columns.RelativeColumn(3);      // 품목
                    columns.RelativeColumn(1.2f);   // 규격
                    columns.RelativeColumn(1);      // 수량
                    columns.RelativeColumn(1.3f);   // 단가
                    columns.RelativeColumn(1.5f);   // 공급가액
                    columns.RelativeColumn(1.3f);   // 세액
                });

                // 🔴 table.Header( — 2쪽에도 열 제목이 다시 나온다(합격선 ⑤).
                table.Header(header =>
                {
                    foreach (var h in new[] { "월", "일", "품목", "규격", "수량", "단가", "공급가액", "세액" })
                    {
                        header.Cell().Element(TaxHeaderCell).AlignCenter().Text(h).FontSize(8).Bold();
                    }
                });

                foreach (var line in d.Lines)
                {
                    table.Cell().Element(TaxDataCell).AlignCenter().Text($"{d.DocDate.Month}").FontSize(8);
                    table.Cell().Element(TaxDataCell).AlignCenter().Text($"{d.DocDate.Day}").FontSize(8);
                    table.Cell().Element(TaxDataCell).Text(line.ItemName).FontSize(8);
                    table.Cell().Element(TaxDataCell).Text(line.Spec ?? "").FontSize(8);
                    table.Cell().Element(TaxDataCell).AlignRight().Text($"{line.Qty:N0}").FontSize(8);
                    table.Cell().Element(TaxDataCell).AlignRight().Text($"{line.UnitPrice:N0}").FontSize(8);
                    table.Cell().Element(TaxDataCell).AlignRight().Text($"{line.Supply:N0}").FontSize(8);
                    table.Cell().Element(TaxDataCell).AlignRight().Text($"{line.Vat:N0}").FontSize(8);
                }

                table.Cell().ColumnSpan(4).Element(TaxHeaderCell).AlignCenter().Text("합  계").FontSize(9).Bold();
                table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.Lines.Sum(x => x.Qty):N0}").FontSize(8).Bold();
                table.Cell().Element(TaxDataCell).Text("").FontSize(8);
                table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalSupply:N0}").FontSize(8).Bold();
                table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalVat:N0}").FontSize(8).Bold();
            });

            // 결제구분 — 🔴 칸만 그린다. 값을 넣을 원천이 어디에도 없다(설계 §8 5번).
            //   입력 자리 신설은 이번 범위 밖이다 — 고객이 종이에 적는다.
            col.Item().PaddingTop(4).Border(1.2f).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                foreach (var h in new[] { "현금", "수표", "어음", "외상미수금" })
                {
                    table.Cell().Element(TaxHeaderCell).AlignCenter().Text(h).FontSize(8).Bold();
                }
                for (var i = 0; i < 4; i++)
                {
                    table.Cell().Element(TaxDataCell).MinHeight(16).Text("").FontSize(8);
                }
            });

            // 영수 / 청구 — 🔴 원천이 없다. 두 낱말을 칸으로 그려 고객이 종이에 동그라미를 친다
            //   (결재 Q-1 ⓒ). 외상미수금으로 추론하지 않는다.
            col.Item().PaddingTop(5).Row(r =>
            {
                r.RelativeItem().Column(left =>
                {
                    left.Item().Row(rr =>
                    {
                        rr.AutoItem().AlignMiddle().Text("이 금액을 ").FontSize(10);
                        rr.AutoItem().Border(0.8f).PaddingHorizontal(7).PaddingVertical(2)
                            .Text("영수").FontSize(10).Bold();
                        rr.AutoItem().AlignMiddle().PaddingHorizontal(3).Text("/").FontSize(10);
                        rr.AutoItem().Border(0.8f).PaddingHorizontal(7).PaddingVertical(2)
                            .Text("청구").FontSize(10).Bold();
                        rr.AutoItem().AlignMiddle().Text(" 함").FontSize(10);
                    });
                    left.Item().PaddingTop(3).Text("이 금액을 영수(청구)함").FontSize(10).Bold();

                    // 비고란
                    left.Item().PaddingTop(5).Text("비고").FontSize(8).Bold();
                    left.Item().PaddingTop(1).Border(0.5f).MinHeight(26).Padding(3)
                        .Text(d.Remark ?? "").FontSize(8);
                });
                r.ConstantItem(92).PaddingLeft(6).AlignBottom()
                    .Element(x => ComposeTaxSeal(x, c.CompanyName, sealImage));
            });

            // 서명란
            col.Item().PaddingTop(5).Border(1.2f).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                foreach (var h in new[] { "담당자", "확인자", "인수자" })
                {
                    table.Cell().Element(TaxHeaderCell).AlignCenter().Text(h).FontSize(8).Bold();
                }
                for (var i = 0; i < 3; i++)
                {
                    table.Cell().Element(TaxDataCell).MinHeight(26).Text("").FontSize(8);
                }
            });
        });
    }

    /// <summary>공급자·공급받는 자 칸 한 줄.</summary>
    private static void TaxInfoRow(ColumnDescriptor col, string label, string? value)
    {
        col.Item().PaddingTop(1).Row(r =>
        {
            r.ConstantItem(52).Text(label).FontSize(8);
            r.RelativeItem().Text(value ?? "").FontSize(9);
        });
    }

    private static IContainer TaxHeaderCell(IContainer container)
        => container.Border(0.5f).Background(TaxGridBg).PaddingVertical(3).PaddingHorizontal(3);

    private static IContainer TaxDataCell(IContainer container)
        => container.Border(0.5f).PaddingVertical(2).PaddingHorizontal(3);

    /// <summary>
    /// 인장 — 🔴 <c>seal_url</c> 에 값이 있으면 그 이미지를, 비어 있으면 지금의 글자 도장을 그린다.
    /// </summary>
    private static void ComposeTaxSeal(IContainer e, string companyName, byte[]? sealImage)
    {
        if (sealImage is not null)
        {
            e.AlignRight().AlignBottom().Width(70).Height(70).Image(sealImage).FitArea();
            return;
        }

        e.AlignRight().AlignBottom().Width(70).Height(70)
            .Border(1.5f).BorderColor(Colors.Red.Medium)
            .AlignCenter().AlignMiddle().Padding(4)
            .Text(text =>
            {
                var displayName = companyName.Length > 4
                    ? companyName[..4] + "\n" + (companyName.Length > 8 ? companyName[4..8] : companyName[4..])
                    : companyName;
                text.AlignCenter();
                text.Span(displayName).FontSize(9).FontColor(Colors.Red.Medium).Bold();
            });
    }

    /// <summary>
    /// 인장 그림을 읽는다. 🔴 지금은 보관소가 없어 <c>seal_url</c> 이 늘 비어 있다(설계 §8-2)
    /// ⇒ 합격선 ④ 는 이번 차수 <b>미달</b>이다. 보관소가 생기는 날 <b>코드 수정 0</b> 으로 켜진다.
    /// 🔴 이 함수는 <b>읽기만</b> 한다 — 파일 보관소·업로드 통로를 만들지 않는다(범위 밖).
    /// </summary>
    private byte[]? TryLoadSealImage(string? sealUrl)
    {
        if (string.IsNullOrWhiteSpace(sealUrl)) return null;

        try
        {
            if (File.Exists(sealUrl)) return File.ReadAllBytes(sealUrl);
            _logger.LogWarning("[PDF] 인장 파일이 없다 — 글자 도장으로 그린다.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] 인장 읽기 실패 — 글자 도장으로 그린다.");
            return null;
        }
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

        // 🔴 20261021작 절 D — 넘침 안내 한 줄.
        //   🔴 자르는 동작(limit)·max_rows·row_pitch 해석은 **한 글자도 안 바꿨다.**
        //   쪽 이어찍기는 이번 범위 밖(결재 Q-4)이라, 조용히 사라지던 것을 **말해 주기만** 한다.
        //   조용히 사라지는 것은 세금 사고다 — 적어도 고객이 알아야 한다.
        //   🔴 20261021작 §9-2 봉합(R-2) — **세금계산서 경로에서만** 나온다.
        //   반려 사유: 이 메서드는 10종이 **같이 타는** 길이라, 거래명세서·견적서 등에서도
        //   알림이 찍혔다 ⇒ 결재 Q-4 의 집행 조건 「나머지 9종 종이 무변화」가 깨졌다.
        //   🔴 자르는 동작·지우는 코드는 여전히 0건이다 — 나오는 **자리**만 좁힌다.
        var rowLimit = maxRows > 0 ? (int)maxRows : d.Lines.Count;
        var printedRows = Math.Min(d.Lines.Count, rowLimit);
        var overflowed = d.Lines.Count > printedRows
            && string.Equals(d.DocType, TaxInvoiceDocType, StringComparison.Ordinal);

        // 안내를 붙일 자리 = 품목 줄 중 가장 왼쪽 칸의 좌표. 그 아래 한 줄에 적는다.
        var noticeAnchor = overflowed
            ? coords.Where(c => c.Key.StartsWith("line.", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.XMm)
                    .FirstOrDefault()
            : null;

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

            // 🔴 넘친 줄이 있으면 마지막 줄 아래 한 줄로 알린다 — **더하기만**.
            if (noticeAnchor is not null)
            {
                var notice = new FieldCoord
                {
                    Key = "overflow_notice",
                    XMm = noticeAnchor.XMm,
                    FontPt = 8f,
                    WidthMm = 150f
                };
                PlaceText(layers, notice, noticeAnchor.YMm + pitch * printedRows,
                    $"품목 {d.Lines.Count}줄 중 {printedRows}줄만 인쇄되었습니다. 양식용지 줄 수 설정을 확인하세요.");
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
                // 🔴 20261021작 절 B-1 — 종전에는 이 값을 읽지 않아 고객이 「인쇄 매수」를
                //   무엇으로 두든 **같은 종이**가 나왔다. 세금계산서는 법정 2매다.
                PrintCopyMode = dto.PrintCopyMode,
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

        /// <summary>인쇄 매수 — both·recipient·supplier (DB-90). 세금계산서 전용 작성기가 읽는다.</summary>
        public string PrintCopyMode { get; set; } = "recipient";

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
