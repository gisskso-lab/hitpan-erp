using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using HitPan.Application.Services;
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
    private static object MakeSnapshot(int lineCount)
    {
        var lineType = Nested("DocumentLine");
        var list = (IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(lineType))!;

        decimal supply = 0m, vat = 0m;
        for (var i = 1; i <= lineCount; i++)
        {
            var unit = 10_000m * i;
            var amount = unit * 3m;
            var tax = amount * 0.1m;
            supply += amount;
            vat += tax;

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
    private static bool FitsOnePage(object svc, object snap, object company, float margin) =>
        (bool)Method("MeasureBothCopiesFitOnePage").Invoke(svc,
            new object?[] { snap, company, null, margin, margin, margin, margin })!;

    /// <summary>세금계산서 종이를 낸다.</summary>
    private static byte[] Render(object svc, object snap, object company, string copyMode, float margin) =>
        (byte[])Method("RenderTaxInvoiceDocument").Invoke(svc,
            new object?[] { snap, company, copyMode, margin, margin, margin, margin })!;

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
    /// 🔴 「한 장」·「두 쪽」 중 어느 쪽이 정답인지는 <b>여기서 못박지 않는다</b> —
    /// 벌 높이가 바뀌면 답도 바뀌어야 하고, 그때도 이 시험은 그대로 성립해야 한다.
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

        var fits = FitsOnePage(svc, snap, company, 10f);
        var pdf = Render(svc, snap, company, "both", 10f);
        Dump($"both-{lineCount}lines.pdf", pdf);
        var pages = PageTexts(pdf);

        Assert.Equal(fits, pages.Length == 1);
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
        var pdf = Render(svc, MakeSnapshot(lineCount), MakeCompany(), "both", 10f);
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
                new object?[] { MakeSnapshot(lines), company, null, 10f, 10f, 10f, 10f });
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
    [InlineData("supplier")]
    [InlineData("recipient")]
    public void 한_벌만_낼_때는_쪽을_안_나눈다(string copyMode)
    {
        var svc = MakeService();
        var pdf = Render(svc, MakeSnapshot(3), MakeCompany(), copyMode, 10f);
        Dump($"one-{copyMode}.pdf", pdf);
        Assert.Equal(1, CountPages(pdf));
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
                    new object?[] { MakeSnapshot(lines), company, null, margin, margin, margin, margin })!;
                sb.AppendLine($"{margin}\t{lines}\t{log.Last}\t{fits}");
            }
        }

        Directory.CreateDirectory(DumpDir);
        File.WriteAllText(Path.Combine(DumpDir, "fit-measurements.txt"), sb.ToString(), Encoding.UTF8);
        Assert.True(sb.Length > 0);
    }

    /// <summary>판정 숫자를 받아 적는 기록기 — 시험 전용.</summary>
    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<PdfRenderService>
    {
        public string Last { get; private set; } = "";

        /// <summary>마지막으로 받아 적은 공급자 벌 높이(pt).</summary>
        public float SupplierHeight { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> kv)
            {
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
