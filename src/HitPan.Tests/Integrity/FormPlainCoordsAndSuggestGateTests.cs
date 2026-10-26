using System.IO.Compression;
using System.Reflection;
using HitPan.API.Controllers;
using HitPan.Application.DTOs.Documents;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261026작2 갈래 W-D — 「올린 양식에 값 넣기」 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>N 을 세지 않는다. 축을 센다.</b> 축은 여섯이다 —
/// ⓐ <c>plain</c>(순백지) + 양식지 그림 + 좌표 · ⓑ <c>preprint</c> 이중인쇄 음성 ·
/// ⓒ 로고 재료 없음 · ⓓ 로고 상태 모름(403) · ⓔ 저장 전 상태 · ⓕ 세금계산서 음성.
/// </para>
/// <para>
/// 🔴 <b>조립을 시험 안에서 복제하지 않는다.</b> 종이는 제품의
/// <c>ComposeDocumentBackground</c>·<c>ComposeBodyCoords</c>·<c>ResolveFormBackground</c>·
/// <c>ToTemplateInfo</c>·<c>BuildSampleSnapshot</c> 를 <b>불러서</b> 낸다.
/// (작1 G-W16 이 조립을 복제해 R-1 버그를 되살려도 10/10 초록이었다 — 거짓 초록.)
/// </para>
/// <para>
/// 🔴 <b>증가량이 아니라 절대 개수를 박는다.</b> 「켠 판 &gt; 끈 판」 같은 비교는
/// 품목 한 줄이 통째로 빠져도 초록이었다(작1 G-W13).
/// </para>
/// <para>
/// 🔴 <b>그림은 코드로 만든다</b> — 손으로 적은 base64 를 쓰지 않는다(작1 ⒜ 사고).
/// <c>SolidPng</c> 가 PNG 를 바이트부터 조립하고, <c>G-WD-0</c> 이 그 그림이 정말
/// 작성기에 읽히는지를 <b>먼저</b> 잰다(자부터 잰다).
/// </para>
/// <para>
/// 🔴 <b>반사 호출은 인자 이름으로 맞춘다</b>(<c>Call</c>) — 제품 서명에 인자가 하나
/// 늘어도 이 파일은 깨지지 않고, <b>이름이 없어지면 큰 소리로</b> 알린다
/// (2026-10-25 작2: 기본값 있는 인자 하나가 반사 게이트 108건을 깼다).
/// </para>
/// <para>
/// 🔴 <b>이 파일이 못 재는 것</b>(「통과」로 적지 않는다) — 축ⓓ(큰 경고 / 작은 캡션 갈림)과
/// 축ⓔ 의 「되묻는 창」은 <c>.razor</c> 안에 있고 이 시험틀에 bUnit 이 없다. 두 자리는
/// <b>미측정</b>이고, 사람이 타는 종단 대본
/// (<c>docs/검증/erp/20261026_종단리허설대본_작2_양식에값넣기.md</c>) 으로만 잰다.
/// </para>
/// </remarks>
public class FormPlainCoordsAndSuggestGateTests
{
    static FormPlainCoordsAndSuggestGateTests()
        => QuestPDF.Settings.License = LicenseType.Community;

    private static readonly Type SvcType = typeof(PdfRenderService);

    // ──────────────────────────────────────────────────────────────────────
    // 자 — 제품을 부르는 연장. 조립은 여기에 없다.
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>제품의 공개 생성자를 <b>긁어서</b> 만든다 — 인자가 늘어도 안 깨진다.</summary>
    private static PdfRenderService MakeService()
    {
        var ctor = SvcType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select(p =>
            p.ParameterType.IsAssignableFrom(typeof(NullLogger<PdfRenderService>))
                ? (object?)NullLogger<PdfRenderService>.Instance
                : null).ToArray();
        return (PdfRenderService)ctor.Invoke(args);
    }

