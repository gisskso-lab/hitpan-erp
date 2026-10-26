using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using HitPan.Contracts.Forms;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴🔴 로고 워터마크 게이트 — 20261026작1 §E-2 (DB-148 · 사장님 오더 2026-10-25 ④).
/// </summary>
/// <remarks>
/// 🔴🪤 <b>이 묶음이 왜 픽셀을 읽나</b> — 2026-10-26 실측에서 나온 1급 함정:
///   <c>&lt;image opacity='0.12'&gt;</c> 는 QuestPDF 2026.2.4 에서 <b>아무 경고 없이 무시된다</b>
///   (중앙픽셀 <c>R=255 G=0 B=0</c> — 대조군과 똑같았다).
///   <c>&lt;g opacity='0.12'&gt;</c> 로 감싸야 <c>R=255 G=223 B=223</c> 로 연해진다.
///   ⇒ 🔴 <b>코드에 <c>opacity</c> 라는 낱말이 있는지 세는 게이트는 거짓 통과한다.</b>
///   그래서 종이를 실제로 그려 <b>픽셀값을 읽는다.</b>
///
/// 🔴 <b>대조군 없는 게이트는 「미측정」이다</b>(누적 31번+). 켠 판과 끈 판을 둘 다 그려 비교한다.
/// 🔴 <b>DB 가 없어도 돈다</b> — 작성기를 반사로 만들고 SVG 생성만 떼어 쓴다.
/// </remarks>
public sealed class WatermarkRenderGateTests
{
    // ══════════════════════════════════════════════════════════════════════
    // 자 — 종이를 그려서 픽셀을 읽는다
    // ══════════════════════════════════════════════════════════════════════

    // 🔴🔴 [자를 먼저 재라] 처음에는 단색 PNG 를 **손으로 적은 base64** 로 두었다.
    //   그 바이트가 유효하지 않아 **아무것도 안 그려졌고**, 게이트 다섯 건이
    //   「그림이 도달하지 않았다」로 빨개졌다. 🔴 실코드가 아니라 **자가 고장난 것**이었다.
    //   ⇒ 손으로 적지 않고 **작성기로 만든다.** 만든 뒤 판독기로 색을 확인해
    //     「자가 정말 빨간 그림을 만들었나」를 같이 본다(아래 SelfCheck).

    private static byte[]? _red;
    private static byte[]? _blue;

    /// <summary>시험용 로고 — 순수 빨강 단색 PNG. 🔴 색이 뚜렷해야 연해진 것을 숫자로 가른다.</summary>
    private static byte[] RedPixelPng() => _red ??= SolidPng("#FF0000");

    /// <summary>시험용 양식지 — 순수 파랑 단색 PNG.</summary>
    private static byte[] BluePixelPng() => _blue ??= SolidPng("#0000FF");

