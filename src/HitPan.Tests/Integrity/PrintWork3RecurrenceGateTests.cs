using System.Data;
using System.Reflection;
using System.Text;
using HitPan.Application.DTOs.Documents;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 인쇄 작3 갈래 D — 재발 방지 시험 <b>뼈대</b> (기대값 먼저).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>기대값 진실원</b> = <c>docs/검증/erp/20261119_WG1-51_갈래D_재발방지_기대값.md</c> §2.
/// 시험 이름 머리의 ID(A-T1 · K4-가 · Y-가 …)는 그 표의 ID 와 <b>글자까지 같다</b>.
/// 기대값은 K4 구별 기준(PM 결재 11/19 · 기준 C)으로 썼다 —
/// <b>Y 템플릿</b> = <c>plain</c> + 저장값 <c>print_background</c>=1 + 그림 경로 + 좌표 1개 이상 + 그림이 실제로 읽힘 → 칸 모양.
/// 양식지 = 지금처럼 칸 모양. 그 밖 빈 A4 = main 처럼 기본 폼.
/// </para>
/// <para>
/// 🔴 <b>main 바탕(갈래 D 가지)에서 빨강 예상 9</b> — A-T1 · A-T5 · A-T10 · A-T12 · Y-가 · Y-가′ · Y-다 · Y-40 · K1-1.
/// 이 9건은 「고침이 실려야 지나가는」 대조군이다(설계 §4 T-R4). main 에서 초록이면 대조군이 죽은 것 — 시험을 고친다.
/// 그 밖 16건은 main 에서도 초록이어야 하는 회귀 칸이다.
/// </para>
/// <para>
/// 🔴 <b>판독 입구</b> — (반사) 비공개 함수 직접 · (렌더) <c>RenderDocumentAsync</c> 를
/// 예시 자료(<c>UseSampleData</c>) + 차림 양식(<c>Template</c>) + <b>「열린 척하는 빈 연결」</b>로 부른다.
/// 회사 정보 읽기는 실패해 기존 catch 길(「(회사 정보 없음)」)로 떨어지고, 전표는 예시 자료라 DB 를 안 본다.
/// 🔴 이 차림이 실제로 catch 길로만 가는지는 <b>미측정</b>(이 PC 시험 실행 금지) — 첫 실행 = 합류 직후 CI(T-R1).
/// 예외로 PDF 가 안 나오면 「차림 불가」 로 따로 센다(빨강에 넣지 않음 · 기대값 문서 §4).
/// </para>
/// <para>
/// 🔴 제품 코드 0 · 기존 시험 파일 0줄 수정(#1) · DB · 앱 · 네트워크 0.
/// </para>
/// </remarks>
public sealed class PrintWork3RecurrenceGateTests
{
    private static readonly Type SvcType = typeof(PdfRenderService);

