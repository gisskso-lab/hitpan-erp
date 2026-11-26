using System.Reflection;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴🔴 양식 스타일 4종 게이트 — W-G1-51 갈래 E⑵ 고침 1 (PM 판단 §72 E-1 · E-4 · E-5).
/// </summary>
/// <remarks>
/// 🔴 <b>이 묶음이 왜 생겼나</b> — [3-V] P2-1: 새 세 스타일(formal · simple · large)을 <b>실제로 그려 보는</b> 시험이 0 이었다.
///   빌드 0/0 은 배치 예외(표 · 테두리 · 자간 조합)를 못 잡는다 — 배치 예외는 그릴 때만 난다.
/// 🔴 [3-V] P1-1(E-1): 면세계산서 · 입금표에도 스타일이 먹었다 → 법정 서식(또는 그 후보) 3종은 스타일 무시(조건 ⑥).
/// 🔴 [3-V] P2-3(E-5): 저장 문에 스타일 허용 목록이 없었다 → 4값만 · 밖 = 사람 말 거절.
/// 🔴 <b>제품 코드로 그린다</b> — 작성기(<c>ComposeHeader</c> · <c>ComposeBody</c> · <c>ComposeHeaderStyled</c> ·
///   <c>ComposeBodyStyled</c>)와 고르기(<c>ResolvePrintStyle</c>)를 반사로 불러 쓴다. 시험이 제 몫으로 표를 그리면
///   실코드가 틀려도 초록이 된다. 종이 조립(머리 · 본문 · 쪽번호)만 <c>RenderDocumentAsync</c> 순백지 줄과 같은 꼴로
///   여기서 짠다 — 그 함수는 DB(회사 · 양식 · 워터마크)를 읽어 시험에서 못 부른다.
/// 🔴 범위 밖: 큰글씨 숫자 칸 줄바꿈 · 쪽을 넘는 한 행(E-2 · 고침 2 몫) — 여기서 단언하지 않는다.
/// </remarks>
public sealed class FormStyleRenderGateTests
{
    static FormStyleRenderGateTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private static readonly string[] AllStyles = { "basic", "formal", "simple", "large" };
    private static readonly string[] NewStyles = { "formal", "simple", "large" };

    // ══════════════════════════════════════════════════════════════════
    // 자 — 제품의 형 · 함수를 반사로 잡는다
    // ══════════════════════════════════════════════════════════════════

    private static Type Nested(string name) =>
        typeof(PdfRenderService).GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{name} 이 없다 — 배선이 바뀌었다.");

    private static MethodInfo StaticMethod(string name) =>
        typeof(PdfRenderService).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{name} 이 없다 — 배선이 바뀌었다.");

    private static object MakeTemplate(string styleKey, string paperMode = "plain", string? backgroundUrl = null)
    {
        var t = Activator.CreateInstance(Nested("FormTemplateInfo"))!;
        t.GetType().GetProperty("PaperMode")!.SetValue(t, paperMode);
        t.GetType().GetProperty("StyleKey")!.SetValue(t, styleKey);
        t.GetType().GetProperty("BackgroundImageUrl")!.SetValue(t, backgroundUrl);
        return t;
    }

    /// <summary>제품의 <c>ResolvePrintStyle(template, useCoords, documentType)</c> — null = basic = 옛 작성기.</summary>
    private static object? ResolveStyle(object? template, string documentType, bool useCoords = false) =>
        StaticMethod("ResolvePrintStyle").Invoke(null, new object?[] { template, useCoords, documentType });

    private static string StyleKeyOf(object style) =>
        (string)style.GetType().GetProperty("Key")!.GetValue(style)!;

