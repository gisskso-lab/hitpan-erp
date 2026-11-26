using System.Data;
using System.Reflection;
using HitPan.API.Controllers;
using HitPan.Application.DTOs.Documents;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴🔴 W-G1-51 갈래 「§59 렌더」 게이트 — 빈 A4 + 양식 그림 + 칸 0 을 전표 [인쇄] 하면 <b>그림만</b>(대표 §59 「그림만 + 안내」).
/// </summary>
/// <remarks>
/// 🔴 근거 = 작업지시서 <c>20261120작_WG1-51_갈래59_그림만안내렌더_작업지시서.md</c> §5 · PM 결재 ① (가) · ⑥ (나) ·
///   개발명세서 <c>20261120작_WG1-51_갈래59_그림만안내_개발명세서.md</c> §5(기대 글자 · 코드 앞 커밋).
/// 🔴 <b>동작 시험</b>이다 — 제품 <c>PdfRenderService.RenderDocumentAsync</c>(5인자)를 직접 불러 PDF 를 그리고 PdfPig 로 읽는다.
///   양식은 <c>IFormTemplateService</c> 가짜가 돌려준 행(= 전표 [인쇄] 문이 양식을 읽는 길 그대로) · 전표 자료는 예시 자료(DB 0 ·
///   자료가 <b>있어야</b> 「글자 0」 이 뜻을 가진다) · DB 는 가짜(회사 읽기 실패 → 기존 대체 글자 · 워터마크는 양식이 「끔」 을 정해 DB 안 탐).
/// 🔴 한글 낱말은 PdfPig 로 안 읽힌다(글꼴 부분 넣기 · <c>TaxInvoicePdfRenderGuardTests</c> 주석) ⇒ 「견적서」 · 자사 이름은
///   <b>글리프 수 0</b> 으로 보고, 기본 폼 표지는 영문 쪽번호 「Page」 · 좌표 칸 표지는 예시 등록번호 「000-00-00000」 으로 본다.
/// 🔴 시험 안에서 제품 줄을 복제하지 않는다. T12 의 줄 단위 변이(머리글 · 쪽번호 줄의 <c>&amp;&amp; !noCoordsImageOnly</c> 지우기)는
///   제품 인자로 끌 수 없어 [4] 몫으로 넘겼다(개발명세서 §5) — 여기 T12 는 「같은 차림 · 판정만 끈 종이에 T1 자를 대면 빨강」 이다.
/// </remarks>
public sealed class FormNoCoordsImageOnlyGateTests
{
    private const string Tenant = "gate-59-tenant";

    /// <summary>작성기가 실제로 받아들이는 1x1 PNG(<c>FormPreviewOverrideGuardTests</c> 와 같은 바이트).</summary>
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    /// <summary>예시 자료의 거래처 등록번호(영문 · 숫자라 PdfPig 로 읽힌다) — 좌표 칸 하나에 찍힌다.</summary>
    private const string SamplePartnerBizNo = "000-00-00000";

    private const string OneCoordJson = "[{\"key\":\"partner_business_no\",\"x_mm\":30,\"y_mm\":40}]";

    // ══════════════════════════════════════════════════════════════════
    // 자 — 그림 · 양식 · 서비스 · 판독
    // ══════════════════════════════════════════════════════════════════

    private static string BackgroundRoot => FormBackgroundPaths.Root;

    /// <summary>허용 폴더 <b>안</b>에 읽히는 PNG 를 놓는다.</summary>
    private static string WriteValidPng()
    {
        Directory.CreateDirectory(BackgroundRoot);
        var p = Path.Combine(BackgroundRoot, $"gate59-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(p, Convert.FromBase64String(OnePixelPngBase64));
        return p;
    }

    /// <summary>허용 폴더 안이지만 <b>없는</b> 그림 경로(T8 — 파일이 없어진 양식).</summary>
    private static string MissingPng() => Path.Combine(BackgroundRoot, $"gate59-missing-{Guid.NewGuid():N}.png");

    private static void Remove(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[gate59] 시험 그림을 못 지웠다(시험 판정과 무관): {ex.Message}");
        }
    }

