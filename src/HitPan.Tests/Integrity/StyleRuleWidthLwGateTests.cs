using System.Reflection;
using HitPan.Application.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig.Graphics.Operations.General;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;
using UglyToad.PdfPig.Tokens;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 R1 회송 T-B1 — 스타일 머리 아래 선 굵기가 제품 PDF 에 <b>값 그대로</b> 들어가는가(결재 기록 §220-1 · 조건부 채택).
/// </summary>
/// <remarks>
/// 🔴 <b>왜 생겼나</b> — R1 에서 머리 아래 선이 모두 1pt 로 읽혔다. 원인 = 장치 판독기가 그래픽 상태 사전의 <c>/LW</c> 를 안 읽음(H-B2 ·
///   개발명세서 §1-3). 제품(QuestPDF)은 굵기 연산자 <c>w</c> 대신 <c>gs</c> 의 <c>/LW</c> 로 굵기를 준다. 장치는 동결이라
///   선 굵기 증거를 <b>제품 쪽 시험</b>으로 받는다(새 꾸러미 0 — 시험 프로젝트에 이미 있는 PdfPig 0.1.10).
/// 🔴 <b>제품 코드로 그린다</b> — 머리 작성기(<c>ComposeHeader</c> · <c>ComposeHeaderStyled</c>) · 고르기(<c>ResolvePrintStyle</c>)를 반사로 부른다.
///   머리만 그려(본문 · 쪽번호 0) 그은 선(stroke)이 머리 선뿐이게 한다. 판독은 이 시험 안의 작은 흐름 읽기(장치 코드 복사 0):
///   <c>q</c>/<c>Q</c> 쌓기 · <c>cm</c> 축척 · <c>w</c> · <c>gs</c>(<c>/LW</c>) → <c>S</c> 마다 실제 굵기.
/// 🔴 기대값 = 제품 코드 값: 격식 1.5 · 0.5(이중선) / 간결 0.75 / 큰글씨 1.5 / basic 1(<c>ComposeHeader</c> <c>LineHorizontal(1)</c>).
/// 🔴 손 변이: 간결 <c>RuleWidth</c> 0.75 → 1 → 빨강 · 되돌림 뒤 파일 해시 같음(개발명세서 T-B1 절).
/// </remarks>
public sealed class StyleRuleWidthLwGateTests
{
    static StyleRuleWidthLwGateTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private static Type Nested(string name) =>
        typeof(PdfRenderService).GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{name} 이 없다 — 배선이 바뀌었다.");

    private static MethodInfo StaticMethod(string name) =>
        typeof(PdfRenderService).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{name} 이 없다 — 배선이 바뀌었다.");

    private static byte[] RenderHeaderPdf(string styleKey)
    {
        var t = Activator.CreateInstance(Nested("FormTemplateInfo"))!;
        t.GetType().GetProperty("PaperMode")!.SetValue(t, "plain");
        t.GetType().GetProperty("StyleKey")!.SetValue(t, styleKey);
        var style = StaticMethod("ResolvePrintStyle").Invoke(null, new object?[] { t, false, "quotation" });
        if (styleKey != "basic")
            Assert.True(style is not null, $"{styleKey} — 제품 고르기가 스타일을 안 골랐다(시험 전제 깨짐)");

        var snapType = Nested("DocumentSnapshot");
        var snap = Activator.CreateInstance(snapType)!;
        snapType.GetProperty("DocType")!.SetValue(snap, "quotation");
        snapType.GetProperty("DocNo")!.SetValue(snap, "GATE-TB1-0001");
        snapType.GetProperty("DocDate")!.SetValue(snap, new DateTime(2026, 11, 26));
        var company = Activator.CreateInstance(Nested("CompanyInfo"))!;
        company.GetType().GetProperty("CompanyName")!.SetValue(company, "히트판상사");

        var header = StaticMethod("ComposeHeader");
        var headerStyled = StaticMethod("ComposeHeaderStyled");
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontFamily("Malgun Gothic").FontSize(10));
                if (style is not null)
                    page.Header().Element(e => headerStyled.Invoke(null, new object?[] { e, snap, company, style }));
                else
                    page.Header().Element(e => header.Invoke(null, new object?[] { e, snap, company }));
            });
        }).GeneratePdfOneAtATime();
    }

    /// <summary>쪽 하나의 그은 선(<c>S</c> · <c>s</c>) 실제 굵기를 그린 차례대로.</summary>
    private static List<double> StrokeWidths(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var page = doc.GetPage(1);

        IToken Resolve(IToken tok) =>
            tok is IndirectReferenceToken r ? doc.Structure.GetObject(r.Data).Data : tok;

        DictionaryToken? extGStates = null;
        if (page.Dictionary.Data.TryGetValue("Resources", out var resTok)
            && Resolve(resTok) is DictionaryToken res
            && res.Data.TryGetValue("ExtGState", out var egTok))
            extGStates = Resolve(egTok) as DictionaryToken;

        // 상태: 선 굵기(사용자 공간) · 축척(행렬식 제곱근). PDF 기본 굵기 = 1.
        var stack = new Stack<(double lw, double a, double b, double c, double d)>();
        (double lw, double a, double b, double c, double d) st = (1.0, 1, 0, 0, 1);
        var widths = new List<double>();

        foreach (var op in page.Operations)
        {
            switch (op)
            {
                case Push:
                    stack.Push(st);
                    break;
                case Pop:
                    if (stack.Count > 0) st = stack.Pop();
                    break;
                case ModifyCurrentTransformationMatrix cm:
                {
                    var m = cm.Value;
                    // 새 CTM = m × 옛 CTM (선형 부분만)
                    st = (st.lw,
                        m[0] * st.a + m[1] * st.c, m[0] * st.b + m[1] * st.d,
                        m[2] * st.a + m[3] * st.c, m[2] * st.b + m[3] * st.d);
                    break;
                }
                case SetLineWidth w:
                    st.lw = w.Width;
                    break;
                case SetGraphicsStateParametersFromDictionary gs:
                    if (extGStates is not null
                        && extGStates.Data.TryGetValue(gs.Name.Data, out var gTok)
                        && Resolve(gTok) is DictionaryToken g
                        && g.Data.TryGetValue("LW", out var lwTok)
                        && Resolve(lwTok) is NumericToken lw)
                        st.lw = lw.Double;
                    break;
                case StrokePath:
                case CloseAndStrokePath:
                    widths.Add(Math.Round(st.lw * Math.Sqrt(Math.Abs(st.a * st.d - st.b * st.c)), 3));
                    break;
            }
        }
        return widths;
    }

    [Theory(DisplayName = "R-B T-B1 🔴 머리 아래 선 굵기 = 코드 값(그래픽 상태 /LW) — 격식 1.5·0.5 / 간결 0.75 / 큰글씨 1.5 / basic 1")]
    [InlineData("formal", new[] { 1.5, 0.5 })]
    [InlineData("simple", new[] { 0.75 })]
    [InlineData("large", new[] { 1.5 })]
    [InlineData("basic", new[] { 1.0 })]
    public void T_B1_HeaderRuleWidthsMatchCode(string styleKey, double[] expected)
    {
        var widths = StrokeWidths(RenderHeaderPdf(styleKey));
        Assert.Equal(expected, widths.ToArray());
    }
}
