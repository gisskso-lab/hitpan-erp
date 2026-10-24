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

    /// <summary>사장님 반려 ③ 에 이름이 적힌 그 6화면.</summary>
    public static TheoryData<string> PrintScreens() => new()
    {
        Path.Combine("Pages", "Sales", "QuotationPage.razor.cs"),
        Path.Combine("Pages", "Sales", "SalesOrderPage.razor.cs"),
        Path.Combine("Pages", "Sales", "DeliveryPage.razor"),
        Path.Combine("Pages", "Purchase", "PurchaseOrderPage.razor.cs"),
        Path.Combine("Pages", "Purchase", "PurchaseReceiptPage.razor.cs"),
        Path.Combine("Pages", "Purchase", "ReturnPage.razor.cs"),
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
}