    private static MethodInfo M(string name) =>
        SvcType.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public
                              | BindingFlags.Instance | BindingFlags.Static
                              | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException(
            $"🔴 PdfRenderService.{name}(…) 이 없다 — 이 게이트가 재던 제품 통로가 사라졌다(20261026작2 W-D).");

    /// <summary>
    /// 🔴 <b>인자 이름으로</b> 제품 메서드를 부른다. 모르는 필수 인자가 생기면
    /// <b>조용히 null 을 넣고 통과하지 않고</b> 그 이름을 적어 빨개진다.
    /// </summary>
    private static object? Call(string name, object? target, Dictionary<string, object?> byName)
    {
        var m = M(name);
        var ps = m.GetParameters();
        var args = new object?[ps.Length];
        var unknown = new List<string>();

        for (var i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            if (p.Name is not null && byName.TryGetValue(p.Name, out var v)) { args[i] = v; continue; }
            if (p.ParameterType == typeof(CancellationToken)) { args[i] = CancellationToken.None; continue; }
            if (p.HasDefaultValue) { args[i] = p.DefaultValue; continue; }
            if (p.ParameterType.IsValueType) { args[i] = Activator.CreateInstance(p.ParameterType); }
            else { args[i] = null; }
            unknown.Add(p.Name ?? "?");
        }

        Assert.True(unknown.Count == 0,
            $"🔴 {name}(…) 에 이 게이트가 모르는 필수 인자가 생겼다: {string.Join(", ", unknown)}. " +
            "값을 추측해 넣으면 거짓 초록이 된다 — 게이트를 같이 고쳐라.");

        return m.Invoke(target, args);
    }

    private static T Call<T>(string name, object? target, Dictionary<string, object?> byName)
        => (T)Call(name, target, byName)!;

    /// <summary><c>Task&lt;T&gt;</c> 를 돌려주는 비공개 메서드를 반사로 기다린다.</summary>
    private static object? CallAwait(string name, object? target, Dictionary<string, object?> byName)
    {
        var task = (Task)Call(name, target, byName)!;
        task.GetAwaiter().GetResult();
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    // ── 그림을 코드로 만든다 (손 base64 금지) ───────────────────────────────

    private static byte[] SolidPng(int w, int h, byte r, byte g, byte b)
    {
        var raw = new MemoryStream();
        for (var y = 0; y < h; y++)
        {
            raw.WriteByte(0);                       // filter: none
            for (var x = 0; x < w; x++) { raw.WriteByte(r); raw.WriteByte(g); raw.WriteByte(b); }
        }

        var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(z);
        }

