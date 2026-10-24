using System.Text.RegularExpressions;
using System.Reflection;
using HitPan.Application.DTOs.Documents;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261024작 — 전표 인쇄 미리보기 게이트 (사장님 반려 ③ · 결재 결-1·2·3).
/// </summary>
/// <remarks>
/// <para>
/// 사장님 워크플로우: <i>"인쇄버튼 클릭시 → 미리보기로 넘어가서, 어떤 양식이 적용되는지
///   보여줘야 함 → 옵션 선택 → 최종 인쇄시 미리보기 화면하고 동일하게 인쇄"</i>
/// </para>
/// <para>
/// 🔴 <b>이 파일이 지키는 축 셋</b>:
///   ① 전표 6화면의 「인쇄」가 <b>화면인쇄로 돌아가지 않는다</b>(결-1)
///   ② 옵션이 <b>저장된 양식 위에 얹힌다</b>(결-2 · 7종 공통 한 벌)
///   ③ 옵션이 없으면 <b>지금 인쇄와 같은 종이</b>다(#1)
/// </para>
/// <para>
/// 🔴 <b>10/24 에 내가 두 번 거짓 초록을 냈다.</b> 그래서 축마다 <b>단독으로</b>
///   빨개지는 모양을 만들었고, 세운 뒤 <b>실제로 빼서</b> 확인했다.
/// </para>
/// <para>
/// 🔴 <b>이 게이트가 못 재는 것</b>: 브라우저에서 창이 실제로 뜨는지 · iframe PDF 인쇄가
///   되는지 · 미리보기와 인쇄물의 글자가 같은지. 전부 <b>브라우저 실측</b>이 필요하다.
/// </para>
/// </remarks>
public class DocumentPrintPreviewGateTests
{
    private static readonly Type SvcType = typeof(PdfRenderService);

