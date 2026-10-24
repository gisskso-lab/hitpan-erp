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

    /// <summary>작성기가 실제로 받아들이는 1x1 PNG. 🔴 "진짜 읽히는 그림"이라야 게이트가 문다.</summary>
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    /// <summary>허용 폴더 <b>안</b>에 유효한 PNG 를 놓는다.</summary>
    private static string WriteValidPng()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HitPan", "form-backgrounds");
        Directory.CreateDirectory(root);
        var p = Path.Combine(root, $"gate-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(p, Convert.FromBase64String(OnePixelPngBase64));
        return p;
    }

    /// <summary>
    /// 허용 폴더 <b>밖</b>에 유효한 PNG 를 놓는다. 🔴 경로 가둠이 <b>단독으로</b>
    /// 빨개지려면, 확장자·존재·디코드를 **전부 통과하는** 파일이 밖에 있어야 한다.
    /// (내가 EXP-C 로 적발: 종전 케이스는 전부 다른 검사에서 걸려 가둠을 안 쟀다.)
    /// </summary>
    private static string WriteValidPngOutsideRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hitpan-gate-outside");
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, $"outside-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(p, Convert.FromBase64String(OnePixelPngBase64));
        return p;
    }

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
    [Fact(DisplayName = "축③ 🔴🔴 실인쇄에는 양식지 그림이 **안 깔린다** — 이중 인쇄 금지(반려 R-1 회귀)")]
    public void Axis3_RealPrint_NeverGetsBackground()
    {
        // 🔴 **유효한 그림을 허용 폴더에 실제로 놓고** 잰다.
        //   종전 판은 존재하지 않는 form.png 를 줬다 ⇒ 방어줄을 빼도 **경로·존재 검사에서**
        //   null 이 나와 **다른 이유로 초록**이었다(내가 EXP-A 로 적발). 거짓 게이트였다.
        var svc = MakeService();
        var png = WriteValidPng();
        try
        {
            var info = PrivateMethod("ToTemplateInfo").Invoke(svc, new object?[] {
                new FormTemplateDto { FormType = "delivery", BackgroundImageUrl = png } })!;

        // 🔴 overrides 가 null = 실인쇄(전표 인쇄·이메일·팩스). 그림은 **절대** 안 간다.
        //    §13-8: 양식용지는 종이에 이미 인쇄돼 있다 — 또 찍으면 이중 인쇄다.
        //    §13-10: 순백지에 업체 그림 찍기는 **등재만**이다.
            // 🔴 이 그림은 **진짜로 읽히는** 파일이다. 방어줄을 빼면 bytes 가 나와 빨개진다.
            var real = PrivateMethod("ResolveFormBackground").Invoke(svc, new object?[] { null, info });
            Assert.Null(real);
        }
        finally { try { File.Delete(png); } catch { /* 지워지면 그만 */ } }
    }

    [Fact(DisplayName = "축③음 🔴 음성 대조군 — 미리보기 경로는 **그리려고 시도한다**(막아 둔 게 아니다)")]
    public void Axis3_Negative_PreviewPathDoesAttempt()
    {
        var svc = MakeService();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HitPan", "form-backgrounds");
        Directory.CreateDirectory(root);
        var good = Path.Combine(root, $"gate-{Guid.NewGuid():N}.png");

        // 1x1 PNG — 작성기가 실제로 받아들이는 바이트다.
        File.WriteAllBytes(good, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        try
        {
            var info = PrivateMethod("ToTemplateInfo").Invoke(svc, new object?[] {
                new FormTemplateDto { FormType = "delivery", BackgroundImageUrl = good } })!;
            // 🔴 [재검증 C-3] 배경은 **양식 설계 미리보기**에서만 깔린다.
            var ov = new PdfPreviewOverrides { UseSampleData = true, IsFormDesignPreview = true };

            // 🔴 이것이 null 이면 「실인쇄에 안 깔린다」가 **아무것도 안 하는 함수**라서 통과한 것이다.
            var preview = PrivateMethod("ResolveFormBackground").Invoke(svc, new object?[] { ov, info });
            Assert.NotNull(preview);
            Assert.NotEmpty((byte[])preview!);
        }
        finally { try { File.Delete(good); } catch { /* 지워지면 그만 */ } }
    }

    [Fact(DisplayName = "축③안 🔴 작성기가 못 읽는 바이트면 **인쇄가 안 죽고** 그림만 빠진다(반려 R-2)")]
    public void Axis3_BadImage_DoesNotKillPrinting()
    {
        var svc = MakeService();
        var load = PrivateMethod("TryLoadFormBackground");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HitPan", "form-backgrounds");
        Directory.CreateDirectory(root);

        // 🔴 PNG 이름인데 속이 PDF·빈 파일·쓰레기. 종전 판은 이걸 그리는 자리까지 끌고 가
        //    QuestPDF 가 터졌고 **그 양식의 인쇄·이메일·팩스가 전부 500** 이었다(#20).
        var cases = new (string Name, byte[] Body)[]
        {
            ($"gate-pdf-{Guid.NewGuid():N}.png", System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n%쓰레기")),
            ($"gate-empty-{Guid.NewGuid():N}.png", Array.Empty<byte>()),
            ($"gate-junk-{Guid.NewGuid():N}.jpg", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
        };
        foreach (var (name, body) in cases)
        {
            var p = Path.Combine(root, name);
            File.WriteAllBytes(p, body);
            try
            {
                // 🔴 던지지 않고 null 이어야 한다. 던지면 그 자리에서 전표가 안 나간다.
                var ex = Record.Exception(() => load.Invoke(svc, new object?[] { p }));
                Assert.Null(ex);
                Assert.Null(load.Invoke(svc, new object?[] { p }));
            }
            finally { try { File.Delete(p); } catch { /* 지워지면 그만 */ } }
        }
    }

    [Fact(DisplayName = "축③밖 🔴 허용 폴더 밖·바깥 주소·그림 아닌 형식은 **안 읽는다**(반려 R-4)")]
    public void Axis3_PathAndFormat_AreFenced()
    {
        var svc = MakeService();
        var load = PrivateMethod("TryLoadFormBackground");

        // 🔴 고객이 본문에 적는 값이다. 검사 없이 읽으면 서버 파일이 종이에 실려 나간다.
        foreach (var bad in new string?[]
        {
            "https://example.com/f.png",          // 바깥 그물 — 인쇄가 남의 서버에 묶인다
            "http://example.com/f.png",
            @"\남의서버\공유\f.png",              // UNC — http 차단을 비켜 가던 길
            @"file:///C:/Windows/win.ini",
            @"C:\Windows\win.ini",                 // 허용 폴더 밖 + 그림 아님
            @"C:\Windows\System32\drivers\etc\hosts",
            "../../../secret.png",                 // 거슬러 오르기
            null, "   ",
        })
        {
            var ex = Record.Exception(() => load.Invoke(svc, new object?[] { bad }));
            Assert.Null(ex);
            Assert.Null(load.Invoke(svc, new object?[] { bad }));
        }
    }

    [Fact(DisplayName = "축③가둠 🔴 허용 폴더 **밖의 멀쩡한 PNG** 도 안 읽는다 — 가둠 단독 측정(반려 R-4)")]
    public void Axis3_Fence_BlocksValidImageOutsideRoot()
    {
        var svc = MakeService();
        var outside = WriteValidPngOutsideRoot();
        try
        {
            // 🔴 이 파일은 확장자·존재·디코드를 **전부 통과한다.** 막는 것은 **경로 가둠뿐**이다.
            //    그래서 가둠을 빼면 이 시험만 빨개진다 — 축이 단독으로 선다.
            Assert.Null(PrivateMethod("TryLoadFormBackground").Invoke(svc, new object?[] { outside }));
        }
        finally { try { File.Delete(outside); } catch { /* 지워지면 그만 */ } }
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

    // ═══════════════════════════════════════════════════════════════
    // 🔴 [재검증 보강] 검증팀 EXP-D 가 뚫은 자리 — 확장자·크기·형제폴더
    //    "방어는 집행됐는데 보호(게이트)가 0건이었다"
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "축③형제 🔴 **형제 폴더**는 안 읽는다 — 앞글자만 비교하면 뚫린다(재검증 R-4)")]
    public void Axis3_SiblingFolder_IsBlocked()
    {
        // 🔴 검증팀 실측: `…\form-backgrounds-probe\a.png` 가 **70바이트 읽혔다.**
        //    `StartsWith(root)` 가 구분자 없이 비교해 **형제 폴더가 통째로 통과**했다.
        var svc = MakeService();
        var sibling = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HitPan", "form-backgrounds-probe");
        Directory.CreateDirectory(sibling);
        var p = Path.Combine(sibling, $"sib-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(p, Convert.FromBase64String(OnePixelPngBase64));
        try
        {
            // 확장자·존재·디코드를 전부 통과하는 파일이다. 막는 것은 **구분자 비교**뿐이다.
            Assert.Null(PrivateMethod("TryLoadFormBackground").Invoke(svc, new object?[] { p }));
        }
        finally
        {
            try { File.Delete(p); Directory.Delete(sibling); } catch { /* 지워지면 그만 */ }
        }
    }

    [Fact(DisplayName = "축③형식 🔴 허용 폴더 안이어도 **그림 아닌 확장자**는 안 읽는다(EXP-D)")]
    public void Axis3_Extension_IsEnforced()
    {
        // 🔴 검증팀 EXP-D: 확장자 화이트리스트를 빼도 **16/16 초록**이었다 —
        //    집행은 돼 있는데 **보호가 없었다**(§13-5 합격선 5 가 글자로 요구한 그것).
        var svc = MakeService();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HitPan", "form-backgrounds");
        Directory.CreateDirectory(root);

        // 속은 멀쩡한 PNG 인데 **이름이 .svg·.pdf** 다. 확장자 검사만이 이것을 막는다.
        foreach (var ext in new[] { ".svg", ".pdf", ".txt", ".exe" })
        {
            var p = Path.Combine(root, $"ext-{Guid.NewGuid():N}{ext}");
            File.WriteAllBytes(p, Convert.FromBase64String(OnePixelPngBase64));
            try
            {
                Assert.Null(PrivateMethod("TryLoadFormBackground").Invoke(svc, new object?[] { p }));
            }
            finally { try { File.Delete(p); } catch { /* 지워지면 그만 */ } }
        }
    }

    [Fact(DisplayName = "축③크기 🔴 상한을 넘는 그림은 안 읽는다 — 종이 한 장이 PC 메모리를 먹으면 안 된다(EXP-D)")]
    public void Axis3_SizeCap_IsEnforced()
    {
        var svc = MakeService();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HitPan", "form-backgrounds");
        Directory.CreateDirectory(root);
        var p = Path.Combine(root, $"big-{Guid.NewGuid():N}.png");

        // 🔴 상한을 **넘기면서도 디코드는 되는** 파일을 만든다.
        //   [왜 이렇게까지] 그냥 큰 쓰레기 파일을 주면 **디코드 검사가 대신 막아**
        //   크기 검사를 빼도 초록이 된다 — 내가 실제로 그 거짓 초록을 한 번 냈다.
        //   ⇒ 멀쩡한 PNG 뒤에 패딩을 붙여 **크기 검사만이 막는** 모양을 만든다.
        var cap = (long)SvcType.GetField("MaxBackgroundBytes",
            BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var png = Convert.FromBase64String(OnePixelPngBase64);
        using (var fs = new FileStream(p, FileMode.Create, FileAccess.Write))
        {
            fs.Write(png, 0, png.Length);
            var pad = new byte[64 * 1024];
            while (fs.Length <= cap) fs.Write(pad, 0, pad.Length);
        }
        try
        {
            Assert.Null(PrivateMethod("TryLoadFormBackground").Invoke(svc, new object?[] { p }));
        }
        finally { try { File.Delete(p); } catch { /* 지워지면 그만 */ } }
    }

    [Fact(DisplayName = "축③설계 🔴 **전표 인쇄 미리보기**에는 배경이 안 깔린다 — 미리보기=인쇄물(재검증 C-3)")]
    public void Axis3_DocumentPreview_GetsNoBackground()
    {
        // 🔴 전표 인쇄 미리보기도 overrides 를 탄다. 거기에 배경이 깔리면
        //    미리보기엔 그림이 있고 실제 인쇄물엔 없다 ⇒ 합격선 ⑤ 가 거짓이 된다.
        var svc = MakeService();
        var png = WriteValidPng();
        try
        {
            var info = PrivateMethod("ToTemplateInfo").Invoke(svc, new object?[] {
                new FormTemplateDto { FormType = "delivery", BackgroundImageUrl = png } })!;

            // 전표 인쇄 미리보기 = IsFormDesignPreview 가 false 다.
            var docPreview = new PdfPreviewOverrides { UseSampleData = false };
            Assert.Null(PrivateMethod("ResolveFormBackground").Invoke(svc, new object?[] { docPreview, info }));

            // 🟢 대조 — 양식 설계 미리보기에서는 깔린다(막아 둔 게 아니다).
            var design = new PdfPreviewOverrides { UseSampleData = true, IsFormDesignPreview = true };
            Assert.NotNull(PrivateMethod("ResolveFormBackground").Invoke(svc, new object?[] { design, info }));
        }
        finally { try { File.Delete(png); } catch { /* 지워지면 그만 */ } }
    }

    // ═══════════════════════════════════════════════════════════════
    // 축 ③링크 — 링크로 울타리를 넘지 못한다 (2차 재검증 R-10)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>검증팀 2차 재검증 R-10 가설을 실측으로 바꾼다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <c>Path.GetFullPath</c> 는 링크를 풀지 않는다. 그래서 허용 폴더 <b>안에</b>
    ///   링크 폴더 하나를 만들어 두면 경로 가둠은 <b>통과하고</b> 실제 파일은 바깥이 된다.
    /// 🔴 이 시험은 <b>건너뛰지 않는다</b> — 윈도는 junction(권한 불필요), 그 밖은
    ///   디렉터리 심볼릭 링크로 <b>같은 축</b>을 잰다. 못 만들면 <b>빨개진다</b>,
    ///   「못 쟀다」를 초록으로 감추지 않는다(게이트 31번+ 사고).
    /// </remarks>
    [Fact(DisplayName = "축③링크 🔴 허용 폴더 안의 **링크**를 지나 바깥 그림을 읽지 못한다(R-10)")]
    public void Axis3_LinkInsideRoot_CannotEscape()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HitPan", "form-backgrounds");
        Directory.CreateDirectory(root);

        // 바깥에 **멀쩡한** PNG — 확장자·존재·크기·디코드를 전부 통과한다.
        //   그래야 빨개지는 이유가 **링크 울타리 하나**로 좁혀진다.
        var outsideDir = Path.Combine(Path.GetTempPath(), $"hitpan-link-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDir);
        File.WriteAllBytes(Path.Combine(outsideDir, "form.png"), Convert.FromBase64String(OnePixelPngBase64));

        var link = Path.Combine(root, $"gate-link-{Guid.NewGuid():N}");
        try
        {
            MakeDirectoryLink(link, outsideDir);

            // 울타리 안처럼 보이는 경로지만, 실제 파일은 바깥이다.
            var sneaky = Path.Combine(link, "form.png");
            Assert.True(File.Exists(sneaky), "링크가 안 걸렸다 — 이 시험은 아무것도 재지 못한다.");

            var svc = MakeService();
            Assert.Null(PrivateMethod("TryLoadFormBackground").Invoke(svc, new object?[] { sneaky }));
        }
        finally
        {
            try { Directory.Delete(link); } catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
            try { Directory.Delete(outsideDir, true); } catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
        }
    }

    /// <summary>윈도는 junction(권한 불필요), 그 밖은 디렉터리 심볼릭 링크.</summary>
    private static void MakeDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(Directory.Exists(link), $"junction 을 만들지 못했다 — {err}");
    }
}