    private static readonly Type InfoType =
        SvcType.GetNestedType("FormTemplateInfo", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfRenderService.FormTemplateInfo 가 없다 — 양식 차림 모양이 바뀌었다.");

    private static readonly MethodInfo ResolveMethod =
        SvcType.GetMethod("ResolveFormBackground", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfRenderService.ResolveFormBackground(…) 가 없다.");

    private static readonly MethodInfo ApplyOptionsMethod =
        SvcType.GetMethod("ApplyPrintOptions", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfRenderService.ApplyPrintOptions(…) 가 없다.");

    /// <summary>작성기가 실제로 받아들이는 1x1 PNG (기존 <c>FormPreviewOverrideGuardTests</c> 와 같은 바이트).</summary>
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    // ── 판독 표지 (공백을 지운 글자로 본다 · 줄 번호 = main 09b63c2f) ─────────────
    /// <summary>기본 폼 본문 표지 — <c>ComposeBody</c> 「[ 거래처 정보 ]」 (:312).</summary>
    private const string BasicBodyMark = "[거래처정보]";
    /// <summary>머리 표지 — 「문서번호: …」 (:274). 머리글을 그릴 때만 나온다.</summary>
    private const string HeaderMark = "문서번호:";
    /// <summary>쪽번호 표지 — 「Page n / m」 (:243).</summary>
    private const string PageNoMark = "Page";
    /// <summary>예시 자료 거래처 이름 — 칸 모양에서도 거래처 칸에 찍힌다(BuildSampleSnapshot).</summary>
    private const string PartnerMark = "(예시)거래처명";
    /// <summary>세금계산서 법정 작성기 표지 — 「세 금 계 산 서」 (:1600). 🔴 CI 첫 런에서 표지 확인(미확인).</summary>
    private const string TaxFormMark = "세금계산서";

    // ═══════════════════════════════════════════════════════════════
    // 차림 도우미
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 「열린 척하는 빈 연결」 — <c>State=Open</c> 이라 여는 시도를 안 하고(DbConnection 아님),
    /// 명령을 만들 수 없어 모든 조회가 그 자리의 catch 로 떨어진다. 🔴 네트워크 · 3306 접속 0.
    /// </summary>
    private static IDbConnection OpenLookingConnection()
    {
        var m = new Mock<IDbConnection>();
        m.SetupGet(c => c.State).Returns(ConnectionState.Open);
        return m.Object;
    }

    private static PdfRenderService NewService() =>
        new(OpenLookingConnection(), NullLogger<PdfRenderService>.Instance);

    /// <summary>시험 하나에 테넌트 폴더 하나 — 그림 파일을 놓고 끝나면 지운다.</summary>
    private sealed class TenantBackground : IDisposable
    {
        public string Tenant { get; } = $"gate-d-{Guid.NewGuid():N}";
        public string Dir { get; }

        public TenantBackground()
        {
            Dir = FormBackgroundPaths.TenantRoot(Tenant);
            Directory.CreateDirectory(Dir);
        }

        /// <summary>읽히는 PNG 를 놓고 경로를 준다.</summary>
        public string WritePng(string name = "bg.png")
        {
            var p = Path.Combine(Dir, name);
            File.WriteAllBytes(p, Convert.FromBase64String(OnePixelPngBase64));
            return p;
        }

        /// <summary>경로만 있고 파일은 없는 자리(Y-나 · 그림 파일을 지운 차림).</summary>
        public string MissingPath() => Path.Combine(Dir, "deleted.png");

        public void Dispose()
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        }
    }

    /// <summary>칸 좌표 JSON — 기본 N=5(거래처 · 일자 · 품목 1행 · 수량 1행 · 합계 · 설계 §1-2 F8 하한).</summary>
    private static string Coords(bool knownKeys = true, int? maxRows = null)
    {
        var keys = knownKeys
            ? new[] { "거래처", "일자", "line.품목", "line.수량", "합계" }
            : new[] { "모르는칸1", "모르는칸2", "모르는칸3", "모르는칸4", "모르는칸5" };
        var parts = new List<string>();
        var y = 30;
        foreach (var k in keys)
        {
            parts.Add("{\"key\":\"" + k + "\",\"x_mm\":20,\"y_mm\":" + y + "}");
            y += 20;
        }
        if (maxRows is int m)
            parts.Add("{\"key\":\"max_rows\",\"x_mm\":" + m + ",\"y_mm\":0}");
        return "[" + string.Join(",", parts) + "]";
    }

    /// <summary>
    /// 양식 차림 — 🔴 공통 고정값(리뷰서 #7): <c>ShowCompanyLogo=true</c> · <c>ShowWatermark=false</c> · <c>ShowBorder=true</c>.
    /// </summary>
    private static FormTemplateDto Form(string formType, string paperMode, bool printBackground,
        string? backgroundUrl, string? coordsJson) => new()
    {
        FormType = formType,
        PaperMode = paperMode,
        PrintBackground = printBackground,
        BackgroundImageUrl = backgroundUrl,
        FieldCoordsJson = coordsJson,
        ShowCompanyLogo = true,
        ShowBorder = true,
        ShowWatermark = false,
    };

    /// <summary>Y 템플릿 차림 — 네 칸을 다 채움(갈래 F-1 조건 ①).</summary>
    private static FormTemplateDto YTemplate(string backgroundUrl, string? coordsJson = null) =>
        Form("delivery", "plain", printBackground: true, backgroundUrl, coordsJson ?? Coords());

    private static async Task<byte[]> RenderAsync(string tenant, string documentType, FormTemplateDto form,
        int lines, PrintOptionOverrides? options = null)
    {
        var ov = new PdfPreviewOverrides
        {
            UseSampleData = true,
            SampleLineCount = lines,
            MarkAsSample = false,
            Template = form,
            Options = options,
        };
        var (bytes, _) = await NewService()
            .RenderDocumentAsync(tenant, documentType, "gate-d-doc", ov, CancellationToken.None)
            .ConfigureAwait(false);
        return bytes;
    }

    private sealed record PdfRead(string Text, int Pages, int Images);

    private static PdfRead ReadPdf(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        var images = 0;
        foreach (var p in doc.GetPages())
        {
            sb.Append(p.Text);
            images += p.GetImages().Count();
        }
        var squashed = new string(sb.ToString().Where(c => !char.IsWhiteSpace(c)).ToArray());
        return new PdfRead(squashed, doc.NumberOfPages, images);
    }

    private static bool Has(PdfRead r, string mark) => r.Text.IndexOf(mark, StringComparison.Ordinal) >= 0;

    /// <summary>기본 폼 = main 과 같은 종이 — 본문 표 · 머리 · 쪽번호 셋 다 있음.</summary>
    private static void AssertBasicForm(PdfRead r, string id)
    {
        Assert.True(Has(r, BasicBodyMark), $"{id}: 기본 폼 본문 「[ 거래처 정보 ]」 가 없다 — 칸 모양으로 나갔다.");
        Assert.True(Has(r, HeaderMark), $"{id}: 머리 「문서번호:」 가 없다 — 머리글이 안 그려졌다.");
        Assert.True(Has(r, PageNoMark), $"{id}: 쪽번호 「Page」 가 없다.");
    }

    /// <summary>칸 모양 = 원본 양식지 main 인쇄와 같은 모양 — 표 · 머리 · 쪽번호 0 · 칸 값 있음.</summary>
    private static void AssertCoordsForm(PdfRead r, string id)
    {
        Assert.False(Has(r, BasicBodyMark), $"{id}: 기본 폼 본문이 나왔다 — 칸 모양이어야 한다.");
        Assert.False(Has(r, HeaderMark), $"{id}: 머리 「문서번호:」 가 나왔다 — 칸 모양은 머리 0.");
        Assert.False(Has(r, PageNoMark), $"{id}: 쪽번호가 나왔다 — 칸 모양은 쪽번호 0.");
        Assert.True(Has(r, PartnerMark), $"{id}: 거래처 칸 값 「(예시) 거래처명」 이 없다 — 칸 값이 안 찍혔다.");
    }

    private static object Info(string paperMode, bool printBackground, string? backgroundUrl)
    {
        var o = Activator.CreateInstance(InfoType)!;
        InfoType.GetProperty("PaperMode")!.SetValue(o, paperMode);
        InfoType.GetProperty("PrintBackground")!.SetValue(o, printBackground);
        InfoType.GetProperty("BackgroundImageUrl")!.SetValue(o, backgroundUrl);
        return o;
    }

    private static byte[]? Resolve(PdfPreviewOverrides? ov, object? info) =>
        (byte[]?)ResolveMethod.Invoke(NewService(), new object?[] { ov, info });

    // ═══════════════════════════════════════════════════════════════
    // §2-1 그림 판정 (반사) — A-T1 ~ A-T5
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "A-T1 🔴 양식지 + 켬 + 읽히는 그림 → 인쇄 그림 null (:1969 안전망 · main 빨강 예상)")]
    public void AT1_PreprintPrintBackgroundOn_ResolvesNull()
    {
        using var t = new TenantBackground();
        Assert.Null(Resolve(null, Info("preprint", true, t.WritePng())));
    }