        var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBe32(ihdr, 0, w);
        WriteBe32(ihdr, 4, h);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // truecolor RGB
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", comp.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteBe32(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16);
        b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe32(len, 0, data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = new byte[4];
        WriteBe32(crc, 0, unchecked((int)Crc32(t, data)));
        s.Write(crc);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(params byte[][] parts)
    {
        var c = 0xFFFFFFFFu;
        foreach (var p in parts)
            foreach (var b in p)
                c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    /// <summary>허용 폴더 <b>안</b>에 코드로 만든 PNG 를 놓고 그 경로를 돌려준다.</summary>
    private static string WritePngInsideRoot(int w = 64, int h = 64)
    {
        var root = Path.Combine(FormBackgroundPaths.Root, "gate-wd");
        Directory.CreateDirectory(root);
        var p = Path.Combine(root, $"wd-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(p, SolidPng(w, h, 0xC0, 0xC0, 0xFF));
        return p;
    }

    // ── 종이를 낸다 — 배경 조립은 제품 함수가 한다 ─────────────────────────

    private static byte[] RenderPage(PageSize size, byte[]? bgImage, object? watermarkSvg,
        int scalePct, Action<PageDescriptor>? content, bool callProductBackground = true)
        => Document.Create(doc => doc.Page(page =>
        {
            page.Size(size);
            // 좌표 경로는 여백 0 이다(제품 호출부와 같은 종이 설정).
            page.MarginTop(0); page.MarginLeft(0); page.MarginRight(0); page.MarginBottom(0);
            page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(10));
            page.PageColor(Colors.White);

            if (callProductBackground)
            {
                Call("ComposeDocumentBackground", null, new Dictionary<string, object?>
                {
                    ["page"] = page,
                    ["pageSize"] = size,
                    ["bgImage"] = bgImage,
                    ["watermarkSvg"] = watermarkSvg,
                    ["watermarkScalePct"] = scalePct
                });
            }

            if (content is not null) content(page);
            else page.Content().Text("");
        })).GeneratePdf();

    private static int ImageCount(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return doc.GetPages().Sum(p => p.GetImages().Count());
    }

    private static string PageText(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Concat(doc.GetPages().Select(p => p.Text));
    }

    private static int Occurrences(string haystack, string needle)
    {
        int n = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
        return n;
    }

    /// <summary>쪽 왼쪽 위 기준 mm 로 글자 자리를 읽는다(좌표 인쇄의 원점과 같은 기준).</summary>
    private static List<(string Text, float XMm, float TopMm)> LettersMm(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var page = doc.GetPage(1);
        var hMm = (float)page.Height * 25.4f / 72f;
        return page.Letters.Select(l => (
            l.Value,
            (float)l.GlyphRectangle.Left * 25.4f / 72f,
            hMm - (float)l.GlyphRectangle.Top * 25.4f / 72f)).ToList();
    }

    // ── 양식·전표 재료 ─────────────────────────────────────────────────────

    private const string CoordsJson = """
        [
          {"key":"partner_business_no","x_mm":30,"y_mm":50,"width_mm":60},
          {"key":"doc_date","x_mm":120,"y_mm":50,"width_mm":50},
          {"key":"line.spec","x_mm":30,"y_mm":90,"width_mm":40}
        ]
        """;

    /// <summary>예시 전표에 꼭 한 번 찍히는 ASCII 표식(한글은 PDF 에서 되읽히지 않는다).</summary>
    private const string BizNoMarker = "000-00-00000";

    private static object TemplateInfo(string paperMode, bool printBackground,
        string? backgroundPath, string? coordsJson)
    {
        var dto = new FormTemplateDto
        {
            PaperMode = paperMode,
            PrintBackground = printBackground,
            BackgroundImageUrl = backgroundPath,
            FieldCoordsJson = coordsJson
        };
        return Call("ToTemplateInfo", MakeService(), new Dictionary<string, object?> { ["dto"] = dto })!;
    }

    private static object Snapshot(string documentType, int lines = 3)
        => Call("BuildSampleSnapshot", null, new Dictionary<string, object?>
        {
            ["documentType"] = documentType,
            ["ov"] = new PdfPreviewOverrides { UseSampleData = true, SampleLineCount = lines, MarkAsSample = true }
        })!;

    private static object Coords(object templateInfo)
        => templateInfo.GetType().GetProperty("FieldCoords")!.GetValue(templateInfo)!;

    private static Action<PageDescriptor> CoordContent(object snapshot, object coords)
        => page => page.Content().Element(e => Call("ComposeBodyCoords", null,
            new Dictionary<string, object?> { ["e"] = e, ["d"] = snapshot, ["coords"] = coords }));

    // ══════════════════════════════════════════════════════════════════════
    // G-WD-0 — 자부터 잰다
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-WD-0 🔴🔴 자부터 잰다 — 코드로 만든 PNG 가 **작성기에 읽히고** 판독기가 그림 1장을 센다")]
    public void Gate0_RulerItself()
    {
        var png = SolidPng(32, 32, 0xC0, 0xC0, 0xFF);
        _ = QuestPDF.Infrastructure.Image.FromBinaryData(png);   // 못 읽으면 여기서 터진다

        Assert.Equal(1, ImageCount(RenderPage(PageSizes.A4, png, null, 0, null)));   // 절대 개수

        // 🔴 자 대조군 — 그림을 안 주면 **0장**이다(판독기가 늘 1 을 돌려주는 자가 아니다).
        Assert.Equal(0, ImageCount(RenderPage(PageSizes.A4, null, null, 0, null)));
    }

    // ══════════════════════════════════════════════════════════════════════
    // 축 ⓐ — plain(순백지) + 양식지 그림 + 좌표
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "축ⓐ-1 🔴🔴 plain 종이에 **그림 1장이 깔리고** 좌표 자리에 값이 찍힌다 (절대 개수·절대 좌표)")]
    public void AxisA_PlainBackgroundAndCoordValues()
    {
        var path = WritePngInsideRoot();
        var info = TemplateInfo("plain", printBackground: true, path, CoordsJson);

        // ① 제품이 plain 에서도 그림을 읽는다
        var bg = Call<byte[]?>("ResolveFormBackground", MakeService(),
            new Dictionary<string, object?> { ["overrides"] = null, ["template"] = info });
        Assert.NotNull(bg);

        // ② 좌표 세 칸이 그대로 읽혔다 (절대 개수)
        Assert.Equal(3, ((System.Collections.ICollection)Coords(info)).Count);

        // ③ 제품 조립으로 종이를 내고 — 그림 1장 + 값 자리
        var snap = Snapshot("quotation");
        var pdf = RenderPage(PageSizes.A4, bg, null, 0, CoordContent(snap, Coords(info)));
        var text = PageText(pdf);

        Assert.Equal(1, ImageCount(pdf));                        // 그림이 배경으로 깔렸다
        Assert.Equal(1, Occurrences(text, BizNoMarker));         // 머리 칸 값이 한 번
        Assert.Equal(2, Occurrences(text, "1EA"));               // 품목 3줄 → 1EA·1BOX·1EA
        Assert.Equal(1, Occurrences(text, "1BOX"));

        // ④ 값이 **내가 적은 자리**에 있다 — 30mm, 50mm(왼쪽 위 기준)
        var at = LettersMm(pdf).Where(l => l.Text == "0" && Math.Abs(l.XMm - 30f) < 8f).ToList();
        Assert.Contains(at, l => Math.Abs(l.TopMm - 50f) < 8f);
    }

