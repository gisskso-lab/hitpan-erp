using System.Collections;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging;
using Moq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;
using Xunit.Abstractions;
using ExportDocumentDto = HitPan.Application.DTOs.Document.DocumentDto;
using ExportDocumentItem = HitPan.Application.DTOs.Document.DocumentItem;
using FormTemplateDto = HitPan.Application.DTOs.Settings.FormTemplateDto;
using PdfPreviewOverrides = HitPan.Application.DTOs.Documents.PdfPreviewOverrides;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 이 게이트 클래스 <b>하나만</b> 병렬을 끈다 — G3 은 일부러 잠금을 우회해 결함을 일으키므로, 같은 프로세스의
/// 다른 시험 PDF 를 깨뜨리지 않게 혼자 돈다(xUnit 은 병렬 끈 묶음을 병렬 묶음이 다 끝난 뒤 따로 돌린다).
/// </summary>
/// <remarks>
/// 🔴 <b>이것은 결재 기록 §98 이 금지한 「§59 시험을 <c>[Collection]</c> 으로 묶어 결함을 가림」 이 아니다.</b>
/// §59 시험(<c>FormNoCoordsStoredGateTests</c> · <c>FormNoCoordsImageOnlyGateTests</c>)은 병렬 그대로이고,
/// 이 게이트 안의 동시성은 시험 안에서 <c>Task.Run</c> 60개로 만든다(작업지시서 §5 · PM 결재 §13 ④).
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PdfConcurrentGenerationGateCollection
{
    public const string Name = "PdfConcurrentGenerationGate";
}

/// <summary>
/// 🔴 <b>PDF 동시 생성 회귀 게이트</b> — W-G1-51 갈래 「PDF 동시 생성 잠금」(2026-11-20 · 대표 결재 §99).
/// </summary>
/// <remarks>
/// <para><b>[결함]</b> 한 프로세스에서 QuestPDF 2026.2.4 의 PDF 생성이 겹치면 일부 PDF 의 글꼴별 ToUnicode 가
/// U+0000 으로 채워진다(종이 모양 무변 · 글자 복사 · 검색 · 추출만 깨짐). 고침 = 제품 생성 8곳을
/// <see cref="PdfGenerationGate.GeneratePdfOneAtATime"/> 한 통로로(정적 <c>lock</c>).</para>
/// <para><b>[판정 D3]</b> 순차 기준 글자열(쪽마다 PdfPig <c>Letters</c> 값 전부 · Type3 포함)과 <b>전체 일치</b>.
/// 「Page」 유무 · 글리프 수 · Type3 뺀 <c>\0</c> 셈은 거짓 초록이라 쓰지 않는다(1V §5).</para>
/// <list type="bullet">
/// <item>G1 순차 기준 1종 · 본문 · 굵은 · Type3(🔴) 세 글꼴 — 아니면 「입력 무효」 빨강</item>
/// <item>G2 제품 길 섞어 60개 동시 → 깨진 장 0</item>
/// <item>G3 잠금 우회 대조 60개 동시(최대 3라운드) → 깨진 장 ≥1 — 0 이면 「대조 성립 안 함」 빨강</item>
/// <item>G4 통로 밖 날 생성 0(제품 + 시험 글자 셈 · 우회 이름표 줄만 허용)</item>
/// </list>
/// <para>🔴 DB 0 · 파일 쓰기 0 · 포트 0 — <c>IDbConnection</c> · <c>IFormTemplateService</c> 는 Mock, 결과 PDF 는 메모리만.</para>
/// <para>🔴 리눅스에서는 G3 이 0 일 수 있다(공개 고침 문구 = Windows x64) — 그때도 초록 · 건너뜀이 아니라 빨강이다.
/// QuestPDF 2026.7.2 이상으로 판을 올리면 G3 0 이 정상이 된다 → 판올림 갈래가 G3 기대를 다시 정하고 G1 · G2 · G4 는 남긴다.</para>
/// </remarks>
[Collection(PdfConcurrentGenerationGateCollection.Name)]
public sealed class PdfConcurrentGenerationGateTests
{
    private const int ParallelCount = 60;
    private const int ControlMaxRounds = 3;
    private const int SequentialRepeats = 3;
    private const string RedCircle = "\U0001F534";
    private const string Tenant = "pdflock-gate-tenant";

