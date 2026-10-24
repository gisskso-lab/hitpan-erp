using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.Interfaces;
using HitPan.Application.DTOs.Documents;
using HitPan.Application.DTOs.Settings;
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

    public Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType, string documentId, CancellationToken ct = default)
        => RenderDocumentAsync(tenantId, documentType, documentId, null, ct);

    /// <summary>
    /// 🔴 20261024 — 미리보기 주입 통로 (사장님 결재 2026-10-21 ㉯ · 오더 2026-10-24).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>조건 1</b> — <paramref name="overrides"/> 가 null 이면 위 4인자 호출과
    ///   <b>한 글자도 다르지 않게</b> 동작한다. 기존 호출부는 안 고쳤다(#1).
    /// 🔴 <b>두 벌 금지</b> — 미리보기 전용 작성기를 따로 만들지 않았다. 같은 통로를 타고
    ///   다른 것은 <b>먹이는 자료</b>뿐이다(20261021작 §11-3).
    /// </remarks>
    public async Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType,
        string documentId, PdfPreviewOverrides? overrides, CancellationToken ct = default)
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

        // 🔴 §11-2 ② — 양식정보설정은 6단계 중 1단계인데 전표는 3~4단계다.
        //   신규 고객이 자를 대고 mm 를 맞추는 그 시점에 전표가 **0건**이라 아무것도 못 봤다.
        var data = overrides?.UseSampleData == true
            ? BuildSampleSnapshot(documentType, overrides)
            : await LoadDocumentAsync(tenantId, documentType, documentId, ct).ConfigureAwait(false);

        // 🔴 3차검증 R-11 봉합 — **DB 에 없는 전표면 여기서 끝낸다.**
        //   저장 안 한 전표의 id 는 화면이 만든 GUID 라 DB 에 없고, 종전에는
        //   빈 스냅샷이 그대로 그려져 **백지**가 떴다. 미리보기 문만 이 값을 세운다.
        if (ShouldRejectMissing(overrides, data.Found))
        {
            throw new KeyNotFoundException(
                "저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오.");
        }

        var company = await LoadCompanyAsync(tenantId, ct).ConfigureAwait(false);

        // 작지② 양식 분기 (사장님 작업지시 2026-05-31)
        // form_templates에서 paper_mode + 여백 + 토글 로드 → plain·preprint 분기
        // 🔴 사장님 §11 *"실제 출력물 양식을 편집하는 대로 보여주는 게 중요함"* —
        //   저장 전 값이 오면 DB 행 **대신** 그것으로 그린다.
        //   🔴 음성 대조군(결재 ㉯ 조건 3): 이 세 줄을 빼면 미리보기가 저장값으로 떨어지고
        //      「편집하는 대로」가 **실패해야** 한다. 조용히 통과하면 게이트가 거짓이다.
        var template = overrides?.Template is not null
            ? ToTemplateInfo(overrides.Template)
            : await TryLoadTemplateAsync(tenantId, documentType, ct).ConfigureAwait(false);

        // 🔴 전표 인쇄 미리보기 옵션 (사장님 결재 2026-10-24 결-2 · 7종 공통).
        //   저장된 양식을 **그대로 쓰고** 그 위에 **이번 한 번만** 바꿀 값을 얹는다.
        //   🔴 저장하지 않는다 — 고객이 미리보기에서 만진 값이 양식으로 굳으면 안 된다.
        //   🔴 옵션이 null 이면 아무것도 안 바뀐다 ⇒ **지금 인쇄와 같은 종이**(#1).
        template = ApplyPrintOptions(template, overrides?.Options);
        var isPreprint = template?.PaperMode == "preprint";
        // 🔴 봉합 2026-08-12 (사장님 지시 "양식정보 설정의 양식용지만 해")
        //   고객이 맞춘 좌표가 있으면 그대로 찍는다. 종전에는 이 값을 읽지도 않아
        //   코드 고정 위치로만 나갔고, 시판 양식지 칸과 안 맞았다.
        var useCoords = isPreprint && (template?.FieldCoords.Count ?? 0) > 0;
        // 🔴 양식지 그림 (§13). 읽기만 한다 — 보관소·업로드 통로는 만들지 않는다(범위 밖).
        // 🔴🔴 [2026-10-24 반려 R-1 봉합] 양식지 그림은 **미리보기에만** 깐다.
        //   [내가 뭘 틀렸나] 결재된 §13-8 은 「종이에는 **안 찍히고**(이미 인쇄돼 있다)
        //     → 미리보기 배경으로 깔려 그 위에 필드 위치를 mm 로 맞춘다」 이고,
        //     §13-10 은 순백지에 업체 그림 찍기를 **「등재만 · 이번에 안 한다」** 로 못박았다.
        //     그런데 나는 **공용 인쇄 흐름**에 깔아 정반대를 넣었다(검증팀 반려 R-1).
        //   [그대로 두면] 양식용지 고객은 **이미 인쇄된 종이 위에 그림이 또 찍혀**
        //     이중 인쇄가 되고(§13-5 합격선 1 위반), 순백지 9종에도 업체 그림이 찍힌다.
        //   [그래서] 미리보기 경로에서만 그린다. 실인쇄 PDF 에는 **0건** —
        //     이메일·팩스·전표 인쇄는 전부 종전과 똑같다(#1).
        var bgImage = ResolveFormBackground(overrides, template);
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
            // 🔴 20261021작 §10-2 ⓑ — 「한 장에 모으기」는 **고객이 켠 경우에만**.
            //   양식 행이 없으면 끔으로 본다(DB-147 기본값과 같다).
            var showTearLine = template?.ShowTearLine ?? false;
            // 🔴 20261024 사장님 결재 — 이 양식에서 직인을 찍나.
            //   🔴 **작성기 서명은 건드리지 않는다.** 처음에 인자를 하나 더했더니
            //     기존 게이트 136건이 통째로 깨졌다 — 그 시험들은 반사(reflection)로
            //     **인자 8개를 정확히** 넘긴다. 기본값이 있어도 Invoke 는 개수를 맞춘다.
            //     ⇒ 뜻은 같고 서명은 그대로인 길로 간다: **직인 주소를 비운다.**
            //   🔴 비우는 것이 왜 안전한가 — company 는 이 인쇄 한 번을 위해 DB 에서
            //     갓 읽은 객체다(LoadCompanyAsync · 캐시 0건). 고객 DB 값은 안 바뀐다.
            //   🔴 **배선만이다.** 직인 그림 보관소가 없어 seal_url 이 늘 비어 있다
            //     ⇒ 지금은 켜도 찍히지 않는다. 보관소가 생기는 날 이 칸이 켬/끔을 가른다.
            //   🔴 로고·테두리는 전달하지 않는다 — 세금계산서 머리·격자는 **법정 서식**이고
            //     끄면 서식이 깨진다(D-5 · 법무팀장 결재가 먼저다).
            if (template is not null && !template.ShowCompanySeal)
            {
                company.SealUrl = null;
            }

            var taxBytes = RenderTaxInvoiceDocument(data, company, copyMode, showTearLine,
                marginTop, marginLeft, marginRight, marginBottom);
            return (taxBytes, $"{KoLabel(documentType)}_{data.DocNo}.pdf");
        }

        var bytes = Document.Create(c =>
        {
            c.Page(page =>
            {
                // 🔴 20261024 사장님 결재 — 양식의 **용지 크기·방향**을 종이에 닿게 한다.
                //   종전에는 `PageSizes.A4` 가 못으로 박혀 있어 고객이 A5·가로를 골라도
                //   종이가 안 바뀌었다(저장은 되는데 아무 일도 안 일어나는 칸이었다).
                //   🔴 모르는 값이면 **A4·세로로 떨어진다** — 고객 종이가 안 나가는 일은 없다(#20).
                page.Size(ResolvePageSize(template, documentType, useCoords));
                // 🔴 스타일 칸을 **읽는다** — 지금은 늘 basic 이다(배선만 · 4종 디자인 미확정).
                //   종전에는 저장·조회만 되고 작성기가 이 칸을 아예 몰랐다. 모르는 값이 오면
                //   ResolveStyleKey 가 적어 둔다 — 조용히 넘어가지 않는다.
                _ = ResolveStyleKey(template);
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

                // 🔴 20261024 사장님 오더 — 업체 양식지 그림을 종이에 깐다 (§13).
                //   사장님: *"양식지를 추가할수도 있어야해. 이미 업체에서 쓰는 양식지가 있을수도 있으니."*
                //   🔴 종전에는 BackgroundImageUrl 이 작성기에 **0건**이었다 — 올려도 안 나왔다.
                //   🔴 그림은 **맨 밑에** 깔고 내용은 그 위에 그린다. 스캔한 양식지 칸에 값이
                //      들어가는 모양을 눈으로 맞추는 것이 미리보기의 존재 이유다.
                //   🔴 그림이 없거나 못 읽으면 **종전과 똑같이** 그린다(#1 — 고객 종이 안 바뀜).
                //   🔴 이 자리는 **공용 흐름**이다 ⇒ 양식용지(preprint) 경로의 10종 전부에 닿는다.
                //      세금계산서도 preprint 면 이 흐름으로 온다(위 전용 분기는 !isPreprint).
                if (bgImage is not null)
                {
                    page.Background().Image(bgImage).FitArea();
                }

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

    /// <summary>
    /// 🔴 <b>이 미리보기를 막아야 하나</b> — 3차검증 R-11 의 판정 한 자리.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 함수로 떼어 놨나</b> — 이 판정은 <b>DB 없이도 재야 한다.</b> 안에 박아 두면
    ///   「DB 가 붙은 환경에서만 잴 수 있다」가 되고, 그러면 게이트가 또 비어 있게 된다.
    ///   그래서 <b>인자 두 개로 떼어</b> 네 경우를 표로 잰다.
    /// <para>
    /// 🟢 <c>RequireExisting=false</c>(「인쇄」·「이메일」·「팩스」) → <b>절대 막지 않는다.</b>
    ///    종전처럼 안내문 종이가 나간다. 여기서 막으면 전표가 안 나가 P0 다(#20).
    /// 🟢 <c>RequireExisting=true</c> + 못 찾음 → <b>막는다.</b> 백지를 보여주지 않는다.
    /// 🟢 예시 자료(<c>UseSampleData</c>)는 애초에 DB 를 안 보므로 <b>항상 통과</b>다 —
    ///    양식정보설정 미리보기가 이 문에 걸리면 안 된다.
    /// </para>
    /// </remarks>
    internal static bool ShouldRejectMissing(PdfPreviewOverrides? overrides, bool found)
    {
        if (overrides?.RequireExisting != true) return false;
        if (overrides.UseSampleData) return false;
        return !found;
    }
    private async Task<DocumentSnapshot> LoadDocumentAsync(string tenantId, string docType, string docId, CancellationToken ct)
    {
        // 🔴 문서별 SQL 이 다르다. 6종 → **8종**(결재 ㉮ — 매입·매출반품 로더 추가 20261024작).
        //   여기 없는 종은 아래 fallback 이 **빈 종이**를 돌려준다. 그래서 화면에
        //   「인쇄」 단추를 달 때는 **이 표에 있는지 먼저 본다** — 없으면 고객이 백지를 받는다.
        return docType switch
        {
            "quotation" => await LoadQuotationAsync(tenantId, docId, ct).ConfigureAwait(false),
            "sales_order" => await LoadSalesOrderAsync(tenantId, docId, ct).ConfigureAwait(false),
            "delivery" => await LoadDeliveryAsync(tenantId, docId, ct).ConfigureAwait(false),
            "tax_invoice" => await LoadTaxInvoiceAsync(tenantId, docId, ct).ConfigureAwait(false),
            "purchase_order" => await LoadPurchaseOrderAsync(tenantId, docId, ct).ConfigureAwait(false),
            "purchase_receipt" => await LoadPurchaseReceiptAsync(tenantId, docId, ct).ConfigureAwait(false),
            "purchase_return" => await LoadPurchaseReturnAsync(tenantId, docId, ct).ConfigureAwait(false),
            "sales_return" => await LoadSalesReturnAsync(tenantId, docId, ct).ConfigureAwait(false),
            // 🔴 로더가 없는 종 — 이 종이는 **백지**다. Found = false 로 올려 미리보기가 막는다(R-11).
            _ => new DocumentSnapshot { DocType = docType, DocNo = docId, Found = false }
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

    /// <summary>
    /// 🔴 매입반품 자료 로더 — 결재 ㉮(20261021작 §19-1 · 백지 4종을 로더까지 이번 차수).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 이번에 붙이나</b> — 10/24 차수가 <b>반품처리 화면에 「인쇄」 단추를 달았다.</b>
    ///   로더가 없으면 그 단추가 <b>빈 종이</b>를 띄운다(미리보기가 곧 인쇄물이므로 종이도 백지).
    ///   「없는 종」이 아니라 <b>고객이 누를 수 있는 단추</b>가 되는 순간 백지는 사고다.
    /// 🔴 칸 이름은 출하 DDL <c>installer/hitpan_db_clean.sql</c> 을 보고 썼다(#13·#36).
    ///   <c>purchase_returns.total_amount</c> 는 <b>공급가</b>다 — 다른 5종과 같은 규약.
    ///   품목 이름 칸이 없어 <c>items</c> 를 JOIN 한다(나머지 로더와 동일).
    /// </remarks>
    private async Task<DocumentSnapshot> LoadPurchaseReturnAsync(string tenantId, string id, CancellationToken ct)
    {
        const string head = """
            SELECT pr.return_no AS DocNo, pr.return_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(pr.total_amount,0) AS TotalSupply, COALESCE(pr.vat_amount,0) AS TotalVat,
                   COALESCE(pr.total_amount,0) + COALESCE(pr.vat_amount,0) AS TotalAmount,
                   pr.memo AS Remark
            FROM purchase_returns pr LEFT JOIN partners p ON p.partner_id = pr.partner_id AND p.tenant_id = pr.tenant_id
            WHERE pr.tenant_id = @TenantId AND pr.return_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(pri.qty,0) AS Qty, COALESCE(pri.unit_price,0) AS UnitPrice,
                   COALESCE(pri.supply_amount,0) AS Supply, COALESCE(pri.vat_amount,0) AS Vat,
                   COALESCE(pri.supply_amount,0) + COALESCE(pri.vat_amount,0) AS Total
            FROM purchase_return_items pri LEFT JOIN items i ON i.item_id = pri.item_id
            WHERE pri.tenant_id = @TenantId AND pri.return_id = @Id ORDER BY pri.return_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "purchase_return", head, lines, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 🔴 매출반품 자료 로더 — 결재 ㉮. 사장님 반려 ③ 의 「반품처리」는 매입·매출 <b>둘 다</b>다.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>sales_return_items.unit_price</c> 는 <b>반품 당시 환불 단가</b>이고
    ///   <c>original_unit_price</c> 는 원거래 단가 복사본이다(DDL 주석). 종이에는
    ///   <b>환불 단가</b>를 찍는다 — 고객에게 돌려주는 돈이 그것이다.
    /// 🔴 <c>is_loss</c>(파손 로스)는 재고 반영 축이라 종이에 안 찍는다. 등재만.
    /// </remarks>
    private async Task<DocumentSnapshot> LoadSalesReturnAsync(string tenantId, string id, CancellationToken ct)
    {
        const string head = """
            SELECT sr.return_no AS DocNo, sr.return_date AS DocDate,
                   p.partner_name AS PartnerName, p.biz_no AS PartnerBusinessNo,
                   p.address AS PartnerAddress, p.manager_name AS PartnerContact,
                   COALESCE(sr.total_amount,0) AS TotalSupply, COALESCE(sr.vat_amount,0) AS TotalVat,
                   COALESCE(sr.total_amount,0) + COALESCE(sr.vat_amount,0) AS TotalAmount,
                   sr.memo AS Remark
            FROM sales_returns sr LEFT JOIN partners p ON p.partner_id = sr.partner_id AND p.tenant_id = sr.tenant_id
            WHERE sr.tenant_id = @TenantId AND sr.return_id = @Id
            """;
        const string lines = """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   COALESCE(sri.qty,0) AS Qty, COALESCE(sri.unit_price,0) AS UnitPrice,
                   COALESCE(sri.supply_amount,0) AS Supply, COALESCE(sri.vat_amount,0) AS Vat,
                   COALESCE(sri.supply_amount,0) + COALESCE(sri.vat_amount,0) AS Total
            FROM sales_return_items sri LEFT JOIN items i ON i.item_id = sri.item_id
            WHERE sri.tenant_id = @TenantId AND sri.return_id = @Id ORDER BY sri.return_item_id
            """;
        return await LoadByQueriesAsync(tenantId, id, "sales_return", head, lines, ct).ConfigureAwait(false);
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
            // 🔴 Found = false — 미리보기는 이걸 보고 **백지를 띄우지 않는다**(3차검증 R-11).
            //   「인쇄」·「이메일」의 안내문 종이 동작은 **한 글자도 안 바뀐다**(#1).
            return new DocumentSnapshot { DocType = docType, DocNo = id, DocDate = DateTime.Today, PartnerName = "(원본 문서를 찾을 수 없습니다)", Found = false };
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

        /// <summary>
        /// 🔴 <b>DB 에서 실제로 찾았나</b> — 못 찾았으면 <c>false</c>(2026-10-24 3차검증 R-11).
        /// </summary>
        /// <remarks>
        /// 🔴 <b>왜 칸을 더하나</b> — 못 찾으면 종전에도 <b>빈 스냅샷</b>을 돌려줬다.
        ///   그 종이는 「인쇄」·「이메일」 쪽에서는 <b>안내문 종이</b>로 쓰여 왔으므로 그대로 둔다(#1).
        ///   그런데 <b>미리보기</b>에서는 그게 <b>백지</b>로 보인다. 저장 안 한 전표의
        ///   <c>_draft.Id</c> 는 화면이 만든 GUID 라 DB 에 없고, 창문은 「비었나」만 봤다
        ///   ⇒ GUID 는 통과하고 <b>백지가 떴다</b>. 「찾았나」를 <b>글자로 추측하지 않고</b>
        ///   이 칸으로 들고 올라간다.
        /// </remarks>
        public bool Found { get; set; } = true;
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
        bool showTearLine,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        // 🔴🔴 20261021작 §17 T-7 / §20 — 지원 범위를 넘는 여백을 먼저 조인다.
        //   까닭·실측표는 TaxInvoiceMaxMarginMm 주석에 있다. 요지: 0~39mm 는 전건 정상,
        //   첫 고장은 40mm(3모드 전부 `[33,0,7]`), 80mm 이상은 종이가 아예 안 나온다.
        //   🔴 §20: 「40mm 이상에서 깨진다」는 맞았고, 상한 30mm 는 과했다(1mm 실측 = 39mm 까지 정상).
        //   🔴 고객이 저장한 값은 안 건드린다 — 여기서만 조인다.
        marginTop = ClampTaxInvoiceMargin(marginTop, "상", d.DocNo);
        marginLeft = ClampTaxInvoiceMargin(marginLeft, "좌", d.DocNo);
        marginRight = ClampTaxInvoiceMargin(marginRight, "우", d.DocNo);
        marginBottom = ClampTaxInvoiceMargin(marginBottom, "하", d.DocNo);

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
        //
        // 🔴 20261021작 §10-2 재결재 — 위 §9-1 의 「재서 저절로 정한다」를 사장님이
        //   **고객 설정**으로 바꾸셨다. 한 벌을 11mm 줄이고 나니 두 길이 다 열렸다.
        //     · 켬(ShowTearLine=1) → 한 장에 두 벌 + 자르는 선.
        //         품목이 다 안 들어가면 **마지막 칸에 「외 N건」**(§10-2 ⓓ · 국세청 서식 관행).
        //     · 끔(기본)          → 벌마다 제 쪽에서 시작 · **품목 전부**(한 줄도 안 사라진다).
        //   🔴 재는 코드(MeasureBothCopiesFitOnePage)는 그대로 쓴다 — 줄 수로 눈대중하지 않는다.
        //
        var bothCopies = drawSupplier && drawRecipient;
        var visibleLines = d.Lines.Count;   // 기본은 전부 찍는다.
        var useTearLine = false;

        if (bothCopies && showTearLine)
        {
            var fit = ResolveVisibleLineCount(
                d, c, sealImage, marginTop, marginLeft, marginRight, marginBottom);
            if (fit >= 0)
            {
                useTearLine = true;
                visibleLines = fit;
            }
            else
            {
                // 🔴 품목을 한 줄도 못 넣을 만큼 여백이 크면 **한 장에 모으지 않는다.**
                //   「외 N건」만 덩그러니 남은 계산서를 내느니 쪽을 나누는 편이 낫다.
                _logger.LogWarning(
                    "세금계산서를 한 장에 모으지 못했다 — 쪽을 나눠 품목을 전부 낸다. " +
                    "여백이 너무 크지 않은지 양식정보설정을 확인하시면 됩니다. 문서번호 {DocNo}", d.DocNo);
            }
        }

        // 🔴 안 찍히는 줄 수 — 「외 N건」의 N. 0 이면 「외」 칸 자체가 안 나온다.
        //   게이트가 이 숫자를 그대로 받아 **종이에 안 찍힌 줄 수와 같은지** 검산한다.
        var hiddenLines = Math.Max(0, d.Lines.Count - visibleLines);
        _logger.LogDebug(
            "세금계산서 품목 담기 — 전체 {Total}줄 중 {Visible}줄 찍고 {Hidden}줄은 「외 N건」 " +
            "(한장모으기 {Tear}) 문서번호 {DocNo}",
            d.Lines.Count, visibleLines, hiddenLines, useTearLine, d.DocNo);

        // 🔴🔴 20261021작 §15-6 T-2 — 자르는 선을 안 쓰는 모든 길(끔 · 단일 벌 ·
        //   켰지만 재서 안 들어간 경우)은 벌마다 **이어쓰기**로 그린다.
        //   장마다 머리를 다시 붙이고 꼬리는 마지막 장에만 붙인다
        //   ⇒ 🔴 품목 0줄짜리 꼬리 종이 0건 · 벌 머리 없는 쪽 0건([4] 반려 사유).
        //   🔴 품목은 한 줄도 안 사라진다 — 이 길에 「외 N건」은 없다(§15-5).
        //   🔴 두 벌은 자료가 같으므로 **한 번만 재서** 양쪽에 같은 쪽 나눔을 쓴다.
        //     벌마다 따로 재면 두 벌의 쪽 수가 어긋나 고객이 비교를 못 한다.
        //     재는 라벨은 둘 중 **긴 쪽**(공급받는 자 보관용)으로 잡아 안전하게 본다.
        List<(int Skip, int Take, bool IsLast)>? copyChunks = null;
        if (!useTearLine && !d.IsCanceledInvoice)
        {
            copyChunks = ResolveCopyPageChunks(
                d, c, sealImage, "공급받는 자 보관용",
                marginTop, marginLeft, marginRight, marginBottom);
            _logger.LogDebug(
                "세금계산서 이어쓰기 — 벌 하나를 {Pages}장에 나눠 담는다(품목 {Total}줄) 문서번호 {DocNo}",
                copyChunks?.Count ?? 0, d.Lines.Count, d.DocNo);
        }

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

                    // 🔴 §15-6 T-2 — 이어쓰기 길. 쪽 나눔을 잰 경우에만 탄다.
                    if (copyChunks is not null)
                    {
                        var labels = new List<string>();
                        if (drawSupplier) labels.Add("공급자 보관용");
                        if (drawRecipient) labels.Add("공급받는 자 보관용");

                        var printed = 0;
                        foreach (var label in labels)
                        {
                            foreach (var chunk in copyChunks)
                            {
                                // 첫 장 앞에는 쪽 나눔을 넣지 않는다 — 빈 종이가 한 장 는다.
                                if (printed > 0) col.Item().PageBreak();

                                var copyLabel = label;
                                var part = chunk;
                                col.Item().Element(e => ComposeTaxInvoiceCopyPage(
                                    e, d, c, copyLabel, sealImage, part.Skip, part.Take, part.IsLast));
                                printed++;
                            }
                        }
                        return;
                    }

                    if (drawSupplier)
                    {
                        col.Item().Element(e => ComposeTaxInvoiceCopy(
                            e, d, c, "공급자 보관용", sealImage, visibleLines));
                    }
                    if (bothCopies)
                    {
                        // 🔴 고객이 켠 경우에만 자르는 선. 끄면 자르는 선 0건 + 쪽을 나눈다.
                        if (useTearLine) col.Item().Element(ComposeTearLine);
                        else col.Item().PageBreak();
                    }
                    if (drawRecipient)
                    {
                        col.Item().Element(e => ComposeTaxInvoiceCopy(
                            e, d, c, "공급받는 자 보관용", sealImage, visibleLines));
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
        e.PaddingVertical(4).Column(col =>
        {
            col.Item().LineHorizontal(0.8f).LineColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(2).AlignCenter().Text("절취선").FontSize(8);
        });
    }

    /// <summary>
    /// 🔴 20261021작 §10-4 S-3 — 한 장에 모을 때 <b>품목을 몇 줄까지 찍을 수 있나</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 전부 들어가면 전부 찍는다(이때 「외 N건」 칸은 아예 안 나온다). 안 들어가면
    /// <b>「외 N건」 칸 한 줄까지 포함해서</b> 들어가는 가장 큰 줄 수를 찾는다 —
    /// 그래야 그 칸 때문에 또 넘치는 일이 없다.
    /// </para>
    /// <para>
    /// 🔴 줄 수로 눈대중하지 않는다. 후보마다 <b>실제로 깔아 보고 높이를 받는다</b>.
    /// 들어가는지 여부는 줄 수에 대해 단조라(줄이 늘면 높이도 는다) 반으로 갈라 찾는다.
    /// </para>
    /// <para>🔴 한 줄도 못 넣으면 <b>-1</b> — 부르는 쪽이 쪽을 나눈다.</para>
    /// </remarks>
    private int ResolveVisibleLineCount(DocumentSnapshot d, CompanyInfo c, byte[]? sealImage,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        var total = d.Lines.Count;
        if (MeasureBothCopiesFitOnePage(
                d, c, sealImage, marginTop, marginLeft, marginRight, marginBottom, total))
        {
            return total;
        }

        var best = -1;
        var lo = 0;
        var hi = total - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (MeasureBothCopiesFitOnePage(
                    d, c, sealImage, marginTop, marginLeft, marginRight, marginBottom, mid))
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
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
    /// <param name="maxLines">
    /// 한 벌에 찍을 품목 줄 수 상한. 전체보다 작으면 <b>「외 N건」 칸까지 포함해</b> 잰다.
    /// 안 주면 전부 찍는 것으로 본다(§9-1 때와 같은 뜻).
    /// </param>
    private bool MeasureBothCopiesFitOnePage(DocumentSnapshot d, CompanyInfo c, byte[]? sealImage,
        float marginTop, float marginLeft, float marginRight, float marginBottom,
        int maxLines = int.MaxValue)
    {
        var probe = new TaxInvoiceFitProbe(d, c, sealImage, maxLines);
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
    /// 🔴 쪽이 딱 맞는 자리에서 반올림 때문에 한 줄이 다음 장으로 밀리는 것을 막는 여유(pt).
    /// 밀리면 <b>품목 0줄짜리 꼬리 종이</b>가 다시 난다 — T-2 가 없애려는 바로 그 모양이다.
    /// </summary>
    private const float TaxInvoicePageSlackPt = 2f;

    /// <summary>
    /// 🔴🔴 20261021작 §17 T-7 / §20 — 세금계산서 이어쓰기가 <b>실측으로 버티는 여백 상한(mm)</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>실측 — 여백 24~63mm 를 1mm 간격으로, 모드 셋(<c>both</c>·<c>recipient</c>·<c>supplier</c>) ×
    /// 품목 40·80·100줄.</b>
    /// <list type="bullet">
    /// <item>🟢 <b>0~39mm</b> — 전건 정상. 머리 없는 쪽 0 · 품목 0줄 쪽 0 · 품목 합계 전건 일치.</item>
    /// <item>🔴 <b>첫 고장은 40mm</b> — <b>세 모드 전부</b> <c>[33,0,7]</c>(가운데 쪽이 머리도 품목도 없다).
    ///   품목 합계는 맞다 ⇒ <b>줄이 사라지는 것이 아니라 빈 쪽이 끼는</b> 고장이다.
    ///   재는 쪽은 「33줄 들어간다」는데 깔면 넘쳐 다음 장으로 밀린다.</item>
    /// <item>🔴 <b>80mm 이상</b> — <c>DocumentLayoutException</c> ⇒ <b>종이가 아예 안 나온다</b>.</item>
    /// <item>🔴 <b>100mm 이상</b> — 이어쓰기 자체가 폴백(<c>chunks == null</c>)으로 떨어진다.</item>
    /// </list>
    /// <para>
    /// 🔴 <b>상한을 39 가 아니라 35 로 두는 사유</b>: 고장의 뿌리는 <b>재는 쪽과 깔리는 쪽의 어긋남</b>이고,
    /// 그 어긋남은 <b>내용 높이에 따라</b> 움직인다. 위 실측은 <b>한 가지 자료 모양</b>으로만 쟀으므로
    /// 경계값(39)에 딱 붙이지 않고 <b>4mm 여유</b>를 둔다. 🔴 이 숫자를 바꿀 일이 생기면
    /// <b>눈대중으로 바꾸지 말고 1mm 실측을 다시 돌린다.</b>
    /// </para>
    /// <para>
    /// 🔴🔴 <b>§20 — PM 이 이 자리에서 두 번 틀렸다.</b> ① 첫 봉합은 상한을 <b>30mm</b> 로 뒀다.
    /// 「40mm 이상에서 깨진다」는 맞았지만 <b>31~39mm 는 고장이 없던 구간</b>이라 그 고객의 종이를
    /// 바꿀 근거가 없었다. ② [4] 3차가 「31~50mm 전건 정상 · 첫 고장 60mm」라 보고해 상한을 50 으로
    /// 올렸더니 <b>40·50mm 가 전부 빨개졌다</b> ⇒ 그 보고도 틀렸다. 🟢 <b>둘 다 1mm 실측으로 갈랐다.</b>
    /// </para>
    /// <para>
    /// 🔴 왜 고객 값을 조이기까지 하나 — 편집창 여백 4칸에 상·하한이 <b>없었고</b> DTO 가
    /// <c>Range(0, 100)</c> 을 받았다. 화면·DTO 만 조이면 <b>이미 큰 값으로 저장한 고객의 종이는
    /// 계속 깨진다</b>(고객이 손을 대야 풀린다). 그래서 그리는 쪽에서도 조인다.
    /// 🔴 <b>고객이 저장한 값은 지우지 않는다</b> — 그릴 때만 조인다. 35mm 이하면 아무 일도 안 한다.
    /// </para>
    /// </remarks>
    internal const float TaxInvoiceMaxMarginMm = 35f;

    /// <summary>🔴 §17 T-7 — 지원 범위를 넘는 여백을 조인다. 조였으면 글로 남긴다.</summary>
    private float ClampTaxInvoiceMargin(float mm, string which, string? docNo)
    {
        if (mm >= 0f && mm <= TaxInvoiceMaxMarginMm)
        {
            return mm;
        }

        var clamped = mm < 0f ? 0f : TaxInvoiceMaxMarginMm;
        _logger.LogWarning(
            "세금계산서 여백이 지원 범위를 벗어났다 — {Which} {Given}mm 를 {Clamped}mm 로 보고 그린다. " +
            "양식정보설정에서 여백을 0~{Max}mm 로 맞추시면 됩니다. 문서번호 {DocNo}",
            which, mm, clamped, TaxInvoiceMaxMarginMm, docNo);
        return clamped;
    }

    /// <summary>
    /// 🔴🔴 20261021작 §15-6 T-2 — <b>벌 하나를 몇 장에 어떻게 나눠 담나</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 앞에서부터 한 장씩 채운다. 장마다 먼저 <b>「남은 줄을 꼬리까지 붙여 이 한 장에 담을 수
    /// 있나」</b>를 재고, 담을 수 있으면 그 장을 마지막 장으로 삼아 끝낸다. 못 담으면
    /// <b>꼬리 없이</b> 담을 수 있는 가장 큰 줄 수를 찾아 그만큼만 담고 다음 장으로 넘긴다.
    /// </para>
    /// <para>
    /// 🔴 <b>담을 줄 수는 늘 1 이상</b>이다 — 이어 쓰는 장의 상한을 <c>남은 줄 - 1</c> 로 두어
    /// 마지막 장에도 품목이 반드시 한 줄 남게 한다. ⇒ <b>품목 0줄짜리 쪽이 날 수 없다.</b>
    /// </para>
    /// <para>
    /// 🔴 줄 수로 눈대중하지 않는다 — 후보마다 <b>실제로 깔아 보고 높이를 받는다</b>
    /// (<c>MeasureCopyPageFits</c>). 들어가는지는 줄 수에 대해 단조라 반으로 갈라 찾는다.
    /// </para>
    /// <para>
    /// 🔴 한 줄도 못 담거나 품목이 없으면 <b><c>null</c></b> — 부르는 쪽이 종전 길로 간다.
    /// </para>
    /// </remarks>
    /// <returns>장마다 (건너뛸 줄 수 · 담을 줄 수 · 마지막 장이냐). 못 재면 <c>null</c>.</returns>
    private List<(int Skip, int Take, bool IsLast)>? ResolveCopyPageChunks(
        DocumentSnapshot d, CompanyInfo c, byte[]? sealImage, string copyLabel,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        var total = d.Lines.Count;
        if (total <= 0)
        {
            return null;   // 품목이 없으면 나눌 것도 없다 — 종전 길 그대로.
        }

        var chunks = new List<(int Skip, int Take, bool IsLast)>();
        var skip = 0;
        while (skip < total)
        {
            var remaining = total - skip;

            // ① 남은 줄을 꼬리까지 붙여 한 장에 담을 수 있나 → 되면 여기서 끝난다.
            if (MeasureCopyPageFits(d, c, sealImage, copyLabel, skip, remaining, true,
                    marginTop, marginLeft, marginRight, marginBottom))
            {
                chunks.Add((skip, remaining, true));
                return chunks;
            }

            // ② 못 담는다 → 꼬리 없는 「이어 쓰는 장」으로 담을 수 있는 가장 큰 줄 수.
            //    상한을 remaining - 1 로 둔다 ⇒ 마지막 장에 품목이 반드시 남는다.
            var take = ResolveMaxLinesOnOnePage(d, c, sealImage, copyLabel, skip, remaining - 1,
                marginTop, marginLeft, marginRight, marginBottom);
            if (take <= 0)
            {
                // 🔴 한 줄도 못 담는다 = 여백이 종이를 거의 다 먹었다. 억지로 나누지 않고
                //   종전 길로 돌려보낸다 — 고객이 여백을 줄이면 바로 풀린다.
                _logger.LogWarning(
                    "세금계산서를 이어쓰기로 나누지 못했다 — 한 장에 품목이 한 줄도 안 들어간다. " +
                    "여백이 너무 크지 않은지 양식정보설정을 확인하시면 됩니다. 문서번호 {DocNo}", d.DocNo);
                return null;
            }

            chunks.Add((skip, take, false));
            skip += take;
        }

        return chunks.Count > 0 ? chunks : null;
    }

    /// <summary>
    /// 🔴 §15-6 T-2 — 이어 쓰는 장 하나에 <b>품목을 몇 줄까지</b> 담을 수 있나(꼬리 없음).
    /// </summary>
    /// <param name="maxTake">찾을 줄 수의 상한. 0 이하이면 찾을 것이 없다.</param>
    /// <returns>담을 수 있는 가장 큰 줄 수. 한 줄도 못 담으면 <b>0</b>.</returns>
    private int ResolveMaxLinesOnOnePage(DocumentSnapshot d, CompanyInfo c, byte[]? sealImage,
        string copyLabel, int skip, int maxTake,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        var best = 0;
        var lo = 1;
        var hi = maxTake;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (MeasureCopyPageFits(d, c, sealImage, copyLabel, skip, mid, false,
                    marginTop, marginLeft, marginRight, marginBottom))
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }

    /// <summary>
    /// 🔴 §15-6 T-2 — 이어 쓰는 장 하나가 A4 한 장에 <b>실제로</b> 들어가는지 잰다.
    /// </summary>
    /// <remarks>
    /// 그릴 때와 <b>같은 종이·같은 여백·같은 글꼴·같은 꼬리</b>로 깔아 보고 높이를 받는다.
    /// 🔴 재다가 실패하면 <b>「안 들어간다」로 본다</b> — 잘못 「들어간다」로 보면 종이가 밀려
    /// 다시 꼬리 종이가 난다.
    /// </remarks>
    private bool MeasureCopyPageFits(DocumentSnapshot d, CompanyInfo c, byte[]? sealImage,
        string copyLabel, int skip, int take, bool isLast,
        float marginTop, float marginLeft, float marginRight, float marginBottom)
    {
        var probe = new TaxInvoicePageProbe(
            x => ComposeTaxInvoiceCopyPage(x, d, c, copyLabel, sealImage, skip, take, isLast));
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
                "세금계산서 이어쓰기 쪽을 재지 못했다 — 그 줄 수는 안 들어가는 것으로 본다. " +
                "문서번호 {DocNo}", d.DocNo);
            return false;
        }

        if (!probe.Measured)
        {
            _logger.LogWarning(
                "세금계산서 이어쓰기 쪽 높이를 받지 못했다 — 안 들어가는 것으로 본다. 문서번호 {DocNo}",
                d.DocNo);
            return false;
        }

        return probe.Height <= probe.AvailableHeight - TaxInvoicePageSlackPt;
    }

    /// <summary>
    /// 🔴 §15-6 T-2 — 이어 쓰는 장 하나의 <b>높이만</b> 받아 오는 자. 종이는 버린다.
    /// </summary>
    private sealed class TaxInvoicePageProbe : IDynamicComponent
    {
        private readonly Action<IContainer> _compose;

        public TaxInvoicePageProbe(Action<IContainer> compose) => _compose = compose;

        /// <summary>높이를 한 번이라도 받았나.</summary>
        public bool Measured { get; private set; }
        public float Height { get; private set; }
        public float AvailableHeight { get; private set; }

        public DynamicComponentComposeResult Compose(QuestPDF.Elements.DynamicContext context)
        {
            if (!Measured)
            {
                var element = context.CreateElement(_compose);
                Height = element.Size.Height;
                AvailableHeight = context.AvailableSize.Height;
                Measured = true;
            }

            return new DynamicComponentComposeResult
            {
                Content = context.CreateElement(x => x.Height(0)),
                HasMoreContent = false
            };
        }
    }

    /// <summary>
    /// 쪽 나눔 판정을 위한 자 — 종이는 버리고 <b>높이만</b> 가져온다(20261021작 §9-1).
    /// </summary>
    private sealed class TaxInvoiceFitProbe : IDynamicComponent
    {
        private readonly DocumentSnapshot _d;
        private readonly CompanyInfo _c;
        private readonly byte[]? _seal;
        private readonly int _maxLines;

        public TaxInvoiceFitProbe(DocumentSnapshot d, CompanyInfo c, byte[]? seal, int maxLines)
        {
            _d = d;
            _c = c;
            _seal = seal;
            _maxLines = maxLines;
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
                    x => ComposeTaxInvoiceCopy(x, _d, _c, "공급자 보관용", _seal, _maxLines));
                var tear = context.CreateElement(ComposeTearLine);
                var recipient = context.CreateElement(
                    x => ComposeTaxInvoiceCopy(x, _d, _c, "공급받는 자 보관용", _seal, _maxLines));

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
    /// <param name="maxLines">
    /// 찍을 품목 줄 수 상한(20261021작 §10-2 ⓓ). 전체보다 작으면 그만큼만 찍고
    /// <b>마지막 칸에 「외 N건」</b>을 적는다 — 고객이 「한 장에 모으기」를 켠 경우에만
    /// 부르는 쪽이 이 값을 줄인다. 합계는 <b>늘 전체 기준</b>이라 금액이 어긋나지 않는다.
    /// </param>
    private static void ComposeTaxInvoiceCopy(IContainer e, DocumentSnapshot d, CompanyInfo c,
        string copyLabel, byte[]? sealImage, int maxLines = int.MaxValue)
    {
        // 🔴 20261021작 §15-6 T-2 — 속을 셋(머리·품목표·꼬리)으로 나눴다.
        //   그려 내는 모양은 한 글자도 안 바뀐다. 나눈 까닭은 「자르는 선 끔 + 품목 넘침」에서
        //   장마다 머리를 다시 붙이고 꼬리는 마지막 장에만 붙이기 위해서다
        //   (ComposeTaxInvoiceCopyPage).
        var shownLines = Math.Min(maxLines, d.Lines.Count);
        e.Column(col =>
        {
            ComposeTaxInvoiceCopyHead(col, d, c, copyLabel);
            ComposeTaxInvoiceLineTable(col, d, 0, shownLines, d.Lines.Count - shownLines, true);
            ComposeTaxInvoiceCopyTail(col, d, c, sealImage);
        });
    }

    /// <summary>
    /// 🔴 20261021작 §15-6 T-2 — <b>벌 하나를 여러 장에 이어 쓸 때의 한 장</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 장마다 <b>머리</b>(보관용 라벨·작성일자·승인번호·공급자/공급받는 자·합계금액)를 다시
    /// 붙인다 — 어느 쪽을 집어도 누구 것인지 안다. 품목은 <paramref name="skip"/> 번째부터
    /// <paramref name="take"/> 줄만 담는다.
    /// </para>
    /// <para>
    /// <b>꼬리</b>(결제구분·영수/청구·비고·직인·서명란)와 품목 <b>합계줄</b>은
    /// <paramref name="isLast"/> 인 장에만 붙는다 ⇒ 🔴 <b>품목이 한 줄도 없는 꼬리 종이가
    /// 날 수 없다.</b> 마지막이 아닌 장에는 「다음 쪽에 이어집니다」를 적는다.
    /// </para>
    /// <para>
    /// 🔴 <b>줄을 버리는 코드는 0건</b>이다 — 「외 N건」은 이 경로에 <b>없다</b>.
    /// 그건 자르는 선 <b>켬</b> 전용이다(§15-5 · <c>ResolveVisibleLineCount</c>).
    /// </para>
    /// </remarks>
    private static void ComposeTaxInvoiceCopyPage(IContainer e, DocumentSnapshot d, CompanyInfo c,
        string copyLabel, byte[]? sealImage, int skip, int take, bool isLast)
    {
        e.Column(col =>
        {
            ComposeTaxInvoiceCopyHead(col, d, c, copyLabel);
            ComposeTaxInvoiceLineTable(col, d, skip, take, 0, isLast);
            if (isLast)
            {
                ComposeTaxInvoiceCopyTail(col, d, c, sealImage);
            }
            else
            {
                col.Item().PaddingTop(2).AlignRight()
                    .Text("품목이 많아 다음 쪽에 이어집니다").FontSize(8);
            }
        });
    }

    /// <summary>세금계산서 한 벌의 <b>머리</b> — 이어 쓰는 장마다 다시 붙는다(§15-6 T-2).</summary>
    private static void ComposeTaxInvoiceCopyHead(ColumnDescriptor col,
        DocumentSnapshot d, CompanyInfo c, string copyLabel)
    {
        // 제목줄 — 왼쪽 보관용 라벨 · 가운데 제목 · 오른쪽 전자 표시
        col.Item().Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text(copyLabel).FontSize(9).Bold();
            r.RelativeItem(2).AlignCenter().Text("세 금 계 산 서").FontSize(14).Bold();
            r.RelativeItem().AlignMiddle().AlignRight().Text("전자").FontSize(9).Bold();
        });

        // 작성일자 · 승인번호
        col.Item().PaddingTop(1).Row(r =>
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
        col.Item().PaddingTop(2).Border(1.2f).Table(table =>
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
            table.Cell().Border(0.5f).Padding(2).Column(x =>
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
            table.Cell().Border(0.5f).Padding(2).Column(x =>
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
        col.Item().PaddingTop(2).Border(1.2f).Table(table =>
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
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalAmount:N0}").FontSize(9).Bold();
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalSupply:N0}").FontSize(9);
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalVat:N0}").FontSize(9);
        });
    }

    /// <summary>세금계산서 한 벌의 <b>품목표</b>(§15-6 T-2 로 떼어 냈다).</summary>
    /// <param name="skip">몇 번째 줄부터 담나 — 이어 쓰는 장의 시작점.</param>
    /// <param name="take">이 장에 담을 줄 수.</param>
    /// <param name="hiddenCount">「외 N건」의 N — 자르는 선 <b>켬</b> 경로에서만 0 보다 크다.</param>
    /// <param name="includeTotalRow">품목 합계줄을 붙이나 — <b>마지막 장에만</b> 붙인다.</param>
    private static void ComposeTaxInvoiceLineTable(ColumnDescriptor col, DocumentSnapshot d,
        int skip, int take, int hiddenCount, bool includeTotalRow)
    {
        // 품목 — 🔴 자르는 코드 0건. 넘치면 다음 장으로 이어 찍는다.
        col.Item().PaddingTop(2).Border(1.2f).Table(table =>
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

            // 🔴 20261021작 §10-2 ⓓ — 「한 장에 모으기」를 켠 경우에만 상한이 걸린다.
            //   기본(끔)에서는 부르는 쪽이 줄 수를 전부 넘기므로 **전부** 찍힌다(§9-1 그대로).
            // 🔴 §15-6 T-2 — 이어 쓰는 장은 skip 번째부터 take 줄만 담는다.
            //   skip·take 는 부르는 쪽이 **실제로 재서** 정한다(ResolveCopyPageChunks).
            foreach (var line in d.Lines.Skip(skip).Take(take))
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

            // 🔴 안 찍힌 줄은 **숫자로 알려 준다** — 조용히 사라지지 않는다.
            //   국세청 세금계산서 서식의 품목란이 원래 몇 칸이고, 넘는 분은 「외 ○건」으로
            //   적는 것이 실제 관행이다(사장님 재결재 §10-2 ⓓ).
            //   합계 금액·수량은 **전체 기준** 그대로라 세액이 어긋나지 않는다.
            if (hiddenCount > 0)
            {
                table.Cell().ColumnSpan(2).Element(TaxDataCell).Text("").FontSize(8);
                table.Cell().ColumnSpan(6).Element(TaxDataCell)
                    .Text($"외 {hiddenCount}건").FontSize(8).Bold();
            }

            // 🔴 §15-6 T-2 — 합계줄은 **마지막 장에만** 붙는다. 이어 쓰는 장마다 합계를 찍으면
            //   한 벌에 합계가 여러 번 나와 고객이 금액을 두 번 읽는다.
            //   합계 금액·수량은 늘 **전체 기준**이라 쪽을 나눠도 세액이 안 어긋난다.
            if (!includeTotalRow) return;

            table.Cell().ColumnSpan(4).Element(TaxHeaderCell).AlignCenter().Text("합  계").FontSize(9).Bold();
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.Lines.Sum(x => x.Qty):N0}").FontSize(8).Bold();
            table.Cell().Element(TaxDataCell).Text("").FontSize(8);
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalSupply:N0}").FontSize(8).Bold();
            table.Cell().Element(TaxDataCell).AlignRight().Text($"{d.TotalVat:N0}").FontSize(8).Bold();
        });
    }

    /// <summary>세금계산서 한 벌의 <b>꼬리</b> — 마지막 장에만 붙는다(§15-6 T-2).</summary>
    private static void ComposeTaxInvoiceCopyTail(ColumnDescriptor col,
        DocumentSnapshot d, CompanyInfo c, byte[]? sealImage)
    {
        // 결제구분 — 🔴 칸만 그린다. 값을 넣을 원천이 어디에도 없다(설계 §8 5번).
        //   입력 자리 신설은 이번 범위 밖이다 — 고객이 종이에 적는다.
        col.Item().PaddingTop(2).Border(1.2f).Table(table =>
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
                table.Cell().Element(TaxDataCell).MinHeight(11).Text("").FontSize(8);
            }
        });

        // 영수 / 청구 — 🔴 원천이 없다. 두 낱말을 칸으로 그려 고객이 종이에 동그라미를 친다
        //   (결재 Q-1 ⓒ). 외상미수금으로 추론하지 않는다.
        col.Item().PaddingTop(3).Row(r =>
        {
            r.RelativeItem().Column(left =>
            {
                left.Item().Row(rr =>
                {
                    rr.AutoItem().AlignMiddle().Text("이 금액을 ").FontSize(9);
                    rr.AutoItem().Border(0.8f).PaddingHorizontal(6).PaddingVertical(1)
                        .Text("영수").FontSize(9).Bold();
                    rr.AutoItem().AlignMiddle().PaddingHorizontal(3).Text("/").FontSize(9);
                    rr.AutoItem().Border(0.8f).PaddingHorizontal(6).PaddingVertical(1)
                        .Text("청구").FontSize(9).Bold();
                    rr.AutoItem().AlignMiddle().Text(" 함").FontSize(9);
                });
                left.Item().PaddingTop(2).Text("이 금액을 영수(청구)함").FontSize(9).Bold();

                // 비고란
                left.Item().PaddingTop(2).Text("비고").FontSize(8).Bold();
                left.Item().PaddingTop(1).Border(0.5f).MinHeight(16).Padding(2)
                    .Text(d.Remark ?? "").FontSize(8);
            });
            r.ConstantItem(78).PaddingLeft(6).AlignBottom()
                .Element(x => ComposeTaxSeal(x, c.CompanyName, sealImage));
        });

        // 서명란
        col.Item().PaddingTop(3).Border(1.2f).Table(table =>
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
                table.Cell().Element(TaxDataCell).MinHeight(14).Text("").FontSize(8);
            }
        });
    }

    /// <summary>공급자·공급받는 자 칸 한 줄.</summary>
    private static void TaxInfoRow(ColumnDescriptor col, string label, string? value)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(52).Text(label).FontSize(8);
            r.RelativeItem().Text(value ?? "").FontSize(8);
        });
    }

    private static IContainer TaxHeaderCell(IContainer container)
        => container.Border(0.5f).Background(TaxGridBg).PaddingVertical(1.5f).PaddingHorizontal(3);

    private static IContainer TaxDataCell(IContainer container)
        => container.Border(0.5f).PaddingVertical(1).PaddingHorizontal(3);

    /// <summary>
    /// 인장 — 🔴 <c>seal_url</c> 에 값이 있으면 그 이미지를, 비어 있으면 지금의 글자 도장을 그린다.
    /// </summary>
    private static void ComposeTaxSeal(IContainer e, string companyName, byte[]? sealImage)
    {
        if (sealImage is not null)
        {
            e.AlignRight().AlignBottom().Width(56).Height(56).Image(sealImage).FitArea();
            return;
        }

        e.AlignRight().AlignBottom().Width(56).Height(56)
            .Border(1.5f).BorderColor(Colors.Red.Medium)
            .AlignCenter().AlignMiddle().Padding(3)
            .Text(text =>
            {
                var displayName = companyName.Length > 4
                    ? companyName[..4] + "\n" + (companyName.Length > 8 ? companyName[4..8] : companyName[4..])
                    : companyName;
                text.AlignCenter();
                text.Span(displayName).FontSize(8).FontColor(Colors.Red.Medium).Bold();
            });
    }
    /// <summary>
    /// 🔴 <b>양식지 그림을 깔지 말지 정하는 유일한 자리</b> (반려 R-1·R-3 봉합).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 함수로 뽑았나</b> — 종전에는 이 판단이 <c>RenderDocumentAsync</c> 본문에
    ///   한 줄로 묻혀 있어 <b>시험이 물 수가 없었다.</b> 검증팀 실측: 그리는 줄과 로더를
    ///   <b>통째로 빼도 게이트 13/13 이 초록</b>이었다(반려 R-3 · 결재 ㉯ 조건 3 미충족).
    ///   ⇒ 판단을 <b>부를 수 있는 한 자리</b>로 모아 시험이 직접 재게 한다.
    /// 🔴 <b>규칙은 하나다</b> — <b>미리보기일 때만</b> 깐다(§13-8).
    ///   실인쇄 PDF 에는 <b>0건</b>이다. 양식용지는 종이에 이미 인쇄돼 있어 또 찍으면
    ///   <b>이중 인쇄</b>가 되고, 순백지는 <b>히트판 양식</b>이라 업체 그림이 들어갈 자리가 아니다
    ///   (§13-7·§13-10 — 그건 <b>등재만</b>).
    /// </remarks>
    /// <summary>
    /// 🔴 저장된 양식 위에 <b>이번 인쇄에만</b> 쓸 옵션을 얹는다 (결-2 · 7종 공통).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>옵션이 없으면 받은 그대로 돌려준다</b> — 기존 인쇄 경로는 한 글자도 안 바뀐다(#1).
    /// 🔴 <b>양식 행이 아예 없을 때도 옵션은 먹어야 한다</b>. 고객이 「기본 양식 만들기」를
    ///   안 눌렀어도 인쇄 매수는 고를 수 있어야 하기 때문이다([1-V] E-7).
    /// 🔴 <b>저장하지 않는다</b> — 돌려주는 것은 이번 한 장을 그릴 입력일 뿐이다.
    /// </remarks>
    private static FormTemplateInfo? ApplyPrintOptions(FormTemplateInfo? template, PrintOptionOverrides? opt)
    {
        if (opt is null) return template;
        if (opt.PrintCopyMode is null && opt.ShowTearLine is null) return template;

        // 양식 행이 없으면 기본값 한 벌을 만들어 거기에 얹는다(E-7).
        var t = template ?? new FormTemplateInfo();

        if (!string.IsNullOrWhiteSpace(opt.PrintCopyMode)) t.PrintCopyMode = opt.PrintCopyMode!;
        if (opt.ShowTearLine is not null) t.ShowTearLine = opt.ShowTearLine.Value;
        return t;
    }

    private byte[]? ResolveFormBackground(PdfPreviewOverrides? overrides, FormTemplateInfo? template)
    {
        // 🔴 이 한 줄이 실인쇄를 지킨다. 빼면 고객 종이에 그림이 또 찍힌다.
        // 🔴 [재검증 C-3 봉합] 종전에는 `overrides is null` 로 **대리 판정**했다.
        //   전표 인쇄 미리보기도 overrides 를 타므로, 그 종이에 배경이 깔리면
        //   **미리보기엔 그림이 있고 인쇄물엔 없다** ⇒ 합격선 ⑤ 가 거짓이 된다.
        //   ⇒ **양식 설계 미리보기일 때만** 깐다.
        if (overrides?.IsFormDesignPreview != true) return null;
        return TryLoadFormBackground(template?.BackgroundImageUrl);
    }

    /// <summary>
    /// 🔴 업체 양식지 그림을 읽는다 — <b>미리보기 배경 전용</b> (§13-8).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>[2026-10-24 반려 R-2·R-4 봉합]</b> 종전 판은 두 가지가 틀렸다.
    /// <para>
    /// <b>R-2 — 「못 읽으면 종전과 똑같이」가 거짓이었다.</b> try/catch 가 <b>파일 읽기만</b>
    ///   감쌌고, 그림을 실제로 <b>그리는</b> <c>GeneratePdf()</c> 는 try 밖이었다.
    ///   검증팀 실측: 디코드 안 되는 바이트에 QuestPDF 가 <b>3/3 예외</b>를 던졌다.
    ///   ⇒ <c>.pdf</c>·<c>.svg</c>·0바이트를 가리키면 그 양식의 <b>인쇄·이메일·팩스가 전부 500</b>
    ///   이 된다(#20 — 워크플로우 흐름은 절대 안 끊긴다).
    ///   🔴 그래서 <b>여기서 미리 디코드해 본다.</b> 여기서 터지면 <c>null</c> 을 돌려주고,
    ///   종이는 <b>그림 없이</b> 나간다. 그리는 자리까지 끌고 가지 않는다.
    /// </para>
    /// <para>
    /// <b>R-4 — 경로가 안 갇혀 있었다.</b> 고객이 본문에 적은 문자열로 서버 파일을
    ///   아무거나 읽을 수 있었다(UNC·<c>file://</c> 가 http 차단을 비켜 갔다).
    ///   ⇒ <b>정해진 폴더 밑</b>만 · <b>그림 확장자</b>만 · <b>크기 상한</b>까지 건다
    ///   (§13-5 합격선 5 — 형식·크기 제한).
    /// </para>
    /// 🔴 <b>읽기만 한다</b> — 보관소·업로드 통로는 여전히 만들지 않는다(범위 밖).
    /// </remarks>
    private byte[]? TryLoadFormBackground(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // ① 바깥 그물 금지 — 인쇄가 남의 서버 응답 시간에 묶이면 고객 장사가 멈춘다.
        //    UNC(\호스트\…) 도 같은 이유로 막는다(R-4 — http 차단을 비켜 가던 길).
        if (url.Contains("://", StringComparison.Ordinal) || url.StartsWith(@"\\", StringComparison.Ordinal))
        {
            _logger.LogWarning("[PDF] 양식지 그림 주소가 바깥·공유 경로다 — 깔지 않는다.");
            return null;
        }

        // ② 그림 확장자만. .pdf·.svg·.exe 따위는 여기서 끝낸다.
        var ext = Path.GetExtension(url);
        if (!AllowedBackgroundExts.Contains(ext))
        {
            _logger.LogWarning("[PDF] 양식지 그림 형식이 아니다({Ext}) — 깔지 않는다.", ext);
            return null;
        }

        try
        {
            // ③ 경로 가둠 — 정해진 폴더 밑이 아니면 안 읽는다(R-4).
            //    `..\..\` 같은 거슬러 오르기는 FullPath 로 펴서 비교하므로 통하지 않는다.
            var full = Path.GetFullPath(url);
            // 🔴 [2026-10-24 재검증 R-4 봉합] 종전에는 **구분자 없이** 앞글자만 비교해
            //   **형제 폴더가 통째로 통과**했다(검증팀 실측: `…\form-backgrounds-probe\a.png`
            //   가 70바이트 읽혔다). 뒤에 구분자를 붙여 **그 폴더 안인지**를 본다.
            var root = Path.GetFullPath(FormBackgroundRoot).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[PDF] 양식지 그림이 허용 폴더 밖이다 — 깔지 않는다.");
                return null;
            }

            if (!File.Exists(full))
            {
                _logger.LogWarning("[PDF] 양식지 그림 파일이 없다 — 그림 없이 그린다.");
                return null;
            }

            // ③-2 🔴 [2026-10-24 재검증 R-10 봉합] 링크로 울타리를 넘는 길을 막는다.
            //   `Path.GetFullPath` 는 **링크를 풀지 않는다.** 폴더 안에 junction 하나를
            //   만들어 두면 ③ 의 울타리는 통과하고 **실제 파일은 바깥**이 된다.
            if (PassesThroughLink(full, root))
            {
                _logger.LogWarning("[PDF] 양식지 그림 경로가 링크를 지난다 — 깔지 않는다.");
                return null;
            }

            // ④ 크기 상한 — 거대 그림은 종이 한 장 때문에 PC 메모리를 먹는다.
            var len = new FileInfo(full).Length;
            if (len <= 0 || len > MaxBackgroundBytes)
            {
                _logger.LogWarning("[PDF] 양식지 그림 크기가 범위 밖이다({Len} bytes) — 깔지 않는다.", len);
                return null;
            }

            var bytes = File.ReadAllBytes(full);

            // ⑤ 🔴 여기서 **미리 그려 본다**(R-2). 작성기가 받아들이지 못하는 바이트면
            //    지금 터뜨리고 null 로 돌려준다 — 인쇄 한복판에서 터지면 전표가 안 나간다.
            try
            {
                _ = QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PDF] 양식지 그림을 작성기가 못 읽는다 — 그림 없이 그린다.");
                return null;
            }

            return bytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF] 양식지 그림 읽기 실패 — 그림 없이 그린다.");
            return null;
        }
    }

    /// <summary>
    /// 🔴 경로가 <b>링크(symlink·junction)</b> 를 지나는가 — 지나면 읽지 않는다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 필요한가</b> — 검증팀 2차 재검증 R-10: <c>Path.GetFullPath</c> 는 링크를
    ///   풀지 않는다. 그래서 허용 폴더 <b>안에</b> junction 을 하나 만들어 두면
    ///   앞글자 비교는 통과하고 실제 파일은 디스크 어디든 될 수 있다.
    /// 🔴 <b>울타리 자체(root)는 보지 않는다</b> — 그 자리는 관리자가 정한 곳이고,
    ///   거기가 링크여도 그것은 관리자의 뜻이다. 보는 것은 <b>울타리 안쪽</b>뿐이다.
    /// 🔴 못 재면 <b>막는 쪽</b>으로 떨어진다 — 종이는 그림 없이 나가고 전표는 산다(#20).
    /// </remarks>
    private static bool PassesThroughLink(string fullPath, string root)
    {
        var rootNoSep = root.TrimEnd(Path.DirectorySeparatorChar);

        var file = new FileInfo(fullPath);
        if (file.LinkTarget is not null) return true;

        for (var dir = file.Directory;
             dir is not null && dir.FullName.Length > rootNoSep.Length;
             dir = dir.Parent)
        {
            if (!dir.FullName.StartsWith(rootNoSep, StringComparison.OrdinalIgnoreCase)) break;
            if (dir.LinkTarget is not null) return true;
        }

        // 🔴🔴 [2026-10-25 3차 재검증 T-1 봉합] **뿌리 자신도 본다** — 위 주석의 면제를 거둔다.
        //   [틀린 전제] *"울타리 자체는 관리자가 정한 곳이니 링크여도 관리자의 뜻"* 이라고 적었다.
        //     🔴 이 뿌리는 **코드 고정값이고 앱이 스스로 만든다.** 관리자가 고른 자리가 아니다.
        //   🔴 검증팀 실측 ACL: `C:\ProgramData\HitPan` 에 `BUILTIN\Users:(I)(CI)(WD,AD,…)`
        //     ⇒ **일반 사용자가 하위 폴더를 만들 수 있다** ⇒ 갓 설치본에서 직원이
        //     `form-backgrounds` 를 먼저 junction 으로 만들어 두면 울타리가 통째로 비켜진다.
        //   🔴 올리기·지우기 쪽(FormBackgroundStore)과 **같이** 닫는다 — 한쪽만 고치면 또 비대칭이다.
        var rootDir = new DirectoryInfo(rootNoSep);
        if (rootDir.LinkTarget is not null) return true;

        return false;
    }

    /// <summary>
    /// 양식지 그림으로 받아 주는 형식. 🔴 이 밖은 작성기까지 가지 않는다(R-2).
    /// 🔴 [2026-10-24 업로드 통로 신설] 목록을 <b>여기서 적지 않는다</b> —
    ///   올리는 쪽과 읽는 쪽이 어긋나면 고객은 「올라갔는데 안 나온다」를 본다.
    /// </summary>
    private static HashSet<string> AllowedBackgroundExts => FormBackgroundPaths.AllowedExts;

    /// <summary>
    /// 양식지 그림 크기 상한. 🔴 올리는 쪽과 <b>같은 값</b>을 쓴다(FormBackgroundPaths).
    /// </summary>
    private const long MaxBackgroundBytes = FormBackgroundPaths.MaxBytes;

    /// <summary>
    /// 🔴 양식의 <b>용지 크기·방향</b>을 종이에 옮긴다 (20261024 사장님 결재).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 생겼나</b> — 작성기에 <c>PageSizes.A4</c> 가 못으로 박혀 있었다.
    ///   고객이 편집기에서 A5·가로를 골라 <b>저장까지 되는데</b> 종이는 늘 A4 세로였다.
    ///   「저장은 되는데 아무 일도 안 일어나는 칸」이 네 개 있었고 그중 둘이 이것이다.
    ///
    /// 🔴 <b>모르는 값은 A4·세로로 떨어진다</b> — 오타나 새 값이 와도 종이는 나간다(#20).
    ///   막는 쪽으로 떨어지면 전표가 안 나가고, 그건 고객 장사가 멈추는 것이다.
    ///
    /// 🔴 <b>세금계산서 전용 작성기는 여기를 타지 않는다</b> — 법정 서식이라 A4 고정이다
    ///   (부가가치세법 시행규칙 · D-5 교훈). 그 쪽을 바꾸려면 법무팀장 결재가 먼저다.
    /// </remarks>
    private PageSize ResolvePageSize(FormTemplateInfo? template,
        string? documentType = null, bool useCoords = false)
    {
        // 🔴🔴 [2026-10-25 [4] 반려 R-5 봉합] **법정 서식은 용지를 바꾸지 않는다.**
        //   [내가 뭘 틀렸나] 세금계산서 전용 작성기로 가는 분기는 `&& !isPreprint` 다.
        //     그래서 **양식용지 모드의 세금계산서는 이 공용 흐름으로 온다** — 전용 작성기를
        //     A4 로 묶어 둔 것만 보고 "세금계산서는 A4 고정 그대로" 라고 적었는데, 절반만 참이었다.
        //     고객이 양식용지 + A5 를 고르면 **법정 서식이 A5 로 나간다**(부가가치세법 시행규칙).
        //   🔴 그래서 종류로 막는다 — 모드와 무관하게 세금계산서는 A4 세로다.
        //     바꾸려면 법무팀장 결재가 먼저다(D-5 와 같은 자리).
        //   🔴 [2026-10-25 재검증 S-3 봉합] 1차 봉합은 `tax_invoice` **하나만** 막았다.
        //     변환표에는 `invoice_exempt`(면세계산서)·`payment_receipt`(입금표)도 있고
        //     이것들도 계산서류다 ⇒ **법정성 판정은 법무팀장 몫**이고, 그 전까지는
        //     **막는 쪽**에 둔다. 모르고 풀어 두면 법정 서식이 A5 로 나가는데,
        //     모르고 막아 두면 고객이 용지를 못 바꾸는 것뿐이다 — 되돌릴 수 있는 쪽을 고른다.
        if (LegalFormDocTypes.Contains(documentType ?? string.Empty))
        {
            if (!IsA4Portrait(template))
            {
                _logger.LogWarning(
                    "[PDF] {DocType} 은 법정 서식이라 용지·방향을 바꾸지 않는다 — A4 세로로 그린다.",
                    documentType);
            }

            return PageSizes.A4;
        }

        // 🔴🔴 [2026-10-25 [4] 반려 R-4 봉합] **좌표를 맞춰 둔 양식용지는 종이를 안 바꾼다.**
        //   [무엇이 일어났나] 좌표 인쇄는 종이 왼쪽 위가 원점이고 여백이 0 이다. 고객이 A4
        //     양식용지에 자를 대고 맞춘 mm 좌표가 있는데 용지를 A5 로 줄이면, 종이 밖으로
        //     나간 값은 **그냥 안 그려진다.** 오류도 안 나고 경고도 없다 ⇒ **조용히 빈 종이.**
        //     이번 차수에서 가장 위험한 자리였다(고객은 전표가 안 나간 줄도 모른다).
        //   🔴 좌표는 **어느 종이에 대고 쟀는지**가 전제다. 그 전제를 코드가 혼자 바꾸지 않는다.
        //     용지를 바꾸려면 고객이 좌표를 다시 맞춰야 하고, 그건 화면이 안내할 일이다(등재).
        if (useCoords && !IsA4Portrait(template))
        {
            _logger.LogWarning(
                "[PDF] 양식용지 좌표가 맞춰져 있어 용지·방향을 바꾸지 않는다 — A4 세로로 그린다. "
                + "용지를 바꾸려면 좌표를 다시 맞춰 주십시오.");
            return PageSizes.A4;
        }

        var size = (template?.PaperSize ?? "A4").Trim();

        var baseSize = size.ToUpperInvariant() switch
        {
            "A4" => PageSizes.A4,
            "A5" => PageSizes.A5,
            "LETTER" => PageSizes.Letter,
            _ => FallbackPageSize(size),
        };

        // 가로는 **같은 종이를 눕히는 것**이다 — 다른 종이를 고르는 것이 아니다.
        var landscape = string.Equals(
            (template?.Orientation ?? "portrait").Trim(), "landscape",
            StringComparison.OrdinalIgnoreCase);

        return landscape ? baseSize.Landscape() : baseSize;
    }

    /// <summary>
    /// 🔴 <b>용지·방향을 바꾸지 않는 종류</b> — 법정 서식(또는 그 후보).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 목록인가</b> — 2026-10-25 재검증 S-3. 1차 봉합은 세금계산서 하나만 막았는데
    ///   변환표에는 <c>invoice_exempt</c>(면세계산서)·<c>payment_receipt</c>(입금표)도 있다.
    /// 🔴 <b>법정성 판정은 법무팀장 몫이다</b>(등재). 판정이 날 때까지는 <b>막는 쪽</b>에 둔다 —
    ///   틀리게 막으면 고객이 용지를 못 바꾸는 것뿐이고, 틀리게 풀면 <b>법정 서식이 A5 로 나간다.</b>
    ///   되돌릴 수 있는 쪽을 고른다.
    /// 🔴 <b>여기서 종류 이름을 손으로 적는다</b> — 긁을 수 있는 자리가 아니다(법적 판단이라서).
    ///   그래서 게이트가 <b>변환표 전건</b>을 돌며 「막히나 / 안 막히나」를 표로 굳힌다.
    /// </remarks>
    private static readonly HashSet<string> LegalFormDocTypes =
        new(StringComparer.Ordinal) { TaxInvoiceDocType, "invoice_exempt", "payment_receipt" };

    /// <summary>
    /// 이 양식이 <b>A4 세로</b>를 그대로 쓰고 있나 — 🔴 법정 서식·좌표 양식지 보호 판정에 쓴다.
    /// </summary>
    /// <remarks>
    /// 🔴 「기본값 그대로」와 「A4 를 골라 둠」을 <b>같게 본다</b> — 둘 다 종이가 안 바뀐다.
    ///   그래서 바꾸지 않았을 때는 경고도 남기지 않는다(쓸데없는 경고는 다음 사람이 끈다).
    /// </remarks>
    private static bool IsA4Portrait(FormTemplateInfo? template)
    {
        var size = (template?.PaperSize ?? "A4").Trim();
        var orientation = (template?.Orientation ?? "portrait").Trim();

        return size.Equals("A4", StringComparison.OrdinalIgnoreCase)
            && !orientation.Equals("landscape", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>모르는 용지값이 왔을 때 — 🔴 조용히 넘기지 않고 적어 둔 뒤 A4 로 간다.</summary>
    private PageSize FallbackPageSize(string size)
    {
        _logger.LogWarning(
            "[PDF] 모르는 용지 크기({Size}) — A4 로 그린다. 양식 설정을 확인해 주십시오.", size);
        return PageSizes.A4;
    }

    /// <summary>
    /// 🔴 디자인 스타일 칸을 <b>읽는다</b> (DB-90 · 20261024 <b>배선만</b>).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>지금은 무엇이 와도 <c>basic</c> 으로 그린다.</b> 출하 DDL 주석이 그렇게 적혀 있다
    ///   — <i>"4종 확정 전까지 basic 단일"</i>. 스타일 4종은 <b>디자인 미확정</b>이고,
    ///   그 그림을 여기서 발명하면 결재 없이 디자인을 정하는 것이 된다.
    /// 🔴 그래서 이 함수가 하는 일은 하나다 — <b>종전에는 아무도 안 읽던 칸을 읽고,
    ///   모르는 값이면 적어 둔다.</b> 4종이 확정되는 날 이 자리가 갈림길이 된다.
    /// </remarks>
    private string ResolveStyleKey(FormTemplateInfo? template)
    {
        var key = (template?.StyleKey ?? "basic").Trim();
        if (key.Length == 0) return "basic";

        if (!string.Equals(key, "basic", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "[PDF] 아직 준비되지 않은 양식 스타일({Key}) — 기본 모양으로 그린다.", key);
            return "basic";
        }

        return "basic";
    }

    /// <summary>
    /// 🔴 양식지 그림을 읽어 올 수 있는 <b>유일한 폴더</b> (R-4 경로 가둠).
    /// 보관소·업로드 통로가 생기면 그 통로가 여기에 넣는다 — 그때 <b>이 코드는 안 바뀐다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 [2026-10-24] 업로드 통로가 생겼다 — 그 통로가 <b>이 폴더 밑 테넌트 칸</b>에 넣는다.
    ///   약속대로 <b>이 울타리 코드는 바뀌지 않았다</b>(테넌트 폴더는 뿌리 안이라 그대로 통과한다).
    /// </remarks>
    private static string FormBackgroundRoot => FormBackgroundPaths.Root;



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
            return ToTemplateInfo(dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "form_templates 조회 실패 - tenant={Tenant} type={Type}, 기본 plain 가도", tenantId, formType);
            return null;
        }
    }

    /// <summary>
    /// 양식 DTO → 작성기 입력. 🔴 <b>매핑은 한 벌이다</b> — 미리보기도 이것을 쓴다.
    /// 두 벌이 되면 미리보기와 인쇄물이 조용히 어긋난다(20261021작 §11-3).
    /// </summary>
    private FormTemplateInfo ToTemplateInfo(FormTemplateDto dto)
    {
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
                // 🔴 20261021작 §10-4 S-2 (DB-147) — 이 한 줄이 없으면 고객이 설정을
                //   켜도 끄나 켜나 **같은 종이**가 나온다.
                ShowTearLine = dto.ShowTearLine,
                // 🔴 봉합 2026-08-12 (사장님 지시 "양식정보 설정의 양식용지만 해")
                //   종전에는 이 값을 **읽어오지도 않았다.** 고객이 양식용지 좌표를 아무리
                //   맞춰도 인쇄물은 코드에 고정된 위치로만 나왔다.
                //   ⇒ 시판 양식지 칸과 안 맞아 양식용지 모드가 사실상 못 쓰는 기능이었다.
                FieldCoords = ParseFieldCoords(dto.FieldCoordsJson),
            // 🔴 20261024 사장님 오더 "인쇄까지 모두 양식지 적용" · §13
            //   *"이미 업체에서 쓰는 양식지가 있을수도 있으니"* — 종전에는 이 칸을
            //   **읽어오지도 않았다**(PdfRenderService 안 BackgroundImageUrl 0건).
            //   고객이 쓰는 양식지를 올려도 종이에 안 나왔다.
            BackgroundImageUrl = dto.BackgroundImageUrl,
            // 🔴 20261024 사장님 결재 — 종전에는 아래 네 칸이 작성기에 **0건**이었다.
            //   편집기에서 고르고 저장까지 되는데 **종이만 안 바뀌는** 칸들이었다.
            //   🔴 PaperSize·Orientation = 실제로 종이가 바뀐다.
            //   🔴 ShowCompanySeal·StyleKey = **배선만**(아래 각 칸 주석에 까닭을 적었다).
            PaperSize = dto.PaperSize,
            Orientation = dto.Orientation,
            ShowCompanySeal = dto.ShowCompanySeal,
            StyleKey = dto.StyleKey
        };
    }

    /// <summary>
    /// 예시 자료 — 🔴 <b>전표 없이</b> 양식을 맞춰 볼 수 있게 한다(20261021작 §11-2 ②).
    /// 양식정보설정은 6단계 중 <b>1단계</b>인데 전표는 3~4단계다. 신규 고객이 자를 대고
    /// mm 를 맞추는 바로 그 시점에 전표가 <b>0건</b>이라 지금은 아무것도 못 봤다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>품목 줄 수를 바꿔 볼 수 있어야 한다</b>(§11-3) — 「몇 줄부터 두 쪽이 되나」·
    ///   「몇 줄부터 외 N건이 되나」를 눈으로 보는 것이 이 화면의 목적이다.
    /// 🔴 종이에 <b>「예시」</b>가 찍힌다 — 진짜 계산서로 오해하면 안 된다.
    /// </remarks>
    private static DocumentSnapshot BuildSampleSnapshot(string documentType, PdfPreviewOverrides ov)
    {
        var count = Math.Clamp(ov.SampleLineCount, 1, 200);
        var lines = new List<DocumentLine>(count);
        for (var i = 1; i <= count; i++)
        {
            // 금액은 decimal 이다 (#4). 줄마다 조금씩 달라야 합계 검산이 눈에 보인다.
            var qty = (decimal)(i % 5 + 1);
            var unit = 10000m + (i % 7) * 1500m;
            var supply = qty * unit;
            var vat = Math.Round(supply * 0.1m, 0, MidpointRounding.AwayFromZero);
            lines.Add(new DocumentLine
            {
                ItemName = $"예시품목 {i}",
                Spec = i % 2 == 0 ? "1BOX" : "1EA",
                Qty = qty,
                UnitPrice = unit,
                Supply = supply,
                Vat = vat,
                Total = supply + vat
            });
        }

        var totalSupply = lines.Sum(l => l.Supply);
        var totalVat = lines.Sum(l => l.Vat);

        return new DocumentSnapshot
        {
            DocType = documentType,
            // 🔴 번호 자리에 「예시」를 적는다 — 어느 양식이든 머리에 반드시 찍히는 칸이다.
            DocNo = "예시-0000",
            DocDate = DateTime.Today,
            PartnerName = "(예시) 거래처명",
            PartnerBusinessNo = "000-00-00000",
            PartnerAddress = "(예시) 주소 — 실제 자료가 아닙니다",
            PartnerContact = "담당자",
            PartnerCeoName = "(예시) 대표자",
            PartnerBizType = "(예시) 업태",
            PartnerBizItem = "(예시) 종목",
            PartnerPhone = "000-0000-0000",
            TotalSupply = totalSupply,
            TotalVat = totalVat,
            TotalAmount = totalSupply + totalVat,
            Remark = ov.MarkAsSample
                ? "🔴 예시 자료입니다 — 실제 거래 내용이 아닙니다. 양식 확인용으로만 쓰십시오."
                : null,
            Lines = lines
        };
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

        /// <summary>
        /// 두 벌을 한 장에 모으고 가운데 자르는 선을 넣나 (DB-147 · 20261021작 §10-2 ⓑ).
        /// 기본 false — 양식 행이 없을 때도 「끔」으로 본다(품목이 조용히 빠지면 안 된다).
        /// </summary>
        public bool ShowTearLine { get; set; }


        /// <summary>
        /// 🔴 업체 양식지 그림 (20261024 사장님 오더 · §13). 종전에는 이 칸이
        /// <b>작성기에 아예 없었다</b> — 고객이 올려도 종이에 안 나왔다.
        /// </summary>
        /// <remarks>
        /// 사장님: <i>"양식지를 추가할수도 있어야해. 이미 업체에서 쓰는 양식지가 있을수도 있으니."</i>
        /// 🔴 스캔한 양식지 위에 내용을 겹쳐 보여줘야 mm 맞추기가 의미를 가진다.
        /// </remarks>
        public string? BackgroundImageUrl { get; set; }

        /// <summary>
        /// 🔴 용지 크기 — <c>A4</c>·<c>A5</c>·<c>Letter</c> (20261024 사장님 결재).
        /// 종전에는 작성기에 <b>이 칸이 없었다</b> — 고객이 A5 를 골라도 A4 가 나왔다.
        /// </summary>
        public string PaperSize { get; set; } = "A4";

        /// <summary>
        /// 🔴 용지 방향 — <c>portrait</c>·<c>landscape</c> (20261024 사장님 결재).
        /// 종전 <b>0건</b>. 방향을 지정하는 코드가 작성기에 아예 없었다.
        /// </summary>
        public string Orientation { get; set; } = "portrait";

        /// <summary>
        /// 🔴 이 양식에서 직인을 찍나 (20261024 사장님 결재 · <b>배선만</b>).
        /// 🔴 <b>「직인이 나온다」는 뜻이 아니다</b> — 직인 그림 보관소가 없어
        /// <c>seal_url</c> 이 늘 비어 있다. 보관소가 생기는 날 이 칸이 켬/끔을 가른다.
        /// </summary>
        public bool ShowCompanySeal { get; set; } = true;

        /// <summary>
        /// 🔴 디자인 스타일 (DB-90 · <b>배선만</b>). 출하 DDL 주석에 적힌 그대로
        /// <i>"4종 확정 전까지 basic 단일"</i> ⇒ 🔴 <b>지금은 무엇이 와도 basic 으로 그린다.</b>
        /// 종전에는 저장·조회만 되고 <b>작성기가 읽지 않았다</b>.
        /// </summary>
        public string StyleKey { get; set; } = "basic";

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
