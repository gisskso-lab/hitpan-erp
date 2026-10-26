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
        var code = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "HitPan.Application", "Services", "PdfRenderService.cs"));
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

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
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
