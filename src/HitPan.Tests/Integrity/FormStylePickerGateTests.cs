using System.Reflection;
using System.Text.RegularExpressions;
using HitPan.API.Controllers;
using HitPan.Application.Services;
using HitPan.Web.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴🔴 양식 편집창 「인쇄 모양」 고르기 게이트 — W-G1-51 갈래 E⑷ 「스타일 고르기」(작업지시서 §9 T1~T5).
/// </summary>
/// <remarks>
/// 🔴 <b>이 묶음이 왜 생겼나</b> — 대표가 스타일 4종을 결재했는데(§11 #1) 편집창은 불러온 값을 그대로 보낼 뿐이라
///   고객이 4종을 <b>고를 길이 0</b> 이었다(「고쳤다 ≠ 갔다」). E⑷ 가 고르기 · 흐리게 · 다시 그림을 더했다.
/// 🔴 <b>판정은 실제로 부른다</b> — 편집창 판정(<c>FormStyleChoice</c> · 순수 C# 링크)과 서버 판정
///   (<c>PdfRenderService.ResolvePrintStyle</c> · 미리보기 문 변환표 <c>FormTemplateController.FormTypeToDocumentType</c>)을
///   반사로 불러 40경우를 대조한다. 글자로 대조하면 조건이 어긋나도 초록이 된다 — 다르면 고객이 거짓 안내를 받는다.
/// 🔴 양식 유형 10종 · 법정 목록은 <b>편집창 소스에서 읽는다</b>(주석 걷은 판) — 시험이 목록을 새로 적지 않는다.
/// 🔴 편집창 글자 시험(T4)은 <b>연결만</b> 본다(동작은 T2 · T3 가 잰다 · bUnit 0).
/// 🔴 범위 밖: 큰글씨 폭 안내(작업지시서 §4 · T6) — 대표 §76 「1안」 으로 쓰지 않음. 화면 실측 = 갈래 D · HQA.
/// </remarks>
public sealed class FormStylePickerGateTests
{
    static FormStylePickerGateTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private static readonly string[] EditorPath =
        { "src", "HitPan.Web", "Components", "Common", "FormTemplateEditorDialog.razor" };

    private static readonly string[] ChoicePath =
        { "src", "HitPan.Web", "Services", "FormStyleChoice.cs" };

    // ══════════════════════════════════════════════════════════════════
    // 자 — 소스 · 반사
    // ══════════════════════════════════════════════════════════════════