    private static string WebRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "HitPan.Web")))
        {
            d = d.Parent;
        }
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "src", "HitPan.Web");
    }


    /// <summary>레포 뿌리 — 작성기 소스를 직접 읽어 「백지인지」를 잰다(축⑥).</summary>
    private static string AppRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "HitPan.Application")))
        {
            d = d.Parent;
        }
        Assert.NotNull(d);
        return d!.FullName;
    }
    private static MethodInfo PrivateMethod(string name) =>
        SvcType.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance
                              | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException($"PdfRenderService.{name}(…) 이 없다 — 20261024작.");

    private static object MakeService() =>
        Activator.CreateInstance(SvcType,
            new object?[] { null, NullLogger<PdfRenderService>.Instance, null })!;

    private static object NewTemplateInfo()
    {
        var svc = MakeService();
        return PrivateMethod("ToTemplateInfo").Invoke(svc,
            new object?[] { new HitPan.Application.DTOs.Settings.FormTemplateDto
            {
                FormType = "tax_invoice",
                PrintCopyMode = "recipient",
                ShowTearLine = false
            } })!;
    }

    private static object? Get(object t, string p) =>
        t.GetType().GetProperty(p)!.GetValue(t);

    // ═══════════════════════════════════════════════════════════════
    // 축 ① 전표 화면이 화면인쇄로 돌아가지 않는다 (결-1)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>사장님 반려 ③ 에 이름이 적힌 화면 — 6종 → **8종**(2차 재검증 R-7-a).</summary>
    public static TheoryData<string> PrintScreens() => new()
    {
        Path.Combine("Pages", "Sales", "QuotationPage.razor.cs"),
        Path.Combine("Pages", "Sales", "SalesOrderPage.razor.cs"),
        Path.Combine("Pages", "Sales", "DeliveryPage.razor"),
        Path.Combine("Pages", "Purchase", "PurchaseOrderPage.razor.cs"),
        Path.Combine("Pages", "Purchase", "PurchaseReceiptPage.razor.cs"),
        Path.Combine("Pages", "Purchase", "ReturnPage.razor.cs"),
        // 🔴 20261024 2차 재검증 R-7-a — 세금계산서가 빠져 있었다. 옵션(공급받는자만·
        //   공급자·모두·절취선)이 **실제로 쓰이는 유일한 종**인데 미리보기를 안 탔다.
        Path.Combine("Pages", "Sales", "TaxInvoicePage.razor"),
        // 🔴 매출반품 — 「반품처리」는 매입·매출 둘 다다(2차 재검증 등재 항목).
        Path.Combine("Pages", "Sales", "SalesReturnPage.razor.cs"),
    };

    [Theory(DisplayName = "축① 🔴 전표 화면의 인쇄가 **화면인쇄로 돌아가지 않는다**(사장님 반려 ③ 회귀)")]
    [MemberData(nameof(PrintScreens))]
    public void Axis1_NoScreenPrintRemains(string relative)
    {
        var file = Path.Combine(WebRoot(), relative);
        Assert.True(File.Exists(file), $"화면 파일이 없다: {relative}");
        var text = File.ReadAllText(file);

        // 🔴 이 한 줄이 돌아오면 양식지·여백·매수·절취선이 **하나도 안 먹는다**.
        //    사장님 실측: "각 전표 인쇄시, 화면인쇄가 되고, 양식적용이 안됨"
        Assert.DoesNotContain("InvokeVoidAsync(\"print\")", text);

        // 🔴 그리고 미리보기 창으로 가야 한다 — 그냥 지우기만 하면 인쇄가 사라진다.
        Assert.Contains("PrintPreview.ShowAsync", text);
    }

    // ═══════════════════════════════════════════════════════════════
    // 축 ② 옵션이 저장된 양식 위에 얹힌다 (결-2 · 7종 공통)
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "축② 🔴 옵션이 저장된 양식을 덮어쓴다 — 인쇄 매수·절취선")]
    public void Axis2_OptionsOverrideSavedTemplate()
    {
        var t = NewTemplateInfo();
        Assert.Equal("recipient", Get(t, "PrintCopyMode"));
        Assert.Equal(false, Get(t, "ShowTearLine"));

        var opt = new PrintOptionOverrides { PrintCopyMode = "both", ShowTearLine = true };
        var applied = PrivateMethod("ApplyPrintOptions").Invoke(null, new object?[] { t, opt })!;

        Assert.Equal("both", Get(applied, "PrintCopyMode"));
        Assert.Equal(true, Get(applied, "ShowTearLine"));
    }

    [Fact(DisplayName = "축②빈 🔴 양식 행이 없어도 옵션은 먹는다 — 「기본 양식 만들기」를 안 누른 고객")]
    public void Axis2_WorksWithoutTemplateRow()
    {
        // [1-V] E-7: 그 단추를 안 누른 고객은 form_templates 행이 **0개**다.
        //   그래도 인쇄 매수는 고를 수 있어야 한다.
        var opt = new PrintOptionOverrides { PrintCopyMode = "supplier" };
        var applied = PrivateMethod("ApplyPrintOptions").Invoke(null, new object?[] { null, opt });

        Assert.NotNull(applied);
        Assert.Equal("supplier", Get(applied!, "PrintCopyMode"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 축 ③ 옵션이 없으면 지금 인쇄와 같은 종이다 (#1)
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "축③ 🔴 옵션이 없으면 **받은 양식 그대로** — 기존 인쇄가 안 바뀐다(#1)")]
    public void Axis3_NoOptions_ReturnsSameInstance()
    {
        var t = NewTemplateInfo();

        // 🔴 null 이든, 안이 비었든 **같은 것을 그대로** 돌려줘야 한다.
        //    새 것을 만들어 돌려주면 그 자리에서 기본값이 섞여 들어온다.
        Assert.Same(t, PrivateMethod("ApplyPrintOptions").Invoke(null, new object?[] { t, null }));
        Assert.Same(t, PrivateMethod("ApplyPrintOptions").Invoke(null,
            new object?[] { t, new PrintOptionOverrides() }));
    }

    [Fact(DisplayName = "축③빈 🔴 양식도 옵션도 없으면 null 그대로 — 없는 양식을 만들어내지 않는다")]
    public void Axis3_NothingAtAll_StaysNull()
    {
        Assert.Null(PrivateMethod("ApplyPrintOptions").Invoke(null, new object?[] { null, null }));
        Assert.Null(PrivateMethod("ApplyPrintOptions").Invoke(null,
            new object?[] { null, new PrintOptionOverrides() }));
    }

    // ═══════════════════════════════════════════════════════════════
    // 축 ④ 고른 옵션이 그 종이에 들어간다 (2차 재검증 R-9)
    // ═══════════════════════════════════════════════════════════════

    private static string DialogSource() =>
        File.ReadAllText(Path.Combine(WebRoot(), "Components", "Common", "DocumentPrintPreviewDialog.razor"));

    [Fact(DisplayName = "축④ 🔴 옵션을 바꾸면 **다시 그린다** — 옛 종이가 인쇄되지 않는다(R-9)")]
    public void Axis4_OptionChange_Redraws()
    {
        var src = DialogSource();

        // 🔴 @bind-Value 로 돌아가면 값만 바뀌고 종이는 옛 blob 이다.
        //    고객이 「공급받는자만」으로 바꾸고 바로 인쇄하면 두 벌이 찍혀 나갔다.
        Assert.DoesNotContain("@bind-Value=\"_copyMode\"", src);
        Assert.DoesNotContain("@bind-Value=\"_tearLine\"", src);

        // 🔴 그리고 바뀔 때 실제로 다시 그려야 한다 — 콜백만 있고 LoadAsync 가 없으면 같은 사고다.
        Assert.Contains("ValueChanged=\"OnCopyModeChangedAsync\"", src);
        Assert.Contains("ValueChanged=\"OnTearLineChangedAsync\"", src);
        foreach (var handler in new[] { "OnCopyModeChangedAsync", "OnTearLineChangedAsync" })
        {
            var at = src.IndexOf($"private async Task {handler}", StringComparison.Ordinal);
            Assert.True(at > 0, $"{handler} 가 없다 — 옵션이 종이에 안 들어간다(R-9).");
            var body = src.Substring(at, Math.Min(240, src.Length - at));
            Assert.Contains("await LoadAsync()", body);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 축 ⑤ 인쇄가 조용히 실패하지 않는다 (2차 재검증 R-8)
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "축⑤ 🔴 팝업이 막히면 **안내가 뜬다** — 반환값을 버리지 않는다(R-8)")]
    public void Axis5_PrintFailure_IsToldToUser()
    {
        var src = DialogSource();
        var at = src.IndexOf("private async Task PrintAsync", StringComparison.Ordinal);
        Assert.True(at > 0, "대화상자에 PrintAsync 가 없다.");
        var body = src.Substring(at);

        // 🔴 InvokeVoidAsync 면 JS 의 false 가 버려진다 ⇒ 예외도 없고 catch 도 안 돈다
        //    ⇒ "눌렀는데 아무 일도 안 난다". 이 한 줄이 돌아오면 그 사고가 돌아온다.
        Assert.DoesNotContain("InvokeVoidAsync(\"hitpanPrintPreviewFrame\"", body);
        Assert.Contains("InvokeAsync<bool>(\"hitpanPrintPreviewFrame\"", body);

        // 🔴 받아 놓고 안 쓰면 같은 사고다 — false 일 때 사람에게 말해야 한다.
        var upToCatch = body.Substring(0, Math.Max(0, body.IndexOf("catch (Exception", StringComparison.Ordinal)));
        Assert.Contains("if (!ok)", upToCatch);
        Assert.Contains("Snackbar.Add", upToCatch);
    }

    // ═══════════════════════════════════════════════════════════════
    // 축 ⑥ 미리보기 창이 띄우는 종이가 **백지가 아니다** (결재 ㉮)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>손으로 적지 않는다.</b> 화면의 <c>ShowAsync("…")</c> 호출부를 <b>긁어서</b> 종을 모은다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 바꿨나</b> — 종전에는 종 이름을 이 파일에 <b>손으로 7개</b> 적어 뒀고,
    ///   그 차수가 새로 단 <c>sales_return</c> 이 <b>목록에 0건</b>이었다. 그래서 그 분기를
    ///   빼도 <b>42/42 초록</b>이 나왔다 — <b>거짓 통과</b>(검증팀 3차 G-1 · NC-6).
    ///   손으로 적는 목록은 <b>다음 종에서 또 빈다.</b> 그래서 호출부에서 긁는다.
    /// 🔴 한 건도 못 긁으면 <b>빨개진다</b> — 「0건이라 통과」를 만들지 않는다.
    /// </remarks>
    public static TheoryData<string> WiredDocTypes()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(WebRoot(), "Pages"),
                     "*.razor*", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file),
                         @"PrintPreview\.ShowAsync\(\s*""([a-z0-9\-_]+)"""))
            {
                found.Add(m.Groups[1].Value);
            }
        }

        var data = new TheoryData<string>();
        foreach (var t in found) data.Add(t);
        if (found.Count == 0) data.Add("(호출부를 한 건도 못 긁었다 — 축⑥이 비었다)");
        return data;
    }

    [Theory(DisplayName = "축⑥ 🔴 인쇄 단추를 단 종은 **자료 로더가 있다** — 백지가 안 나간다(결재 ㉮)")]
    [MemberData(nameof(WiredDocTypes))]
    public void Axis6_WiredTypesHaveLoader(string screenType)
    {
        // ① 화면이 쓰는 이름(하이픈 꼴)을 서버가 받아 주나 — 실제 그 함수를 불러 본다.
        var map = typeof(HitPan.API.Controllers.DocumentController)
            .GetMethod("PrintTypeToDocumentType", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("DocumentController.PrintTypeToDocumentType 이 없다.");
        var documentType = map.Invoke(null, new object?[] { screenType }) as string;
        Assert.False(documentType is null,
            $"화면은 \"{screenType}\" 로 부르는데 서버 문이 그 이름을 모른다 — 창이 400 으로 막힌다.");

        // ② 그 종에 **자료 로더**가 있나 — 없으면 fallback 이 빈 종이를 돌려준다.
        var src = File.ReadAllText(Path.Combine(AppRoot(), "src", "HitPan.Application",
            "Services", "PdfRenderService.cs"));
        var at = src.IndexOf("private async Task<DocumentSnapshot> LoadDocumentAsync", StringComparison.Ordinal);
        Assert.True(at > 0, "LoadDocumentAsync 가 없다.");
        var sw = src.Substring(at, Math.Min(2000, src.Length - at));
        var cut = sw.IndexOf("_ => new DocumentSnapshot", StringComparison.Ordinal);
        Assert.True(cut > 0, "LoadDocumentAsync 의 fallback 을 못 찾았다 — 축이 비었다.");

        // 🔴 fallback **앞**에 적혀 있어야 한다. 뒤면(=없으면) 백지다.
        Assert.Contains($"\"{documentType}\" =>", sw.Substring(0, cut));
    }

    // ═══════════════════════════════════════════════════════════════
    // 축 ⑦ 저장 안 한 전표는 **백지를 띄우지 않는다** (3차검증 R-11)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 저장 안 한 전표의 id 는 <b>화면이 만든 GUID</b> 라 DB 에 없다. 창문은
    ///   「비었나」만 봤으므로 GUID 는 통과하고 <b>백지</b>가 떴다(검증팀 3차 R-11).
    /// </summary>
    /// <remarks>
    /// 🔴 이 축은 <b>DB 없이도 재야 한다</b> — 그래서 판정이 함수로 떼어져 있고,
    ///   여기서 <b>네 경우를 표로</b> 잰다. 「DB 가 있어야 잴 수 있다」로 두면 또 빈다.
    /// </remarks>
    [Theory(DisplayName = "축⑦ 🔴 저장 안 한 전표는 **막는다** · 「인쇄」·「이메일」은 종전대로(R-11)")]
    // RequireExisting, UseSampleData, found  →  막아야 하나
    [InlineData(true,  false, false, true)]   // 🔴 미리보기 + DB 에 없다 → 막는다
    [InlineData(true,  false, true,  false)]  // 미리보기 + 찾았다 → 그린다
    [InlineData(false, false, false, false)]  // 🔴 「인쇄」·「이메일」 → **절대 막지 않는다**(#20 P0)
    [InlineData(true,  true,  false, false)]  // 🔴 양식설정 예시 자료 → DB 를 안 보므로 통과
    public void Axis7_MissingDocument_IsBlockedOnlyInPreview(
        bool requireExisting, bool useSample, bool found, bool expectReject)
    {
        var ov = new PdfPreviewOverrides { RequireExisting = requireExisting, UseSampleData = useSample };
        var m = PrivateMethod("ShouldRejectMissing");
        Assert.Equal(expectReject, (bool)m.Invoke(null, new object?[] { ov, found })!);
    }

    [Fact(DisplayName = "축⑦없음 🔴 인자를 안 주면 **막지 않는다** — 기존 4인자 호출이 안 바뀐다(#1)")]
    public void Axis7_NoOverrides_NeverBlocks()
    {
        var m = PrivateMethod("ShouldRejectMissing");
        Assert.False((bool)m.Invoke(null, new object?[] { null, false })!);
        Assert.False((bool)m.Invoke(null, new object?[] { null, true })!);
    }

    [Fact(DisplayName = "축⑦문 🔴 **미리보기 문이 실제로 그 값을 세운다** — 서비스만 고치면 안 먹는다")]
    public void Axis7_PreviewEndpoint_SetsRequireExisting()
    {
        var src = File.ReadAllText(Path.Combine(AppRoot(), "src", "HitPan.API",
            "Controllers", "DocumentController.cs"));
        var at = src.IndexOf("public async Task<IActionResult> PreviewPdf", StringComparison.Ordinal);
        Assert.True(at > 0, "전표 미리보기 문이 없다.");
        var body = src.Substring(at, Math.Min(2000, src.Length - at));

        Assert.Contains("RequireExisting = true", body);
        // 🔴 그리고 **사람 말**로 돌려줘야 한다 — 「(404)」는 고객에게 뜻이 없다.
        Assert.Contains("KeyNotFoundException", body);
    }
}
