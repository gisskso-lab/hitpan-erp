using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using HitPan.Application.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261021작 §9-3(R-3) — <b>세금계산서 PDF 작성기를 실제로 타는</b> 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 새로 세웠나</b> — 기존 <c>FormTemplateExportGuardTests</c> 는
/// <c>new PdfExportService()</c> 를 직접 만들어서, <b>PdfRenderService 의 세금계산서 작성기를
/// 통째로 지워도 초록</b>이었다. 시험 2500건 통과가 이 작업을 하나도 안 지켰다([4] 적발).
/// </para>
/// <para>
/// 🔴 <b>합격 기준</b> — 작성기(<c>RenderTaxInvoiceDocument</c>)나 쪽 나눔 판정
/// (<c>MeasureBothCopiesFitOnePage</c>)을 지우면 <b>이 파일이 빨개진다.</b>
/// 그래서 일부러 reflection 으로 그 이름을 찾는다 — 이름이 없으면 그 자리에서 실패한다.
/// </para>
/// <para>
/// 🔴 기존 시험 파일은 <b>한 줄도 안 고쳤다</b>(#1). 이 파일은 더하기만 한 것이다.
/// </para>
/// <para>
/// DB 는 안 쓴다 — 작성기는 이미 불러온 자료 객체만 받아 그린다. 그래서 CI 에서
/// <b>건너뛰지 않고</b> 항상 돈다(거짓 초록의 두 번째 뿌리를 같이 막는다).
/// </para>
/// </remarks>
public class TaxInvoicePdfRenderGuardTests
{
    private static readonly Type SvcType = typeof(PdfRenderService);

    /// <summary>사람이 종이를 직접 볼 수 있게 떨군다 — [4] 재검증이 이 자리를 읽는다.</summary>
    private static readonly string DumpDir =
        Path.Combine(Path.GetTempPath(), "hitpan-taxinvoice-gate");

    // ── reflection 도우미 ───────────────────────────────────────────────