    /// <summary>단색 PNG 한 장을 만든다 — 🔴 바이트를 손으로 적지 않는다.</summary>
    private static byte[] SolidPng(string hex)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(20, 20, Unit.Millimetre);
                p.Margin(0);
                p.PageColor(Color.FromHex(hex));
                p.Content().Text(string.Empty);
            });
        }).GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = QuestPDF.Infrastructure.ImageFormat.Png,
            RasterDpi = 72
        }).First();
    }

    [Fact(DisplayName = "G-W0 🔴🔴 **자부터 잰다** — 시험용 그림이 정말 빨강·파랑이고 판독기가 그 색을 읽는다")]
    public void GW0_SelfCheck_ProbeImagesAndDecoderWork()
    {
        // 🔴 이 축이 없으면 아래 모든 축이 「그림이 안 왔다」로 빨개져도
        //   실코드를 의심하게 된다 — 실제로 그런 일이 있었다(위 주석).
        var red = PngPixels.Decode(RedPixelPng());
        var blue = PngPixels.Decode(BluePixelPng());

        var r = red.At(red.Width / 2, red.Height / 2);
        var b = blue.At(blue.Width / 2, blue.Height / 2);

        Assert.True(r.R > 200 && r.G < 60 && r.B < 60, $"시험용 로고가 빨강이 아니다({r}).");
        Assert.True(b.B > 200 && b.R < 60 && b.G < 60, $"시험용 양식지가 파랑이 아니다({b}).");
    }

    private static PdfRenderService MakeRenderService()
    {
        var ctor = typeof(PdfRenderService).GetConstructors().First();
        var args = ctor.GetParameters().Select(p =>
            p.ParameterType.IsGenericType
            && p.ParameterType.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>)
                ? Activator.CreateInstance(
                    typeof(NullLogger<>).MakeGenericType(p.ParameterType.GetGenericArguments()[0]))
                : null).ToArray();

        return (PdfRenderService)Activator.CreateInstance(
            typeof(PdfRenderService),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: args, culture: null)!;
    }

    /// <summary>
    /// 🔴 작성기의 워터마크 SVG 생성기를 **그대로** 부른다 — 시험이 제 몫을 따로 만들지 않는다.
    /// </summary>
    /// <remarks>
    /// 🔴 시험이 SVG 를 손으로 조립하면 **실코드가 틀려도 초록**이 된다
    ///   (10/25 「글자로 재는 축은 어디 있나를 못 잰다」와 같은 모양).
    /// </remarks>
    private static string? BuildSvg(byte[]? logo, string ext, decimal opacity, decimal saturate)
    {
        var svc = MakeRenderService();
        var m = typeof(PdfRenderService).GetMethod("TryBuildWatermarkSvg",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        return (string?)m!.Invoke(svc, new object?[] { logo, "x" + ext, opacity, saturate });
    }

    /// <summary>한 장을 그려 PNG 로 뽑는다 — 🔴 흰 종이 위에 그려야 연해진 정도가 읽힌다.</summary>
    private static byte[] RenderPage(Action<PageDescriptor> body)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(60, 60, Unit.Millimetre);
                p.Margin(0);
                p.PageColor(Colors.White);
                body(p);
            });
        }).GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = QuestPDF.Infrastructure.ImageFormat.Png,
            RasterDpi = 72
        }).First();
    }

    /// <summary>워터마크 한 겹만 깐 종이 — 켠 판.</summary>
    private static byte[] RenderWatermarkOnly(string svg) => RenderPage(page =>
        page.Background().Layers(l =>
        {
            l.PrimaryLayer().Extend();
            l.Layer().AlignCenter().AlignMiddle().Width(40, Unit.Millimetre).Svg(svg);
        }));

    // ══════════════════════════════════════════════════════════════════════
    // G-W1 · 워터마크가 **픽셀로** 연해지나 (대조군 = 끈 판)
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W1 🔴🔴 워터마크를 켜면 종이 가운데가 **실제로 연해진다** (픽셀 판독 · 대조군 = 끈 판)")]
    public void GW1_WatermarkActuallyLightensCenterPixel()
    {
        var svg = BuildSvg(RedPixelPng(), ".png", WatermarkDefaults.Opacity, WatermarkDefaults.Saturate);
        Assert.NotNull(svg);

        var on = PngPixels.Decode(RenderWatermarkOnly(svg!));
        var off = PngPixels.Decode(RenderPage(_ => { }));   // 🔴 대조군 — 오늘의 종이(흰 종이)

        var onCenter = on.At(on.Width / 2, on.Height / 2);
        var offCenter = off.At(off.Width / 2, off.Height / 2);

        // 🔴 대조군이 정말 흰 종이인가 — 자부터 잰다. 여기가 틀리면 아래 비교가 뜻이 없다.
        Assert.True(offCenter is { R: > 250, G: > 250, B: > 250 },
            $"대조군이 흰 종이가 아니다({offCenter}) — 자가 고장났다.");

        // 🔴 켠 판은 **흰색이 아니어야** 한다 — 워터마크가 도달했다는 뜻이다.
        Assert.True(onCenter.G < 250 || onCenter.B < 250,
            $"워터마크를 켰는데 종이 가운데가 그대로 흰색이다({onCenter}) — 그림이 종이에 도달하지 않았다.");

        // 🔴 그러나 **연해야** 한다 — 진한 로고가 찍히면 본문을 가린다
        //   (사장님: "문서의 내용을 해치지 않을정도의 명도채도").
        //   순수 빨강이 그대로 찍히면 G·B 가 0 에 가깝다.
        Assert.True(onCenter.G > 180 && onCenter.B > 180,
            $"워터마크가 너무 진하다({onCenter}) — 본문을 가린다. opacity 가 안 걸린 것일 수 있다.");
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W2 · 🪤 opacity 가 <g> 에 걸렸나 — <image> 에 걸면 무시된다
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2 🔴🔴🪤 opacity 는 **`<g>` 에** 걸려 있다 — 음성 대조군: `<image>` 에 걸면 **안 연해진다**(실측으로 증명)")]
    public void GW2_OpacityMustBeOnGroupNotImage()
    {
        var svg = BuildSvg(RedPixelPng(), ".png", 0.12m, 1.0m);
        Assert.NotNull(svg);

        // ① 우리 코드가 정말 <g> 에 걸고 있나 — 자리를 **중괄호로 걸어** 본다.
        //    🔴 「opacity 라는 낱말이 있나」로 재면 <image opacity> 도 통과한다.
        Assert.Matches(@"<g opacity='[0-9.]+'", svg!);
        Assert.DoesNotMatch(@"<image[^>]*\sopacity=", svg!);

        // ② 🔴 음성 대조군 — 같은 숫자를 `<image>` 에 걸어 그려 보고
        //    **연해지지 않는다는 것**을 픽셀로 증명한다. 이것이 이 함정의 실체다.
        var b64 = Convert.ToBase64String(RedPixelPng());
        var wrongSvg =
            "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' "
            + "width='1000' height='1000' viewBox='0 0 1000 1000'>"
            + $"<image x='0' y='0' width='1000' height='1000' opacity='0.12' "
            + $"xlink:href='data:image/png;base64,{b64}'/></svg>";

        var right = PngPixels.Decode(RenderWatermarkOnly(svg!));
        var wrong = PngPixels.Decode(RenderWatermarkOnly(wrongSvg));

        var rightCenter = right.At(right.Width / 2, right.Height / 2);
        var wrongCenter = wrong.At(wrong.Width / 2, wrong.Height / 2);

        // 🔴 틀린 판은 **진하게** 나온다 — 그것이 이 축이 막는 사고다.
        Assert.True(wrongCenter.G < 60 && wrongCenter.B < 60,
            $"`<image opacity>` 판이 연해졌다({wrongCenter}) — 작성기 판이 바뀌었나. "
            + "이 축의 전제(「<image> 에 걸면 무시된다」)를 다시 실측하라.");

        // 🔴 우리 판은 연하다 — 두 판이 **다르다**는 것이 측정의 핵심이다.
        Assert.True(rightCenter.G - wrongCenter.G > 100,
            $"우리 판과 틀린 판이 구별되지 않는다(우리={rightCenter} 틀림={wrongCenter}) — 자가 고장났다.");
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W3 · 채도가 걸리나 (대조군 = 채도 1.0)
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W3 🔴 채도가 **실제로** 걸린다 — 색이 회색 쪽으로 간다 (대조군 = 채도 1.0)")]
    public void GW3_SaturateActuallyDesaturates()
    {
        // 🔴 명도는 1.0 로 고정해 **채도만** 본다 — 두 축을 같이 움직이면 어느 쪽이
        //   들었는지 알 수 없다(10/25 「축을 세라」).
        var grey = BuildSvg(RedPixelPng(), ".png", 1.0m, 0.0m);
        var vivid = BuildSvg(RedPixelPng(), ".png", 1.0m, 1.0m);
        Assert.NotNull(grey);
        Assert.NotNull(vivid);

        var g = PngPixels.Decode(RenderWatermarkOnly(grey!));
        var v = PngPixels.Decode(RenderWatermarkOnly(vivid!));

        var gc = g.At(g.Width / 2, g.Height / 2);
        var vc = v.At(v.Width / 2, v.Height / 2);

        // 🔴 채도 1.0 은 빨강 그대로 — R 이 크고 G·B 가 작다.
        Assert.True(vc.R - vc.G > 100, $"대조군(채도 1.0)이 빨강이 아니다({vc}) — 자가 고장났다.");

        // 🔴 채도 0 은 R·G·B 가 서로 가까워진다(회색).
        Assert.True(Math.Abs(gc.R - gc.G) < 40 && Math.Abs(gc.G - gc.B) < 40,
            $"채도 0 인데 색이 그대로다({gc}) — feColorMatrix 가 안 걸렸다.");
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W4 · 3겹이 동시에 사나 (양식지 그림 + 워터마크 + 본문)
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W4 🔴 양식지 그림·워터마크·본문 **3겹이 동시에** 산다 (서로 안 잡아먹는다)")]
    public void GW4_ThreeLayersCoexist()
    {
        var svg = BuildSvg(RedPixelPng(), ".png", WatermarkDefaults.Opacity, 1.0m);
        Assert.NotNull(svg);

        var png = RenderPage(page =>
        {
            page.Background().Layers(l =>
            {
                l.Layer().Image(BluePixelPng()).FitArea();                 // 1겹 양식지(파랑)
                l.PrimaryLayer().Extend();
                l.Layer().AlignCenter().AlignMiddle()
                         .Width(30, Unit.Millimetre).Svg(svg!);            // 2겹 워터마크(빨강)
            });
            page.Content().AlignCenter().AlignMiddle()
                .Text("본문").FontSize(20).FontColor(Colors.Black);        // 3겹 본문
        });

        var img = PngPixels.Decode(png);
        var corner = img.At(4, 4);                                  // 양식지만 있는 자리
        var center = img.At(img.Width / 2, img.Height / 2);

        // 🔴 모서리는 **양식지 그림만** — 파랑이어야 한다.
        Assert.True(corner.B > 200 && corner.R < 60,
            $"모서리에 양식지 그림이 없다({corner}) — 1겹이 죽었다.");

        // 🔴 가운데는 양식지 + 워터마크가 **섞여** 있어야 한다 — 파랑에 빨강이 더해진다.
        Assert.True(center.R > corner.R + 10,
            $"가운데에 워터마크가 안 섞였다(모서리={corner} 가운데={center}) — 2겹이 죽었다.");
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W6 · 🔴 절대 게이트 — 깨진 그림에도 종이는 나간다 (#20)
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W6 🔴🔴 깨진 로고·빈 바이트로도 **예외 0건** — 워터마크만 빠지고 종이는 나간다 (#20)")]
    public void GW6_BrokenLogoNeverBreaksThePaper()
    {
        // 🔴 워터마크는 **모든 양식**에 걸린다 ⇒ 한 군데 터지면 전표 인쇄·이메일·팩스가
        //   전부 멈춘다. 이것이 이 차수의 **절대 게이트**다.

        // ① 빈 바이트 · null → SVG 를 안 만든다(null).
        Assert.Null(BuildSvg(null, ".png", 0.12m, 0.15m));
        Assert.Null(BuildSvg(Array.Empty<byte>(), ".png", 0.12m, 0.15m));

        // ② 그림이 아닌 형식 → null (data: 주소에 모르는 형식을 적지 않는다)
        Assert.Null(BuildSvg(RedPixelPng(), ".pdf", 0.12m, 0.15m));
        Assert.Null(BuildSvg(RedPixelPng(), ".svg", 0.12m, 0.15m));

        // ③ 🔴 속이 깨진 바이트를 **실제로 그려 본다** — 예외가 나면 안 된다.
        //   확장자는 png 인데 속은 "%PDF-" 다(이름은 거짓말을 한다).
        var junk = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
        var svg = BuildSvg(junk, ".png", 0.12m, 0.15m);
        Assert.NotNull(svg);   // 글자는 만들어진다 — 바이트 검사는 보관소·로더가 한다.

        var ex = Record.Exception(() => RenderWatermarkOnly(svg!));
        Assert.Null(ex);       // 🔴 그려도 안 터진다 — 종이는 나간다.
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W12 · 로캘이 바뀌어도 소수점은 '.'
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W12 🔴 로캘이 유럽이어도 소수점이 `.` 로 찍힌다 — `,` 면 **모든 양식이 500** 이 된다")]
    public void GW12_DecimalSeparatorSurvivesForeignLocale()
    {
        // 🔴 교정 기록: 한국 로캘(ko-KR)은 소수점이 '.' 라 **ko-KR 에서는 안 터진다**(실측 확인).
        //   터지는 것은 서버 로캘이 de-DE 등으로 잡힌 PC 다. 「우리 PC 에서는 된다」는 검증이 아니다.
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var svg = BuildSvg(RedPixelPng(), ".png", 0.12m, 0.15m);
            Assert.NotNull(svg);

            Assert.Contains("opacity='0.12'", svg!, StringComparison.Ordinal);
            Assert.Contains("values='0.15'", svg!, StringComparison.Ordinal);
            Assert.DoesNotContain("0,12", svg!, StringComparison.Ordinal);

            // 🔴 글자만 보지 않고 **그려 본다** — 깨진 SVG 는 작성기가 예외를 던진다.
            Assert.Null(Record.Exception(() => RenderWatermarkOnly(svg!)));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W14 · 코드 제안값 == DDL DEFAULT
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W14 🔴 코드 제안값과 **DDL DEFAULT 가 같다** — 공통 설정 행이 없는 고객과 있는 고객의 종이가 달라지면 안 된다")]
    public void GW14_CodeDefaultsMatchDdlDefaults()
    {
        var ddl = File.ReadAllText(Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql"));
        var table = Regex.Match(ddl,
            @"CREATE TABLE `print_common_settings`[\s\S]*?ENGINE=").Value;
        Assert.False(string.IsNullOrEmpty(table),
            "출하 DDL 에 print_common_settings 가 없다 — 신규 고객에게 공통 설정이 영영 안 생긴다.");

        AssertDefault(table, "watermark_opacity", WatermarkDefaults.Opacity);
        AssertDefault(table, "watermark_saturate", WatermarkDefaults.Saturate);
        AssertDefault(table, "watermark_scale_pct", WatermarkDefaults.ScalePct);

        static void AssertDefault(string table, string column, decimal expected)
        {
            // 🔴 `decimal(3,2)` 안에 콤마가 있어 `[^,]*?` 로는 DEFAULT 에 닿지 못한다
            //   — 처음 판이 그것 때문에 「DEFAULT 가 없다」고 했다. 줄 끝까지 본다.
            var m = Regex.Match(table, $@"`{column}`[^\r\n]*?DEFAULT\s+([0-9.]+)");
            Assert.True(m.Success, $"DDL 에 {column} 의 DEFAULT 가 없다.");
            var actual = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.True(actual == expected,
                $"{column}: DDL DEFAULT {actual} 와 코드 제안값 {expected} 가 다르다 — "
                + "공통 설정 행이 없는 고객과 있는 고객의 종이가 달라진다.");
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W15 · 🪤 PrimaryLayer 가 빠지면 터진다
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W15 🔴🪤 `Layers` 에 **PrimaryLayer 가 있다** — 빠지면 그 양식의 인쇄가 전부 예외다(실측으로 증명)")]
    public void GW15_LayersMustHaveExactlyOnePrimaryLayer()
    {
        // ① 우리 코드에 있나
        // 🔴🔴 [2026-10-26 [4] 중 R-5 봉합] 종전에는 `File.ReadAllText` 로 **날 텍스트**를 봤다.
        //   그래서 누가 `// layers.PrimaryLayer().Extend();` 로 **주석 처리만 해도 초록**이었다.
        //   같은 레포에 주석을 걷어내는 자(`ReadCode = StripComments(ReadSource(...))`)가
        //   **이미 있었는데** 새 게이트가 그걸 안 썼다. 이제 그 자를 **그대로** 쓴다(두 벌 금지).
        var code = ReadCodeNoComments("src", "HitPan.Application", "Services", "PdfRenderService.cs");
        Assert.Matches(@"page\.Background\(\)\.Layers\(", code);
        Assert.Matches(@"layers\.PrimaryLayer\(\)\.Extend\(\);", code);

        // ② 🔴 음성 대조군 — 빼면 **정말 터지는지** 그려서 증명한다.
        //   이것이 없으면 ① 은 「왜 필요한지 모르는 글자 검사」일 뿐이다.
        var svg = BuildSvg(RedPixelPng(), ".png", 0.12m, 0.15m);
        var ex = Record.Exception(() => RenderPage(page =>
            page.Background().Layers(l =>
                l.Layer().AlignCenter().AlignMiddle().Width(30, Unit.Millimetre).Svg(svg!))));

        Assert.NotNull(ex);
        Assert.Contains("primary layer", ex!.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 주석을 걷어낸 코드를 읽는다 — [2026-10-26 [4] R-5 봉합].
    /// </summary>
    /// <remarks>
    /// 🔴 <b>걷어내기를 새로 쓰지 않는다.</b> 같은 레포의
    ///   <c>FormPaperSizeAndBackgroundUploadGateTests.ReadCode</c> 를 반사로 부른다 —
    ///   그 함수에는 <b>정확성을 재는 축</b>(축⒢ <c>AxisG_StripCommentsIsCorrect</c>)이 붙어 있다.
    ///   두 벌을 만들면 한쪽만 고쳐지는 날이 온다(이 차수의 §B-4 교훈과 같은 모양).
    /// </remarks>
    private static string ReadCodeNoComments(params string[] parts)
    {
        var m = typeof(FormPaperSizeAndBackgroundUploadGateTests).GetMethod(
            "ReadCode", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        return (string)m!.Invoke(null, new object?[] { parts })!;
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W16 · 🔴 가로 용지에서도 워터마크가 **종이에 남는다** ([4] 반려 R-1)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 이 축이 왜 생겼나 — 2026-10-26 [4] 검증팀 실측:
    /// <b>가로 양식 + 크기 80% 이상이면 워터마크가 통째로 사라졌다</b>
    /// (A4가로·A5가로에서 PDF 7.3KB · 래스터 완전 백지 · 예외 0 · 로그 0).
    /// 고객이 사장님 오더의 <i>"정중앙 크게"</i> 를 따르려고 숫자를 올리는 <b>바로 그 동작</b>이
    /// 기능을 없앴다. ⇒ 가로·세로 × 60·80·100 을 <b>전수로 픽셀을 읽는다.</b>
    /// </summary>
    [Theory(DisplayName = "G-W16 🔴 세로·가로 용지 × 60·80·100% — 아홉 조합 **전부** 종이에 로고가 남는다")]
    // 세로 — 종전에도 되던 쪽(양성 대조군)
    [InlineData(210f, 297f, 60)]
    [InlineData(210f, 297f, 80)]
    [InlineData(210f, 297f, 100)]
    // 🔴 가로 — 종전에 **사라졌던 쪽**
    [InlineData(297f, 210f, 60)]
    [InlineData(297f, 210f, 80)]
    [InlineData(297f, 210f, 100)]
    // 🔴 A5 가로 — 더 짧은 쪽
    [InlineData(210f, 148f, 60)]
    [InlineData(210f, 148f, 80)]
    [InlineData(210f, 148f, 100)]
    public void GW16_WatermarkSurvivesOnLandscapePaper(float widthMm, float heightMm, int scalePct)
    {
        // 🔴🔴 [2026-10-26 재검증 V-1 봉합] **제품의 조립을 불러서** 그린다.
        //   종전 이 축은 `Layers`·`MaxWidth`·`MaxHeight` 를 **시험 안에서 복제**하고
        //   제품에서 가져오는 것은 길이 한 줄뿐이었다 ⇒ 검증팀이 실코드에 R-1 버그를
        //   되살려 놓고 돌렸는데 **10/10 초록**이었다. 자가 엉뚱한 것을 재고 있었다.
        var png = RenderWithProductBackground(widthMm, heightMm, scalePct);

        var px = PngPixels.Decode(png);
        var c = px.At(px.Width / 2, px.Height / 2);

        // 🔴 흰 종이면 **그림이 안 들어간 것**이다 — 종전 사고의 모양이 바로 이것이었다.
        Assert.True(c.R > 200 && c.G < 252 && c.B < 252,
            $"{widthMm}×{heightMm}mm · {scalePct}% 에서 중앙픽셀이 {c} 다 — 워터마크가 종이에 없다.");
    }

    [Fact(DisplayName = "G-W16음 🔴🔴 음성 대조군 — **폭만 보는 종전 조립**은 가로 용지에서 백지를 낸다(자가 제 몫을 하는 증거)")]
    public void GW16_Negative_WidthOnlyLosesTheImageOnLandscape()
    {
        // 🔴 이 축은 **자를 재는 축**이다. 종전 조립(폭 × % · 상한 없음)을 그려서
        //   「정말 그림이 사라지나」를 보여 준다. 이것이 흰 종이를 못 내면
        //   위 G-W16 의 양성 단언은 「원래 되던 것」을 재는 셈이 되므로 그때는 둘 다 못 믿는다.
        const float w = 297f, h = 210f;
        var oldSide = w * 100 / 100f;      // 종전: 폭의 100% = 297mm (종이 높이 210mm 를 넘는다)

        var svg = BuildSvg(RedPixelPng(), ".png", 0.12m, 0.15m);
        var png = RenderSizedPage(w, h, page =>
            page.Background().Layers(l =>
            {
                l.PrimaryLayer().Extend();
                l.Layer().AlignCenter().AlignMiddle().Width(oldSide, Unit.Millimetre).Svg(svg!);
            }));

        var px = PngPixels.Decode(png);
        var c = px.At(px.Width / 2, px.Height / 2);

        Assert.True(c.R > 250 && c.G > 250 && c.B > 250,
            $"종전 계산이 백지를 내지 않았다({c}) — 이 음성 대조군의 전제가 바뀌었다. 다시 재라.");

        // 🔴🔴 그리고 **제품 조립은 같은 종이에서 그림을 남긴다** — 같은 용지·같은 100% 로 대조한다.
        //   둘을 한 축에 둔 까닭: 「종전 조립은 사라지고 제품 조립은 남는다」가 한 번에 읽혀야
        //   다음 사람이 이 축을 지우지 않는다.
        var good = PngPixels.Decode(RenderWithProductBackground(w, h, 100));
        var gc = good.At(good.Width / 2, good.Height / 2);
        Assert.True(gc.R > 200 && gc.G < 252 && gc.B < 252,
            $"제품 조립이 같은 종이에서 백지를 냈다({gc}) — R-1 이 재발했다.");
    }

    [Fact(DisplayName = "G-W16자 🔴 자 검사 — 이 축은 **제품의 조립 함수**를 부른다(복제본을 재지 않는다)")]
    public void GW16_SelfCheck_UsesProductAssembly()
    {
        // 🔴 V-1 의 재발 방지 — 제품에 조립 함수가 있고, 작성기가 **그 함수를 부르는지** 본다.
        //   함수가 사라지면 아래 반사가 빨개지고, 호출이 사라지면 글자 검사가 빨개진다.
        Assert.NotNull(typeof(PdfRenderService).GetMethod("ComposeDocumentBackground",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));

        var code = ReadCodeNoComments("src", "HitPan.Application", "Services", "PdfRenderService.cs");
        Assert.Matches(@"ComposeDocumentBackground\(\s*\r?\n?\s*page,", code);
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W17 · 🔴 「공통 + 양식마다」 판정 전수 ([4] 반려 R-2 · [3-V] P1-2)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 이 축이 왜 생겼나 — 종전에는 <c>show_watermark</c> 가 <c>NOT NULL DEFAULT 0</c> 이고
    /// 코드가 비널러블 <c>bool</c> 이어서 <b>「나는 정하지 않았다」가 없었다.</b>
    /// 그래서 ① 공통 켜기가 <b>양식 행이 있는 고객에게 영영 안 닿고</b>,
    /// ② 공통만 켠 고객이 양식에서 <b>여백만 고쳐 저장해도 워터마크가 조용히 사라졌다.</b>
    /// 🔴 그 조합(「공통 켬 + 양식 행 있음」)을 재는 시험이 <b>0건</b>이었다.
    /// </summary>
    [Theory(DisplayName = "G-W17 🔴 양식별→공통 판정 전수 — 「안 정했다(null)」가 공통을 따른다")]
    // 안 정한 양식(기본) — 공통을 그대로 따른다. 🔴 이 두 줄이 R-2 의 봉합이다.
    [InlineData("quotation", null, true, true)]
    [InlineData("quotation", null, false, false)]
    // 이 양식만 켬 — 공통이 꺼져 있어도 깐다.
    [InlineData("quotation", true, false, true)]
    [InlineData("quotation", true, true, true)]
    // 이 양식만 끔 — 공통이 켜져 있어도 안 깐다.
    [InlineData("quotation", false, true, false)]
    [InlineData("quotation", false, false, false)]
    // 🔴 세금계산서(결-K) — 무엇을 켜도 안 깐다.
    [InlineData("tax_invoice", null, true, false)]
    [InlineData("tax_invoice", true, true, false)]
    [InlineData("tax_invoice", true, false, false)]
    public void GW17_WatermarkDecisionTable(string docType, bool? formSays, bool commonOn, bool expected)
    {
        var m = typeof(PdfRenderService).GetMethod("ShouldLayWatermark",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(m);

        var actual = (bool)m!.Invoke(null, new object?[] { docType, formSays, commonOn })!;
        Assert.Equal(expected, actual);
    }

    [Fact(DisplayName = "G-W17자 🔴 자 검사 — 켬/끔 칸이 **세 값**이다(비널러블로 되돌리면 빨개진다)")]
    public void GW17_ShowWatermarkMustBeThreeValued()
    {
        // 🔴 여기가 비널러블로 되돌아가면 위 표의 `null` 줄은 **표현 자체가 불가능**해진다.
        //   그러면 R-2 가 그대로 재발한다 ⇒ 칸의 모양을 못으로 박는다.
        var info = typeof(PdfRenderService).GetNestedType("FormTemplateInfo", BindingFlags.NonPublic);
        Assert.NotNull(info);
        var prop = info!.GetProperty("ShowWatermark");
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool?), prop!.PropertyType);

        var dto = typeof(HitPan.Application.DTOs.Settings.FormTemplateDto).GetProperty("ShowWatermark");
        Assert.NotNull(dto);
        Assert.Equal(typeof(bool?), dto!.PropertyType);

        // 🔴 DDL 두 자리(마이그·출하본)도 NOT NULL 이 아니어야 한다 — 칸이 두 값이면 코드가 무의미하다.
        foreach (var path in new[]
                 {
                     Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL",
                         "DB-148_form_watermark_print_background.sql"),
                     Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql")
                 })
        {
            var sql = File.ReadAllText(path);
            var m = Regex.Match(sql, @"`show_watermark`[^\r\n]*");
            Assert.True(m.Success, $"show_watermark 칸이 없다: {path}");
            Assert.DoesNotContain("NOT NULL", m.Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W20 · 🔴 네 형식 전부 그려지고, 깨진 바이트로도 **안 터진다** ([3-V] P1-1)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 [3-V] P1-1 — 게이트가 <c>.png</c> <b>하나만</b> 먹였는데 보관소는 네 형식을 받는다.
    /// ⇒ 「형식 때문에 인쇄가 500 이 되는 길」이 <b>미측정</b>이었다. 네 형식을 전부 그린다.
    /// </summary>
    [Theory(DisplayName = "G-W20 🔴 보관소가 받는 네 형식 모두 **종이에 그려진다**")]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    [InlineData(".bmp")]
    public void GW20_AllAllowedFormatsRender(string ext)
    {
        var bytes = ext switch
        {
            ".jpg" => SolidImage("#FF0000", QuestPDF.Infrastructure.ImageFormat.Jpeg),
            ".webp" => SolidImage("#FF0000", QuestPDF.Infrastructure.ImageFormat.Webp),
            ".bmp" => Bmp24Red(),
            _ => RedPixelPng()
        };

        var svg = BuildSvg(bytes, ext, 0.12m, 0.15m);
        Assert.NotNull(svg);

        var px = PngPixels.Decode(RenderWatermarkOnly(svg!));
        var c = px.At(px.Width / 2, px.Height / 2);

        Assert.True(c.R > 200 && c.G < 252 && c.B < 252,
            $"{ext} 로고가 종이에 안 그려졌다(중앙픽셀 {c}).");
    }

    [Fact(DisplayName = "G-W20음 🔴 음성 대조군 — 그림이 아닌 바이트가 와도 **종이는 나간다**(500 아님)")]
    public void GW20_Negative_JunkBytesNeverThrow()
    {
        // 🔴 보관소가 미리 열어 보므로 여기까지 올 일은 거의 없다. 그래도 **인쇄는 멈추면 안 된다**(#20).
        var junk = System.Text.Encoding.UTF8.GetBytes("이건 그림이 아니다");
        var svg = BuildSvg(junk, ".png", 0.12m, 0.15m);
        Assert.NotNull(svg);

        var ex = Record.Exception(() =>
        {
            var px = PngPixels.Decode(RenderWatermarkOnly(svg!));
            var c = px.At(px.Width / 2, px.Height / 2);
            // 아무것도 안 그려진다 — 흰 종이다. 그것이 옳은 결과다.
            Assert.True(c.R > 250 && c.G > 250 && c.B > 250, $"깨진 바이트가 뭔가를 그렸다({c}).");
        });
        Assert.Null(ex);
    }

    [Fact(DisplayName = "G-W20전 🔴 SVG 를 **미리 열어 본다** — 못 열면 null 이고 그리는 자리로 안 넘어간다")]
    public void GW20_SvgIsDecodedBeforeDrawing()
    {
        // 🔴 [3-V] P1-1 — 그리는 자리(`GeneratePdf()` 안)에는 try/catch 가 없다.
        //   ⇒ 여는 일을 **미리** 해야 한다. 깨진 글자를 주면 열기가 거절되는지 실제로 본다.
        var svc = MakeRenderService();
        var m = typeof(PdfRenderService).GetMethod("TryDecodeWatermarkSvg",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);

        var good = m!.Invoke(svc, new object?[] { BuildSvg(RedPixelPng(), ".png", 0.12m, 0.15m) });
        Assert.NotNull(good);

        // 🔴 음성 대조군 — SVG 가 아니면 **예외를 던지지 않고 null** 로 떨어져야 한다.
        var bad = m.Invoke(svc, new object?[] { "<svg this is not xml" });
        Assert.Null(bad);
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-W5 · 🔴 양식지 그림은 **켠 양식에만** 종이에 깔린다 (설계 §7 G-W5)
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W5 🔴 `print_background` 켬/끔이 **실제로 그림을 읽고/안 읽고**를 가른다(대조군 두 판)")]
    public void GW5_PrintBackgroundGatesTheImage()
    {
        var tenant = $"gate-{Guid.NewGuid():N}";
        var dir = HitPan.Application.Services.FormBackgroundPaths.TenantRoot(tenant);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "bg.png");
        File.WriteAllBytes(file, BluePixelPng());

        try
        {
            var infoType = typeof(PdfRenderService).GetNestedType("FormTemplateInfo", BindingFlags.NonPublic)!;
            var resolve = typeof(PdfRenderService).GetMethod("ResolveFormBackground",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var svc = MakeRenderService();

            // ① 끈 양식(기본) — 🔴 그림을 **읽지 않는다**. 끈 고객의 종이는 종전과 같다(#1).
            var off = Activator.CreateInstance(infoType)!;
            infoType.GetProperty("BackgroundImageUrl")!.SetValue(off, file);
            infoType.GetProperty("PrintBackground")!.SetValue(off, false);
            Assert.Null(resolve.Invoke(svc, new object?[] { null, off }));

            // ② 켠 양식 — 🔴 **읽는다**. 이 대조군이 없으면 ① 은 「울타리가 아니라 고장」일 수 있다.
            var on = Activator.CreateInstance(infoType)!;
            infoType.GetProperty("BackgroundImageUrl")!.SetValue(on, file);
            infoType.GetProperty("PrintBackground")!.SetValue(on, true);
            var bytes = (byte[]?)resolve.Invoke(svc, new object?[] { null, on });
            Assert.NotNull(bytes);
            Assert.True(bytes!.Length > 0, "켠 양식인데 그림이 비었다.");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* 지우지 못해도 시험 결과는 바뀌지 않는다 */ }
        }
    }

    /// <summary>그 형식으로 단색 그림 한 장을 만든다 — 🔴 바이트를 손으로 적지 않는다.</summary>
    private static byte[] SolidImage(string hex, QuestPDF.Infrastructure.ImageFormat fmt)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(20, 20, Unit.Millimetre);
                p.Margin(0);
                p.PageColor(Color.FromHex(hex));
                p.Content().Text(string.Empty);
            });
        }).GenerateImages(new ImageGenerationSettings { ImageFormat = fmt, RasterDpi = 72 }).First();
    }

    /// <summary>
    /// 24비트 BMP 한 장(2×2 빨강) — 🔴 작성기가 BMP 를 안 만들어 주므로 <b>헤더를 직접</b> 쓴다.
    /// </summary>
    /// <remarks>BMP 헤더는 단순해서 손으로 적어도 유효하다. 유효한지는 G-W20 이 픽셀로 증명한다.</remarks>
    private static byte[] Bmp24Red()
    {
        int w = 2, h = 2, rowRaw = w * 3, pad = (4 - rowRaw % 4) % 4, row = rowRaw + pad;
        int pixels = row * h, off = 14 + 40, size = off + pixels;
        var b = new byte[size];
        b[0] = (byte)'B'; b[1] = (byte)'M';
        BitConverter.GetBytes(size).CopyTo(b, 2);
        BitConverter.GetBytes(off).CopyTo(b, 10);
        BitConverter.GetBytes(40).CopyTo(b, 14);
        BitConverter.GetBytes(w).CopyTo(b, 18);
        BitConverter.GetBytes(h).CopyTo(b, 22);
        BitConverter.GetBytes((short)1).CopyTo(b, 26);
        BitConverter.GetBytes((short)24).CopyTo(b, 28);
        BitConverter.GetBytes(pixels).CopyTo(b, 34);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = off + y * row + x * 3;
                b[i] = 0; b[i + 1] = 0; b[i + 2] = 255;   // BGR = 빨강
            }
        return b;
    }

    /// <summary>워터마크 한 변을 **제품 코드로** 계산한다 — 시험이 제 몫으로 곱하지 않는다.</summary>
    private static float WatermarkSideMm(float widthMm, float heightMm, int scalePct)
    {
        var m = typeof(PdfRenderService).GetMethod("ResolveWatermarkSideMm",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(m);
        return (float)m!.Invoke(null, new object?[] { widthMm, heightMm, scalePct })!;
    }

    /// <summary>
    /// 🔴🔴 <b>제품의 배경 조립 함수를 불러</b> 그 종이를 그린다 — 재검증 V-1 봉합의 핵심.
    /// </summary>
    /// <remarks>
    /// 🔴 시험은 <b>종이 크기와 로고만</b> 준다. 겹을 어떻게 쌓는지 · 상한을 어디에 거는지 ·
    ///   길이를 무엇으로 곱하는지는 <b>전부 제품 코드</b>가 정한다.
    ///   ⇒ 제품이 R-1 로 되돌아가면 이 축이 <b>빨개진다</b>(종전 판은 초록이었다).
    /// </remarks>
    private static byte[] RenderWithProductBackground(float widthMm, float heightMm, int scalePct)
    {
        var svgText = BuildSvg(RedPixelPng(), ".png", 0.12m, 0.15m);
        Assert.NotNull(svgText);

        // 🔴 SVG 를 여는 것도 제품 코드로 한다(P1-1 의 그 함수).
        var decode = typeof(PdfRenderService).GetMethod("TryDecodeWatermarkSvg",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(decode);
        var svgImage = decode!.Invoke(MakeRenderService(), new object?[] { svgText });
        Assert.NotNull(svgImage);

        var compose = typeof(PdfRenderService).GetMethod("ComposeDocumentBackground",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(compose);

        // 🔴 PageSize 는 point 다 — 제품이 쓰는 단위 그대로 넘긴다.
        var size = new PageSize(widthMm * 72f / 25.4f, heightMm * 72f / 25.4f);

        return RenderSizedPage(widthMm, heightMm, page =>
            compose!.Invoke(null, new object?[] { page, size, null, svgImage, scalePct }));
    }

    /// <summary>원하는 크기의 흰 종이 한 장을 그려 PNG 로 뽑는다.</summary>
    private static byte[] RenderSizedPage(float widthMm, float heightMm, Action<PageDescriptor> body)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(widthMm, heightMm, Unit.Millimetre);
                p.Margin(0);
                p.PageColor(Colors.White);
                body(p);
            });
        }).GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = QuestPDF.Infrastructure.ImageFormat.Png,
            RasterDpi = 72
        }).First();
    }

    /// <summary>
    /// 레포 뿌리를 찾는다 — 🔴 <b>격리 worktree 에서도</b> 찾아야 한다.
    /// </summary>
    /// <remarks>
    /// 🔴🔴 [2026-10-26 대조실험 중 발견] 종전에는 <c>Directory.Exists(".git")</c> 만 봤다.
    ///   <b>격리 worktree 에서는 <c>.git</c> 이 폴더가 아니라 파일</b>이다(안에 gitdir 경로가 적혀 있다)
    ///   ⇒ 뿌리를 못 찾아 이 파일의 게이트가 <b>코드와 무관하게 빨개졌다.</b>
    ///   봉합 전 나무를 worktree 로 떠서 대조실험을 하다가 G-W14·G-W15 가 빨개져 드러났다.
    ///   🔴 이 레포는 <b>갈래 작업을 worktree 로</b> 한다(10/15 교훈) — 거짓 빨강은 그날의
    ///   판단을 흐린다. 폴더든 파일이든 <c>.git</c> 이 있으면 뿌리다.
    /// </remarks>
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null
               && !Directory.Exists(Path.Combine(dir, ".git"))
               && !File.Exists(Path.Combine(dir, ".git")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        Assert.NotNull(dir);
        return dir!;
    }
}

/// <summary>
/// 🔴 아주 작은 PNG 판독기 — <b>게이트가 픽셀을 읽기 위해서만</b> 있다.
/// </summary>
/// <remarks>
/// 🔴 <b>왜 직접 푸나</b> — <c>System.Drawing</c> 은 Windows 전용이라 꾸러미를 더하면
///   빌드 경고(CA1416)가 생긴다(#19 경고 0). 그리고 이 묶음이 읽어야 하는 것은
///   <b>한 점의 색</b>뿐이다. 그 한 점을 못 읽으면 이 차수의 1급 함정
///   (<c>&lt;image opacity&gt;</c> 가 조용히 무시되는 것)을 영영 못 잡는다.
/// 🔴 받아들이는 범위를 좁게 둔다 — QuestPDF 가 내는 PNG(8비트 RGB/RGBA, 인터레이스 없음).
///   그 밖이면 <b>또렷하게 실패</b>한다. 조용히 0 을 돌려주지 않는다.
/// </remarks>
internal sealed class PngPixels
{
    public int Width { get; private init; }
    public int Height { get; private init; }
    private byte[] Rgba { get; init; } = Array.Empty<byte>();

    public readonly record struct Rgb(int R, int G, int B)
    {
        public override string ToString() => $"R={R} G={G} B={B}";
    }

    public Rgb At(int x, int y)
    {
        var i = ((y * Width) + x) * 4;
        return new Rgb(Rgba[i], Rgba[i + 1], Rgba[i + 2]);
    }

    public static PngPixels Decode(byte[] png)
    {
        Assert.True(png.Length > 8, "PNG 가 너무 짧다.");
        Assert.True(png[1] == 'P' && png[2] == 'N' && png[3] == 'G', "PNG 서명이 아니다.");

        int width = 0, height = 0, bitDepth = 0, colorType = -1;
        using var idat = new MemoryStream();

        var pos = 8;
        while (pos + 8 <= png.Length)
        {
            var len = ReadBe32(png, pos);
            var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = pos + 8;

            if (type == "IHDR")
            {
                width = ReadBe32(png, data);
                height = ReadBe32(png, data + 4);
                bitDepth = png[data + 8];
                colorType = png[data + 9];
                Assert.True(png[data + 12] == 0, "인터레이스 PNG 는 이 판독기가 받지 않는다.");
            }
            else if (type == "IDAT")
            {
                idat.Write(png, data, len);
            }
            else if (type == "IEND")
            {
                break;
            }

            pos = data + len + 4;
        }

        Assert.True(bitDepth == 8, $"8비트 PNG 만 받는다 (받은 깊이: {bitDepth}).");
        Assert.True(colorType is 2 or 6, $"RGB·RGBA PNG 만 받는다 (받은 종류: {colorType}).");

        var channels = colorType == 6 ? 4 : 3;
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var bytes = raw.ToArray();

        var stride = (width * channels) + 1;
        Assert.True(bytes.Length >= stride * height,
            $"풀린 바이트가 모자라다 ({bytes.Length} < {stride * height}).");

        var rgba = new byte[width * height * 4];
        var prev = new byte[width * channels];

        for (var y = 0; y < height; y++)
        {
            var filter = bytes[y * stride];
            var line = new byte[width * channels];
            Array.Copy(bytes, (y * stride) + 1, line, 0, width * channels);

            // 🔴 PNG 필터 5종을 되돌린다 — 하나라도 빠뜨리면 **엉뚱한 색**을 읽는다.
            for (var i = 0; i < line.Length; i++)
            {
                byte a = i >= channels ? line[i - channels] : (byte)0;
                byte b = prev[i];
                byte c = i >= channels ? prev[i - channels] : (byte)0;

                line[i] = filter switch
                {
                    0 => line[i],
                    1 => (byte)(line[i] + a),
                    2 => (byte)(line[i] + b),
                    3 => (byte)(line[i] + ((a + b) / 2)),
                    4 => (byte)(line[i] + Paeth(a, b, c)),
                    _ => throw new InvalidDataException($"모르는 PNG 필터: {filter}")
                };
            }

            for (var x = 0; x < width; x++)
            {
                var s = x * channels;
                var d = ((y * width) + x) * 4;
                rgba[d] = line[s];
                rgba[d + 1] = line[s + 1];
                rgba[d + 2] = line[s + 2];
                rgba[d + 3] = channels == 4 ? line[s + 3] : (byte)255;
            }

            prev = line;
        }

        return new PngPixels { Width = width, Height = height, Rgba = rgba };
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static int ReadBe32(byte[] b, int i) =>
        (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
}