    /// <summary>
    /// 양식 행. 🔴 <c>ShowWatermark = false</c> — 워터마크는 고객 설정대로(PM ⑤)이고, 양식이 「끔」 을 정하면
    /// 공통 설정(DB)을 읽지 않는다(<c>ShouldLayWatermark</c>). 그래서 이 묶음은 DB 없이 돈다.
    /// </summary>
    private static FormTemplateDto Template(string paperMode = "plain", bool printBackground = true,
        string? background = null, string? coordsJson = null, string styleKey = "basic") => new()
    {
        FormType = "estimate",
        TemplateName = "gate59",
        PaperMode = paperMode,
        PrintBackground = printBackground,
        BackgroundImageUrl = background,
        FieldCoordsJson = coordsJson,
        StyleKey = styleKey,
        ShowWatermark = false,
    };

    private static PdfRenderService MakeService(FormTemplateDto? template, bool throwOnLoad = false)
    {
        var forms = new Mock<IFormTemplateService>();
        var setup = forms.Setup(f => f.GetDefaultAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()));
        if (throwOnLoad)
            setup.ThrowsAsync(new InvalidOperationException("gate59 양식 읽기 실패"));
        else
            setup.ReturnsAsync(template);
        return new PdfRenderService(new Mock<IDbConnection>().Object, NullLogger<PdfRenderService>.Instance, forms.Object);
    }

    /// <summary>
    /// 전표 [인쇄] 문 꼴 — <c>NoCoordsImageOnly</c> 참 · 설정 미리보기 아님. 자료는 예시 자료(DB 0).
    /// <paramref name="noCoords"/> 거짓 = 메일 · 팩스 · 4인자 꼴(새 칸을 안 세움).
    /// </summary>
    private static PdfPreviewOverrides PrintDoor(bool noCoords = true, bool designPreview = false) => new()
    {
        UseSampleData = true,
        NoCoordsImageOnly = noCoords,
        IsFormDesignPreview = designPreview,
    };

    private static async Task<byte[]> RenderAsync(FormTemplateDto? template, string documentType = "quotation",
        PdfPreviewOverrides? overrides = null)
    {
        var svc = MakeService(template);
        var (bytes, _) = await svc.RenderDocumentAsync(Tenant, documentType, "gate59-doc", overrides ?? PrintDoor());
        return bytes;
    }

    private sealed record Paper(int Pages, int Images, int Glyphs, string Text)
    {
        public bool HasPageNumber => Text.Contains("Page", StringComparison.Ordinal);
    }

    private static Paper Read(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var pages = doc.GetPages().ToList();
        return new Paper(
            pages.Count,
            pages.Sum(p => p.GetImages().Count()),
            pages.Sum(p => p.Letters.Count),
            string.Concat(pages.Select(p => p.Text)));
    }

    /// <summary>🔴 T1 판독 자 — 그림만인가: 쪽 1 · 그림 ≥1 · 글리프 0 · 쪽번호 0.</summary>
    private static bool IsImageOnly(Paper p) =>
        p.Pages == 1 && p.Images >= 1 && p.Glyphs == 0 && !p.HasPageNumber;

    private static string Describe(Paper p) =>
        $"쪽 {p.Pages} · 그림 {p.Images} · 글리프 {p.Glyphs} · 쪽번호 {(p.HasPageNumber ? "있음" : "없음")}";

    // ══════════════════════════════════════════════════════════════════
    // T1 · T1+ · T10 — K3 = 그림만
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "§59 T1 🔴 K3(빈 A4 · 저장된 그림 찍기 켬 · 그림 읽힘 · 칸 0 · 견적서) 전표 [인쇄] = 쪽 1 · 그림 ≥1 · 글리프 0 · 「Page」 0")]
    public async Task T1_K3_PrintDoor_ImageOnly()
    {
        var png = WriteValidPng();
        try
        {
            var paper = Read(await RenderAsync(Template(background: png)));
            Assert.True(IsImageOnly(paper), $"K3 종이가 그림만이 아니다 — {Describe(paper)}");
        }
        finally { Remove(png); }
    }

    [Theory(DisplayName = "§59 T1+ 🔴 K3 = 7종 한 벌 — 수주서 · 거래명세서 · 발주서 · 매입명세서 · 매입반품 · 판매반품도 그림만")]
    [InlineData("sales_order")]
    [InlineData("delivery")]
    [InlineData("purchase_order")]
    [InlineData("purchase_receipt")]
    [InlineData("purchase_return")]
    [InlineData("sales_return")]
    public async Task T1plus_K3_OtherTypes_ImageOnly(string documentType)
    {
        var png = WriteValidPng();
        try
        {
            var paper = Read(await RenderAsync(Template(background: png), documentType));
            Assert.True(IsImageOnly(paper), $"{documentType} K3 종이가 그림만이 아니다 — {Describe(paper)}");
        }
        finally { Remove(png); }
    }

    [Fact(DisplayName = "§59 T10 🔴 K3 + 스타일 formal = T1 과 같음(그림 있는 양식은 스타일 무시)")]
    public async Task T10_K3_StyleIgnored()
    {
        var png = WriteValidPng();
        try
        {
            var paper = Read(await RenderAsync(Template(background: png, styleKey: "formal")));
            Assert.True(IsImageOnly(paper), $"K3 + formal 종이가 그림만이 아니다 — {Describe(paper)}");
        }
        finally { Remove(png); }
    }

    // ══════════════════════════════════════════════════════════════════
    // T2~T9 · T7b — K3 밖은 지금 그대로(#1)
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "§59 T2 🔴 K3 + 칸 1개(Y) = 좌표 경로 그대로 — 칸 값 있음 · 그림 있음 · 쪽번호 0")]
    public async Task T2_Y_WithOneCoord_StaysCoords()
    {
        var png = WriteValidPng();
        try
        {
            var paper = Read(await RenderAsync(Template(background: png, coordsJson: OneCoordJson)));
            Assert.Contains(SamplePartnerBizNo, paper.Text, StringComparison.Ordinal);
            Assert.True(paper.Images >= 1, $"Y 종이에 그림이 없다 — {Describe(paper)}");
            Assert.False(paper.HasPageNumber, $"Y 종이에 쪽번호가 찍혔다(좌표 경로 아님) — {Describe(paper)}");
        }
        finally { Remove(png); }
    }

    [Fact(DisplayName = "§59 T3 🔴 K4(옛 칸 · 그림 0 · 저장 끔) = 기본 폼 그대로 — 「Page」 있음 · 그림 0")]
    public async Task T3_K4_BasicForm()
    {
        var paper = Read(await RenderAsync(Template(printBackground: false, coordsJson: OneCoordJson)));
        Assert.True(paper.HasPageNumber, $"K4 가 기본 폼이 아니다 — {Describe(paper)}");
        Assert.True(paper.Glyphs > 0, $"K4 종이에 글자가 없다 — {Describe(paper)}");
        Assert.Equal(0, paper.Images);
    }

    [Fact(DisplayName = "§59 T4 🔴 K3 + 설정 미리보기(IsFormDesignPreview) = 지금 그대로 — 그림 + 기본 폼")]
    public async Task T4_DesignPreview_Unchanged()
    {
        var png = WriteValidPng();
        try
        {
            var paper = Read(await RenderAsync(Template(background: png), overrides: PrintDoor(designPreview: true)));
            Assert.True(paper.Images >= 1, $"설정 미리보기에 그림이 없다 — {Describe(paper)}");
            Assert.True(paper.HasPageNumber, $"설정 미리보기가 기본 폼이 아니다 — {Describe(paper)}");
        }
        finally { Remove(png); }
    }

    [Fact(DisplayName = "§59 T5 🔴 K3 + NoCoordsImageOnly 거짓(메일 · 팩스 · 4인자 꼴) = 지금 그대로 — 그림 + 기본 폼")]
    public async Task T5_NotPrintDoor_Unchanged()
    {
        var png = WriteValidPng();
        try
        {
            var paper = Read(await RenderAsync(Template(background: png), overrides: PrintDoor(noCoords: false)));
            Assert.True(paper.Images >= 1, $"메일 · 팩스 꼴 종이에 그림이 없다 — {Describe(paper)}");
            Assert.True(paper.HasPageNumber, $"메일 · 팩스 꼴 종이가 기본 폼이 아니다 — {Describe(paper)}");
        }
        finally { Remove(png); }
    }

    [Fact(DisplayName = "§59 T6 🔴 양식지 + 그림 + 칸 0 = 지금 그대로 — 그림 0 · 데이터 있음")]
    public async Task T6_Preprint_Unchanged()
    {
        var png = WriteValidPng();
        try
        {
            var paper = Read(await RenderAsync(Template(paperMode: "preprint", background: png)));
            Assert.Equal(0, paper.Images);
            Assert.True(paper.Glyphs > 0, $"양식지 종이에 데이터가 없다 — {Describe(paper)}");
        }
        finally { Remove(png); }
    }

    [Fact(DisplayName = "§59 T7 🔴 세금계산서 K3 = 법정 작성기 그대로 — 글리프 있음 · 그림 0")]
    public async Task T7_TaxInvoice_LegalComposer()
    {
        var png = WriteValidPng();
        try
        {
            var paper = Read(await RenderAsync(Template(background: png), "tax_invoice"));
            Assert.True(paper.Glyphs > 0, $"세금계산서 K3 종이에 글자가 없다 — {Describe(paper)}");
            Assert.Equal(0, paper.Images);
        }
        finally { Remove(png); }
    }

    [Theory(DisplayName = "§59 T7b 🔴 면세계산서 · 입금표 K3 = 지금 그대로(PM ⑥ (나)) — 그림 + 기본 폼")]
    [InlineData("invoice_exempt")]
    [InlineData("payment_receipt")]
    public async Task T7b_LegalCandidates_Unchanged(string documentType)
    {
        var png = WriteValidPng();
        try
        {
            var paper = Read(await RenderAsync(Template(background: png), documentType));
            Assert.True(paper.Images >= 1, $"{documentType} K3 종이에 그림이 없다 — {Describe(paper)}");
            Assert.True(paper.HasPageNumber, $"{documentType} K3 가 기본 폼이 아니다(그림만으로 바뀜) — {Describe(paper)}");
        }
        finally { Remove(png); }
    }

    [Fact(DisplayName = "§59 T8 🔴 K3 인데 그림 파일 없음 = 기본 폼(데이터 있는 종이 · #20) — 「Page」 있음 · 그림 0")]
    public async Task T8_MissingImage_BasicForm()
    {
        var paper = Read(await RenderAsync(Template(background: MissingPng())));
        Assert.True(paper.HasPageNumber, $"그림 없는 K3 가 기본 폼이 아니다 — {Describe(paper)}");
        Assert.Equal(0, paper.Images);
    }

    [Fact(DisplayName = "§59 T9 🔴 저장 끔 + 그림 + 칸 0 = 기본 폼 · 그림 0")]
    public async Task T9_SavedOff_BasicForm()
    {
        var png = WriteValidPng();
        try
        {
            var paper = Read(await RenderAsync(Template(printBackground: false, background: png)));
            Assert.True(paper.HasPageNumber, $"저장 끔 양식이 기본 폼이 아니다 — {Describe(paper)}");
            Assert.Equal(0, paper.Images);
        }
        finally { Remove(png); }
    }

    // ══════════════════════════════════════════════════════════════════
    // T11 — 판정 유일 자리(IsNoCoordsImageOnly) 표
    // ══════════════════════════════════════════════════════════════════

    private static MethodInfo Judge =>
        typeof(PdfRenderService).GetMethod("IsNoCoordsImageOnly", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsNoCoordsImageOnly 가 없다 — §59 판정 자리가 사라졌다.");

    private static object TemplateInfo(FormTemplateDto dto) =>
        (typeof(PdfRenderService).GetMethod("ToTemplateInfo", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ToTemplateInfo 가 없다 — 배선이 바뀌었다."))
        .Invoke(MakeService(null), new object?[] { dto })!;

    private static bool JudgeOf(string documentType, PdfPreviewOverrides? overrides, bool isYTemplate, FormTemplateDto dto) =>
        (bool)Judge.Invoke(null, new object?[] { documentType, overrides, isYTemplate, TemplateInfo(dto) })!;

    [Fact(DisplayName = "§59 T11 🔴 판정 표 — T1 · T1+ · T10 차림만 참 · T2~T9 · T7b · overrides 없음 = 거짓")]
    public void T11_JudgementTable()
    {
        const string bg = "gate59.png";   // 판정은 경로 글자만 본다 — 그림 읽힘은 렌더가 isYTemplate 로 넘긴다.
        var k3 = Template(background: bg);

        // 참 — T1 · T1+(7종) · T10
        foreach (var t in new[] { "quotation", "sales_order", "delivery", "purchase_order", "purchase_receipt", "purchase_return", "sales_return" })
            Assert.True(JudgeOf(t, PrintDoor(), true, k3), $"{t} K3 판정이 거짓이다.");
        Assert.True(JudgeOf("quotation", PrintDoor(), true, Template(background: bg, styleKey: "formal")), "T10 판정이 거짓이다(스타일은 판정 인자 아님).");

        // 거짓
        Assert.False(JudgeOf("quotation", PrintDoor(), true, Template(background: bg, coordsJson: OneCoordJson)), "T2 Y(칸 1)가 참이다.");
        Assert.False(JudgeOf("quotation", PrintDoor(), false, Template(printBackground: false, coordsJson: OneCoordJson)), "T3 K4 가 참이다.");
        Assert.False(JudgeOf("quotation", PrintDoor(designPreview: true), true, k3), "T4 설정 미리보기가 참이다.");
        Assert.False(JudgeOf("quotation", PrintDoor(noCoords: false), true, k3), "T5 메일 · 팩스 꼴이 참이다.");
        Assert.False(JudgeOf("quotation", null, true, k3), "overrides 없음(4인자 호출)이 참이다.");
        Assert.False(JudgeOf("quotation", PrintDoor(), false, Template(paperMode: "preprint", background: bg)), "T6 양식지가 참이다.");
        Assert.False(JudgeOf("tax_invoice", PrintDoor(), true, k3), "T7 세금계산서가 참이다.");
        Assert.False(JudgeOf("invoice_exempt", PrintDoor(), true, k3), "T7b 면세계산서가 참이다(PM ⑥ (나)).");
        Assert.False(JudgeOf("payment_receipt", PrintDoor(), true, k3), "T7b 입금표가 참이다(PM ⑥ (나)).");
        Assert.False(JudgeOf("quotation", PrintDoor(), false, k3), "T8 그림 못 읽음(Y 판정 거짓)이 참이다.");
        Assert.False(JudgeOf("quotation", PrintDoor(), false, Template(printBackground: false, background: bg)), "T9 저장 끔이 참이다.");
    }

    // ══════════════════════════════════════════════════════════════════
    // T12 — 음성 대조군: T1 자가 무는가
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "§59 T12 음성대조군 🔴 같은 K3 양식 · 같은 자료에서 판정만 끈 종이에 T1 자를 대면 빨강")]
    public async Task T12_NegativeControl_JudgeBites()
    {
        var png = WriteValidPng();
        try
        {
            var template = Template(background: png);
            var on = Read(await RenderAsync(template));
            var off = Read(await RenderAsync(template, overrides: PrintDoor(noCoords: false)));

            Assert.True(IsImageOnly(on), $"양성 — 판정 켬 종이가 그림만이 아니다 — {Describe(on)}");
            Assert.False(IsImageOnly(off), $"음성 — 판정 끈 종이(머리글 · 본문 · 쪽번호 있음)를 T1 자가 「그림만」 이라 했다 — 자가 안 문다 — {Describe(off)}");
            Assert.True(off.Glyphs > 0 && off.HasPageNumber, $"음성 종이가 기본 폼이 아니다 — {Describe(off)}");
        }
        finally { Remove(png); }
    }

    // ══════════════════════════════════════════════════════════════════
    // N1 — 안내 판정(서비스) · N2 — 안내 문(컨트롤러) · N3 — 인쇄 문이 칸을 세운다
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "§59 N1 🔴 안내 판정 — K3 참 · K4 · Y · 양식지 · 세금계산서 · 면세계산서 · 입금표 · 그림 없음 거짓")]
    public async Task N1_NoticeJudgement_FollowsRenderJudgement()
    {
        var png = WriteValidPng();
        try
        {
            Assert.True(await MakeService(Template(background: png)).IsNoCoordsImageOnlyAsync(Tenant, "quotation"));

            Assert.False(await MakeService(Template(printBackground: false, coordsJson: OneCoordJson)).IsNoCoordsImageOnlyAsync(Tenant, "quotation"));
            Assert.False(await MakeService(Template(background: png, coordsJson: OneCoordJson)).IsNoCoordsImageOnlyAsync(Tenant, "quotation"));
            Assert.False(await MakeService(Template(paperMode: "preprint", background: png)).IsNoCoordsImageOnlyAsync(Tenant, "quotation"));
            Assert.False(await MakeService(Template(background: png)).IsNoCoordsImageOnlyAsync(Tenant, "tax_invoice"));
            Assert.False(await MakeService(Template(background: png)).IsNoCoordsImageOnlyAsync(Tenant, "invoice_exempt"));
            Assert.False(await MakeService(Template(background: png)).IsNoCoordsImageOnlyAsync(Tenant, "payment_receipt"));
            Assert.False(await MakeService(Template(background: MissingPng())).IsNoCoordsImageOnlyAsync(Tenant, "quotation"));
            Assert.False(await MakeService(Template(printBackground: false, background: png)).IsNoCoordsImageOnlyAsync(Tenant, "quotation"));
        }
        finally { Remove(png); }
    }

    [Fact(DisplayName = "§59 N1 🔴 안내 판정 — 양식 서비스 없음 · 양식 읽기 예외 = 거짓 · 예외 안 남(#20)")]
    public async Task N1_NoticeJudgement_FailsClosed()
    {
        var noForms = new PdfRenderService(new Mock<IDbConnection>().Object, NullLogger<PdfRenderService>.Instance, null);
        Assert.False(await noForms.IsNoCoordsImageOnlyAsync(Tenant, "quotation"));

        var throwing = MakeService(null, throwOnLoad: true);
        var ex = await Record.ExceptionAsync(() => throwing.IsNoCoordsImageOnlyAsync(Tenant, "quotation"));
        Assert.Null(ex);
        Assert.False(await throwing.IsNoCoordsImageOnlyAsync(Tenant, "quotation"));
    }

    private static DocumentController MakeController(IPdfRenderService pdf, string? tenant)
    {
        var controller = new DocumentController(null!, null!, null!, null!, null!, null!, null!, pdf);
        var http = new DefaultHttpContext();
        if (tenant is not null) http.Items["TenantId"] = tenant;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    [Fact(DisplayName = "§59 N2 🔴 안내 문 — K3 = 200 noCoordsImageOnly 참(값 · 경로 응답 0)")]
    public async Task N2_NoticeDoor_K3_True()
    {
        var png = WriteValidPng();
        try
        {
            var result = await MakeController(MakeService(Template(background: png)), Tenant).PrintNotice("quotation");
            var ok = Assert.IsType<OkObjectResult>(result);
            var props = ok.Value!.GetType().GetProperties();
            var only = Assert.Single(props);
            Assert.Equal("noCoordsImageOnly", only.Name);
            Assert.True((bool)only.GetValue(ok.Value)!, "K3 양식인데 안내 문이 거짓을 말한다.");
        }
        finally { Remove(png); }
    }

    /// <summary>
    /// 문이 렌더 서비스에 무엇을 넘기나 적어 두는 가짜 — 기본 구현(DIM)에 기대지 않고 세 메서드를 모두 직접 구현한다.
    /// </summary>
    private sealed class RecordingPdf : IPdfRenderService
    {
        public PdfPreviewOverrides? Seen { get; private set; }
        public int NoticeCalls { get; private set; }

        public Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType,
            string documentId, CancellationToken ct = default)
            => Task.FromResult((new byte[] { 1 }, "gate59.pdf"));

        public Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType,
            string documentId, PdfPreviewOverrides? overrides, CancellationToken ct = default)
        {
            Seen = overrides;
            return Task.FromResult((new byte[] { 1 }, "gate59.pdf"));
        }

        public Task<bool> IsNoCoordsImageOnlyAsync(string tenantId, string documentType, CancellationToken ct = default)
        {
            NoticeCalls++;
            return Task.FromResult(false);
        }
    }

    [Theory(DisplayName = "§59 N2 🔴 안내 문 — 급여명세서 · 모르는 종 = 400 · 받은 글자 되적지 않음 · 판정 안 부름")]
    [InlineData("payslip")]
    [InlineData("gate59-unknown-type")]
    public async Task N2_NoticeDoor_RejectsPayslipAndUnknown(string type)
    {
        var pdf = new RecordingPdf();
        var result = await MakeController(pdf, Tenant).PrintNotice(type);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.DoesNotContain(type, System.Text.Json.JsonSerializer.Serialize(bad.Value), StringComparison.Ordinal);
        Assert.Equal(0, pdf.NoticeCalls);
    }

    [Fact(DisplayName = "§59 N2 🔴 안내 문 — tenant 없음 = Forbid(#2 · JWT 클레임에서만)")]
    public async Task N2_NoticeDoor_NoTenant_Forbid()
    {
        var pdf = new RecordingPdf();
        var result = await MakeController(pdf, null).PrintNotice("quotation");
        Assert.IsType<ForbidResult>(result);
        Assert.Equal(0, pdf.NoticeCalls);
    }

    [Fact(DisplayName = "§59 N3 🔴 전표 [인쇄] 문(PreviewPdf)이 NoCoordsImageOnly 를 실제로 세운다 — 설정 미리보기 아님(PM ① (가))")]
    public async Task N3_PrintDoor_SetsNoCoordsImageOnly()
    {
        var pdf = new RecordingPdf();
        var result = await MakeController(pdf, Tenant)
            .PreviewPdf("quotation", "gate59-doc", null, NullLogger<DocumentController>.Instance);

        Assert.IsType<FileContentResult>(result);
        Assert.NotNull(pdf.Seen);
        Assert.True(pdf.Seen!.NoCoordsImageOnly, "전표 [인쇄] 문이 그림만 판정 칸을 안 세운다 — K3 가 기본 폼으로 나간다.");
        Assert.False(pdf.Seen.IsFormDesignPreview);
    }
}