    private static Type Nested(string name) =>
        SvcType.GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            $"PdfRenderService.{name} 이 없다 — 세금계산서 작성기가 사라졌다(20261021작 절 B-3).");

    private static MethodInfo Method(string name) =>
        SvcType.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"PdfRenderService.{name}(…) 이 없다 — 세금계산서 작성기가 사라졌다(20261021작 절 B-3·§9-1).");

    private static void Set(object target, string prop, object? value)
    {
        var p = target.GetType().GetProperty(prop)
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{prop} 이 없다.");
        p.SetValue(target, value);
    }

    private static object MakeService() =>
        Activator.CreateInstance(SvcType,
            new object?[] { null, NullLogger<PdfRenderService>.Instance, null })!;

    /// <summary>품목 <paramref name="lineCount"/> 줄짜리 세금계산서 자료 한 벌.</summary>
    /// <param name="totalsFromLines">
    /// 🔴 20261021작 §10-4 S-3 대조군용 — 합계 금액만 <b>다른 줄 수 기준</b>으로 잡는다.
    /// 「외 N건」 칸이 종이에 실제로 찍히는지를 재려면, 합계까지 똑같고 <b>그 칸만 다른</b>
    /// 종이가 하나 필요하다. 안 주면 lineCount 와 같다(종전과 똑같이 돈다).
    /// </param>
    private static object MakeSnapshot(int lineCount, int? totalsFromLines = null)
    {
        var lineType = Nested("DocumentLine");
        var list = (IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(lineType))!;

        var totalLines = totalsFromLines ?? lineCount;
        decimal supply = 0m, vat = 0m;
        for (var i = 1; i <= totalLines; i++)
        {
            supply += 10_000m * i * 3m;
            vat += 10_000m * i * 3m * 0.1m;
        }

        for (var i = 1; i <= lineCount; i++)
        {
            var unit = 10_000m * i;
            var amount = unit * 3m;
            var tax = amount * 0.1m;

            var line = Activator.CreateInstance(lineType)!;
            Set(line, "ItemName", $"시험품목 {i.ToString("00", CultureInfo.InvariantCulture)}");
            Set(line, "Spec", "1BOX");
            Set(line, "Qty", 3m);
            Set(line, "UnitPrice", unit);
            Set(line, "Supply", amount);
            Set(line, "Vat", tax);
            Set(line, "Total", amount + tax);
            list.Add(line);
        }

        var snap = Activator.CreateInstance(Nested("DocumentSnapshot"))!;
        Set(snap, "DocType", "tax_invoice");
        Set(snap, "DocNo", "TI-TEST-0001");
        Set(snap, "DocDate", new DateTime(2026, 10, 21));
        Set(snap, "PartnerName", "시험 거래처");
        Set(snap, "PartnerBusinessNo", "987-65-43210");
        Set(snap, "PartnerAddress", "경기도 성남시 분당구 시험로 1");
        Set(snap, "PartnerCeoName", "거래처대표");
        Set(snap, "PartnerBizType", "도소매");
        Set(snap, "PartnerBizItem", "전자부품");
        Set(snap, "PartnerPhone", "031-000-0000");
        Set(snap, "Remark", "시험 비고");
        Set(snap, "TotalSupply", supply);
        Set(snap, "TotalVat", vat);
        Set(snap, "TotalAmount", supply + vat);
        Set(snap, "Lines", list);
        return snap;
    }

    private static object MakeCompany()
    {
        var c = Activator.CreateInstance(Nested("CompanyInfo"))!;
        Set(c, "CompanyName", "히트판 시험상사");
        Set(c, "BusinessNo", "123-45-67890");
        Set(c, "RepresentativeName", "대표자");
        Set(c, "Address", "서울특별시 중구 시험대로 100");
        Set(c, "BizType", "제조");
        Set(c, "BizItem", "기계");
        Set(c, "Phone", "02-0000-0000");
        return c;
    }

    /// <summary>두 벌이 한 장에 들어가는지 — 작성기와 같은 자로 잰다.</summary>
    /// <param name="maxLines">
    /// 품목 줄 상한(20261021작 §10-4 S-3). 안 주면 전부 찍는 것으로 본다 = §9-1 때와 같은 뜻.
    /// </param>
    private static bool FitsOnePage(object svc, object snap, object company, float margin,
        int maxLines = int.MaxValue) =>
        (bool)Method("MeasureBothCopiesFitOnePage").Invoke(svc,
            new object?[] { snap, company, null, margin, margin, margin, margin, maxLines })!;

    /// <summary>세금계산서 종이를 낸다.</summary>
    /// <param name="showTearLine">
    /// 🔴 고객 설정 <c>form_templates.show_tear_line</c>(DB-147 · §10-2 ⓑ).
    /// 켬 = 한 장에 두 벌 + 자르는 선(넘치면 「외 N건」) / 끔 = 두 쪽 + 품목 전부.
    /// 🔴 <b>기본을 false 로 둔다</b> — DB 기본값과 같아야 시험이 고객 종이와 같은 말을 한다.
    /// </param>
    private static byte[] Render(object svc, object snap, object company, string copyMode, float margin,
        bool showTearLine = false) =>
        (byte[])Method("RenderTaxInvoiceDocument").Invoke(svc,
            new object?[] { snap, company, copyMode, showTearLine, margin, margin, margin, margin })!;

    /// <summary>
    /// 🔴 종이를 내고 <b>그 글자를 읽는다</b> — 쪽마다 뽑은 글.
    /// </summary>
    /// <remarks>
    /// 🔴 한글 글자는 안 나온다(글꼴을 쪽아 넣으면서 한글 되읽기 표가 안 붙는다).
    /// 그래서 <b>숫자·영문으로 판정한다</b> — 사업자등록번호와 규격(<c>1BOX</c>)이
    /// 벌마다·품목 줄마다 꼭 한 번씩 찍히므로, 그 수를 세면 벌과 줄을 셀 수 있다.
    /// </remarks>
    private static string[] PageTexts(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return doc.GetPages().Select(p => p.Text).ToArray();
    }

    private static int CountPages(byte[] pdf) => PageTexts(pdf).Length;

    /// <summary>공급받는 자 등록번호 = 벌 하나에 꼭 한 번. 이 수가 곧 그 쪽에 있는 벌의 수다.</summary>
    private const string CopyMarker = "987-65-43210";

    /// <summary>품목 줄마다 찍히는 규격.</summary>
    private const string LineMarker = "1BOX";

    private static int Occurrences(string haystack, string needle)
    {
        var n = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            n++;
            at += needle.Length;
        }
        return n;
    }

    private static string Dump(string name, byte[] pdf)
    {
        Directory.CreateDirectory(DumpDir);
        var path = Path.Combine(DumpDir, name);
        File.WriteAllBytes(path, pdf);
        return path;
    }

    // ── 게이트 ─────────────────────────────────────────────────────────

    /// <summary>작성기가 살아 있고, 세금계산서 종이를 실제로 낸다.</summary>
    [Fact]
    public void 세금계산서_작성기가_PdfRenderService_에_살아있다()
    {
        var svc = MakeService();
        var pdf = Render(svc, MakeSnapshot(3), MakeCompany(), "both", 10f);
        Dump("both-3lines.pdf", pdf);

        Assert.True(pdf.Length > 5_000, $"종이가 너무 작다({pdf.Length}B) — 작성기가 안 돈 것이다.");
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
    }

    /// <summary>
    /// 🔴 R1-G1 — <b>판정과 종이가 같은 말을 한다.</b>
    /// 「한 장에 들어간다」고 재면 종이도 한 장이고, 「안 들어간다」고 재면 쪽이 나뉜다.
    /// </summary>
    /// <remarks>
    /// 🔴 이것이 반려를 닫는 자리다. 반려 당시에는 판정 자체가 없어서, 종이가 두 쪽인데도
    /// 절취선을 1쪽 한가운데 그렸다(= 자르면 반 토막 계산서).
    /// <para>
    /// 🔴 <b>20261021작 §10 재결재로 뜻이 바뀌었다</b> — 「한 장이냐 두 쪽이냐」를 이제
    /// <b>고객 설정</b>(DB-147)이 정한다. 그래서 끈 상태에서는 <b>늘 두 쪽</b>이 정답이다.
    /// 켠 상태는 아래 <c>자르는선_켜면_늘_한_장에_담는다</c> 가 맡는다.
    /// 종전 문장은 한 줄도 안 지웠다(#1).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(30)]
    public void 쪽_나눔_판정과_실제_쪽수가_일치한다(int lineCount)
    {
        var svc = MakeService();
        var snap = MakeSnapshot(lineCount);
        var company = MakeCompany();

        var pdf = Render(svc, snap, company, "both", 10f, showTearLine: false);
        Dump($"both-{lineCount}lines.pdf", pdf);
        var pages = PageTexts(pdf);

        // 끔 = 벌마다 제 쪽. 재는 값이 무엇이든 한 장으로 합치지 않는다(§10-2).
        Assert.Equal(2, pages.Length);
    }

    /// <summary>
    /// 🔴 20261021작 §10-2 ⓐ·ⓑ — <b>고객이 켜면 늘 한 장</b>이다.
    /// 한 벌을 줄였고(§10-4 S-1), 그래도 넘치면 「외 N건」으로 담는다(S-3).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(30)]
    public void 자르는선_켜면_늘_한_장에_담는다(int lineCount)
    {
        var svc = MakeService();
        var pdf = Render(svc, MakeSnapshot(lineCount), MakeCompany(), "both", 10f, showTearLine: true);
        Dump($"tear-on-{lineCount}lines.pdf", pdf);

        var pages = PageTexts(pdf);
        Assert.Single(pages);
        // 한 장 안에 벌이 둘 — 가운데를 자르면 두 장이 된다.
        Assert.Equal(2, Occurrences(pages[0], CopyMarker));
    }

    /// <summary>
    /// 🔴🔴 R1-G1·G2 의 본체 — <b>한 벌이 쪽 경계에 걸쳐 쪼개지지 않는다.</b>
    /// 반려 사유를 그대로 뒤집은 자리다.
    /// </summary>
    /// <remarks>
    /// 판정법: 벌 하나에 공급받는 자 등록번호가 꼭 한 번 찍힌다 ⇒ 쪽마다 그 수를 세면
    /// 그 쪽에서 <b>시작한 벌</b>의 수다.
    /// <list type="bullet">
    /// <item>한 장짜리면 그 한 장에 벌이 <b>둘</b>(두 벌 + 절취선)</item>
    /// <item>나뉘면 <b>어느 쪽도 벌을 둘 담지 않는다</b> — 담는 순간 그 쪽 한가운데가 경계가 된다</item>
    /// </list>
    /// 🔴 반려 당시 판(쪽 나눔 판정 없음)에서는 품목 3줄에서 1쪽이 벌을 <b>둘</b> 담고
    /// 2쪽이 <b>0</b> 을 담았다 ⇒ 이 자리가 빨개진다.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(30)]
    public void 한_벌이_쪽_경계에_걸쳐_쪼개지지_않는다(int lineCount)
    {
        var svc = MakeService();
        var pdf = Render(svc, MakeSnapshot(lineCount), MakeCompany(), "both", 10f);
        Dump($"split-check-{lineCount}lines.pdf", pdf);

        var pages = PageTexts(pdf);
        var copiesPerPage = pages.Select(t => Occurrences(t, CopyMarker)).ToArray();
        var where = string.Join(" · ", copiesPerPage.Select((n, i) => $"{i + 1}쪽={n}벌"));

        Assert.Equal(2, copiesPerPage.Sum());

        if (pages.Length == 1)
        {
            Assert.Equal(2, copiesPerPage[0]);
            return;
        }

        Assert.All(copiesPerPage, n => Assert.True(n <= 1,
            $"한 쪽이 벌을 둘 담았다 — 그 쪽 한가운데가 경계가 된다(자르면 반 토막 계산서). {where}"));
    }

    /// <summary>
    /// 🔴 품목은 한 줄도 사라지지 않는다 — 두 벌 모두에 입력한 줄이 그대로 다 찍힌다.
    /// 「외 N건」으로 감추면 이 자리가 빨개진다(합격선 ⑤).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(30)]
    public void 품목_줄은_한_줄도_사라지지_않는다(int lineCount)
    {
        var svc = MakeService();
        // 🔴 기본(자르는 선 끔)에서는 한 줄도 안 사라진다 — §10-2 가 그대로 못박은 자리다.
        var pdf = Render(svc, MakeSnapshot(lineCount), MakeCompany(), "both", 10f, showTearLine: false);
        var all = string.Concat(PageTexts(pdf));

        Assert.Equal(lineCount * 2, Occurrences(all, LineMarker));
    }

    /// <summary>
    /// 🔴 R1-G2 — 품목이 많아 안 들어가면 <b>쪽을 나눈다</b>.
    /// 벌이 쪽 경계에 걸쳐 쪼개지면 안 된다 ⇒ 벌마다 제 쪽에서 시작한다.
    /// </summary>
    [Fact]
    public void 품목이_많으면_쪽을_나눈다()
    {
        var svc = MakeService();
        var snap = MakeSnapshot(30);
        var company = MakeCompany();

        Assert.False(FitsOnePage(svc, snap, company, 10f),
            "품목 30줄짜리가 한 장에 들어간다고 나왔다 — 반 토막 계산서가 나간다.");

        var pdf = Render(svc, snap, company, "both", 10f);
        Dump("split-30lines.pdf", pdf);
        Assert.True(CountPages(pdf) >= 2, "쪽이 안 나뉘었다.");
    }

    /// <summary>
    /// 🔴 품목은 한 줄도 사라지지 않는다 — 줄을 늘리면 벌 높이가 <b>반드시</b> 커진다.
    /// 「외 N건」으로 감추거나 잘라 내면 높이가 안 늘어 이 자리가 빨개진다(합격선 ⑤).
    /// </summary>
    [Fact]
    public void 품목을_늘리면_벌_높이가_같이_커진다()
    {
        var svc = MakeService();
        var company = MakeCompany();
        var log = new CapturingLogger();
        var svcLogged = Activator.CreateInstance(SvcType, new object?[] { null, log, null })!;
        var measure = Method("MeasureBothCopiesFitOnePage");

        float HeightOf(int lines)
        {
            measure.Invoke(svcLogged,
                new object?[] { MakeSnapshot(lines), company, null, 10f, 10f, 10f, 10f, int.MaxValue });
            return log.SupplierHeight;
        }

        var h3 = HeightOf(3);
        var h30 = HeightOf(30);
        Assert.True(h30 > h3,
            $"품목을 3줄에서 30줄로 늘렸는데 벌 높이가 안 늘었다({h3} → {h30}) — 줄이 사라지고 있다.");

        // 늘어난 높이가 줄당 최소 8pt 는 돼야 한다(글자 8pt + 칸 여백).
        Assert.True(h30 - h3 >= 27 * 8f,
            $"27줄을 더했는데 높이가 {h30 - h3}pt 밖에 안 늘었다 — 일부 줄이 안 그려진다.");

        // 종이에도 그만큼 들어갔나 — 종이가 안 커지면 그리다 만 것이다.
        var small = Render(svc, MakeSnapshot(3), company, "both", 10f);
        var big = Render(svc, MakeSnapshot(30), company, "both", 10f);
        Assert.True(big.Length > small.Length, "품목 30줄 종이가 3줄 종이보다 안 크다.");
    }

    /// <summary>
    /// 🔴 벌이 하나뿐이면(<c>supplier</c>·<c>recipient</c>) 절취선도 쪽 나눔도 없다 —
    /// 쪽 나눔 봉합이 <b>한 벌 경로를 안 건드렸는지</b> 지킨다.
    /// </summary>
    [Theory]
    [InlineData("supplier", false)]
    [InlineData("recipient", false)]
    [InlineData("supplier", true)]
    [InlineData("recipient", true)]
    public void 한_벌만_낼_때는_쪽을_안_나눈다(string copyMode, bool showTearLine)
    {
        var svc = MakeService();
        var pdf = Render(svc, MakeSnapshot(3), MakeCompany(), copyMode, 10f, showTearLine);
        Dump($"one-{copyMode}-tear{showTearLine}.pdf", pdf);
        Assert.Equal(1, CountPages(pdf));
        // 🔴 벌이 하나뿐이면 「한 장에 모으기」 설정은 아무 일도 하지 않는다 —
        //   품목도 그대로 다 나온다(상한이 안 걸린다).
        Assert.Equal(3, Occurrences(string.Concat(PageTexts(pdf)), LineMarker));
    }

    /// <summary>
    /// 잰 숫자를 글로 남긴다 — 「한 장이냐」 판정의 근거를 사람이 확인할 수 있게.
    /// 판정은 하지 않는다(숫자만 적는다).
    /// </summary>
    [Fact]
    public void 쪽_나눔_판정_숫자를_남긴다()
    {
        var company = MakeCompany();
        var sb = new StringBuilder();
        sb.AppendLine("세금계산서 쪽 나눔 판정 실측 (20261021작 §9-1)");
        sb.AppendLine("A4 = 841.89pt 높이 / 1mm = 2.8346pt");
        sb.AppendLine("여백mm\t품목줄\t공급자pt\t절취선pt\t받는자pt\t합계pt\t남은높이pt\t한장?");

        foreach (var margin in new[] { 5f, 10f, 15f, 20f })
        {
            foreach (var lines in new[] { 1, 3, 5, 10, 30 })
            {
                var log = new CapturingLogger();
                var svc = Activator.CreateInstance(SvcType, new object?[] { null, log, null })!;
                var fits = (bool)Method("MeasureBothCopiesFitOnePage").Invoke(svc,
                    new object?[] { MakeSnapshot(lines), company, null, margin, margin, margin, margin, int.MaxValue })!;
                sb.AppendLine($"{margin}\t{lines}\t{log.Last}\t{fits}");
            }
        }

        Directory.CreateDirectory(DumpDir);
        File.WriteAllText(Path.Combine(DumpDir, "fit-measurements.txt"), sb.ToString(), Encoding.UTF8);
        Assert.True(sb.Length > 0);
    }


    // ── 🔴 20261021작 §10-4 S-3 음성 대조군 ⓐⓑⓒⓓ ──────────────────────
    //   근거: 작업지시서 §10-4 「음성 대조군 추가」. 셋을 다 재야 측정이다.

    /// <summary>
    /// 🔴 S-3 대조군 ⓐ — <b>자르는 선 끔</b> → 두 쪽 · 품목 <b>전부</b> · 「외」 0건.
    /// </summary>
    /// <remarks>
    /// §10-2 가 못박은 자리다. 끈 고객의 종이에서는 품목이 <b>한 줄도</b> 사라지면 안 된다.
    /// 판정은 쪽 수로만 하지 않는다 — 벌 표식과 품목 표식을 같이 센다(§10-5).
    /// </remarks>
    [Fact]
    public void S3_대조군_가_자르는선_끄면_두쪽_품목전부_외N건_0건()
    {
        var log = new CapturingLogger();
        var svc = Activator.CreateInstance(SvcType, new object?[] { null, log, null })!;

        var pdf = Render(svc, MakeSnapshot(30), MakeCompany(), "both", 10f, showTearLine: false);
        Dump("s3-a-tear-off-30lines.pdf", pdf);
        var pages = PageTexts(pdf);
        var all = string.Concat(pages);

        Assert.Equal(2, pages.Length);                              // 두 쪽
        Assert.Equal(2, Occurrences(all, CopyMarker));              // 벌 둘
        Assert.All(pages, t => Assert.Equal(1, Occurrences(t, CopyMarker)));  // 쪽마다 한 벌
        Assert.Equal(30 * 2, Occurrences(all, LineMarker));         // 품목 전부 × 두 벌
        Assert.Equal(0, log.HiddenLines);                           // 「외 N건」 0건
    }

    /// <summary>
    /// 🔴 S-3 대조군 ⓑ — <b>자르는 선 켬 + 품목 적음</b> → 한 장 · 자르는 선 1 · 「외」 0건.
    /// </summary>
    [Fact]
    public void S3_대조군_나_자르는선_켜고_품목적으면_한장_외N건_0건()
    {
        var log = new CapturingLogger();
        var svc = Activator.CreateInstance(SvcType, new object?[] { null, log, null })!;

        var pdf = Render(svc, MakeSnapshot(3), MakeCompany(), "both", 10f, showTearLine: true);
        Dump("s3-b-tear-on-3lines.pdf", pdf);
        var pages = PageTexts(pdf);

        Assert.Single(pages);                              // 한 장
        Assert.Equal(2, Occurrences(pages[0], CopyMarker));         // 그 한 장에 벌 둘 = 자르는 선 1
        Assert.Equal(3 * 2, Occurrences(pages[0], LineMarker));     // 품목 전부
        Assert.Equal(0, log.HiddenLines);                           // 「외 N건」 0건
    }

    /// <summary>
    /// 🔴🔴 S-3 대조군 ⓒ·ⓓ — <b>자르는 선 켬 + 품목 많음</b> → 한 장 · 「외 N건」 1건 ·
    /// <b>N 검산</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>N 검산</b>: 종이에 찍힌 품목 줄 수 = 전체 − N 이어야 한다. 한 줄이라도 어긋나면
    /// 고객이 받는 숫자가 거짓말이 된다.
    /// </para>
    /// <para>
    /// 🔴 <b>ⓓ 「빼면 실패하나」</b>: 「외 N건」 칸을 <b>그리는 코드를 지우면</b> 이 자리가
    /// 빨개져야 한다. 그래서 <b>대조군 종이</b>를 하나 더 낸다 — 품목 줄 수도 합계 금액도
    /// 똑같고 <b>「외 N건」 칸만 없는</b> 종이다. 두 종이에서 뽑은 글이 같으면,
    /// 그 칸이 종이에 <b>안 찍혔다</b>는 뜻이다.
    /// (이 PDF 는 한글이 안 뽑힌다 ⇒ 「외」 라는 낱말로는 못 센다 · §10-5)
    /// </para>
    /// </remarks>
    [Fact]
    public void S3_대조군_다_자르는선_켜고_품목많으면_한장_외N건_1건_N검산()
    {
        var company = MakeCompany();
        var log = new CapturingLogger();
        var svc = Activator.CreateInstance(SvcType, new object?[] { null, log, null })!;

        const int total = 30;
        var pdf = Render(svc, MakeSnapshot(total), company, "both", 10f, showTearLine: true);
        Dump("s3-c-tear-on-30lines.pdf", pdf);
        var pages = PageTexts(pdf);

        Assert.Single(pages);                              // 한 장
        Assert.Equal(2, Occurrences(pages[0], CopyMarker));         // 벌 둘

        var hidden = log.HiddenLines;
        Assert.True(hidden > 0, "품목 30줄을 한 장에 모았는데 「외 N건」이 0 이다 — 줄이 조용히 사라졌거나 재질 못했다.");

        // 🔴 N 검산 — 찍힌 줄 수 × 두 벌 = (전체 − N) × 2
        var shown = total - hidden;
        Assert.Equal(shown * 2, Occurrences(pages[0], LineMarker));

        // 🔴 ⓓ — 「외 N건」 칸만 없는 대조군. 합계 금액까지 똑같이 맞춘다.
        var controlPdf = Render(svc, MakeSnapshot(shown, totalsFromLines: total), company,
            "both", 10f, showTearLine: true);
        Dump("s3-d-control-no-overflow-row.pdf", controlPdf);
        var controlPages = PageTexts(controlPdf);

        Assert.Single(controlPages);
        Assert.Equal(shown * 2, Occurrences(controlPages[0], LineMarker));   // 품목 줄 수 같다
        Assert.NotEqual(controlPages[0], pages[0]);                          // 그런데 종이가 다르다
        Assert.True(pages[0].Length > controlPages[0].Length,
            $"「외 {hidden}건」 칸이 종이에 안 찍혔다 — 대조군과 글이 같다(그리는 코드를 지워도 초록이 난다).");
    }

    // ── 🔴 20261021작 §10-4 S-4 = §9-2 R2-G 대조군 ⓐⓑⓒ ─────────────────
    //   좌표(양식용지) 경로의 넘침 안내가 **세금계산서에서만** 나오는지.
    //   §9 에서 세 건 다 안 돌렸다 — 여기서 돌린다.

    /// <summary>좌표 경로로 종이 한 장을 낸다 — 10종이 같이 타는 길이다.</summary>
    private static string CoordsPageText(string docType, int lineCount, int maxRows)
    {
        var snap = MakeSnapshot(lineCount);
        Set(snap, "DocType", docType);

        var coordType = Nested("FieldCoord");
        var coords = (IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(coordType))!;

        void Coord(string key, float x, float y)
        {
            var c = Activator.CreateInstance(coordType)!;
            Set(c, "Key", key);
            Set(c, "XMm", x);
            Set(c, "YMm", y);
            Set(c, "WidthMm", 40f);
            coords.Add(c);
        }

        Coord("row_pitch", 0f, 6f);
        Coord("max_rows", maxRows, 0f);
        Coord("line.item_name", 20f, 80f);
        Coord("line.qty", 110f, 80f);

        var compose = SvcType.GetMethod("ComposeBodyCoords",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("PdfRenderService.ComposeBodyCoords 가 없다 — 좌표 경로가 사라졌다.");

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(0); page.MarginLeft(0); page.MarginRight(0); page.MarginBottom(0);
                page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(9));
                page.Content().Element(e => compose.Invoke(null, new object?[] { e, snap, coords }));
            });
        }).GeneratePdf();

        return string.Concat(PageTexts(bytes));
    }

    /// <summary>
    /// 🔴 R2-G ⓐ·ⓑ·ⓒ — 넘침 안내는 <b>세금계산서 경로에서만</b> 나온다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 반려 사유(§9-2): 이 안내가 <c>delivery</c> 에서도 찍혀, 결재 Q-4 의 집행 조건
    /// 「나머지 9종 종이 무변화」가 깨졌다.
    /// </para>
    /// <para>
    /// 🔴 한글이 안 뽑히므로 <b>글자로 세지 않는다</b> — 같은 자료·같은 좌표로 종류만 바꿔
    /// 내고, <b>두 종이의 글이 같은지</b>로 잰다. 안내가 붙으면 글이 길어진다.
    /// </para>
    /// </remarks>
    [Fact]
    public void S4_R2G_넘침안내는_세금계산서에서만_나온다()
    {
        // ⓐ 세금계산서 + 넘침 → 나온다
        var taxOverflow = CoordsPageText("tax_invoice", lineCount: 12, maxRows: 3);
        // ⓑ 거래명세서 + 넘침 → 0건 (같은 자료·같은 좌표. 안내만 빠져야 한다)
        var deliveryOverflow = CoordsPageText("delivery", lineCount: 12, maxRows: 3);
        // ⓒ 세금계산서인데 안 넘침 → 0건
        var taxNoOverflow = CoordsPageText("tax_invoice", lineCount: 2, maxRows: 3);
        //   ⓒ 의 짝 — 같은 자료의 거래명세서. 안내가 양쪽 다 없으면 글이 똑같아야 한다.
        var deliveryNoOverflow = CoordsPageText("delivery", lineCount: 2, maxRows: 3);

        Assert.True(taxOverflow.Length > deliveryOverflow.Length,
            "ⓐ 세금계산서가 넘쳤는데 안내가 안 나왔다(거래명세서와 글이 같거나 짧다).");
        Assert.NotEqual(deliveryOverflow, taxOverflow);

        Assert.Equal(taxNoOverflow, deliveryNoOverflow);   // ⓒ 안 넘치면 종류를 바꿔도 같은 종이
    }

    /// <summary>판정 숫자를 받아 적는 기록기 — 시험 전용.</summary>
    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<PdfRenderService>
    {
        public string Last { get; private set; } = "";

        /// <summary>마지막으로 받아 적은 공급자 벌 높이(pt).</summary>
        public float SupplierHeight { get; private set; }

        /// <summary>
        /// 🔴 20261021작 §10-4 S-3 — 작성기가 「안 찍었다」고 말한 줄 수(= 「외 N건」의 N).
        /// 게이트가 이 숫자를 종이에 실제로 찍힌 줄 수와 맞춰 본다.
        /// </summary>
        public int HiddenLines { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> kv)
            {
                var hiddenEntry = kv.FirstOrDefault(x => x.Key == "Hidden");
                if (hiddenEntry.Value is not null)
                {
                    HiddenLines = Convert.ToInt32(hiddenEntry.Value, CultureInfo.InvariantCulture);
                }

                var want = new[] { "Supplier", "Tear", "Recipient", "Needed", "Available" };
                var parts = want
                    .Select(w => kv.FirstOrDefault(x => x.Key == w).Value)
                    .Where(v => v is not null)
                    .Select(v => Convert.ToSingle(v, CultureInfo.InvariantCulture).ToString("F1", CultureInfo.InvariantCulture))
                    .ToArray();
                if (parts.Length == want.Length)
                {
                    Last = string.Join("\t", parts);
                    SupplierHeight = Convert.ToSingle(
                        kv.First(x => x.Key == "Supplier").Value, CultureInfo.InvariantCulture);
                }
            }
        }
    }
}