    /// <summary>품목 <paramref name="lineCount"/> 줄짜리 전표 하나.</summary>
    private static object MakeSnapshot(string docType, int lineCount, bool longName)
    {
        var snapType = Nested("DocumentSnapshot");
        var lineType = Nested("DocumentLine");
        var snap = Activator.CreateInstance(snapType)!;
        snapType.GetProperty("DocType")!.SetValue(snap, docType);
        snapType.GetProperty("DocNo")!.SetValue(snap, "GATE-E2-0001");
        // 날짜를 고정한다 — 두 판을 그림으로 대조하므로 자정을 넘겨도 같은 글자여야 한다.
        snapType.GetProperty("DocDate")!.SetValue(snap, new DateTime(2026, 11, 20));
        snapType.GetProperty("PartnerName")!.SetValue(snap, "가나상사");
        snapType.GetProperty("PartnerBusinessNo")!.SetValue(snap, "000-00-00000");
        snapType.GetProperty("PartnerAddress")!.SetValue(snap, "서울시 어딘가 1");
        snapType.GetProperty("Remark")!.SetValue(snap, "비고 한 줄");

        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(lineType))!;
        decimal supply = 0m, vat = 0m;
        for (var i = 0; i < lineCount; i++)
        {
            var line = Activator.CreateInstance(lineType)!;
            var name = longName && i == 0
                ? "스테인리스 판형 열교환기 고온용 가스켓 포함 세트 특주 규격"
                : $"품목 {i + 1}";
            lineType.GetProperty("ItemName")!.SetValue(line, name);
            lineType.GetProperty("Spec")!.SetValue(line, longName && i == 0 ? "600x400x2T 양면" : "규격");
            lineType.GetProperty("Qty")!.SetValue(line, 3m);
            lineType.GetProperty("UnitPrice")!.SetValue(line, 12_000m);
            lineType.GetProperty("Supply")!.SetValue(line, 36_000m);
            lineType.GetProperty("Vat")!.SetValue(line, 3_600m);
            lineType.GetProperty("Total")!.SetValue(line, 39_600m);
            list.Add(line);
            supply += 36_000m;
            vat += 3_600m;
        }
        snapType.GetProperty("Lines")!.SetValue(snap, list);
        snapType.GetProperty("TotalSupply")!.SetValue(snap, supply);
        snapType.GetProperty("TotalVat")!.SetValue(snap, vat);
        snapType.GetProperty("TotalAmount")!.SetValue(snap, supply + vat);
        return snap;
    }

    private static object MakeCompany()
    {
        var c = Activator.CreateInstance(Nested("CompanyInfo"))!;
        c.GetType().GetProperty("CompanyName")!.SetValue(c, "히트판상사");
        c.GetType().GetProperty("BusinessNo")!.SetValue(c, "111-11-11111");
        c.GetType().GetProperty("RepresentativeName")!.SetValue(c, "홍길동");
        c.GetType().GetProperty("Address")!.SetValue(c, "경기도 어딘가 2");
        c.GetType().GetProperty("Phone")!.SetValue(c, "02-000-0000");
        return c;
    }

    /// <summary>
    /// 🔴 순백지 한 장을 <b>제품 작성기</b>로 그린다 — 어느 작성기를 쓸지도 제품의 <c>ResolvePrintStyle</c> 가 고른다.
    /// </summary>
    /// <remarks>
    /// 조립은 <c>RenderDocumentAsync</c> 순백지 · 좌표 아님 · 머리 켬 줄과 같다:
    ///   A4 · 여백 20mm · 맑은 고딕 10 · 흰 종이 · 머리(스타일이면 <c>ComposeHeaderStyled</c>) ·
    ///   본문(스타일이면 <c>ComposeBodyStyled</c> · 아니면 <c>ComposeBody(e, d, false, showBorder)</c>) ·
    ///   쪽번호(스타일 묶음의 <c>PageNumberSize</c> · basic = 8).
    /// </remarks>
    private static List<byte[]> RenderPlain(string docType, string styleKey, int lineCount, bool showBorder,
        bool longName = false)
    {
        var style = ResolveStyle(MakeTemplate(styleKey), docType);
        var snap = MakeSnapshot(docType, lineCount, longName);
        var company = MakeCompany();
        var header = StaticMethod("ComposeHeader");
        var body = StaticMethod("ComposeBody");
        var headerStyled = StaticMethod("ComposeHeaderStyled");
        var bodyStyled = StaticMethod("ComposeBodyStyled");
        var pageNoSize = style is null
            ? 8f
            : (float)style.GetType().GetProperty("PageNumberSize")!.GetValue(style)!;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(20, Unit.Millimetre);
                page.MarginLeft(20, Unit.Millimetre);
                page.MarginRight(20, Unit.Millimetre);
                page.MarginBottom(20, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(10));
                page.PageColor(Colors.White);

                if (style is not null)
                    page.Header().Element(e => headerStyled.Invoke(null, new object?[] { e, snap, company, style }));
                else
                    page.Header().Element(e => header.Invoke(null, new object?[] { e, snap, company }));

                if (style is not null)
                    page.Content().Element(e => bodyStyled.Invoke(null, new object?[] { e, snap, showBorder, style }));
                else
                    page.Content().Element(e => body.Invoke(null, new object?[] { e, snap, false, showBorder }));

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ").FontSize(pageNoSize); t.CurrentPageNumber().FontSize(pageNoSize);
                    t.Span(" / ").FontSize(pageNoSize); t.TotalPages().FontSize(pageNoSize);
                });
            });
        }).GenerateImagesOneAtATime(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 72
        }).ToList();
    }

    private static bool SamePaper(List<byte[]> a, List<byte[]> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.AsSpan().SequenceEqual(p.Second));

    // ══════════════════════════════════════════════════════════════════
    // ⓐ 고르기 조건 — E-1 법정 서식 3종 = 스타일 무시 (조건 ⑥)
    // ══════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "E⑵ 고침1 ⓐ 🔴 세금계산서 · 면세계산서 · 입금표는 어떤 스타일 키가 와도 **basic**(조건 ⑥ · 법정 서식 후보)")]
    [InlineData("tax_invoice")]
    [InlineData("invoice_exempt")]
    [InlineData("payment_receipt")]
    public void A_LegalFormTypesNeverTakeStyle(string docType)
    {
        foreach (var key in new[] { "formal", "simple", "large", "FORMAL", " large " })
        {
            Assert.Null(ResolveStyle(MakeTemplate(key), docType));
        }
    }

    [Fact(DisplayName = "E⑵ 고침1 ⓐ 🔴 법정 서식 목록(`LegalFormDocTypes`)을 **긁어서** 본다 — 목록에 든 종류는 전부 스타일 무시")]
    public void A_EveryLegalFormTypeIsScrapedAndBlocked()
    {
        var field = typeof(PdfRenderService).GetField("LegalFormDocTypes", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LegalFormDocTypes 가 없다 — 배선이 바뀌었다.");
        var legal = ((IEnumerable<string>)field.GetValue(null)!).ToList();

        // 🔴 0건이면 이 시험은 아무것도 안 본다 — 긁기가 깨진 것이다.
        Assert.True(legal.Count >= 3, $"법정 서식 목록이 {legal.Count} 건뿐이다 — 긁기를 먼저 의심하라.");
        foreach (var docType in legal)
        {
            foreach (var key in NewStyles)
            {
                if (ResolveStyle(MakeTemplate(key), docType) is not null)
                    Assert.Fail($"법정 서식 {docType} 에 스타일 {key} 가 먹었다(조건 ⑥ 빠짐).");
            }
        }
    }

    [Theory(DisplayName = "E⑵ 고침1 ⓐ 🔴 법정 서식이 아닌 순백지 · 그림 0 · 좌표 아님 = 새 세 스타일이 **먹는다**(대조군)")]
    [InlineData("quotation")]
    [InlineData("sales_order")]
    [InlineData("delivery")]
    [InlineData("purchase_order")]
    [InlineData("purchase_receipt")]
    public void A_NonLegalPlainTakesNewStyles(string docType)
    {
        foreach (var key in NewStyles)
        {
            var style = ResolveStyle(MakeTemplate(key), docType);
            Assert.NotNull(style);
            Assert.Equal(key, StyleKeyOf(style!));
        }

        // basic · 모르는 값 = null = 옛 작성기(종전 그대로).
        Assert.Null(ResolveStyle(MakeTemplate("basic"), docType));
        Assert.Null(ResolveStyle(MakeTemplate("modern"), docType));
    }

    [Fact(DisplayName = "E⑵ 고침1 ⓐ 🔴 양식 없음 · 양식지 · 좌표 경로 · 그림 있는 양식 = 스타일 무시(조건 ①~④ 그대로)")]
    public void A_OtherConditionsStillBlock()
    {
        Assert.Null(ResolveStyle(null, "quotation"));
        Assert.Null(ResolveStyle(MakeTemplate("large", paperMode: "preprint"), "quotation"));
        Assert.Null(ResolveStyle(MakeTemplate("large"), "quotation", useCoords: true));
        Assert.Null(ResolveStyle(MakeTemplate("large", backgroundUrl: "tenant/form.png"), "quotation"));
    }

    // ══════════════════════════════════════════════════════════════════
    // ⓑ 그리기 — 4종 각각 · 예외 0 · 쪽 1 이상 (E-4)
    // ══════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "E⑵ 고침1 ⓑ 🔴 스타일 4종 각각 실제로 그린다 — 테두리 켬/끔 × 품목 0 · 1 · 긴 품명 · 80줄 · 예외 0 · 쪽 1 이상")]
    [InlineData("basic")]
    [InlineData("formal")]
    [InlineData("simple")]
    [InlineData("large")]
    public void B_EachStyleRendersWithoutException(string styleKey)
    {
        // 견적서 = 법정 서식 아님 ⇒ 새 세 스타일이 정말 새 작성기를 탄다(basic 은 옛 작성기).
        var style = ResolveStyle(MakeTemplate(styleKey), "quotation");
        if (styleKey == "basic") Assert.Null(style);
        else Assert.Equal(styleKey, StyleKeyOf(style!));

        foreach (var showBorder in new[] { true, false })
        {
            foreach (var (lines, longName) in new[] { (0, false), (1, false), (1, true), (80, false) })
            {
                var pages = RenderPlain("quotation", styleKey, lines, showBorder, longName);
                Assert.True(pages.Count >= 1,
                    $"{styleKey} · 테두리 {showBorder} · 품목 {lines} · 긴 품명 {longName} — 쪽이 0 이다.");
                Assert.All(pages, p => Assert.True(p.Length > 0));

                if (lines == 80)
                {
                    // 80줄은 A4 한 쪽에 안 든다 — 여러 쪽 머리 반복 · 쪽번호 줄이 같이 그려진다.
                    Assert.True(pages.Count >= 2,
                        $"{styleKey} · 테두리 {showBorder} · 품목 80줄인데 {pages.Count} 쪽 — 품목이 조용히 빠졌나.");
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ⓒ 법정 서식 3종 = basic 과 같은 종이 (E-1 을 종이로)
    // ══════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "E⑵ 고침1 ⓒ 🔴 세금계산서 · 면세계산서 · 입금표는 formal · simple · large 를 골라도 **basic 과 같은 종이**(그림 바이트 대조)")]
    [InlineData("tax_invoice")]
    [InlineData("invoice_exempt")]
    [InlineData("payment_receipt")]
    public void C_LegalFormTypesPrintSameAsBasic(string docType)
    {
        var basic = RenderPlain(docType, "basic", 3, showBorder: true);
        Assert.NotEmpty(basic);

        foreach (var key in NewStyles)
        {
            var styled = RenderPlain(docType, key, 3, showBorder: true);
            Assert.True(SamePaper(basic, styled),
                $"{docType} 에 {key} 를 고르니 basic 과 다른 종이가 나왔다 — 법정 서식 후보에 스타일이 먹었다(조건 ⑥).");
        }
    }

    [Fact(DisplayName = "E⑵ 고침1 ⓒ 자 검사 — 법정 서식이 아닌 견적서는 large 와 basic 종이가 **다르다**(그림 대조 자가 차이를 보나)")]
    public void C_RulerSeesDifferenceOnNonLegal()
    {
        var basic = RenderPlain("quotation", "basic", 3, showBorder: true);
        var large = RenderPlain("quotation", "large", 3, showBorder: true);

        Assert.False(SamePaper(basic, large),
            "견적서 large 와 basic 이 같은 종이다 — 그림 대조 자가 차이를 못 본다(ⓒ 가 거짓 초록일 수 있다).");
    }

    // ══════════════════════════════════════════════════════════════════
    // ⓓ 저장 문 허용 4값 (E-5)
    // ══════════════════════════════════════════════════════════════════

    private static MethodInfo SaveStyleResolver =>
        typeof(FormTemplateService).GetMethod("ResolveStyleKeyToSave", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ResolveStyleKeyToSave 가 없다 — 배선이 바뀌었다.");

    [Theory(DisplayName = "E⑵ 고침1 ⓓ 🔴 저장 문 스타일 — 빈 값 · 없음 = basic(종전 그대로) · 허용 4값 = 그 글자 그대로")]
    [InlineData(null, "basic")]
    [InlineData("", "basic")]
    [InlineData("   ", "basic")]
    [InlineData("basic", "basic")]
    [InlineData("formal", "formal")]
    [InlineData("simple", "simple")]
    [InlineData("large", "large")]
    public void D_SaveStyleAllowedOrBlank(string? input, string expected)
    {
        var got = (string)SaveStyleResolver.Invoke(null, new object?[] { input })!;
        Assert.Equal(expected, got);
    }

    [Theory(DisplayName = "E⑵ 고침1 ⓓ 🔴 저장 문 스타일 — 허용 4값 밖은 **사람 말 거절**(InvalidOperationException = 400 · 500 0) · 입력 값 되적지 않음")]
    [InlineData("modern")]
    [InlineData("FORMAL")]
    [InlineData(" large")]
    [InlineData("아무거나")]
    [InlineData("abcdefghijklmnopqrstu")] // 21자 — 출하 DDL varchar(20) 을 넘는 값
    public void D_SaveStyleOutsideRejected(string input)
    {
        var ex = Assert.Throws<TargetInvocationException>(() => SaveStyleResolver.Invoke(null, new object?[] { input }));
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("양식 스타일", inner.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(input.Trim(), inner.Message, StringComparison.Ordinal);
    }

    private static (FormTemplateService Svc, Mock<IUnitOfWork> Uow) MakeFormTemplateService()
    {
        var tx = new Mock<ISharedTransaction>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tx.Object);
        return (new FormTemplateService(uow.Object, NullLogger<FormTemplateService>.Instance), uow);
    }

    [Theory(DisplayName = "E⑵ 고침1 ⓓ 🔴 저장 문 배선 — 만들기에 4값 밖 스타일 = DB 줄 **전** 거절(행 0)")]
    [InlineData("modern")]
    [InlineData("abcdefghijklmnopqrstu")]
    public async Task D_CreateRejectsBeforeDb(string styleKey)
    {
        var (svc, uow) = MakeFormTemplateService();
        var req = new CreateFormTemplateRequest
        {
            FormType = "quotation",
            TemplateName = "T",
            PaperMode = "plain",
            StyleKey = styleKey,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync("tenant-1", req));
        Assert.Contains("양식 스타일", ex.Message, StringComparison.Ordinal);
        uow.Verify(u => u.GetDbConnection(), Times.Never());
    }

    [Theory(DisplayName = "E⑵ 고침1 ⓓ 🔴 저장 문 배선 — 고치기에 4값 밖 스타일 = DB 줄 **전** 거절(행 0)")]
    [InlineData("modern")]
    [InlineData("abcdefghijklmnopqrstu")]
    public async Task D_UpdateRejectsBeforeDb(string styleKey)
    {
        var (svc, uow) = MakeFormTemplateService();
        var req = new UpdateFormTemplateRequest
        {
            FormType = "quotation",
            TemplateName = "T",
            PaperMode = "plain",
            StyleKey = styleKey,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateAsync("tenant-1", "template-1", req));
        Assert.Contains("양식 스타일", ex.Message, StringComparison.Ordinal);
        uow.Verify(u => u.GetDbConnection(), Times.Never());
    }
}
