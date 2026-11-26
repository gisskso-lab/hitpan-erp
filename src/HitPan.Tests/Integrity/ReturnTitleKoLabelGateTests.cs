using System.Reflection;
using HitPan.Application.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 R1 회송 갈래 R-D — 반품 2종 인쇄 제목(대표 결재 §221 #5 「매입반품」 · 「판매반품」).
/// </summary>
/// <remarks>
/// 🔴 <b>왜 생겼나</b> — 제목 표 <c>KoLabel</c> 에 반품 2종이 없어 매입 반품 · 판매 반품 전표가 「문서」 로 찍혔다(회송 지시서 §2-5).
/// 🔴 <b>제품 코드로 본다</b> — <c>KoLabel</c> · 머리 작성기(<c>ComposeHeader</c> · <c>ComposeHeaderStyled</c>) · 고르기
///   (<c>ResolvePrintStyle</c>)를 반사로 불러 실제로 그린 PDF 의 낱말을 읽는다. 파일 이름(<c>{KoLabel}_{문서번호}.pdf</c>)도 같은 표를 탄다.
///   <c>RenderDocumentAsync</c> 는 DB 를 읽어 시험에서 못 부른다 — 머리만 같은 작성기로 그린다(<c>FormStyleRenderGateTests</c> 와 같은 꼴).
/// 🔴 손 변이(개발명세서 R-D 절): V-D1 반품 줄 하나 지움 → T-D1 빨강 · V-D2 판매 반품 줄을 매입 글자로 → 빨강.
/// </remarks>
public sealed class ReturnTitleKoLabelGateTests
{
    static ReturnTitleKoLabelGateTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private static Type Nested(string name) =>
        typeof(PdfRenderService).GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{name} 이 없다 — 배선이 바뀌었다.");

    private static MethodInfo StaticMethod(string name) =>
        typeof(PdfRenderService).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{name} 이 없다 — 배선이 바뀌었다.");

    private static string KoLabel(string docType) =>
        (string)StaticMethod("KoLabel").Invoke(null, new object?[] { docType })!;

    private static object MakeTemplate(string styleKey)
    {
        var t = Activator.CreateInstance(Nested("FormTemplateInfo"))!;
        t.GetType().GetProperty("PaperMode")!.SetValue(t, "plain");
        t.GetType().GetProperty("StyleKey")!.SetValue(t, styleKey);
        return t;
    }

    private static object MakeSnapshot(string docType)
    {
        var snapType = Nested("DocumentSnapshot");
        var snap = Activator.CreateInstance(snapType)!;
        snapType.GetProperty("DocType")!.SetValue(snap, docType);
        snapType.GetProperty("DocNo")!.SetValue(snap, "GATE-RD-0001");
        snapType.GetProperty("DocDate")!.SetValue(snap, new DateTime(2026, 11, 26));
        snapType.GetProperty("PartnerName")!.SetValue(snap, "가나상사");
        return snap;
    }

    private static object MakeCompany()
    {
        var c = Activator.CreateInstance(Nested("CompanyInfo"))!;
        c.GetType().GetProperty("CompanyName")!.SetValue(c, "히트판상사");
        return c;
    }

    /// <summary>머리만 제품 작성기로 그려 PDF 낱말을 돌려준다. basic = 옛 <c>ComposeHeader</c> · 나머지 = 제품 고르기 뒤 <c>ComposeHeaderStyled</c>.</summary>
    private static string[] HeaderWords(string docType, string styleKey)
    {
        var snap = MakeSnapshot(docType);
        var company = MakeCompany();
        var style = StaticMethod("ResolvePrintStyle").Invoke(null, new object?[] { MakeTemplate(styleKey), false, docType });
        if (styleKey != "basic")
            Assert.True(style is not null, $"{docType} · {styleKey} — 제품 고르기가 스타일을 안 골랐다(시험 전제 깨짐)");
        var header = StaticMethod("ComposeHeader");
        var headerStyled = StaticMethod("ComposeHeaderStyled");

        var pdf = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(10));
                if (style is not null)
                    page.Header().Element(e => headerStyled.Invoke(null, new object?[] { e, snap, company, style }));
                else
                    page.Header().Element(e => header.Invoke(null, new object?[] { e, snap, company }));
            });
        }).GeneratePdfOneAtATime();

        using var read = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return read.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).ToArray();
    }

    [Theory(DisplayName = "R-D T-D1 🔴 반품 2종 제목 = 대표 결재 글자 · 「문서」 0 · 파일 이름 접두도 같은 표")]
    [InlineData("purchase_return", "매입반품")]
    [InlineData("sales_return", "판매반품")]
    public void T_D1_ReturnTitles(string docType, string expected)
    {
        Assert.Equal(expected, KoLabel(docType));
        foreach (var styleKey in new[] { "basic", "simple", "large" })
        {
            var words = HeaderWords(docType, styleKey);
            Assert.Contains(expected, words);
            Assert.DoesNotContain("문서", words);
        }
    }

    /// <summary>
    /// 🔴 2026-11-26 P3-①(CTO §226-2 · PM §227) — T-D1 에 격식(formal) 스타일이 빠져 있었다. 격식 제목은 내장 글꼴(Noto Serif KR) ·
    /// 자간(제목 크기 비례)으로 그려 PdfPig 가 글자 사이를 낱말로 끊을 수 있다 ⇒ 낱말 「문서」 0 은 T-D1 과 같게 보고,
    /// 제목 글자는 낱말을 이어 붙인 글에서 찾는다(머리에 「문서」 가 낱말로 없음 + 이은 글에 결재 글자).
    /// </summary>
    [Theory(DisplayName = "R-D T-D1 격식 🔴 반품 2종 격식(formal) 머리 제목 = 대표 결재 글자 · 낱말 「문서」 0")]
    [InlineData("purchase_return", "매입반품")]
    [InlineData("sales_return", "판매반품")]
    public void T_D1_ReturnTitles_Formal(string docType, string expected)
    {
        var words = HeaderWords(docType, "formal");
        Assert.Contains(expected, string.Concat(words), StringComparison.Ordinal);
        Assert.DoesNotContain("문서", words);
    }

    [Theory(DisplayName = "R-D T-D2 회귀 — 옛 6종 제목 그대로")]
    [InlineData("quotation", "견적서")]
    [InlineData("sales_order", "수주서")]
    [InlineData("delivery", "거래명세서")]
    [InlineData("tax_invoice", "세금계산서")]
    [InlineData("purchase_order", "발주서")]
    [InlineData("purchase_receipt", "매입명세서")]
    public void T_D2_OldSixUnchanged(string docType, string expected) =>
        Assert.Equal(expected, KoLabel(docType));

    [Theory(DisplayName = "R-D T-D3 회귀 — 모르는 종 = 「문서」 · 하이픈 꼴(화면 키)은 이 표의 키가 아님")]
    [InlineData("unknown_type")]
    [InlineData("purchase-return")]
    [InlineData("sales-return")]
    [InlineData("return")]
    public void T_D3_UnknownIsDocument(string docType) =>
        Assert.Equal("문서", KoLabel(docType));
}