    private static string ReadSource(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null, "레포 루트를 찾아야 한다");
        var path = Path.Combine(new[] { dir! }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {Path.Combine(parts)}");
        return File.ReadAllText(path, System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// 주석 걷은 소스 — 걷기는 기존 게이트의 <c>StripComments</c>(문자열 · 축자 문자열 안은 안 건드림)를 <b>그대로 부른다</b>
    /// (걷기 규칙 두 벌 0 · 2026-10-25 재검증 S-2 · T-3 교훈이 든 판).
    /// </summary>
    private static string ReadCode(params string[] parts)
    {
        var strip = typeof(FormPaperSizeAndBackgroundUploadGateTests)
            .GetMethod("StripComments", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(strip is not null, "주석 걷기 함수(StripComments)를 못 찾았다 — 배선이 바뀌었다.");
        return (string)strip!.Invoke(null, new object[] { ReadSource(parts) })!;
    }

    /// <summary>편집창 「양식 유형」 목록의 키(차례 그대로).</summary>
    private static List<string> EditorFormTypes()
    {
        var code = ReadCode(EditorPath);
        var block = Regex.Match(code, @"FormTypes\s*=\s*new\(\)\s*\{(.*?)\};", RegexOptions.Singleline);
        Assert.True(block.Success, "편집창 양식 유형 목록(FormTypes)을 못 찾았다 — 이름이 바뀌었나.");
        return Regex.Matches(block.Groups[1].Value, @"\[""([a-z_]+)""\]\s*=")
            .Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>편집창 법정 목록(<c>LegalFormTypes</c>) — 편집창이 <c>WhyNotUsed</c> 에 넘기는 그 목록.</summary>
    private static string[] EditorLegalFormTypes()
    {
        var code = ReadCode(EditorPath);
        var list = Regex.Match(code, @"LegalFormTypes\s*=\s*\{([^}]*)\}");
        Assert.True(list.Success, "편집창 법정 목록(LegalFormTypes)을 못 찾았다 — 이름이 바뀌었나.");
        return Regex.Matches(list.Groups[1].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
    }

    private static Type RenderNested(string name) =>
        typeof(PdfRenderService).GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{name} 이 없다 — 배선이 바뀌었다.");

    /// <summary>미리보기 문이 양식 유형을 렌더 종 이름으로 바꾸는 변환표(고객 길 그대로).</summary>
    private static string? ServerDocumentType(string formType)
    {
        var m = typeof(FormTemplateController).GetMethod("FormTypeToDocumentType", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(m is not null, "미리보기 문 변환표(FormTypeToDocumentType)를 못 찾았다 — 배선이 바뀌었다.");
        return (string?)m!.Invoke(null, new object?[] { formType });
    }

    /// <summary>서버 스타일 판정 — 묶음을 주면 참(= 인쇄 모양이 종이에 쓰임).</summary>
    private static bool ServerGivesStyle(string documentType, string paperMode, string? backgroundUrl)
    {
        var resolve = typeof(PdfRenderService).GetMethod("ResolvePrintStyle", BindingFlags.Static | BindingFlags.NonPublic);
        // 🔴 서명을 못 부르면 멈춤 → PM(작업지시서 §9 T3 · 글자 대조로 낮추지 않는다).
        Assert.True(resolve is not null, "서버 스타일 판정(ResolvePrintStyle)을 못 찾았다 — 멈춤 → PM.");
        var ps = resolve!.GetParameters();
        Assert.True(ps.Length == 3 && ps[1].ParameterType == typeof(bool) && ps[2].ParameterType == typeof(string),
            "ResolvePrintStyle 서명이 (template, useCoords, documentType) 이 아니다 — 멈춤 → PM.");

        var t = Activator.CreateInstance(RenderNested("FormTemplateInfo"))!;
        t.GetType().GetProperty("PaperMode")!.SetValue(t, paperMode);
        t.GetType().GetProperty("BackgroundImageUrl")!.SetValue(t, backgroundUrl);
        t.GetType().GetProperty("StyleKey")!.SetValue(t, "large");
        return resolve.Invoke(null, new object?[] { t, false, documentType }) is not null;
    }

    private static readonly string[] PaperModes = { "plain", "preprint" };
    private static readonly string[] Backgrounds = { "", "x.png" };

    // ══════════════════════════════════════════════════════════════════
    // T1 — Normalize 9값
    // ══════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "T1 🔴 불러온 값 정규화 — 공백 자름 · 대소문자 무시 · 빈 값 · 모르는 값 = 기본")]
    [InlineData(null, "basic")]
    [InlineData("", "basic")]
    [InlineData("  ", "basic")]
    [InlineData("basic", "basic")]
    [InlineData("FORMAL", "formal")]
    [InlineData(" Simple ", "simple")]
    [InlineData("large", "large")]
    [InlineData("fancy", "basic")]
    [InlineData("basic ", "basic")]
    public void T1_NormalizePicksAllowedLowercase(string? raw, string expected)
    {
        Assert.Equal(expected, FormStyleChoice.Normalize(raw));
    }

    // ══════════════════════════════════════════════════════════════════
    // T2 — WhyNotUsed 40경우
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "T2 🔴 흐리게 판정 40경우 — 쓰임 = 정확히 7(7종 전표 × 빈 A4 × 그림 없음) · 차례 법정 → 양식용지 → 그림")]
    public void T2_WhyNotUsedFortyCases()
    {
        var formTypes = EditorFormTypes();
        var legal = EditorLegalFormTypes();
        Assert.Equal(10, formTypes.Count);
        Assert.Equal(3, legal.Length);
        Assert.All(legal, l => Assert.Contains(l, formTypes));

        int used = 0, byLegal = 0, byPreprint = 0, byPicture = 0;
        foreach (var ft in formTypes)
        foreach (var paper in PaperModes)
        foreach (var bg in Backgrounds)
        {
            var got = FormStyleChoice.WhyNotUsed(ft, paper, bg, legal);
            string? expected =
                legal.Contains(ft) ? FormStyleChoice.WhyLegal
                : paper != "plain" ? FormStyleChoice.WhyPreprint
                : bg.Length > 0 ? FormStyleChoice.WhyPicture
                : null;
            Assert.True(expected == got, $"{ft} · {paper} · 그림 '{bg}' — 기대 '{expected ?? "쓰임"}' · 실제 '{got ?? "쓰임"}'");

            if (got is null) used++;
            else if (got == FormStyleChoice.WhyLegal) byLegal++;
            else if (got == FormStyleChoice.WhyPreprint) byPreprint++;
            else if (got == FormStyleChoice.WhyPicture) byPicture++;
        }

        Assert.Equal(7, used);
        Assert.Equal(12, byLegal);
        Assert.Equal(14, byPreprint);
        Assert.Equal(7, byPicture);
    }

    // ══════════════════════════════════════════════════════════════════
    // T3 — 서버 대조 40/40
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "T3 🔴🔴 서버 대조 — 같은 40경우를 미리보기 변환표 → ResolvePrintStyle(반사)에 넣어 「쓰임」 이 40/40 같다")]
    public void T3_SameAsServerStyleDecision()
    {
        var formTypes = EditorFormTypes();
        var legal = EditorLegalFormTypes();
        Assert.Equal(10, formTypes.Count);

        var mismatches = new List<string>();
        var compared = 0;
        foreach (var ft in formTypes)
        {
            var docType = ServerDocumentType(ft);
            Assert.True(docType is not null, $"미리보기 문이 양식 유형 '{ft}' 을 못 바꾼다 — 편집창과 서버 목록이 어긋났다.");

            foreach (var paper in PaperModes)
            foreach (var bg in Backgrounds)
            {
                var editorUsed = FormStyleChoice.WhyNotUsed(ft, paper, bg, legal) is null;
                var serverUsed = ServerGivesStyle(docType!, paper, bg);
                compared++;
                if (editorUsed != serverUsed)
                {
                    mismatches.Add($"{ft}({docType}) · {paper} · 그림 '{bg}' — 편집창 {(editorUsed ? "쓰임" : "흐리게")} · 서버 {(serverUsed ? "그림" : "기본")}");
                }
            }
        }

        Assert.Equal(40, compared);
        Assert.True(mismatches.Count == 0,
            "편집창 안내와 서버 종이가 다르다 — 고객이 거짓 안내를 받는다:\n" + string.Join("\n", mismatches));
    }

    // ══════════════════════════════════════════════════════════════════
    // T4 — 편집창 연결(글자 · 연결만)
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "T4 🔴 편집창 연결 — 흐리게 · 까닭 줄 = WhyNotUsed(… LegalFormTypes) · 항목 = Options · 처리기가 다시 그림 · 불러오기 정규화")]
    public void T4_EditorWiring()
    {
        var code = ReadCode(EditorPath);

        // T4-1 법정 목록은 넘겨 받는다(새로 적지 않음)
        Assert.Single(Regex.Matches(code,
            @"FormStyleChoice\.WhyNotUsed\(\s*_form\.FormType\s*,\s*_form\.PaperMode\s*,\s*_form\.BackgroundImageUrl\s*,\s*LegalFormTypes\s*\)"));
        Assert.Matches(@"private\s+string\?\s+StyleWhyNotUsed\s*=>\s*FormStyleChoice\.WhyNotUsed\(", code);

        // T4-2 고르기 칸 — 흐리게 · 칸 밑 까닭 줄 · 항목
        var select = Regex.Match(code, @"<MudSelect\s+T=""string""\s+Value=""@_form\.StyleKey""(.*?)</MudSelect>", RegexOptions.Singleline);
        Assert.True(select.Success, "인쇄 모양 고르기 칸을 못 찾았다.");
        Assert.Contains(@"ValueChanged=""OnStyleChangedAsync""", select.Value, StringComparison.Ordinal);
        Assert.Contains(@"Disabled=""@(StyleWhyNotUsed is not null)""", select.Value, StringComparison.Ordinal);
        Assert.Contains(@"Label=""@FormStyleChoice.Label""", select.Value, StringComparison.Ordinal);
        Assert.Matches(@"@foreach\s*\(\s*var\s+styleOption\s+in\s+FormStyleChoice\.Options\s*\)", select.Value);
        Assert.Matches(@"</MudSelect>\s*@if\s*\(\s*StyleWhyNotUsed\s+is\s+\{\s*Length:\s*>\s*0\s*\}\s+styleWhy\s*\)\s*\{\s*<MudText\s+Typo=""Typo\.caption""[^>]*>@styleWhy</MudText>",
            code.Substring(select.Index));

        // T4-3 처리기가 미리보기를 다시 그린다
        var handler = Regex.Match(code, @"private\s+async\s+Task\s+OnStyleChangedAsync\(string\s+v\)\s*\{(.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(handler.Success, "OnStyleChangedAsync 처리기를 못 찾았다.");
        Assert.Contains("_form.StyleKey = v;", handler.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("await PreviewAsync();", handler.Groups[1].Value, StringComparison.Ordinal);

        // T4-4 불러오기 정규화 · 옛 줄은 주석으로만
        Assert.Contains("StyleKey = FormStyleChoice.Normalize(Template.StyleKey),", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StyleKey = Template.StyleKey,", code, StringComparison.Ordinal);

        // T4-5 판정 파일에 법정 종 이름 0(목록 두 벌 0)
        var choice = ReadCode(ChoicePath);
        foreach (var name in new[] { "tax_invoice", "invoice_exempt", "payment_receipt" })
        {
            Assert.DoesNotContain(name, choice, StringComparison.Ordinal);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // T5 — 항목 값 = 서버 허용 목록
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "T5 🔴 고르기 항목 값 집합 = 서버 KnownStyleKeys(반사) · 각 값 정규화 = 자기 자신")]
    public void T5_OptionsEqualServerKnownStyleKeys()
    {
        var field = typeof(PdfRenderService).GetField("KnownStyleKeys", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(field is not null, "서버 허용 목록(KnownStyleKeys)을 못 찾았다 — 배선이 바뀌었다.");
        var server = ((string[])field!.GetValue(null)!).ToHashSet(StringComparer.Ordinal);

        var keys = FormStyleChoice.Options.Select(o => o.Key).ToList();
        Assert.Equal(4, keys.Count);
        Assert.True(keys.ToHashSet(StringComparer.Ordinal).SetEquals(server),
            $"편집창 항목({string.Join(",", keys)})과 서버 허용 목록({string.Join(",", server.OrderBy(x => x))})이 다르다.");
        Assert.All(keys, k => Assert.Equal(k, FormStyleChoice.Normalize(k)));
        Assert.Contains(FormStyleChoice.DefaultKey, server);
    }

    // ══════════════════════════════════════════════════════════════════
    // T5b — 항목 값 = 저장 문 허용 4값 (E⑷ 고침 1-③ · PM 판단 §78 · [4] P3-2)
    // ══════════════════════════════════════════════════════════════════

    /// <remarks>
    /// 🔴 서버 쪽 스타일 집합은 두 자리다 — 렌더 <c>KnownStyleKeys</c>(T5) · 저장 문 <c>AllowedStyleKeys</c>(E⑵ 고침 1 · 글자 그대로 비교).
    ///   저장 집합만 바뀌면 고르기가 저장이 거절할 값을 내놓아 「저장」 이 막힌다 → 저장 집합도 반사로 대조한다.
    /// </remarks>
    [Fact(DisplayName = "T5b 🔴 고르기 항목 값 집합 = 저장 문 AllowedStyleKeys(반사) — 저장이 거절할 값을 고르기가 내놓지 않는다")]
    public void T5b_OptionsEqualSaveAllowedStyleKeys()
    {
        var field = typeof(FormTemplateService).GetField("AllowedStyleKeys", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(field is not null, "저장 문 허용 목록(AllowedStyleKeys)을 못 찾았다 — 배선이 바뀌었다.");
        var saveAllowed = (string[])field!.GetValue(null)!;

        Assert.Equal(4, saveAllowed.Length);
        Assert.Equal(saveAllowed.Length, saveAllowed.Distinct(StringComparer.Ordinal).Count());

        var keys = FormStyleChoice.Options.Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        Assert.True(keys.SetEquals(saveAllowed),
            $"편집창 항목({string.Join(",", keys.OrderBy(x => x, StringComparer.Ordinal))})과 "
            + $"저장 문 허용 목록({string.Join(",", saveAllowed.OrderBy(x => x, StringComparer.Ordinal))})이 다르다.");
    }
}