    [Fact(DisplayName = "축ⓐ-1자 🔴 자 대조군 — 좌표를 **안 주면** 그 자리에 글자가 없다(늘 초록인 자가 아니다)")]
    public void AxisA_RulerBitesWhenNoCoords()
    {
        var snap = Snapshot("quotation");
        var empty = TemplateInfo("plain", true, null, null);
        var pdf = RenderPage(PageSizes.A4, null, null, 0, CoordContent(snap, Coords(empty)));

        Assert.Equal(0, Occurrences(PageText(pdf), BizNoMarker));
        Assert.DoesNotContain(LettersMm(pdf), l => l.Text == "0" && Math.Abs(l.XMm - 30f) < 8f
                                                                 && Math.Abs(l.TopMm - 50f) < 8f);
    }

    [Fact(DisplayName = "축ⓐ-2 🔴🔴 좌표 쓰임 판정에 `isPreprint &&` 가 **없다** — 되살리면 순백지 고객 좌표가 또 죽는다")]
    public void AxisA_CallSiteHasNoPreprintConstraint()
    {
        var line = CoordDecisionLine();
        Assert.False(line.Contains("isPreprint", StringComparison.Ordinal),
            "🔴 좌표 쓰임 판정이 `preprint` 에 묶여 있다 — 순백지(plain) 고객이 좌표를 맞춰도 " +
            $"값이 좌표 자리로 안 간다(사장님 10/25 반려의 그 자리). 지금 줄: {line}");
    }