    /// <summary>G4 — 시험 쪽에서 날 생성이 허용되는 줄에 붙는 이름표(G3 대조가 일부러 잠금을 우회하는 자리만).</summary>
    private const string BypassLabel = "G4-허용: G3 대조 우회";

    /// <summary>G4 — 이름표 붙은 날 생성 줄 수(이 파일 <see cref="PureBypassingGate"/> 한 줄).</summary>
    private const int ExpectedBypassLines = 1;

    /// <summary>G4 — 제품 통로 호출 수(<c>PdfRenderService</c> 5 · <c>PdfExportService</c> 3 · 작업지시서 §3).</summary>
    private const int ExpectedGateCalls = 8;

    private readonly ITestOutputHelper _out;

    static PdfConcurrentGenerationGateTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public PdfConcurrentGenerationGateTests(ITestOutputHelper output)
    {
        _out = output;
    }

    // ══════════════════════════════════════════════════════════════
    // G1 — 순차 기준 1종 · 세 글꼴
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G1 🔴 PDF 동시 생성 게이트 — 입력마다 순차 3번이 글자열 1종이고 본문 · 굵은 · Type3(🔴) 세 글꼴이 다 든다(아니면 입력 무효)")]
    public async Task G1_SequentialBaseline_IsOneKind_WithThreeFonts()
    {
        var inputs = BuildInputs();
        inputs.Add(PureInput());

        foreach (var input in inputs)
        {
            var baseline = await BuildBaselineAsync(input);
            _out.WriteLine($"G1 {input.Name}: 쪽 {baseline.Pages} · 글자 {baseline.LetterCount} · 글꼴 [{string.Join(", ", baseline.FontNames)}]");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G2 — 제품 길 섞어 60개 동시 → 깨진 장 0
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G2 🔴 PDF 동시 생성 게이트 — 제품 길(견적 · 세금계산서 작성기 2꼴 · 내보내기 3종)을 섞어 60개 동시에 만들어도 순차 기준과 글자열이 다른 장이 0이다")]
    public async Task G2_ProductPaths_Parallel60_NoBrokenPdf()
    {
        var inputs = BuildInputs();
        var baselines = new Dictionary<string, Baseline>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            baselines[input.Name] = await BuildBaselineAsync(input);
        }

        foreach (var input in inputs) input.Log?.Clear();

        var jobs = Enumerable.Range(0, ParallelCount).Select(i => inputs[i % inputs.Count]).ToList();
        var sw = Stopwatch.StartNew();
        var results = await RunParallelAsync(jobs, j => j.Generate());
        sw.Stop();

        var broken = Diff(results, r => baselines[r.Input.Name]);
        _out.WriteLine($"G2 제품 길 병렬 {ParallelCount}: 깨진 장 {broken.Count} · {sw.ElapsedMilliseconds}ms · 꼴 {inputs.Count}종 · cpu {Environment.ProcessorCount}");

        // 세금계산서 측정 길(:1860 · :2079)에 실제로 닿았는지 — 제품이 남기는 로그로 센다(제품 코드 추가 0).
        foreach (var input in inputs.Where(i => i.Log is not null && i.ReachMarker is not null))
        {
            var runs = jobs.Count(j => ReferenceEquals(j, input));
            var reached = input.Log!.Count(m => m.Contains(input.ReachMarker!, StringComparison.Ordinal));
            var failedMeasure = input.Log!.Where(m => m.Contains("재지 못했다", StringComparison.Ordinal)
                                                   || m.Contains("모으지 못했다", StringComparison.Ordinal)).ToList();
            _out.WriteLine($"G2 도달 {input.Name}: 표지 {reached}/{runs} · 측정 실패 로그 {failedMeasure.Count}");
            Assert.True(reached >= runs,
                $"🔴 G2 「{input.Name}」 측정 길 도달 표지가 {reached}/{runs} 다 — 이 꼴이 측정 호출에 닿지 않으면 그 자리 잠금은 동작으로 안 재진다.");
            Assert.True(failedMeasure.Count == 0,
                $"🔴 G2 「{input.Name}」 측정이 동시 생성 중에 실패했다: {string.Join(" | ", failedMeasure.Take(3))}");
        }

        Assert.True(broken.Count == 0,
            $"🔴 G2 제품 길을 {ParallelCount}개 동시에 만들었더니 순차 기준과 글자열이 다른 PDF 가 {broken.Count}장이다 — "
          + "PDF 생성이 한 통로(PdfGenerationGate)로 줄 서지 않는다(잠금이 빠졌거나 잠금 객체가 호출마다 다름).\n  "
          + string.Join("\n  ", broken.Take(10)));
    }

    // ══════════════════════════════════════════════════════════════
    // G3 — 잠금 우회 대조 60개 동시 → 깨진 장 ≥1 (0 이면 빨강)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G3 🔴 PDF 동시 생성 게이트 — 잠금 걷은 대조(날 생성 60개 동시)는 깨진 장이 1장 이상이다 · 3라운드 모두 0이면 「대조 성립 안 함」 빨강")]
    public async Task G3_BypassControl_Parallel60_BreaksAtLeastOne()
    {
        var input = PureInput();
        var baseline = await BuildBaselineAsync(input);

        var bypass = new GenInput("순수 QuestPDF 잠금 우회(대조)", () => Task.FromResult(PureBypassingGate()), null, null);
        var jobs = Enumerable.Range(0, ParallelCount).Select(_ => bypass).ToList();

        var perRound = new List<int>();
        var sw = Stopwatch.StartNew();
        List<string> broken = new();
        for (var round = 0; round < ControlMaxRounds; round++)
        {
            var results = await RunParallelAsync(jobs, j => j.Generate());
            broken = Diff(results, _ => baseline);
            perRound.Add(broken.Count);
            if (broken.Count > 0) break;
        }
        sw.Stop();

        var after = TextOf(await input.Generate());
        _out.WriteLine($"G3 대조 병렬 {ParallelCount}: 라운드별 깨진 장 [{string.Join(", ", perRound)}] · {sw.ElapsedMilliseconds}ms · cpu {Environment.ProcessorCount} · 대조 뒤 순차 1장 = 기준 {after.Text == baseline.Text}");
        foreach (var b in broken.Take(3)) _out.WriteLine($"G3 깨진 예: {b}");

        Assert.True(perRound.Any(n => n > 0),
            $"🔴 G3 대조 성립 안 함 — 잠금 효과 증명 불가. 잠금을 걷은 날 생성 {ParallelCount}개 동시를 {perRound.Count}라운드 돌렸는데 "
          + $"깨진 장이 [{string.Join(", ", perRound)}] 이다. 이 환경에서 결함이 안 나면 G2 의 0 은 잠금 덕인지 알 수 없다 "
          + "(리눅스 · 판올림 뒤라면 그 갈래가 G3 기대를 다시 정한다 — 초록으로 지나가지 않는다).");
    }

    // ══════════════════════════════════════════════════════════════
    // G4 — 통로 밖 날 생성 0 (제품 + 시험 · 글자 셈)
    // ══════════════════════════════════════════════════════════════
    private static readonly Regex RawGeneratePdf = new(@"\.GeneratePdf\s*\(\s*\)", RegexOptions.CultureInvariant);
    private static readonly Regex GateCall = new(@"\.GeneratePdfOneAtATime\s*\(\s*\)", RegexOptions.CultureInvariant);
    private static readonly Regex OtherGenerate = new(@"\.Generate(Images|Xps|Svg)\s*\(", RegexOptions.CultureInvariant);

    [Fact(DisplayName = "G4 🔴 PDF 동시 생성 게이트 — 통로 밖 날 생성 0: 제품 날 호출은 통로 파일 1 · 통로 호출 8 · 이미지/XPS/SVG 0 · 시험 날 호출은 G3 우회 이름표 줄만")]
    public void G4_NoRawGenerationOutsideGate()
    {
        var root = FindRepoRoot();
        var product = CodeLines(root, "src", "HitPan.Application").Concat(CodeLines(root, "src", "HitPan.API")).ToList();
        var tests = CodeLines(root, "src", "HitPan.Tests").ToList();

        var productRaw = product.Where(l => RawGeneratePdf.IsMatch(l.Text)).ToList();
        var productGate = product.Where(l => GateCall.IsMatch(l.Text)).ToList();
        var productOther = product.Where(l => OtherGenerate.IsMatch(l.Text)).ToList();
        var testRaw = tests.Where(l => RawGeneratePdf.IsMatch(l.Text)).ToList();
        var testLabeled = testRaw.Where(l => l.Text.Contains(BypassLabel, StringComparison.Ordinal)
                                          && l.File.EndsWith(nameof(PdfConcurrentGenerationGateTests) + ".cs", StringComparison.Ordinal)).ToList();
        var testUnlabeled = testRaw.Except(testLabeled).ToList();

        _out.WriteLine($"G4 제품 날 호출 {productRaw.Count} [{string.Join(", ", productRaw.Select(l => l.Where))}]");
        _out.WriteLine($"G4 제품 통로 호출 {productGate.Count} [{string.Join(", ", productGate.Select(l => l.Where))}]");
        _out.WriteLine($"G4 제품 이미지/XPS/SVG {productOther.Count} · 시험 날 호출 {testRaw.Count}(이름표 {testLabeled.Count} [{string.Join(", ", testLabeled.Select(l => l.Where))}])");

        Assert.True(productRaw.Count == 1 && productRaw[0].File.EndsWith(nameof(PdfGenerationGate) + ".cs", StringComparison.Ordinal),
            $"🔴 G4 제품 코드의 날 GeneratePdf 호출은 통로 파일({nameof(PdfGenerationGate)}.cs) 안 1개뿐이어야 한다 — 지금 {productRaw.Count}개: "
          + string.Join(", ", productRaw.Select(l => l.Where))
          + $"\n  통로 밖에서 만들면 동시 인쇄 · 메일 · 팩스 때 글자층이 깨진 PDF 가 나간다. `.{nameof(PdfGenerationGate.GeneratePdfOneAtATime)}()` 로 바꿔라.");
        Assert.True(productGate.Count == ExpectedGateCalls,
            $"🔴 G4 제품 통로 호출이 {productGate.Count}개다 — {ExpectedGateCalls}개(PdfRenderService 5 · PdfExportService 3)를 기대했다: "
          + string.Join(", ", productGate.Select(l => l.Where)));
        Assert.True(productOther.Count == 0,
            "🔴 G4 제품 코드에 이미지/XPS/SVG 생성이 생겼다 — 같은 생성 엔진이라 통로 밖이면 같은 결함 길이다: "
          + string.Join(", ", productOther.Select(l => l.Where)));
        Assert.True(testUnlabeled.Count == 0,
            "🔴 G4 시험 코드에 통로 밖 날 GeneratePdf 호출이 있다 — 같은 시험 프로세스에서 제품 길 PDF 를 깨뜨린다(PM 결재 §13 ⑤ (가)). "
          + $"`.{nameof(PdfGenerationGate.GeneratePdfOneAtATime)}()` 로 바꿔라: " + string.Join(", ", testUnlabeled.Select(l => l.Where)));
        Assert.True(testLabeled.Count == ExpectedBypassLines,
            $"🔴 G4 G3 대조 우회 이름표 줄이 {testLabeled.Count}개다 — {ExpectedBypassLines}개를 기대했다(대조가 통로로 바뀌면 G3 이 증명을 잃는다).");
    }

    // ══════════════════════════════════════════════════════════════
    // 입력 — DB 0 · 파일 0
    // ══════════════════════════════════════════════════════════════
    private sealed record GenInput(string Name, Func<Task<byte[]>> Generate, ConcurrentQueue<string>? Log, string? ReachMarker);

    private static List<GenInput> BuildInputs()
    {
        var k4Log = new ConcurrentQueue<string>();
        var tearOffLog = new ConcurrentQueue<string>();
        var tearOnLog = new ConcurrentQueue<string>();
        return new List<GenInput>
        {
            new("견적 K4(RenderDocumentAsync · :430)", () => RenderK4Async(k4Log), k4Log, null),
            // 한 장 모으기 끔 → 이어쓰기 쪽 재기(MeasureCopyPageFits · :2079) + 본판(:1737)
            new("세금계산서 이어쓰기(작성기 · :2079 + :1737)", () => Task.FromResult(RenderTaxInvoice(tearOffLog, showTearLine: false)),
                tearOffLog, "세금계산서 이어쓰기 — 벌 하나를"),
            // 한 장 모으기 켬 → 두 벌 한 쪽 재기(MeasureBothCopiesFitOnePage · :1860) + 본판(:1737)
            new("세금계산서 한 장 모으기(작성기 · :1860 + :1737)", () => Task.FromResult(RenderTaxInvoice(tearOnLog, showTearLine: true)),
                tearOnLog, "(한장모으기 True)"),
            new("거래명세서(PdfExportService · :89)", () => Task.FromResult(new PdfExportService().GenerateDeliveryPdf(ExportSample())), null, null),
            new("견적서(PdfExportService · :183)", () => Task.FromResult(new PdfExportService().GenerateQuotationPdf(ExportSample())), null, null),
            new("세금계산서(PdfExportService · :318)", () => Task.FromResult(new PdfExportService().GenerateTaxInvoicePdf(ExportSample())), null, null),
        };
    }

    private static GenInput PureInput() =>
        new("순수 QuestPDF(통로 · 대조 기준)", () => Task.FromResult(PureDoc().GeneratePdfOneAtATime()), null, null);

    /// <summary>G3 대조 — 일부러 잠금 밖에서 만든다. 이 한 줄만 G4 가 허용한다(이름표).</summary>
    private static byte[] PureBypassingGate() => PureDoc().GeneratePdf(); // G4-허용: G3 대조 우회

    /// <summary>제품 코드 0 · 본문 + 굵은 + Type3(🔴) 세 글꼴.</summary>
    private static IDocument PureDoc() => Document.Create(c => c.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(20);
        page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(10));
        page.Content().Column(col =>
        {
            col.Item().Text("견 적 서").Bold().FontSize(20);
            col.Item().Text($"{RedCircle} 예시 자료입니다 — 실제 거래 내용이 아닙니다");
            for (var i = 0; i < 20; i++) col.Item().Text($"견적서 품목 {i} 수량 3 단가 1,000 원 비고 ABC");
        });
    }));

    private static async Task<byte[]> RenderK4Async(ConcurrentQueue<string> log)
    {
        var forms = new Mock<IFormTemplateService>();
        forms.Setup(f => f.GetDefaultAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormTemplateDto
            {
                FormType = "estimate",
                TemplateName = "pdflock-gate",
                PaperMode = "plain",
                PrintBackground = false,
                BackgroundImageUrl = null,
                FieldCoordsJson = "[{\"key\":\"partner_business_no\",\"x_mm\":30,\"y_mm\":40}]",
                StyleKey = "basic",
                ShowWatermark = false,
            });
        var svc = new PdfRenderService(new Mock<IDbConnection>().Object, new QueueLogger(log), forms.Object);
        // 예시 자료 비고에 🔴 가 든다(MarkAsSample 기본 참) — Type3 글꼴이 생긴다.
        var (bytes, _) = await svc.RenderDocumentAsync(Tenant, "quotation", "pdflock-gate-doc",
            new PdfPreviewOverrides { UseSampleData = true, NoCoordsImageOnly = true });
        return bytes;
    }

    private static ExportDocumentDto ExportSample()
    {
        var dto = new ExportDocumentDto();
        dto.Tenant.CompanyName = "히트판 시험상사";
        dto.Tenant.BizNo = "123-45-67890";
        dto.Tenant.CeoName = "대표자";
        dto.Tenant.Address = "서울특별시 중구 시험대로 100";
        dto.Partner.PartnerName = $"{RedCircle} 시험 거래처";
        dto.Partner.BizNo = "987-65-43210";
        dto.Header.DocNo = "PDFLOCK-0001";
        dto.Header.OrderDate = new DateTime(2026, 11, 20);
        for (var i = 1; i <= 3; i++)
        {
            dto.Items.Add(new ExportDocumentItem
            {
                ItemName = $"{RedCircle} 시험품목 {i}",
                Spec = "1BOX",
                Unit = "EA",
                Qty = 3m,
                UnitPrice = 10_000m * i,
                Amount = 30_000m * i,
                VatAmount = 3_000m * i,
            });
        }
        dto.Header.SupplyAmount = dto.Items.Sum(x => x.Amount);
        dto.Header.VatAmount = dto.Items.Sum(x => x.VatAmount);
        dto.Header.TotalAmount = dto.Header.SupplyAmount + dto.Header.VatAmount;
        return dto;
    }

    // ── 세금계산서 작성기(반사 · 선례 TaxInvoicePdfRenderGuardTests) ──
    private static readonly Type SvcType = typeof(PdfRenderService);

    private static Type Nested(string name) =>
        SvcType.GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"PdfRenderService.{name} 이 없다 — 세금계산서 작성기 입력을 못 만든다.");

    private static void Set(object target, string prop, object? value) =>
        (target.GetType().GetProperty(prop)
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{prop} 이 없다."))
        .SetValue(target, value);

    private static byte[] RenderTaxInvoice(ConcurrentQueue<string> log, bool showTearLine)
    {
        var svc = new PdfRenderService(new Mock<IDbConnection>().Object, new QueueLogger(log), null);
        var lineType = Nested("DocumentLine");
        var lines = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(lineType))!;
        decimal supply = 0m, vat = 0m;
        for (var i = 1; i <= 3; i++)
        {
            var unit = 10_000m * i;
            var amount = unit * 3m;
            var tax = amount * 0.1m;
            supply += amount;
            vat += tax;
            var line = Activator.CreateInstance(lineType)!;
            Set(line, "ItemName", $"{RedCircle} 시험품목 {i.ToString("00", CultureInfo.InvariantCulture)}");
            Set(line, "Spec", "1BOX");
            Set(line, "Qty", 3m);
            Set(line, "UnitPrice", unit);
            Set(line, "Supply", amount);
            Set(line, "Vat", tax);
            Set(line, "Total", amount + tax);
            lines.Add(line);
        }

        var snap = Activator.CreateInstance(Nested("DocumentSnapshot"))!;
        Set(snap, "DocType", "tax_invoice");
        Set(snap, "DocNo", "PDFLOCK-TI-0001");
        Set(snap, "DocDate", new DateTime(2026, 11, 20));
        Set(snap, "PartnerName", $"{RedCircle} 시험 거래처");
        Set(snap, "PartnerBusinessNo", "987-65-43210");
        Set(snap, "PartnerAddress", "경기도 성남시 분당구 시험로 1");
        Set(snap, "PartnerCeoName", "거래처대표");
        Set(snap, "TotalSupply", supply);
        Set(snap, "TotalVat", vat);
        Set(snap, "TotalAmount", supply + vat);
        Set(snap, "Lines", lines);

        var company = Activator.CreateInstance(Nested("CompanyInfo"))!;
        Set(company, "CompanyName", "히트판 시험상사");
        Set(company, "BusinessNo", "123-45-67890");
        Set(company, "RepresentativeName", "대표자");
        Set(company, "Address", "서울특별시 중구 시험대로 100");

        var render = SvcType.GetMethod("RenderTaxInvoiceDocument", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("PdfRenderService.RenderTaxInvoiceDocument(…) 이 없다.");
        const float margin = 10f;
        return (byte[])render.Invoke(svc, new object?[] { snap, company, "both", showTearLine, margin, margin, margin, margin })!;
    }

    // ══════════════════════════════════════════════════════════════
    // 판정 도우미
    // ══════════════════════════════════════════════════════════════
    private sealed record PdfText(string Text, int Pages, int LetterCount, IReadOnlyList<string> FontNames, string ZeroFonts);

    private sealed record Baseline(string Text, int Pages, int LetterCount, IReadOnlyList<string> FontNames);

    private static bool IsType3(string font) => font.Contains("Type3", StringComparison.OrdinalIgnoreCase);

    private static bool IsBold(string font) => !IsType3(font) && font.Contains("Bold", StringComparison.OrdinalIgnoreCase);

    private static PdfText TextOf(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var pages = doc.GetPages().ToList();
        var letters = pages.SelectMany(p => p.Letters).ToList();
        var text = string.Join("\f", pages.Select(p => string.Concat(p.Letters.Select(l => l.Value))));
        var fonts = letters.Select(l => l.FontName ?? "(이름 없음)").Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var zero = string.Join(",", letters
            .Where(l => string.IsNullOrEmpty(l.Value) || l.Value[0] == '\0')
            .GroupBy(l => l.FontName ?? "(이름 없음)")
            .Select(g => $"{g.Key}:{g.Count()}"));
        return new PdfText(text, pages.Count, letters.Count, fonts, zero);
    }

    /// <summary>G1 — 순차 3번 → 1종 · 세 글꼴. 아니면 「입력 무효」 빨강(G2 · G3 도 이 기준 위에서만 잰다).</summary>
    private static async Task<Baseline> BuildBaselineAsync(GenInput input)
    {
        var texts = new List<PdfText>();
        for (var i = 0; i < SequentialRepeats; i++) texts.Add(TextOf(await input.Generate()));

        var kinds = texts.Select(t => t.Text).Distinct(StringComparer.Ordinal).Count();
        Assert.True(kinds == 1,
            $"🔴 G1 입력 무효 — 「{input.Name}」 을 순차로 {SequentialRepeats}번 만들었는데 글자열이 {kinds}종이다. 기준이 하나가 아니면 D3 비교가 성립 안 한다.");

        var first = texts[0];
        var hasType3 = first.FontNames.Any(IsType3);
        var hasBold = first.FontNames.Any(IsBold);
        var hasBody = first.FontNames.Any(f => !IsType3(f) && !IsBold(f));
        Assert.True(hasBody && hasBold && hasType3,
            $"🔴 G1 입력 무효 — 「{input.Name}」 기준에 세 글꼴이 다 있어야 한다(본문 {hasBody} · 굵은 {hasBold} · Type3(🔴) {hasType3}). "
          + $"글꼴 [{string.Join(", ", first.FontNames)}] — 글꼴이 빠진 입력은 그 글꼴의 깨짐을 못 잡는다(1V §5 · 작업지시서 C3).");
        Assert.True(first.ZeroFonts.Length == 0,
            $"🔴 G1 입력 무효 — 「{input.Name}」 순차 기준에 이미 \\0 글자가 있다: {first.ZeroFonts}");

        return new Baseline(first.Text, first.Pages, first.LetterCount, first.FontNames);
    }

    private sealed record GenResult(int Index, GenInput Input, byte[]? Bytes, Exception? Error);

    private static async Task<List<GenResult>> RunParallelAsync(List<GenInput> jobs, Func<GenInput, Task<byte[]>> gen)
    {
        var bag = new ConcurrentBag<GenResult>();
        await Task.WhenAll(jobs.Select((job, i) => Task.Run(async () =>
        {
            try { bag.Add(new GenResult(i, job, await gen(job), null)); }
            catch (Exception ex) { bag.Add(new GenResult(i, job, null, ex)); }
        })));
        return bag.OrderBy(r => r.Index).ToList();
    }

    private static List<string> Diff(List<GenResult> results, Func<GenResult, Baseline> baselineOf)
    {
        var broken = new List<string>();
        foreach (var r in results)
        {
            if (r.Error is not null)
            {
                broken.Add($"#{r.Index} {r.Input.Name}: 생성 예외 {r.Error.GetType().Name} — {r.Error.Message}");
                continue;
            }

            var b = baselineOf(r);
            var t = TextOf(r.Bytes!);
            if (string.Equals(t.Text, b.Text, StringComparison.Ordinal)) continue;
            broken.Add($"#{r.Index} {r.Input.Name}: 쪽 {t.Pages}/{b.Pages} · 글자 {t.LetterCount}/{b.LetterCount} · \\0 글꼴 [{t.ZeroFonts}]");
        }
        return broken;
    }

    // ── G4 글자 셈 ──
    private sealed record CodeLine(string File, int Line, string Text)
    {
        public string Where => $"{File}:{Line}";
    }

    private static IEnumerable<CodeLine> CodeLines(string root, params string[] parts)
    {
        var dir = Path.Combine(new[] { root }.Concat(parts).ToArray());
        Assert.True(Directory.Exists(dir), $"G4 셈 폴더가 있어야 한다: {dir}");
        foreach (var path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                     .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         .Any(seg => seg is "bin" or "obj"))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                // `//` 로 시작하는 줄(`///` 문서 주석 · 옛 줄 주석)은 뺀다.
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                yield return new CodeLine(rel, i + 1, lines[i]);
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")), "레포 루트를 찾아야 한다");
        return dir!;
    }

    /// <summary>제품이 남기는 로그를 모은다 — 세금계산서 측정 길 도달 표지(제품 코드 추가 0).</summary>
    private sealed class QueueLogger : ILogger<PdfRenderService>
    {
        private readonly ConcurrentQueue<string> _q;

        public QueueLogger(ConcurrentQueue<string> q) => _q = q;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _q.Enqueue($"{logLevel}: {formatter(state, exception)}");
    }
}
