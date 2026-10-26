using System.Reflection;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴🔴 격자 묶기 게이트 — 설계 §7 <b>G-W7</b> · 지시서 §E-2 <b>G-W13</b>.
/// </summary>
/// <remarks>
/// 🔴🔴 <b>이 묶음이 왜 생겼나</b> — 2026-10-26 [4] 검증팀 반려 R-4:
///   격자 축(<c>show_border</c>)에 <b>게이트가 0건</b>이었다.
///   PM 주장 ②(「켠 판은 한 픽셀도 안 바뀐다」)는 <b>코드 대조로만</b> 참이었고,
///   *"재 둔 자가 없으니 다음 사람이 깨도 아무도 모른다"* 가 반려 사유였다.
///
/// 🔴 사장님 오더 2026-10-25: <i>"테두리, 격자, 로고 켜고 끄는것 = 양식만 가져오고,
///   테두리, 격자, 로고는 끄는것 동의"</i> ⇒ <b>끄면 선이 정말 없어야</b> 하고,
///   <b>켜면 종전과 같아야</b> 한다(#1 — 기존 고객의 종이는 안 바뀐다).
///
/// 🔴 글자가 아니라 <b>픽셀</b>로 잰다. 「<c>borderWidth</c> 라는 낱말이 있나」를 세는 게이트는
///   값이 뒤집혀도 초록이다(누적 31번+).
/// 🔴 품목 줄이 조용히 사라지지 않는지(#20)도 **쪽 수로** 같이 잰다 —
///   격자를 끄면 칸 패딩이 4→5 로 늘어나므로 그 자리가 위험하다.
/// </remarks>
public sealed class GridBorderRenderGateTests
{
    static GridBorderRenderGateTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    // ══════════════════════════════════════════════════════════════════
    // 자 — 전표 한 장을 **제품 코드로** 그린다
    // ══════════════════════════════════════════════════════════════════

