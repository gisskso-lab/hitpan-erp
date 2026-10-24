using System.Collections;
using System.Reflection;
using HitPan.Application.DTOs.Documents;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261024 D2 — 미리보기 주입·양식지 그림 게이트 (사장님 결재 ㉯ <b>조건 3</b>).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 파일이 지키는 것</b> — 결재 ㉯ 조건 3: 선택 인자를 넣는 코드를 빼면
/// 미리보기가 <b>실패해야</b> 한다. 조용히 저장값으로 떨어지면 게이트가 거짓이 된다.
/// </para>
/// <para>
/// 🔴 <b>축을 센다, 건수를 세지 않는다.</b> 축은 넷이다:
///   ① 주입 통로가 있나 · ② 저장 전 양식값이 <b>이기나</b> ·
///   ③ 양식지 그림이 <b>작성기까지 닿나</b> · ④ 예시 자료가 줄 수를 <b>따르나</b>.
/// 축마다 <b>음성 대조군</b>을 같이 둔다.
/// </para>
/// <para>
/// 🔴 기존 시험 파일은 <b>한 줄도 안 고쳤다</b>(#1). 이 파일은 더하기만 한 것이다.
/// DB 는 안 쓴다 — 매핑·예시자료는 자료 객체만 다룬다. CI 에서 <b>건너뛰지 않는다.</b>
/// </para>
/// <para>
/// 🔴 <b>이 게이트가 못 재는 것</b>(「통과」로 적지 않는다): 종이에 그림이 실제로 깔린
/// <b>모양</b> · 미리보기 ↔ 인쇄 <b>글자 완전 일치</b>. 둘 다 회사정보를 DB 에서 읽어야
/// 그릴 수 있어 여기서는 못 쟀다. 합격선 ①은 <b>여전히 미측정</b>이다.
/// </para>
/// </remarks>
public class FormPreviewOverrideGuardTests
{
    private static readonly Type SvcType = typeof(PdfRenderService);

    private static object MakeService() =>
        Activator.CreateInstance(SvcType,
            new object?[] { null, NullLogger<PdfRenderService>.Instance, null })!;

    private static MethodInfo PrivateMethod(string name) =>
        SvcType.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance
                              | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException(
            $"PdfRenderService.{name}(…) 이 없다 — 미리보기 주입 통로가 사라졌다(20261024 D2).");

