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
/// <para>🔴 [고침 2 · 작업지시서 §15 R3 · PM §15-PM ①] <b>판정 = D3 + 문서 지문</b>(입력마다 전 글꼴 <c>\0</c> 0 · 쪽 수 · 필수 낱말 · 금지 낱말) —
/// 지문은 프로세스 상태와 무관해서 기준이 이미 오염돼도 잡고, 날짜 글자가 없어 자정과도 무관하다. 위 「쓰지 않는다」 는 <b>유일 판정</b> 이야기이고 지문은 D3 위에 덧붙인다.</para>
/// <para>🔴 [고침 2 · R3 · PM §15-PM ③] <b>시험 실행 순서에 기대지 않는다</b> — xUnit 기본 순서는 이름 해시 순(소스 · 가나다 순 아님 · 실측 G3→G1→K9→G2→G4).
/// 앞 시험이 프로세스를 오염시켜도 기준 세우는 자리의 지문이 빨강을 낸다.</para>
/// <para>🔴 [고침 2 · R3 · PM §15-PM ②] 지문 낱말에 <b>제품 양식 글자</b>(「1 / 2 쪽」 · 「절취선」 · 「거 래 명 세 서」 · 「납품조건」 · 「[전자]」)와 쪽 수가 든다 —
/// 인쇄 양식을 바꾸는 갈래는 이 게이트가 빨강이 되므로 지문 표(<see cref="BuildInputs"/> · <see cref="PureInput"/>)를 같은 커밋에서 같이 고친다.</para>
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
    // 옛 줄(고침 2 · 상한 감쌈 전 · 작업지시서 §15 R4 · PM §15-PM ⑤): public async Task G1_SequentialBaseline_IsOneKind_WithThreeFonts()
    public Task G1_SequentialBaseline_IsOneKind_WithThreeFonts() => WithinDeadline(G1Body, "G1");

    private async Task G1Body()
    {
        var inputs = BuildInputs();
        inputs.Add(PureInput());

        foreach (var input in inputs)
        {
            // 🔴 [고침 1 · R2 · K10] 기준을 세우기 전 날짜 — 기준 글자열에 오늘 날짜가 찍히는지(자정 가설의 구조 근거) 출력한다.
            var today = DateTime.Today;
            var baseline = await BuildBaselineAsync(input);
            _out.WriteLine($"G1 {input.Name}: 쪽 {baseline.Pages} · 글자 {baseline.LetterCount} · 글꼴 [{string.Join(", ", baseline.FontNames)}]");
            // 🔴 [고침 2 · R2 · K2] 기준 지문은 BuildBaselineAsync 가 이미 단언했다 — 여기까지 오면 맞은 것이다.
            _out.WriteLine($"G1 {input.Name}: 지문 맞음(쪽 {input.Fp.Pages} · 필수 [{string.Join(", ", input.Fp.Must)}] · 금지 0)");
            _out.WriteLine($"G1 {input.Name}: 기준 글자열에 오늘 날짜 {today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} 듦 "
                + baseline.Text.Contains(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                + DateShiftNote(today));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G2 — 제품 길 섞어 60개 동시 → 깨진 장 0
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G2 🔴 PDF 동시 생성 게이트 — 제품 길(견적 · 세금계산서 작성기 2꼴 · 내보내기 3종)을 섞어 60개 동시에 만들어도 순차 기준과 글자열이 다른 장이 0이다")]
    // 옛 줄(고침 2 · 상한 감쌈 전 · 작업지시서 §15 R4 · PM §15-PM ⑤): public async Task G2_ProductPaths_Parallel60_NoBrokenPdf()
    public Task G2_ProductPaths_Parallel60_NoBrokenPdf() => WithinDeadline(G2Body, "G2");

    private async Task G2Body()
    {
        // 🔴 [고침 1 · R2 (다-2) · PM §14-9 ①] 판정 시작 날짜 — 끝에서 다르면 빨강 문구에 표지만 덧붙인다(빼기 · 건너뜀 0).
        var day0 = DateTime.Today;
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

        var broken = Diff(results, r => new[] { baselines[r.Input.Name].Text });
        _out.WriteLine($"G2 제품 길 병렬 {ParallelCount}: 깨진 장 {broken.Count} · {sw.ElapsedMilliseconds}ms · 꼴 {inputs.Count}종 · cpu {Environment.ProcessorCount}");
        foreach (var b in broken.Take(10)) _out.WriteLine($"G2 다른 장: {b.Desc}");

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

        // 🔴 [고침 1 · R1 · [4] P1-1 · PM §14-9 ③] 아래 D10 「기준 다시 세움」 덩이를 되돌린다 — 옛 줄 주석으로 보존.
        //   이 완화는 시계만 골라내지 않고 「병렬 뒤에도 같은 꼴로 계속 틀리는」 지속형 깨짐까지 뺐다([4] 변이 지속형 G2 초록 2/2 · 깨진 장 59 가림).
        //   이제 G2 = 순수 D3(순차 기준과 한 장이라도 다르면 빨강). 날짜는 위 day0 · 아래 DateShiftNote 로 표지만 붙인다.
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③): // 🔴 기준이 시계로 바뀐 경우만 가른다 — 견적 K4 예시 자료의 날짜는 `DateTime.Today`(PdfRenderService
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③): //   BuildSampleSnapshot)라 병렬 도중 자정을 넘기면 뒤에 만든 장의 날짜 글자가 기준과 달라진다(A2 2회 · 00:00 실측).
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③): //   병렬 뒤 순차 기준을 다시 세워, 기준이 실제로 바뀐 입력에 한해 **새 기준과 글자 하나까지 같은 장만** 깨짐에서 뺀다.
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③): //   글자층이 깨진 장(\0)은 새 기준과도 다르므로 그대로 빨강이다. 기준이 안 바뀌었으면 아무것도 안 뺀다.
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③): var clockShifted = 0;
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③): if (broken.Count > 0)
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③): {
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):     var after = new Dictionary<string, string>(StringComparer.Ordinal);
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):     foreach (var input in inputs.Where(i => broken.Any(b => ReferenceEquals(b.Result.Input, i))))
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):     {
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):         after[input.Name] = (await BuildBaselineAsync(input)).Text;
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):     }
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):     var before = broken.Count;
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):     broken = broken.Where(b => !(after.TryGetValue(b.Result.Input.Name, out var a)
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):                                  && !string.Equals(a, baselines[b.Result.Input.Name].Text, StringComparison.Ordinal)
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):                                  && string.Equals(b.Text, a, StringComparison.Ordinal))).ToList();
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):     clockShifted = before - broken.Count;
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③):     _out.WriteLine($"G2 기준 다시 세움: 시계로 바뀐 기준과 같은 장 {clockShifted} · 남은 깨진 장 {broken.Count}");
        // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③): }

        Assert.True(broken.Count == 0,
            $"🔴 G2 제품 길을 {ParallelCount}개 동시에 만들었더니 순차 기준과 글자열이 다른 PDF 가 {broken.Count}장이다 — "
          + "PDF 생성이 한 통로(PdfGenerationGate)로 줄 서지 않는다(잠금이 빠졌거나 잠금 객체가 호출마다 다름)"
          // 옛 줄(고침 1 · D10 되돌림 · PM §14-9 ③): + $"(시계로 바뀐 기준과 같아 뺀 장 {clockShifted}).\n  "
          + DateShiftNote(day0) + ".\n  "
          + string.Join("\n  ", broken.Take(10).Select(b => b.Desc)));
    }

    // ══════════════════════════════════════════════════════════════
    // G3 — 잠금 우회 대조 60개 동시 → 깨진 장 ≥1 (0 이면 빨강)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G3 🔴 PDF 동시 생성 게이트 — 잠금 걷은 대조(날 생성 60개 동시)는 깨진 장이 1장 이상이다 · 3라운드 모두 0이면 「대조 성립 안 함」 빨강")]
    // 옛 줄(고침 2 · 상한 감쌈 전 · 작업지시서 §15 R4 · PM §15-PM ⑤): public async Task G3_BypassControl_Parallel60_BreaksAtLeastOne()
    public Task G3_BypassControl_Parallel60_BreaksAtLeastOne() => WithinDeadline(G3Body, "G3");

    private async Task G3Body()
    {
        var input = PureInput();
        var baseline = await BuildBaselineAsync(input);

        // 옛 줄(고침 2 · 지문 칸 전 · 작업지시서 §15 R1): var bypass = new GenInput("순수 QuestPDF 잠금 우회(대조)", () => Task.FromResult(PureBypassingGate()), null, null);
        var bypass = new GenInput("순수 QuestPDF 잠금 우회(대조)", () => Task.FromResult(PureBypassingGate()), null, null, input.Fp);
        var jobs = Enumerable.Range(0, ParallelCount).Select(_ => bypass).ToList();

        var perRound = new List<int>();
        var sw = Stopwatch.StartNew();
        List<BrokenPdf> broken = new();
        for (var round = 0; round < ControlMaxRounds; round++)
        {
            var results = await RunParallelAsync(jobs, j => j.Generate());
            broken = Diff(results, _ => new[] { baseline.Text });
            perRound.Add(broken.Count);
            if (broken.Count > 0) break;
        }
        sw.Stop();

        var after = TextOf(await input.Generate());
        _out.WriteLine($"G3 대조 병렬 {ParallelCount}: 라운드별 깨진 장 [{string.Join(", ", perRound)}] · {sw.ElapsedMilliseconds}ms · cpu {Environment.ProcessorCount} · 대조 뒤 순차 1장 = 기준 {after.Text == baseline.Text}");
        foreach (var b in broken.Take(3)) _out.WriteLine($"G3 깨진 예: {b.Desc}");

        Assert.True(perRound.Any(n => n > 0),
            $"🔴 G3 대조 성립 안 함 — 잠금 효과 증명 불가. 잠금을 걷은 날 생성 {ParallelCount}개 동시를 {perRound.Count}라운드 돌렸는데 "
          + $"깨진 장이 [{string.Join(", ", perRound)}] 이다. 이 환경에서 결함이 안 나면 G2 의 0 은 잠금 덕인지 알 수 없다 "
          + "(리눅스 · 판올림 뒤라면 그 갈래가 G3 기대를 다시 정한다 — 초록으로 지나가지 않는다).");
    }

    // ══════════════════════════════════════════════════════════════
    // G4 — 통로 밖 날 생성 0 (제품 + 시험 · 글자 셈)
    // ══════════════════════════════════════════════════════════════
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④): private static readonly Regex RawGeneratePdf = new(@"\.GeneratePdf\s*\(\s*\)", RegexOptions.CultureInvariant);
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④): private static readonly Regex GateCall = new(@"\.GeneratePdfOneAtATime\s*\(\s*\)", RegexOptions.CultureInvariant);
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④): private static readonly Regex OtherGenerate = new(@"\.Generate(Images|Xps|Svg)\s*\(", RegexOptions.CultureInvariant);

    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④): [Fact(DisplayName = "G4 🔴 PDF 동시 생성 게이트 — 통로 밖 날 생성 0: 제품 날 호출은 통로 파일 1 · 통로 호출 8 · 이미지/XPS/SVG 0 · 시험 날 호출은 G3 우회 이름표 줄만")]
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④): public void G4_NoRawGenerationOutsideGate()
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④): {
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     var root = FindRepoRoot();
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     var product = CodeLines(root, "src", "HitPan.Application").Concat(CodeLines(root, "src", "HitPan.API")).ToList();
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     var tests = CodeLines(root, "src", "HitPan.Tests").ToList();

    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     var productRaw = product.Where(l => RawGeneratePdf.IsMatch(l.Text)).ToList();
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     var productGate = product.Where(l => GateCall.IsMatch(l.Text)).ToList();
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     var productOther = product.Where(l => OtherGenerate.IsMatch(l.Text)).ToList();
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     var testRaw = tests.Where(l => RawGeneratePdf.IsMatch(l.Text)).ToList();
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     var testLabeled = testRaw.Where(l => l.Text.Contains(BypassLabel, StringComparison.Ordinal)
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):                                       && l.File.EndsWith(nameof(PdfConcurrentGenerationGateTests) + ".cs", StringComparison.Ordinal)).ToList();
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     var testUnlabeled = testRaw.Except(testLabeled).ToList();

    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     _out.WriteLine($"G4 제품 날 호출 {productRaw.Count} [{string.Join(", ", productRaw.Select(l => l.Where))}]");
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     _out.WriteLine($"G4 제품 통로 호출 {productGate.Count} [{string.Join(", ", productGate.Select(l => l.Where))}]");
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     _out.WriteLine($"G4 제품 이미지/XPS/SVG {productOther.Count} · 시험 날 호출 {testRaw.Count}(이름표 {testLabeled.Count} [{string.Join(", ", testLabeled.Select(l => l.Where))}])");

    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     Assert.True(productRaw.Count == 1 && productRaw[0].File.EndsWith(nameof(PdfGenerationGate) + ".cs", StringComparison.Ordinal),
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):         $"🔴 G4 제품 코드의 날 GeneratePdf 호출은 통로 파일({nameof(PdfGenerationGate)}.cs) 안 1개뿐이어야 한다 — 지금 {productRaw.Count}개: "
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):       + string.Join(", ", productRaw.Select(l => l.Where))
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):       + $"\n  통로 밖에서 만들면 동시 인쇄 · 메일 · 팩스 때 글자층이 깨진 PDF 가 나간다. `.{nameof(PdfGenerationGate.GeneratePdfOneAtATime)}()` 로 바꿔라.");
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     Assert.True(productGate.Count == ExpectedGateCalls,
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):         $"🔴 G4 제품 통로 호출이 {productGate.Count}개다 — {ExpectedGateCalls}개(PdfRenderService 5 · PdfExportService 3)를 기대했다: "
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):       + string.Join(", ", productGate.Select(l => l.Where)));
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     Assert.True(productOther.Count == 0,
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):         "🔴 G4 제품 코드에 이미지/XPS/SVG 생성이 생겼다 — 같은 생성 엔진이라 통로 밖이면 같은 결함 길이다: "
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):       + string.Join(", ", productOther.Select(l => l.Where)));
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     Assert.True(testUnlabeled.Count == 0,
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):         "🔴 G4 시험 코드에 통로 밖 날 GeneratePdf 호출이 있다 — 같은 시험 프로세스에서 제품 길 PDF 를 깨뜨린다(PM 결재 §13 ⑤ (가)). "
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):       + $"`.{nameof(PdfGenerationGate.GeneratePdfOneAtATime)}()` 로 바꿔라: " + string.Join(", ", testUnlabeled.Select(l => l.Where)));
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):     Assert.True(testLabeled.Count == ExpectedBypassLines,
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④):         $"🔴 G4 G3 대조 우회 이름표 줄이 {testLabeled.Count}개다 — {ExpectedBypassLines}개를 기대했다(대조가 통로로 바뀌면 G3 이 증명을 잃는다).");
    // 옛 줄(고침 1 · G4 넓힘 전 · PM §14-9 ④): }

    // 🔴 [고침 1 · R5 · [4] P2-1 · P3-4 · [3-V] P2-1 · P3-5 · PM §14-9 ④] G4 = 낱말 셈(괄호 무관) · 범위 = src 전부 − 백오피스 두 폴더.
    //   줄마다 ① `//` 로 시작하는 줄 빼기 ② 문자열 · 문자 글과 줄 끝 `//…` 지우기 ③ 생성 API 전 꼴 낱말을 센다.
    //   통로 낱말(GeneratePdfOneAtATime · GenerateImagesOneAtATime)은 `\b` 로 저절로 갈린다.
    //   못 세는 꼴(기록 · 작업지시서 §14-7 W2): 문자열 안 이름 반사 호출 · 보간 구멍 안 · 다른 어셈블리로 감싼 생성.
    private static readonly Regex RawGenerationWord = new(
        @"\b(GeneratePdf|GeneratePdfAndShow|GenerateImages|GenerateXps|GenerateXpsAndShow|GenerateSvg|ShowInCompanion|ShowInCompanionAsync)\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex StringOrCharLiteral = new(
        @"(?:\$@|@\$|@)""(?:[^""]|"""")*""|\$?""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)+'",
        RegexOptions.CultureInvariant);

    private static readonly Regex GatePdfCall = new(@"\.GeneratePdfOneAtATime\b", RegexOptions.CultureInvariant);
    private static readonly Regex GateImagesCall = new(@"\.GenerateImagesOneAtATime\b", RegexOptions.CultureInvariant);

    /// <summary>G4 허용 ⓒ — 이름만 같은 옛 메서드 선언(QuestPDF 아님 · 부르는 줄 0). 부르는 줄이 생기면 헛빨강 → 문구로 안내.</summary>
    private const string LegacyExportDeclaration = "public byte[] GeneratePdf(DocumentDto data, string title)";

    private static readonly string[] BackofficeFolders = { "HitPan.Backoffice", "HitPan.Backoffice.API" };

    [Fact(DisplayName = "G4 🔴 PDF 동시 생성 게이트 — 통로 밖 날 생성 0: src 전부(백오피스 두 폴더는 수만 출력)에서 생성 API 낱말은 통로 파일(PDF 1 · 그림 1) · G3 우회 이름표 줄 · 옛 선언 1 만 · 제품 통로 호출 8")]
    public void G4_NoRawGenerationOutsideGate()
    {
        var root = FindRepoRoot();
        var all = CodeWords(root).ToList();
        var backoffice = all.Where(l => BackofficeFolders.Contains(l.TopFolder, StringComparer.Ordinal)).ToList();
        var erp = all.Except(backoffice).ToList();
        var isProduct = (CodeWordLine l) => l.TopFolder is "HitPan.Application" or "HitPan.API";

        var gateFile = "src/HitPan.Application/Services/" + nameof(PdfGenerationGate) + ".cs";
        var gateFileHits = erp.Where(l => l.File == gateFile).SelectMany(l => l.Words.Select(w => (Line: l, Word: w))).ToList();
        var labeled = erp.Where(l => l.Words.Count > 0
                                     && l.Raw.Contains(BypassLabel, StringComparison.Ordinal)
                                     && l.File.EndsWith("/" + nameof(PdfConcurrentGenerationGateTests) + ".cs", StringComparison.Ordinal)).ToList();
        var legacyDecl = erp.Where(l => l.Words.Count > 0
                                        && l.File == "src/HitPan.Application/Services/" + nameof(PdfExportService) + ".cs"
                                        && l.Stripped.Contains(LegacyExportDeclaration, StringComparison.Ordinal)).ToList();
        var outside = erp.Where(l => l.Words.Count > 0 && l.File != gateFile)
                         .Except(labeled).Except(legacyDecl).ToList();
        var productGate = erp.Where(l => isProduct(l)).Sum(l => GatePdfCall.Matches(l.Stripped).Count);
        var productGateWhere = erp.Where(l => isProduct(l) && GatePdfCall.IsMatch(l.Stripped)).Select(l => l.Where).ToList();
        var testImagesGate = erp.Where(l => !isProduct(l)).Sum(l => GateImagesCall.Matches(l.Stripped).Count);
        var gatePdf = gateFileHits.Count(h => h.Word == "GeneratePdf");
        var gateImages = gateFileHits.Count(h => h.Word == "GenerateImages");
        var gateOther = gateFileHits.Count - gatePdf - gateImages;
        var backofficeHits = backoffice.Where(l => l.Words.Count > 0).ToList();

        _out.WriteLine($"G4 셈 줄 {all.Count} · 통로 파일 날 GeneratePdf {gatePdf} · GenerateImages {gateImages} · 그 밖 {gateOther}");
        _out.WriteLine($"G4 허용 밖 날 생성 {outside.Count} [{string.Join(", ", outside.Select(l => $"{l.Where}({string.Join("/", l.Words)})"))}]");
        _out.WriteLine($"G4 G3 우회 이름표 줄 {labeled.Count} [{string.Join(", ", labeled.Select(l => l.Where))}] · 옛 선언 {legacyDecl.Count} [{string.Join(", ", legacyDecl.Select(l => l.Where))}]");
        _out.WriteLine($"G4 제품 통로 호출 {productGate} [{string.Join(", ", productGateWhere)}] · 시험 그림 통로 호출 {testImagesGate}(출력만)");
        _out.WriteLine($"G4 백오피스(출력만 · C7 열린 건) 날 생성 {backofficeHits.Count} [{string.Join(", ", backofficeHits.Select(l => l.Where))}]");

        Assert.True(outside.Count == 0,
            $"🔴 G4 통로 밖 날 생성이 {outside.Count}곳이다 — 같은 프로세스에서 겹치면 글자층이 깨진 PDF 가 나간다: "
          + string.Join(", ", outside.Select(l => $"{l.Where}({string.Join("/", l.Words)})"))
          + $"\n  `.{nameof(PdfGenerationGate.GeneratePdfOneAtATime)}()` · `.{nameof(PdfGenerationGate.GenerateImagesOneAtATime)}(settings)` 로 바꿔라."
          + $"\n  (옛 `{nameof(PdfExportService)}` 의 같은 이름 메서드를 부르는 줄이 생겨 걸렸다면 허용 목록은 결재 거리다 — 작업지시서 §14-7 W3.)");
        Assert.True(gatePdf == 1 && gateImages == 1 && gateOther == 0,
            $"🔴 G4 통로 파일의 날 생성 낱말은 GeneratePdf 1 · GenerateImages 1 이어야 한다 — 지금 {gatePdf} · {gateImages} · 그 밖 {gateOther}: "
          + string.Join(", ", gateFileHits.Select(h => $"{h.Line.Where}({h.Word})")));
        Assert.True(productGate == ExpectedGateCalls,
            $"🔴 G4 제품 통로 호출이 {productGate}개다 — {ExpectedGateCalls}개(PdfRenderService 5 · PdfExportService 3)를 기대했다: "
          + string.Join(", ", productGateWhere));
        Assert.True(labeled.Count == ExpectedBypassLines,
            $"🔴 G4 G3 대조 우회 이름표 줄이 {labeled.Count}개다 — {ExpectedBypassLines}개를 기대했다(대조가 통로로 바뀌면 G3 이 증명을 잃는다).");
    }

    // ══════════════════════════════════════════════════════════════
    // K9 — 격식 제목 글꼴 첫 등록(통로 잠금 안) 동시 20 → 교착 0 (보조)
    // ══════════════════════════════════════════════════════════════
    private const int FormalParallelCount = 20;
    private static readonly TimeSpan FormalDeadline = TimeSpan.FromSeconds(30);

    /// <remarks>
    /// 🔴 [고침 1 · R6 · K9 · PM §14-9 ⑤ (가)] 제품 <c>RenderDocumentAsync</c> 는 격식 제목 글꼴 첫 등록을 PDF 생성과 같은 잠금 안에서 한다
    /// (<c>PdfGenerationGate.RunOneAtATime</c>). 등록 공장(<c>Lazy</c>)은 이 잠금을 안 잡으므로 잡는 순서는 늘 「통로 → Lazy」 한 방향이다.
    /// 첫 등록은 프로세스당 1번이라 앞 시험이 이미 등록했으면 이 시험은 「첫 등록」 을 못 탄다 — 교착 0 의 주 근거는 코드 읽기이고 이 시험은 보조다.
    /// </remarks>
    [Fact(DisplayName = "K9 🔴 PDF 동시 생성 게이트 — 격식 양식(제목 내장 글꼴 등록 = 통로 잠금 안) 20개를 동시에 만들어도 30초 안에 다 끝난다(교착 0 · 등록 실패 로그 0)")]
    public async Task K9_FormalTitleFontRegistration_Parallel20_NoDeadlock()
    {
        var log = new ConcurrentQueue<string>();
        var sw = Stopwatch.StartNew();
        var all = Task.WhenAll(Enumerable.Range(0, FormalParallelCount).Select(_ => Task.Run(() => RenderK4Async(log, "formal"))));
        var first = await Task.WhenAny(all, Task.Delay(FormalDeadline));
        sw.Stop();

        Assert.True(ReferenceEquals(first, all),
            $"🔴 K9 격식 양식 {FormalParallelCount}개 동시 생성이 {FormalDeadline.TotalSeconds}초 안에 안 끝났다 — 글꼴 등록 · PDF 생성 잠금 교착 의심"
          // 옛 줄(고침 2 · 뒤 시험 안내 전 · 작업지시서 §15 R5): + "(등록 공장 안에서 통로 잠금을 잡지 않았나 확인).");
          + "(등록 공장 안에서 통로 잠금을 잡지 않았나 확인). "
          + $"이 시험이 교착으로 빨강이면 같은 프로세스의 뒤 PDF 시험도 통로 잠금을 기다리다 상한({PdfTestDeadline.TotalSeconds}초)에 빨강이 된다 — 원인은 이 시험이다.");
        var pdfs = await all;
        var fontFail = log.Where(m => m.Contains("격식 제목 글꼴을 등록하지 못했다", StringComparison.Ordinal)).ToList();
        _out.WriteLine($"K9 격식 양식 병렬 {FormalParallelCount}: {sw.ElapsedMilliseconds}ms · 빈 PDF {pdfs.Count(p => p.Length == 0)} · 등록 실패 로그 {fontFail.Count} · cpu {Environment.ProcessorCount}");
        Assert.True(pdfs.All(p => p.Length > 0), "🔴 K9 격식 양식 PDF 중 빈 것이 있다.");
        Assert.True(fontFail.Count == 0, $"🔴 K9 격식 제목 글꼴 등록 실패 로그가 {fontFail.Count}건이다: {string.Join(" | ", fontFail.Take(3))}");
    }

    // ══════════════════════════════════════════════════════════════
    // 입력 — DB 0 · 파일 0
    // ══════════════════════════════════════════════════════════════
    // 옛 줄(고침 2 · 지문 칸 전 · 작업지시서 §15 R1 · PM §15-PM ⑥): private sealed record GenInput(string Name, Func<Task<byte[]>> Generate, ConcurrentQueue<string>? Log, string? ReachMarker);
    private sealed record GenInput(string Name, Func<Task<byte[]>> Generate, ConcurrentQueue<string>? Log, string? ReachMarker, Fingerprint Fp);

    /// <summary>
    /// 🔴 [고침 2 · 작업지시서 §15-2 · R1] 문서 지문 — 입력마다 고정 쪽 수 · 반드시 들 낱말. 프로세스 상태와 무관한 기대값이다
    /// (기준이 이미 오염돼도 잡음 · 낱말에 날짜 0). 금지 낱말 = 다른 지문의 필수 낱말 합집합 − 자기 필수 낱말(<see cref="FingerprintProblem"/>).
    /// 🔴 양식 글자(「1 / 2 쪽」 · 「절취선」 · 「거 래 명 세 서」 · 「납품조건」 · 「[전자]」)를 바꾸는 갈래는 이 표를 같은 커밋에서 맞춘다(PM §15-PM ②).
    /// </summary>
    private sealed record Fingerprint(int Pages, string[] Must);

    private static readonly Fingerprint FpK4 = new(1, new[] { "예시-0000" });
    private static readonly Fingerprint FpTaxContinue = new(2, new[] { "PDFLOCK-TI-0001", "1 / 2 쪽" });
    private static readonly Fingerprint FpTaxOnePage = new(1, new[] { "PDFLOCK-TI-0001", "절취선" });
    private static readonly Fingerprint FpDelivery = new(1, new[] { "거 래 명 세 서" });
    private static readonly Fingerprint FpQuotation = new(1, new[] { "납품조건" });
    private static readonly Fingerprint FpTaxExport = new(1, new[] { "[전자]" });
    private static readonly Fingerprint FpPure = new(1, new[] { "비고 ABC" });

    private static readonly Fingerprint[] AllFingerprints =
        { FpK4, FpTaxContinue, FpTaxOnePage, FpDelivery, FpQuotation, FpTaxExport, FpPure };

    private static List<GenInput> BuildInputs()
    {
        var k4Log = new ConcurrentQueue<string>();
        var tearOffLog = new ConcurrentQueue<string>();
        var tearOnLog = new ConcurrentQueue<string>();
        return new List<GenInput>
        {
            // 옛 줄(고침 2 · 지문 칸 전 · R1): new("견적 K4(RenderDocumentAsync · :430)", () => RenderK4Async(k4Log), k4Log, null),
            new("견적 K4(RenderDocumentAsync · :430)", () => RenderK4Async(k4Log), k4Log, null, FpK4),
            // 한 장 모으기 끔 → 이어쓰기 쪽 재기(MeasureCopyPageFits · :2079) + 본판(:1737)
            new("세금계산서 이어쓰기(작성기 · :2079 + :1737)", () => Task.FromResult(RenderTaxInvoice(tearOffLog, showTearLine: false)),
                // 옛 줄(고침 2 · 지문 칸 전 · R1): tearOffLog, "세금계산서 이어쓰기 — 벌 하나를"),
                tearOffLog, "세금계산서 이어쓰기 — 벌 하나를", FpTaxContinue),
            // 한 장 모으기 켬 → 두 벌 한 쪽 재기(MeasureBothCopiesFitOnePage · :1860) + 본판(:1737)
            new("세금계산서 한 장 모으기(작성기 · :1860 + :1737)", () => Task.FromResult(RenderTaxInvoice(tearOnLog, showTearLine: true)),
                // 옛 줄(고침 2 · 지문 칸 전 · R1): tearOnLog, "(한장모으기 True)"),
                tearOnLog, "(한장모으기 True)", FpTaxOnePage),
            // 옛 줄(고침 2 · 지문 칸 전 · R1): new("거래명세서(PdfExportService · :89)", () => Task.FromResult(new PdfExportService().GenerateDeliveryPdf(ExportSample())), null, null),
            new("거래명세서(PdfExportService · :89)", () => Task.FromResult(new PdfExportService().GenerateDeliveryPdf(ExportSample())), null, null, FpDelivery),
            // 옛 줄(고침 2 · 지문 칸 전 · R1): new("견적서(PdfExportService · :183)", () => Task.FromResult(new PdfExportService().GenerateQuotationPdf(ExportSample())), null, null),
            new("견적서(PdfExportService · :183)", () => Task.FromResult(new PdfExportService().GenerateQuotationPdf(ExportSample())), null, null, FpQuotation),
            // 옛 줄(고침 2 · 지문 칸 전 · R1): new("세금계산서(PdfExportService · :318)", () => Task.FromResult(new PdfExportService().GenerateTaxInvoicePdf(ExportSample())), null, null),
            new("세금계산서(PdfExportService · :318)", () => Task.FromResult(new PdfExportService().GenerateTaxInvoicePdf(ExportSample())), null, null, FpTaxExport),
        };
    }

    private static GenInput PureInput() =>
        // 옛 줄(고침 2 · 지문 칸 전 · R1): new("순수 QuestPDF(통로 · 대조 기준)", () => Task.FromResult(PureDoc().GeneratePdfOneAtATime()), null, null);
        new("순수 QuestPDF(통로 · 대조 기준)", () => Task.FromResult(PureDoc().GeneratePdfOneAtATime()), null, null, FpPure);

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

    // 옛 줄(고침 1 · K9 전 — 양식 스타일 인자 보탬 · 기본값 = 옛 값 "basic"): private static async Task<byte[]> RenderK4Async(ConcurrentQueue<string> log)
    private static async Task<byte[]> RenderK4Async(ConcurrentQueue<string> log, string styleKey = "basic")
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
                // 옛 줄(고침 1 · K9 전): StyleKey = "basic",
                StyleKey = styleKey,
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
        // 🔴 [고침 1 · R2 (다-2)] 순차 3번 시작 날짜 — 자정을 걸치면 빨강 문구에 표지만 붙는다(빨강 그대로).
        var day0 = DateTime.Today;
        var texts = new List<PdfText>();
        for (var i = 0; i < SequentialRepeats; i++) texts.Add(TextOf(await input.Generate()));

        var kinds = texts.Select(t => t.Text).Distinct(StringComparer.Ordinal).Count();
        Assert.True(kinds == 1,
            // 옛 줄(고침 1 · R2 전): $"🔴 G1 입력 무효 — 「{input.Name}」 을 순차로 {SequentialRepeats}번 만들었는데 글자열이 {kinds}종이다. 기준이 하나가 아니면 D3 비교가 성립 안 한다.");
            $"🔴 G1 입력 무효 — 「{input.Name}」 을 순차로 {SequentialRepeats}번 만들었는데 글자열이 {kinds}종이다. 기준이 하나가 아니면 D3 비교가 성립 안 한다."
          + DateShiftNote(day0));

        var first = texts[0];
        // 🔴 [고침 2 · R2 · [4] P1-A · PM §15-PM ①] 기준이 같은 프로세스의 현재 상태로 서므로, 이미 오염된 프로세스면 기준도 오염된다 —
        //   프로세스 상태와 무관한 문서 지문으로 기준 자체를 먼저 확인한다(순서 장치 0 · §15-PM ③).
        var fpProblem = FingerprintProblem(input, first);
        Assert.True(fpProblem is null,
            $"🔴 G1 입력 무효 — 「{input.Name}」 기준 지문 어긋남(이 프로세스가 이미 오염됐거나 양식 글자가 바뀜 — 양식을 바꾼 갈래면 지문 표를 같은 커밋에서 맞춤): {fpProblem}");
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

    /// <summary>
    /// 🔴 [고침 1 · R2 (다-2) · PM §14-9 ①] 판정 중 날짜가 바뀌었으면 빨강 문구에 붙일 표지 — 안 바뀌었으면 빈 글.
    /// 견적 K4 예시 자료 날짜는 <c>DateTime.Today</c>(제품 <c>BuildSampleSnapshot</c> · 시험에서 넣을 이음매 없음)라 자정을 걸치면
    /// 날짜 글자가 달라질 수 있다. 이 표지는 <b>문구만</b> 바꾼다 — 빨강을 초록으로 · 건너뜀으로 바꾸는 길은 0 이다.
    /// </summary>
    private static string DateShiftNote(DateTime before)
    {
        var after = DateTime.Today;
        return after == before
            ? string.Empty
            : $" 🕛 판정 중 날짜 바뀜({before.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}→{after.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}) — 예시 자료 날짜 글자 차이일 수 있으나 빨강 그대로 · 다시 돌려 확인";
    }

    /// <summary>
    /// 🔴 [고침 2 · R2 · 작업지시서 §15-2 (가)] 문서 지문 판정 — ⓐ 전 글꼴(Type3 포함) <c>\0</c> 글자 0 ⓑ 쪽 = <see cref="Fingerprint.Pages"/>
    /// ⓒ 필수 낱말 전부 ⓓ 금지 낱말(다른 지문의 필수 − 자기 필수) 0. 맞으면 <c>null</c>, 어긋나면 까닭 글.
    /// D3 를 대신하지 않고 위에 덧붙인다(ⓐ 단독은 1V §5 의 거짓 초록 꼴 → 단독으로 안 씀).
    /// </summary>
    private static string? FingerprintProblem(GenInput input, PdfText t)
    {
        var fp = input.Fp;
        var problems = new List<string>();
        if (t.ZeroFonts.Length > 0) problems.Add($"\\0 글자 {t.ZeroFonts}");
        if (t.Pages != fp.Pages) problems.Add($"쪽 {t.Pages}≠{fp.Pages}");
        foreach (var must in fp.Must.Where(m => !t.Text.Contains(m, StringComparison.Ordinal)))
            problems.Add($"필수 없음 「{must}」");
        var forbidden = AllFingerprints.Where(f => !ReferenceEquals(f, fp))
                                       .SelectMany(f => f.Must)
                                       .Except(fp.Must, StringComparer.Ordinal)
                                       .Distinct(StringComparer.Ordinal);
        foreach (var word in forbidden.Where(w => t.Text.Contains(w, StringComparison.Ordinal)))
            problems.Add($"금지 있음 「{word}」");
        return problems.Count == 0 ? null : string.Join(" · ", problems);
    }

    /// <summary>🔴 [고침 2 · R4 · PM §15-PM ⑤] PDF 를 만드는 시험(G1 · G2 · G3) 하나의 상한 — 정상 G2 2.4~5.2 s 의 20배 넘는 여유.</summary>
    private static readonly TimeSpan PdfTestDeadline = TimeSpan.FromSeconds(120);

    /// <summary>
    /// 🔴 [고침 2 · R4 · [4] P3-B] 시험 본문을 상한 안에서 돌린다 — 앞 시험(K9 등)이 교착으로 통로 잠금을 쥔 채 멈추면 뒤 시험이
    /// 끝없이 기다려 CI 잡 시간 초과(30분)로만 보이던 것을, 그 시험 하나의 빨강으로 바꾼다.
    /// 이 상한은 <b>시험 쪽</b>이다 — 제품 통로 대기 타임아웃 없음(결재 6)은 그대로다. 멈춘 본문은 스레드 풀(배경) 스레드에 남는다.
    /// </summary>
    private static async Task WithinDeadline(Func<Task> body, string name)
    {
        var run = Task.Run(body);
        var first = await Task.WhenAny(run, Task.Delay(PdfTestDeadline));
        Assert.True(ReferenceEquals(first, run),
            $"🔴 {name} 이 {PdfTestDeadline.TotalSeconds}초 안에 안 끝났다 — PDF 통로 잠금이 안 풀린다. 앞 시험(K9 등)이 교착으로 빨강이면 원인은 그 시험이다.");
        await run;
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

    private sealed record BrokenPdf(GenResult Result, string Desc, string? Text);

    /// <summary>D3 — 글자열이 기준(<paramref name="okTexts"/>)과 하나도 안 맞는 장. 생성 예외도 깨진 장으로 센다.</summary>
    private static List<BrokenPdf> Diff(List<GenResult> results, Func<GenResult, IReadOnlyList<string>> okTexts)
    {
        var broken = new List<BrokenPdf>();
        foreach (var r in results)
        {
            if (r.Error is not null)
            {
                broken.Add(new BrokenPdf(r, $"#{r.Index} {r.Input.Name}: 생성 예외 {r.Error.GetType().Name} — {r.Error.Message}", null));
                continue;
            }

            var ok = okTexts(r);
            var t = TextOf(r.Bytes!);
            // 🔴 [고침 2 · R2] D3 비교 앞에 문서 지문 — 기준이 오염돼 오염된 장과 같아도 지문이 깨진 장으로 센다.
            var fpProblem = FingerprintProblem(r.Input, t);
            if (fpProblem is not null)
            {
                broken.Add(new BrokenPdf(r, $"#{r.Index} {r.Input.Name}: 지문 {fpProblem}", t.Text));
                continue;
            }
            if (ok.Any(x => string.Equals(t.Text, x, StringComparison.Ordinal))) continue;
            var firstDiff = 0;
            while (firstDiff < Math.Min(t.Text.Length, ok[0].Length) && t.Text[firstDiff] == ok[0][firstDiff]) firstDiff++;
            broken.Add(new BrokenPdf(r,
                $"#{r.Index} {r.Input.Name}: 쪽 {t.Pages} · 글자 {t.LetterCount} · 글자열 길이 {t.Text.Length}/{ok[0].Length} · 첫 다름 {firstDiff} · \\0 글꼴 [{t.ZeroFonts}]",
                t.Text));
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

    /// <summary>G4 낱말 셈 한 줄 — <see cref="Raw"/> 는 원문(이름표 확인용) · <see cref="Stripped"/> 는 문자열 · 문자 글 · 줄 끝 주석을 지운 글.</summary>
    private sealed record CodeWordLine(string File, int Line, string Raw, string Stripped, IReadOnlyList<string> Words)
    {
        public string Where => $"{File}:{Line}";

        /// <summary><c>src/</c> 바로 아래 폴더 이름(예: <c>HitPan.Application</c>).</summary>
        public string TopFolder => File.Split('/') is { Length: > 2 } parts ? parts[1] : string.Empty;
    }

    /// <summary>🔴 [고침 1 · R5] <c>src</c> 아래 모든 <c>.cs</c>(bin · obj 빼기 · <c>//</c> 시작 줄 빼기) — 줄마다 생성 API 낱말을 센다.</summary>
    private static IEnumerable<CodeWordLine> CodeWords(string root)
    {
        foreach (var l in CodeLines(root, "src"))
        {
            var stripped = StringOrCharLiteral.Replace(l.Text, "\"\"");
            var cut = stripped.IndexOf("//", StringComparison.Ordinal);
            if (cut >= 0) stripped = stripped[..cut];
            var words = RawGenerationWord.Matches(stripped).Select(m => m.Value).ToList();
            yield return new CodeWordLine(l.File, l.Line, l.Text, stripped, words);
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