    [Fact(DisplayName = "A-T2 설계 미리보기는 양식지 + 켬이어도 그림을 깐다 (main 초록)")]
    public void AT2_DesignPreview_StillLaysImage()
    {
        using var t = new TenantBackground();
        var bytes = Resolve(new PdfPreviewOverrides { IsFormDesignPreview = true }, Info("preprint", true, t.WritePng()));
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes!);
    }

    [Fact(DisplayName = "A-T3 빈 A4 + 켬 + 읽히는 그림 → 인쇄 · 전표 미리보기 둘 다 그림 (main 초록)")]
    public void AT3_PlainPrintBackgroundOn_LaysImage_PrintAndSlipPreview()
    {
        using var t = new TenantBackground();
        var info = Info("plain", true, t.WritePng());
        Assert.NotNull(Resolve(null, info));
        Assert.NotNull(Resolve(new PdfPreviewOverrides { UseSampleData = false }, info));
    }

    [Fact(DisplayName = "A-T4 끈 양식은 빈 A4 · 양식지 모두 그림 null (회귀 · main 초록)")]
    public void AT4_PrintBackgroundOff_NullForBoth()
    {
        using var t = new TenantBackground();
        var png = t.WritePng();
        Assert.Null(Resolve(null, Info("plain", false, png)));
        Assert.Null(Resolve(null, Info("preprint", false, png)));
    }

    [Fact(DisplayName = "A-T5 🔴 옵션이 양식지에 켬을 얹어도 전표 미리보기 그림 0 (반사 조합 · main 빨강 예상)")]
    public void AT5_OptionTurnsOnPreprint_SlipPreviewStillNoImage()
    {
        using var t = new TenantBackground();
        var options = new PrintOptionOverrides { PrintBackground = true };
        var applied = ApplyOptionsMethod.Invoke(null, new object?[] { Info("preprint", false, t.WritePng()), options });
        Assert.NotNull(applied);
        Assert.Null(Resolve(new PdfPreviewOverrides { UseSampleData = false, Options = options }, applied));
    }

    // ═══════════════════════════════════════════════════════════════
    // §2-2 기본 폼 · 칸 모양 (렌더) — A-T6 ~ A-T12 · K4 · Y · K1
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "A-T6 좌표 남은 빈 A4(그림 0 · 끔 · 로고 머리 켬) = 기본 폼 = main 과 같음 (K4 기준 · main 초록)")]
    public async Task AT6_PlainWithCoords_NoImage_BasicForm()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "quotation", Form("quotation", "plain", false, null, Coords()), 2));
        AssertBasicForm(r, "A-T6");
        Assert.Equal(0, r.Images);
    }

    [Fact(DisplayName = "A-T7 좌표 0 빈 A4 = 기본 폼 · 그림 0 (회귀 · main 초록)")]
    public async Task AT7_PlainNoCoords_BasicForm()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "quotation", Form("quotation", "plain", false, null, null), 2));
        AssertBasicForm(r, "A-T7");
        Assert.Equal(0, r.Images);
    }

    [Fact(DisplayName = "A-T8a 양식지 + 좌표 + 끔 = 칸 모양 · 그림 0 (회귀 · 판독기 양성 대조 짝 · main 초록)")]
    public async Task AT8a_PreprintWithCoords_CoordsForm()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "quotation", Form("quotation", "preprint", false, t.WritePng(), Coords()), 2));
        AssertCoordsForm(r, "A-T8a");
        Assert.Equal(0, r.Images);
    }

    [Fact(DisplayName = "A-T8b 양식지 + 좌표 0 + 끔 = 고정 배치(머리 0 · 쪽번호 0) · 그림 0 (회귀 · main 초록)")]
    public async Task AT8b_PreprintNoCoords_FixedLayout()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "quotation", Form("quotation", "preprint", false, t.WritePng(), null), 2));
        Assert.False(Has(r, HeaderMark), "A-T8b: 양식지에 머리글이 나왔다.");
        Assert.False(Has(r, PageNoMark), "A-T8b: 양식지에 쪽번호가 나왔다.");
        Assert.Equal(0, r.Images);
    }

    [Fact(DisplayName = "A-T9 빈 A4 세금계산서 + 좌표 = 법정 작성기 그대로 (회귀 · main 초록 · 표지 CI 확인)")]
    public async Task AT9_PlainTaxInvoiceWithCoords_LegalWriter()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "tax_invoice", Form("tax_invoice", "plain", false, null, Coords()), 2));
        Assert.True(Has(r, TaxFormMark), "A-T9: 법정 서식 표지 「세 금 계 산 서」 가 없다 — 좌표 경로로 빠졌다.");
        Assert.False(Has(r, BasicBodyMark), "A-T9: 공용 기본 폼 본문이 나왔다 — 법정 작성기가 아니다.");
    }

    [Fact(DisplayName = "A-T10 🔴 양식지 세금계산서 + 켬 + 그림 → 그림 0 (main 빨강 예상)")]
    public async Task AT10_PreprintTaxInvoicePrintBackgroundOn_NoImage()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "tax_invoice", Form("tax_invoice", "preprint", true, t.WritePng(), Coords()), 2));
        Assert.Equal(0, r.Images);
    }

    [Fact(DisplayName = "A-T12 🔴 Y 차림 + 키가 하나도 안 맞는 좌표 = 그림만 있는 종이 · 예외 0 (Y 쪽 남는 결함 · main 빨강 예상)")]
    public async Task AT12_YTemplateUnknownKeys_ImageOnlyPage()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "delivery", YTemplate(t.WritePng(), Coords(knownKeys: false)), 2));
        Assert.False(Has(r, BasicBodyMark), "A-T12: 기본 폼이 나왔다 — Y 템플릿은 칸 모양이어야 한다.");
        Assert.True(r.Images >= 1, "A-T12: 그림이 없다 — 「빈 종이」 가 됐다.");
    }

    [Fact(DisplayName = "K4-가 빈 A4 + 좌표 + 그림 0 + 끔 = main 과 같은 종이 (main 초록 · A 첫 판 빨강)")]
    public async Task K4Ga_PlainCoordsNoImageOff_SameAsMain()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "delivery", Form("delivery", "plain", false, null, Coords()), 2));
        AssertBasicForm(r, "K4-가");
        Assert.Equal(0, r.Images);
    }

    [Fact(DisplayName = "K4-나 빈 A4 + 좌표 + 그림 경로 + 끔 = main 과 같음 · 그림 0 (main 초록 · A 첫 판 빨강)")]
    public async Task K4Na_PlainCoordsImageOff_SameAsMainNoImage()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "delivery", Form("delivery", "plain", false, t.WritePng(), Coords()), 2));
        AssertBasicForm(r, "K4-나");
        Assert.Equal(0, r.Images);
    }

    [Theory(DisplayName = "K4-다 K4-나 차림 계산서 · 영수증 = main 과 같음 (P2-1 닫힘 · main 초록 · A 첫 판 빨강)")]
    [InlineData("invoice_exempt")]
    [InlineData("payment_receipt")]
    public async Task K4Da_InvoiceExemptAndReceipt_SameAsMain(string documentType)
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, documentType, Form(documentType, "plain", false, t.WritePng(), Coords()), 2));
        Assert.True(Has(r, HeaderMark), $"K4-다({documentType}): 머리 「문서번호:」 가 없다 — 칸 모양으로 나갔다.");
        Assert.True(Has(r, PageNoMark), $"K4-다({documentType}): 쪽번호가 없다.");
        Assert.Equal(0, r.Images);
    }

    [Fact(DisplayName = "K4-라 K4-나 + 옵션 켬 = 기본 폼 + 그림 · 칸 모양 0 (저장값 판정 · main 초록)")]
    public async Task K4Ra_OptionOverridesPrintBackground_StillBasicForm()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "delivery", Form("delivery", "plain", false, t.WritePng(), Coords()), 2,
            new PrintOptionOverrides { PrintBackground = true }));
        AssertBasicForm(r, "K4-라");
        Assert.True(r.Images >= 1, "K4-라: 옵션으로 켠 그림이 없다.");
    }

    [Fact(DisplayName = "Y-가 🔴 Y 템플릿 품목 1 = 칸 모양 + 그림 (원본 양식지 main 인쇄 모양 · main 빨강 예상)")]
    public async Task YGa_YTemplate_CoordsFormWithImage()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "delivery", YTemplate(t.WritePng()), 1));
        AssertCoordsForm(r, "Y-가");
        Assert.True(r.Images >= 1, "Y-가: Y 템플릿인데 그림이 없다.");
    }

    [Fact(DisplayName = "Y-가′ 🔴 Y 템플릿 인쇄 = 전표 미리보기 (글자 · 그림 수 같음 · main 빨강 예상)")]
    public async Task YGaPrime_YTemplate_PreviewEqualsPrint()
    {
        using var t = new TenantBackground();
        var form = YTemplate(t.WritePng());
        var print = ReadPdf(await RenderAsync(t.Tenant, "delivery", form, 1));
        var preview = ReadPdf(await RenderAsync(t.Tenant, "delivery", form, 1, new PrintOptionOverrides()));
        AssertCoordsForm(print, "Y-가′(인쇄)");
        Assert.Equal(print.Text, preview.Text);
        Assert.Equal(print.Images, preview.Images);
    }

    [Fact(DisplayName = "Y-나 Y 템플릿 그림 파일을 지운 차림 = 기본 폼 · 그림 0 · 예외 0 (기준 C · main 초록 · A 첫 판 빨강)")]
    public async Task YNa_YTemplateImageMissing_BasicForm()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "delivery", YTemplate(t.MissingPath()), 2));
        AssertBasicForm(r, "Y-나");
        Assert.Equal(0, r.Images);
    }

    [Fact(DisplayName = "Y-다 🔴 Y 템플릿 + max_rows 10 + 품목 40 = 칸 모양 1쪽 잘림 · 예외 0 (남는 결함 · main 빨강 예상)")]
    public async Task YDa_YTemplateMaxRows_TruncatedOnePage()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "delivery", YTemplate(t.WritePng(), Coords(maxRows: 10)), 40));
        AssertCoordsForm(r, "Y-다");
        Assert.Equal(1, r.Pages);
        Assert.False(Has(r, "예시품목11"), "Y-다: max_rows 10 을 넘은 줄이 찍혔다.");
        Assert.True(r.Images >= 1, "Y-다: 그림이 없다.");
        // 🔴 안 (가) 서버 경고 로그 1줄 = 갈래 A 고침 문구 확정 뒤 판독(뼈대에서 로그 단언 0 · 기대값 문서 §2-2).
    }

    [Fact(DisplayName = "K1-0 저장 문 길 빈 A4 + 그림 + 좌표 + 끔 = 기본 폼 · 그림 0 (main 초록 · A 첫 판 빨강)")]
    public async Task K1Zero_SaveDoorPlainOff_BasicForm()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "quotation", Form("quotation", "plain", false, t.WritePng(), Coords()), 2));
        AssertBasicForm(r, "K1-0");
        Assert.Equal(0, r.Images);
    }

    [Fact(DisplayName = "K1-1 🔴 저장 문 길 빈 A4 + 그림 + 좌표 + 켬 = Y 와 같은 모양 (main 빨강 예상)")]
    public async Task K1One_SaveDoorPlainOn_YShape()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "quotation", Form("quotation", "plain", true, t.WritePng(), Coords()), 2));
        AssertCoordsForm(r, "K1-1");
        Assert.True(r.Images >= 1, "K1-1: 그림이 없다.");
    }

    // ═══════════════════════════════════════════════════════════════
    // §2-3 품목 40줄
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "K4-40 K4-가 + 품목 40 = 기본 폼 여러 쪽 · 40줄 모두 (main 초록 · A 첫 판 빨강)")]
    public async Task K4Forty_BasicFormMultiPage()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "delivery", Form("delivery", "plain", false, null, Coords()), 40));
        AssertBasicForm(r, "K4-40");
        Assert.True(r.Pages >= 2, $"K4-40: 품목 40줄인데 {r.Pages}쪽 — 기본 폼은 여러 쪽이어야 한다.");
        Assert.True(Has(r, "예시품목40"), "K4-40: 마지막 품목이 안 찍혔다.");
    }

    [Fact(DisplayName = "Y-40 🔴 Y 템플릿 + 품목 40(max_rows 없음) = 칸 모양 1쪽 · 예외 0 (남는 결함 · main 빨강 예상)")]
    public async Task YForty_CoordsFormOnePage()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "delivery", YTemplate(t.WritePng()), 40));
        AssertCoordsForm(r, "Y-40");
        Assert.Equal(1, r.Pages);
        Assert.False(Has(r, "예시품목40"), "Y-40: 297mm 를 넘은 줄이 찍혔다 — 잘림 모양이 바뀌었다(기록 칸).");
    }

    // ═══════════════════════════════════════════════════════════════
    // §2-4 기존 게이트 (A-T11)
    // ═══════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    // 🆕 D 마감(2026-11-20) — 기대값 문서 §2-5 Y-라 · §2-6 E2M-폭 · E2M-법정
    //   작업지시서 docs/운영기록/20261120작_WG1-51_장치15차_D마감_작업지시서.md §8 D-1 · D-4
    //   🔴 반사 대상(StyleKey · ResolvePrintStyle · IsBodyTooNarrowForStyle · LargeStyle)은 갈래 E⑵ 코드에만 있다 ⇒
    //     정적 필드로 붙잡지 않는다(형 초기화 실패가 이 클래스의 다른 시험까지 깨지 않게) · 시험마다 찾고 없으면 그 시험만 빨강.
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Y-라 Y 템플릿 + 전표 미리보기 옵션 그림 끔 = 기본 폼 · 그림 0 (E⑴⑶ [4] A-1 수용 · main 초록)")]
    public async Task YRa_YTemplateOptionImageOff_BasicForm()
    {
        using var t = new TenantBackground();
        var r = ReadPdf(await RenderAsync(t.Tenant, "delivery", YTemplate(t.WritePng()), 1,
            new PrintOptionOverrides { PrintBackground = false }));
        AssertBasicForm(r, "Y-라");
        Assert.Equal(0, r.Images);
    }

    private static MethodInfo E2Method(string name)
    {
        var m = SvcType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(m is not null, $"PdfRenderService.{name}(…) 가 없다 — 갈래 E⑵ 코드가 실려야 초록(첫 실행 = 합류본).");
        return m!;
    }

    private static object E2LargeStyle()
    {
        var f = SvcType.GetField("LargeStyle", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(f is not null, "PdfRenderService.LargeStyle 이 없다 — 갈래 E⑵ 코드가 실려야 초록.");
        return f!.GetValue(null)!;
    }

    [Fact(DisplayName = "E2M-폭 큰글씨 A4 세로 좌우 25mm = 폭 모자람 참(basic 값) · 20mm = 거짓 (제품 함수 직접 · E⑵ P3-2)")]
    public void E2M_Width_LargeStyleFallsBackAt25mm()
    {
        var narrow = E2Method("IsBodyTooNarrowForStyle");
        var large = E2LargeStyle();
        var a4 = QuestPDF.Helpers.PageSizes.A4.Width;
        Assert.True((bool)narrow.Invoke(null, new object[] { large, a4, 25f, 25f })!,
            "E2M-폭: A4 세로 좌우 25mm 인데 큰글씨가 대체되지 않는다(문턱 459).");
        Assert.False((bool)narrow.Invoke(null, new object[] { large, a4, 20f, 20f })!,
            "E2M-폭 대조: A4 세로 좌우 20mm 인데 대체된다.");
        Assert.Equal(459f, (float)large.GetType().GetProperty("MinBodyWidth")!.GetValue(large)!);
    }

    [Theory(DisplayName = "E2M-법정 법정 3종 documentType = 스타일 null · delivery 대조 = 큰글씨 (제품 함수 직접 · E⑵ P3-2)")]
    [InlineData("tax_invoice", true)]
    [InlineData("invoice_exempt", true)]
    [InlineData("payment_receipt", true)]
    [InlineData("delivery", false)]
    public void E2M_Legal_StyleNullForLegalTypes(string documentType, bool expectNull)
    {
        var resolve = E2Method("ResolvePrintStyle");
        var info = Info("plain", false, null);
        var styleKey = InfoType.GetProperty("StyleKey");
        Assert.True(styleKey is not null, "FormTemplateInfo.StyleKey 가 없다 — 갈래 E⑵ 코드가 실려야 초록.");
        styleKey!.SetValue(info, "large");
        var style = resolve.Invoke(null, new object?[] { info, false, documentType });
        if (expectNull)
            Assert.True(style is null, $"E2M-법정: {documentType} 인데 스타일이 붙었다.");
        else
            Assert.True(style is not null, "E2M-법정 대조: delivery 큰글씨인데 스타일이 null 이다.");
    }

    [Fact(DisplayName = "A-T11 기존 인쇄 게이트(축⒧-2 · G-W5 · 축③) 시험 클래스가 그대로 있다 (main 초록)")]
    public void AT11_ExistingPrintGatesStillPresent()
    {
        foreach (var gate in new[]
                 {
                     typeof(FormPaperSizeAndBackgroundUploadGateTests),
                     typeof(WatermarkRenderGateTests),
                     typeof(FormPreviewOverrideGuardTests),
                 })
        {
            var facts = gate.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Count(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any());
            Assert.True(facts >= 1, $"A-T11: {gate.Name} 에 시험이 0개 — 기존 게이트가 사라졌다.");
        }
        Assert.NotNull(typeof(WatermarkRenderGateTests).GetMethod(nameof(WatermarkRenderGateTests.GW5_PrintBackgroundGatesTheImage)));
    }
}