    private static object? Get(object target, string prop) =>
        (target.GetType().GetProperty(prop)
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{prop} 이 없다."))
        .GetValue(target);

    // ═══════════════════════════════════════════════════════════════
    // 축 ① 주입 통로가 있나 — 없으면 미리보기가 저장값으로만 그린다
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "축① 🔴 정본 통로에 5인자 주입 입구가 있다 — 빼면 「편집하는 대로」가 불가능해진다")]
    public void Axis1_OverrideEntryPoint_Exists()
    {
        var m = SvcType.GetMethod(nameof(PdfRenderService.RenderDocumentAsync),
            new[] { typeof(string), typeof(string), typeof(string),
                    typeof(PdfPreviewOverrides), typeof(CancellationToken) });

        // 🔴 이 입구가 없으면 저장 전 값을 먹일 길이 0개다([1-V] P-2).
        Assert.NotNull(m);
    }

    [Fact(DisplayName = "축①음 🔴 음성 대조군 — 종전 4인자 호출이 그대로 살아 있다(조건 1 · 기존 호출부 무변화)")]
    public void Axis1_Negative_LegacyFourArgStillExists()
    {
        var m = SvcType.GetMethod(nameof(PdfRenderService.RenderDocumentAsync),
            new[] { typeof(string), typeof(string), typeof(string), typeof(CancellationToken) });

        // 🔴 이것이 사라지면 기존 호출부(이메일·팩스·DocumentController)가 전부 깨진다.
        //    결재 ㉯ 조건 1 = 기존 호출부는 한 글자도 안 고친다 (#1).
        Assert.NotNull(m);
    }

    // ═══════════════════════════════════════════════════════════════
    // 축 ② 저장 전 양식값이 이기나
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "축② 🔴 저장 전 양식값이 작성기 입력으로 그대로 넘어간다 — 여백·매수·절취선·모드")]
    public void Axis2_UnsavedTemplate_WinsThroughMapper()
    {
        var svc = MakeService();
        var dto = new FormTemplateDto
        {
            FormType = "tax_invoice",
            PaperMode = "preprint",
            MarginTopMm = 7,
            MarginLeftMm = 8,
            MarginRightMm = 9,
            MarginBottomMm = 11,
            ShowCompanyLogo = false,
            ShowBorder = false,
            PrintCopyMode = "supplier",
            ShowTearLine = true
        };

        var info = PrivateMethod("ToTemplateInfo").Invoke(svc, new object?[] { dto })!;

        // 🔴 자로 잰 mm 가 한 칸이라도 안 넘어가면 「편집하는 대로」가 거짓이 된다.
        Assert.Equal("preprint", Get(info, "PaperMode"));
        Assert.Equal(7m, Get(info, "MarginTopMm"));
        Assert.Equal(8m, Get(info, "MarginLeftMm"));
        Assert.Equal(9m, Get(info, "MarginRightMm"));
        Assert.Equal(11m, Get(info, "MarginBottomMm"));
        Assert.Equal(false, Get(info, "ShowCompanyLogo"));
        Assert.Equal(false, Get(info, "ShowBorder"));
        Assert.Equal("supplier", Get(info, "PrintCopyMode"));
        Assert.Equal(true, Get(info, "ShowTearLine"));
    }

    [Fact(DisplayName = "축②음 🔴 음성 대조군 — 매핑이 한 벌이다. 두 벌이 되면 미리보기와 인쇄물이 어긋난다")]
    public void Axis2_Negative_MapperIsSingleCopy()
    {
        // 🔴 ToTemplateInfo 가 사라지면(= 매핑을 미리보기용으로 따로 베끼면) 여기서 터진다.
        //    20261021작 §11-3: 미리보기 전용으로 따로 그리는 코드를 만들지 않는다.
        var m = PrivateMethod("ToTemplateInfo");
        Assert.Single(m.GetParameters());
        Assert.Equal(typeof(FormTemplateDto), m.GetParameters()[0].ParameterType);
    }

    // ═══════════════════════════════════════════════════════════════
    // 축 ③ 양식지 그림이 작성기까지 닿나 — 20261024 사장님 오더의 정면
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "축③ 🔴 양식지 그림이 작성기 입력에 닿는다 — 종전 0건이던 자리(§13)")]
    public void Axis3_FormBackground_ReachesRenderer()
    {
        var svc = MakeService();
        const string path = @"C:\forms\partner-form.png";
        var dto = new FormTemplateDto
        {
            FormType = "delivery",
            BackgroundImageUrl = path
        };

        var info = PrivateMethod("ToTemplateInfo").Invoke(svc, new object?[] { dto })!;

        // 🔴 이 한 줄이 없으면 고객이 양식지를 올려도 종이에 안 나온다.
        //    사장님: 이미 업체에서 쓰는 양식지가 있을수도 있으니.
        Assert.Equal(path, Get(info, "BackgroundImageUrl"));
    }

    [Fact(DisplayName = "축③음 🔴 음성 대조군 — 바깥 http 주소는 안 깐다(인쇄가 남의 서버에 묶이면 안 된다)")]
    public void Axis3_Negative_RemoteUrlRejected()
    {
        var svc = MakeService();
        var load = PrivateMethod("TryLoadFormBackground");

        // 🔴 바깥 주소는 거절 — 고객 PC 는 인터넷이 끊길 수 있다. 인쇄가 멈추면 장사가 멈춘다.
        Assert.Null(load.Invoke(svc, new object?[] { "https://example.com/form.png" }));
        Assert.Null(load.Invoke(svc, new object?[] { "http://example.com/form.png" }));

        // 없는 파일·빈 값도 조용히 null — 그림 없이라도 종이는 나와야 한다(#1).
        Assert.Null(load.Invoke(svc, new object?[] { @"C:\no-such-dir\no-such-file.png" }));
        Assert.Null(load.Invoke(svc, new object?[] { null }));
        Assert.Null(load.Invoke(svc, new object?[] { "   " }));
    }

    // ═══════════════════════════════════════════════════════════════
    // 축 ④ 예시 자료 — 전표 0건 시점에 양식을 맞출 수 있나
    // ═══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "축④ 🔴 예시 자료가 품목 줄 수를 따른다 — 「몇 줄부터 두 쪽이 되나」를 눈으로 본다")]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(40)]
    [InlineData(80)]
    public void Axis4_SampleSnapshot_HonorsLineCount(int lines)
    {
        var ov = new PdfPreviewOverrides { UseSampleData = true, SampleLineCount = lines };
        var snap = PrivateMethod("BuildSampleSnapshot")
            .Invoke(null, new object?[] { "delivery", ov })!;

        var list = (IList)Get(snap, "Lines")!;
        Assert.Equal(lines, list.Count);

        // 🔴 합계가 줄 합과 맞아야 한다 — 안 맞으면 미리보기로 검산을 못 한다.
        decimal sumSupply = 0, sumVat = 0;
        foreach (var l in list)
        {
            sumSupply += (decimal)Get(l!, "Supply")!;
            sumVat += (decimal)Get(l!, "Vat")!;
        }
        Assert.Equal(sumSupply, (decimal)Get(snap, "TotalSupply")!);
        Assert.Equal(sumVat, (decimal)Get(snap, "TotalVat")!);
        Assert.Equal(sumSupply + sumVat, (decimal)Get(snap, "TotalAmount")!);
    }

    [Fact(DisplayName = "축④음 🔴 음성 대조군 — 예시 종이에 「예시」가 반드시 찍힌다(진짜 계산서로 오해 금지)")]
    public void Axis4_Negative_SampleIsMarked()
    {
        var ov = new PdfPreviewOverrides { UseSampleData = true, SampleLineCount = 2 };
        var snap = PrivateMethod("BuildSampleSnapshot")
            .Invoke(null, new object?[] { "tax_invoice", ov })!;

        // 🔴 번호 칸·거래처 칸·비고에 예시 표식이 있어야 한다. 하나라도 빠지면
        //    고객이 미리보기 종이를 진짜 전표로 쓸 수 있다(§11-3).
        Assert.Contains("예시", (string)Get(snap, "DocNo")!);
        Assert.Contains("예시", (string)Get(snap, "PartnerName")!);
        Assert.Contains("예시", (string)Get(snap, "Remark")!);
    }

    [Fact(DisplayName = "축④음2 🔴 줄 수가 터무니없어도 종이는 나온다 — 0·음수·과대는 조인다")]
    public void Axis4_Negative_LineCountClamped()
    {
        foreach (var bad in new[] { 0, -5, 100000 })
        {
            var ov = new PdfPreviewOverrides { UseSampleData = true, SampleLineCount = bad };
            var snap = PrivateMethod("BuildSampleSnapshot")
                .Invoke(null, new object?[] { "delivery", ov })!;
            var list = (IList)Get(snap, "Lines")!;

            Assert.InRange(list.Count, 1, 200);
        }
    }
}