    [Fact(DisplayName = "축ⓐ-2자 🔴 자 검사 — 되살린 모양을 같은 자로 재면 **빨개진다**(주석에 숨겨도 안 속는다)")]
    public void AxisA_RulerCatchesRestoredBug()
    {
        // 되살린 모양 = 종전 줄. 같은 자를 대면 걸린다.
        var restored = "var useCoords = isPreprint && (template?.FieldCoords.Count ?? 0) > 0;";
        Assert.Contains("isPreprint", StripComments(restored), StringComparison.Ordinal);

        // 주석으로 지운 척 하는 모양에는 안 걸려야 한다(자가 주석을 글자로 세지 않는다).
        Assert.DoesNotContain("isPreprint",
            StripComments("// var useCoords = isPreprint && x;\nvar useCoords = (c ?? 0) > 0;"),
            StringComparison.Ordinal);
    }

    /// <summary>제품의 <c>useCoords</c> 결정 줄 — 주석을 걷어 낸 뒤 찾는다.</summary>
    private static string CoordDecisionLine()
    {
        var src = StripComments(File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "PdfRenderService.cs")));
        var line = src.Split('\n').FirstOrDefault(l => l.Contains("useCoords =", StringComparison.Ordinal));
        Assert.False(line is null,
            "🔴 `useCoords = …` 결정 줄이 없다 — 좌표 경로가 사라졌거나 이름이 바뀌었다. 게이트를 같이 고쳐라.");
        return line!;
    }

    private static string StripComments(string src)
    {
        var sb = new System.Text.StringBuilder(src.Length);
        for (var i = 0; i < src.Length; i++)
        {
            if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n') i++;
                if (i < src.Length) sb.Append('\n');
                continue;
            }
            if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                i++;
                continue;
            }
            sb.Append(src[i]);
        }
        return sb.ToString();
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "src", "HitPan.sln"))) d = d.Parent;
        Assert.False(d is null, "🔴 레포 뿌리를 못 찾았다.");
        return d!.FullName;
    }

    // ══════════════════════════════════════════════════════════════════════
    // 축 ⓑ — preprint(양식용지를 사다 쓰는 고객) 이중 인쇄 음성
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "축ⓑ 🔴🔴 preprint + 기본(안 켬) ⇒ 종이에 그림 **0장** · 설계 미리보기·켠 고객만 1장 (절대 개수)")]
    public void AxisB_PreprintDefaultNoDoublePrint()
    {
        var path = WritePngInsideRoot();
        var svc = MakeService();

        // ① 양식용지 고객의 기본 — 안 켰다 ⇒ 읽지도 않는다
        var off = TemplateInfo("preprint", printBackground: false, path, CoordsJson);
        var bgOff = Call<byte[]?>("ResolveFormBackground", svc,
            new Dictionary<string, object?> { ["overrides"] = null, ["template"] = off });
        Assert.Null(bgOff);
        Assert.Equal(0, ImageCount(RenderPage(PageSizes.A4, bgOff, null, 0, null)));   // 종이까지 0장

        // ② 설계 미리보기(양식 편집창)에서는 깔린다 — 칸을 맞추려면 보여야 한다
        var preview = Call<byte[]?>("ResolveFormBackground", svc, new Dictionary<string, object?>
        {
            ["overrides"] = new PdfPreviewOverrides { IsFormDesignPreview = true },
            ["template"] = off
        });
        Assert.NotNull(preview);
        Assert.Equal(1, ImageCount(RenderPage(PageSizes.A4, preview, null, 0, null)));

        // ③ 고객이 스스로 켠 경우 — 고객이 고른 대로 그린다(우리가 몰래 끄는 길은 없다)
        var on = TemplateInfo("preprint", printBackground: true, path, CoordsJson);
        Assert.NotNull(Call<byte[]?>("ResolveFormBackground", svc,
            new Dictionary<string, object?> { ["overrides"] = null, ["template"] = on }));
    }

    [Fact(DisplayName = "축ⓑ자 🔴 자 대조군 — **늘 깔게** 바꾼 모양을 같은 자로 재면 0장 축이 빨개진다")]
    public void AxisB_RulerCatchesAlwaysLay()
    {
        var path = WritePngInsideRoot();
        // 「늘 깔기」 = 판정을 건너뛰고 바로 읽는 모양.
        var always = Call<byte[]?>("TryLoadFormBackground", MakeService(),
            new Dictionary<string, object?> { ["url"] = path });
        Assert.NotNull(always);
        Assert.Equal(1, ImageCount(RenderPage(PageSizes.A4, always, null, 0, null)));
        // ⇒ 축ⓑ 의 「0장」은 늘 0 인 자가 아니다. 같은 자가 1장을 센다.
    }

    // ══════════════════════════════════════════════════════════════════════
    // 축 ⓒ — 로고 재료가 없다
    // ══════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "축ⓒ 🔴 로고 재료가 없으면 워터마크 그림이 **안 만들어진다**(null) — 켬/끔과 무관")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AxisC_NoLogoNoWatermarkSvg(string? logoUrl)
    {
        var svc = MakeService();
        Assert.Null(Call<byte[]?>("TryLoadLogoImage", svc,
            new Dictionary<string, object?> { ["logoUrl"] = logoUrl }));
        Assert.Null(Call<string?>("TryBuildWatermarkSvg", svc, new Dictionary<string, object?>
        {
            ["logoBytes"] = null,
            ["logoUrl"] = logoUrl,
            ["opacity"] = 0.12m,
            ["saturate"] = 0.3m
        }));
    }

    [Fact(DisplayName = "축ⓒ 🔴🔴 재료가 없으면 종이는 **글자 하나 안 바뀐다** — 그림 0장 + 글이 같다(절대 비교)")]
    public void AxisC_NoLogoPaperUnchanged()
    {
        var snap = Snapshot("quotation");
        var info = TemplateInfo("plain", false, null, CoordsJson);

        var withCall = RenderPage(PageSizes.A4, null, null, 0, CoordContent(snap, Coords(info)));
        var without = RenderPage(PageSizes.A4, null, null, 0, CoordContent(snap, Coords(info)),
            callProductBackground: false);

        Assert.Equal(0, ImageCount(withCall));
        Assert.Equal(PageText(without), PageText(withCall));
        Assert.Equal(1, Occurrences(PageText(withCall), BizNoMarker));   // 값은 그대로 찍힌다
    }

    [Fact(DisplayName = "축ⓒ자 🔴 자 대조군 — 재료가 **있으면** 같은 자가 그림 1장을 센다")]
    public void AxisC_RulerBitesWithMaterial()
        => Assert.Equal(1, ImageCount(RenderPage(PageSizes.A4, SolidPng(16, 16, 0, 0, 0), null, 0, null)));

    // ══════════════════════════════════════════════════════════════════════
    // 축 ⓔ — 올리기만 하고 저장하지 않으면 양식 행이 안 바뀐다 (서버 쪽)
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "축ⓔ 🔴🔴 그림 올리기는 양식 서비스를 **0번** 부른다 — 저장 단추를 눌러야 행이 바뀐다 (절대 개수)")]
    public async Task AxisE_UploadDoesNotTouchTemplateRow()
    {
        var service = new Mock<IFormTemplateService>(MockBehavior.Loose);
        var store = new Mock<IFormBackgroundStore>(MockBehavior.Strict);
        store.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                 It.IsAny<CancellationToken>()))
             .Returns(Task.FromResult<(string?, string?)>(
                 (Path.Combine(FormBackgroundPaths.TenantRoot("t1"), "a.png"), null)));

        var sut = new FormTemplateController(service.Object, NullLogger<FormTemplateController>.Instance);
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = "t1";               // 🔴 tenant_id 는 JWT 가 세운 이 칸에서만 온다(#2)
        sut.ControllerContext = new ControllerContext { HttpContext = http };

        var bytes = SolidPng(8, 8, 1, 2, 3);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "form.png");

        var res = await sut.UploadBackground(file, store.Object, CancellationToken.None);

        Assert.IsType<OkObjectResult>(res);
        Assert.Single(store.Invocations);                // 보관소에 딱 한 번 (절대 1)
        Assert.Empty(service.Invocations);               // 🔴 양식 행은 손도 안 댔다 (절대 0)
    }

    [Fact(DisplayName = "축ⓔ자 🔴 자 대조군 — 양식 서비스를 부르면 같은 셈이 **1** 이 된다(0 이 늘 0 인 셈이 아니다)")]
    public async Task AxisE_RulerCountsServiceCalls()
    {
        var service = new Mock<IFormTemplateService>(MockBehavior.Loose);
        Assert.Empty(service.Invocations);               // 부르기 전 = 0
        await service.Object.DeactivateAsync("t1", "x");
        Assert.Single(service.Invocations);               // 부른 뒤 = 1 ⇒ 이 셈은 살아 있다
    }

    // ══════════════════════════════════════════════════════════════════════
    // 축 ⓕ — 세금계산서는 워터마크를 켜는 길이 없다
    // ══════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "축ⓕ 🔴🔴 세금계산서는 **어떤 조합으로도** 워터마크가 안 깔린다 (여섯 조합 전수 · 하나씩 박는다)")]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void AxisF_TaxInvoiceNeverLaysWatermark(bool? formSays, bool commonEnabled)
        => Assert.False((bool)Call("ShouldLayWatermark", null, new Dictionary<string, object?>
        {
            ["documentType"] = "tax_invoice",
            ["formSays"] = formSays,
            ["commonEnabled"] = commonEnabled
        })!);

    [Fact(DisplayName = "축ⓕ자 🔴 자 대조군 — 같은 판정이 **거래명세서에서는 켜진다**(늘 false 를 돌려주는 자가 아니다)")]
    public void AxisF_RulerOnOtherDocTypes()
    {
        Assert.True((bool)Call("ShouldLayWatermark", null, new Dictionary<string, object?>
        { ["documentType"] = "delivery_note", ["formSays"] = true, ["commonEnabled"] = false })!);
        Assert.True((bool)Call("ShouldLayWatermark", null, new Dictionary<string, object?>
        { ["documentType"] = "delivery_note", ["formSays"] = null, ["commonEnabled"] = true })!);
        Assert.False((bool)Call("ShouldLayWatermark", null, new Dictionary<string, object?>
        { ["documentType"] = "delivery_note", ["formSays"] = false, ["commonEnabled"] = true })!);
    }

    [Theory(DisplayName = "축ⓕ 🔴🔴 세금계산서 길은 **DB 를 아예 안 만진다** — 연결이 null 인데도 null 을 돌려준다")]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void AxisF_TaxInvoicePathNeverTouchesDb(bool? formSays)
    {
        var dto = new FormTemplateDto { PaperMode = "plain", ShowWatermark = formSays };
        var svc = MakeService();                                  // 🔴 DB 연결이 null 인 제품 객체
        var info = Call("ToTemplateInfo", svc, new Dictionary<string, object?> { ["dto"] = dto });

        var layer = CallAwait("BuildWatermarkLayerAsync", svc, new Dictionary<string, object?>
        {
            ["tenantId"] = "t1",
            ["documentType"] = "tax_invoice",
            ["template"] = info,
            ["company"] = null,
            ["ct"] = CancellationToken.None
        });

        // DB 를 읽거나 company 를 만졌으면 여기 오기 전에 터진다 ⇒ 통과 자체가 「안 만졌다」의 증거다.
        Assert.Null(layer);
    }
}