    private static Type SnapshotType =>
        typeof(PdfRenderService).GetNestedType("DocumentSnapshot", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("DocumentSnapshot 이 없다 — 배선이 바뀌었다.");

    private static Type LineType =>
        typeof(PdfRenderService).GetNestedType("DocumentLine", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("DocumentLine 이 없다 — 배선이 바뀌었다.");

    /// <summary>품목 <paramref name="lineCount"/> 줄짜리 전표 하나를 만든다.</summary>
    private static object MakeSnapshot(int lineCount)
    {
        var snap = Activator.CreateInstance(SnapshotType)!;
        SnapshotType.GetProperty("DocType")!.SetValue(snap, "quotation");
        SnapshotType.GetProperty("DocNo")!.SetValue(snap, "GATE-0001");
        SnapshotType.GetProperty("PartnerName")!.SetValue(snap, "가나상사");
        SnapshotType.GetProperty("PartnerBusinessNo")!.SetValue(snap, "000-00-00000");
        SnapshotType.GetProperty("TotalSupply")!.SetValue(snap, 1000m);
        SnapshotType.GetProperty("TotalVat")!.SetValue(snap, 100m);
        SnapshotType.GetProperty("TotalAmount")!.SetValue(snap, 1100m);

        var listType = typeof(List<>).MakeGenericType(LineType);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        for (var i = 0; i < lineCount; i++)
        {
            var line = Activator.CreateInstance(LineType)!;
            LineType.GetProperty("ItemName")!.SetValue(line, $"품목 {i + 1}");
            LineType.GetProperty("Spec")!.SetValue(line, "규격");
            LineType.GetProperty("Qty")!.SetValue(line, 1m);
            LineType.GetProperty("UnitPrice")!.SetValue(line, 1000m);
            LineType.GetProperty("Supply")!.SetValue(line, 1000m);
            LineType.GetProperty("Vat")!.SetValue(line, 100m);
            LineType.GetProperty("Total")!.SetValue(line, 1100m);
            list.Add(line);
        }
        SnapshotType.GetProperty("Lines")!.SetValue(snap, list);
        return snap;
    }

    /// <summary>
    /// 🔴 <b>제품의 본문 작성기</b>(<c>ComposeBody</c>)를 그대로 불러 그린다 —
    /// 시험이 제 몫으로 표를 그리면 실코드가 틀려도 초록이 된다.
    /// </summary>
    private static List<byte[]> RenderBody(int lineCount, bool showBorder)
    {
        var compose = typeof(PdfRenderService).GetMethod("ComposeBody",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ComposeBody 가 없다.");

        var snap = MakeSnapshot(lineCount);

        return Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(10, Unit.Millimetre);
                p.PageColor(Colors.White);
                p.DefaultTextStyle(t => t.FontSize(10));
                p.Content().Element(e => compose.Invoke(null, new object?[] { e, snap, false, showBorder }));
            });
        }).GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 72
        }).ToList();
    }

    /// <summary>
    /// 🔴 <b>가로로 길게 그어진 선</b>의 개수를 센다 — 격자를 재는 자.
    /// </summary>
    /// <remarks>
    /// 🔴🪤 <b>처음에는 「회색 픽셀 개수」로 쟀고 그 자가 틀렸다.</b> 격자를 끈 종이에서도
    ///   5,530 개가 나왔다 — 그것은 선이 아니라 <b>글자 가장자리</b>(안티에일리어싱)였다.
    ///   한글 글자 한 쪽에서 수천 개가 나온다. ⇒ 픽셀 수가 아니라 <b>「한 줄로 길게 이어졌나」</b>
    ///   를 센다. 선은 가로로 이어지고, 글자는 안 이어진다.
    /// 🔴 이 자가 참인지는 아래 자 검사(G-W7자)가 같이 잰다 — 켠 판에서 선이 세어져야 한다.
    /// </remarks>
    private static int CountHorizontalLines(byte[] png)
    {
        var px = PngPixels.Decode(png);
        var lines = 0;

        for (var y = 0; y < px.Height; y++)
        {
            var run = 0;
            var longest = 0;
            for (var x = 0; x < px.Width; x++)
            {
                var c = px.At(x, y);
                var grey = Math.Abs(c.R - c.G) < 8 && Math.Abs(c.G - c.B) < 8
                           && c.R is >= 150 and <= 245;
                if (grey) { run++; if (run > longest) longest = run; }
                else run = 0;
            }

            // 🔴 종이 폭의 **절반 이상** 이어졌으면 그것은 글자가 아니라 선이다.
            if (longest > px.Width / 2) lines++;
        }

        return lines;
    }

    // ══════════════════════════════════════════════════════════════════
    // G-W7 · 끄면 선이 **없다** / 켜면 **있다** (대조군 두 판)
    // ══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W7 🔴 격자를 끄면 선·회색 바탕이 **종이에서 사라진다**(켠 판이 대조군)")]
    public void GW7_BorderOffRemovesGridFromPaper()
    {
        var on = RenderBody(8, showBorder: true);
        var off = RenderBody(8, showBorder: false);

        Assert.Single(on);
        Assert.Single(off);

        var linesOn = CountHorizontalLines(on[0]);
        var linesOff = CountHorizontalLines(off[0]);

        // 🔴 켠 판에는 가로선이 **여러 줄** 있어야 한다 — 없으면 이 축의 자가 고장난 것이다
        //   (거래처 상자 · 품목표 머리 · 품목 줄마다 한 줄).
        Assert.True(linesOn >= 5,
            $"켠 판에 가로선이 {linesOn} 줄뿐이다 — 자를 먼저 의심하라(선 색·두께가 바뀌었나).");

        // 🔴 끈 판에는 가로선이 **하나도 없어야** 한다. 사장님 오더가 「끄는 것」이었다.
        Assert.Equal(0, linesOff);
    }

    // ══════════════════════════════════════════════════════════════════
    // G-W13 · 🔴 **품목 줄이 조용히 사라지지 않는다** (#20)
    // ══════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "G-W13 🔴 격자를 꺼도 **쪽 수가 늘지 않는다** — 패딩 4→5 로 한 줄이 밀리면 사고다")]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(25)]
    [InlineData(35)]
    public void GW13_TurningBorderOffDoesNotPushLinesToNextPage(int lineCount)
    {
        var on = RenderBody(lineCount, showBorder: true);
        var off = RenderBody(lineCount, showBorder: false);

        // 🔴 [4] R-4 가 ⚠️미확인으로 남겨 둔 자리다 — *"코드상 사라질 길이 안 보인다. 실측 0회"*.
        //   이제 그려서 센다. 끈 판이 **더 많은 쪽**을 쓰면 한 줄이 다음 장으로 밀렸다는 뜻이고,
        //   그건 고객에게 「품목이 빠진 종이」로 보일 수 있다.
        Assert.True(off.Count <= on.Count,
            $"품목 {lineCount} 줄: 격자를 끄니 쪽 수가 {on.Count} → {off.Count} 로 늘었다.");

        // 🔴 그리고 두 판 **모두** 종이가 나온다 — 끈 판이 아예 안 그려지면 그것도 사고다(#20).
        Assert.NotEmpty(on);
        Assert.NotEmpty(off);
    }

    [Fact(DisplayName = "G-W13자 🔴 자 검사 — 켠 판과 끈 판이 **정말 다른 종이**다(둘이 같으면 위 축들이 무의미하다)")]
    public void GW13_SelfCheck_TwoRendersDiffer()
    {
        var on = RenderBody(8, showBorder: true)[0];
        var off = RenderBody(8, showBorder: false)[0];

        Assert.False(on.AsSpan().SequenceEqual(off),
            "켠 판과 끈 판이 바이트까지 같다 — showBorder 가 아무 일도 안 하고 있다.");
    }
}
