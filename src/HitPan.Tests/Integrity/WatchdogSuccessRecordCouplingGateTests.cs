using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 인쇄 작3 갈래 D — <b>D6 관문</b>: 워치독 성공 종점 결합 정적 게이트.
/// </summary>
/// <remarks>
/// 근거(#42): 작업지시서 <c>docs/운영기록/20261120작_WG1-51_장치15차_D마감_작업지시서.md</c> §8 「D6 관문」 ·
///   설계 <c>docs/설계/erp/20261118_WG1-51_인쇄작3_반려봉합_설계.md</c> §7-4 D-8 ⓑ D6 · 결재 기록 §76 「갈래 D 몫」 ·
///   기대값 <c>docs/검증/erp/20261119_WG1-51_갈래D_재발방지_기대값.md</c> §2-7.
/// <para>
/// 왜 — 갈래 F-1 「Y 템플릿 옮기기」 는 <b>이 판의 업데이트 성공 기록</b>이 생긴 뒤에만 돈다(D-8 ⓑ).
///   그 기록은 워치독이 교체 표식을 지운 <b>뒤</b> 한 번 남긴다. 기록 줄이 표식 지움 앞으로 옮겨지거나 지워지거나,
///   워치독이 쓰는 표와 F-1 이 읽는 표 이름이 갈리면 옮기기가 「되돌아갈 수 있는 창」 안에서 돌거나 영영 안 돈다.
/// </para>
/// <para>
/// ① 워치독 성공 길: <c>UpdateFolderRecovery.DeleteMarker(AppRoot(), _logger);</c> <b>뒤</b>에
///    <c>RecordApplyStatusAsync(manifest, "success", …)</c> 가 정확히 1번(main <c>UpdateOrchestrator.cs:278</c> → <c>:280</c>) ·
///    사이에 돌아가기(return) · 예외 갈래 0.
/// ② 그 기록이 실제로 쓰는 표(<c>WatchdogStatusWriter.WriteApplyStatusAsync</c> 의 INSERT) =
///    F-1 옮기기가 「success」 를 읽는 표(<c>FormTemplateService.cs:583~592</c>).
/// </para>
/// <para>
/// 🔴 <b>대조군이 같은 시험 실행 안에 있다</b> — 원본 소스 = 초록 · 성공 기록 줄을 표식 지움 앞으로 옮긴 메모리 사본 = 빨강 ·
///   지운 사본 = 빨강 · 두 번 넣은 사본 = 빨강 · F-1 쪽 표 이름을 바꾼 사본 = 빨강. 판정 함수 하나를 원본 · 사본에 똑같이 쓴다.
///   새 잡 · 새 런 0 · 제품 심은 결함 셈과 따로.
/// </para>
/// <para>
/// 🔴 워치독 · 업데이트 코드 0줄(#43) — 이 시험은 소스 글자를 <b>읽기만</b> 한다. 사본은 메모리 문자열뿐(파일 쓰기 0).
///   ② 의 F-1 쪽 글자는 갈래 F-1 코드가 있어야 있다 ⇒ 갈래 D 가지(main 바닥) 단독 = ② 빨강이 정상 · 첫 실행 = 합류본(T-R1).
/// </para>
/// </remarks>
public sealed class WatchdogSuccessRecordCouplingGateTests
{
    private const string OrchestratorRel = "src/HitPan.Watchdog/AutoUpdate/UpdateOrchestrator.cs";
    private const string WriterRel = "src/HitPan.Watchdog/AutoUpdate/WatchdogStatusWriter.cs";
    private const string ReaderRel = "src/HitPan.Application/Services/FormTemplateService.cs";

    /// <summary>교체 표식 지움 — 성공 길의 그 줄(다른 메서드의 <c>DeleteMarker(appRoot, …)</c> 와 가름).</summary>
    private static readonly Regex MarkerDeleteRx =
        new(@"UpdateFolderRecovery\.DeleteMarker\(\s*AppRoot\(\)\s*,", RegexOptions.CultureInvariant);

    /// <summary>성공 기록 줄.</summary>
    private static readonly Regex SuccessRecordRx =
        new(@"RecordApplyStatusAsync\(\s*manifest\s*,\s*""success""\s*,", RegexOptions.CultureInvariant);

    /// <summary>두 줄 사이에 끼면 「뒤에 1번」 이 성립하지 않는 흐름 글자.</summary>
    private static readonly Regex FlowBreakRx =
        new(@"\breturn\b|\bcatch\b|\bfinally\b|\bthrow\b", RegexOptions.CultureInvariant);

    /// <summary>워치독 기록 함수가 쓰는 표(INSERT 대상 · 칸 목록에 applied_version · result).</summary>
    private static readonly Regex WriterInsertRx =
        new(@"INSERT\s+INTO\s+`?(?<t>[A-Za-z0-9_]+)`?\s*\([^)]*applied_version[^)]*result", RegexOptions.CultureInvariant);

    /// <summary>F-1 옮기기가 「success」 를 읽는 표.</summary>
    private static readonly Regex ReaderSuccessRx =
        new(@"FROM\s+`?(?<t>[A-Za-z0-9_]+)`?\s+WHERE\s+result\s*=\s*'success'", RegexOptions.CultureInvariant);

    // ═══════════════════════════════════════════════════════════════
    // 판정 함수 — 원본 · 사본에 똑같이 쓴다
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// ① 판정. 주석 줄(<c>//</c> 머리 · <c>///</c>)은 셈에서 뺀다. 빈 목록 = 초록 · 아니면 빨강 사유.
    /// </summary>
    internal static List<string> JudgeSuccessAfterMarker(string orchestratorSource)
    {
        var why = new List<string>();
        var lines = orchestratorSource.Replace("\r\n", "\n").Split('\n');
        var code = new List<(int No, string Text)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal)) continue;
            code.Add((i + 1, t));
        }

        var successAt = code.Select((c, idx) => (c, idx)).Where(x => SuccessRecordRx.IsMatch(x.c.Text)).ToList();
        if (successAt.Count != 1)
        {
            why.Add($"성공 기록 줄 수 = {successAt.Count} (정확히 1 이어야 함)");
            return why;
        }

        var sIdx = successAt[0].idx;
        var markerBefore = -1;
        for (var k = sIdx - 1; k >= 0; k--)
        {
            if (MarkerDeleteRx.IsMatch(code[k].Text)) { markerBefore = k; break; }
        }
        if (markerBefore < 0)
        {
            why.Add($"성공 기록 줄(:{successAt[0].c.No}) 앞에 교체 표식 지움 줄이 없다");
            return why;
        }

        for (var k = markerBefore + 1; k < sIdx; k++)
        {
            if (FlowBreakRx.IsMatch(code[k].Text))
                why.Add($"표식 지움(:{code[markerBefore].No})과 성공 기록(:{successAt[0].c.No}) 사이에 흐름 끊김 줄 :{code[k].No}");
        }
        return why;
    }

    /// <summary>워치독 기록 함수(<c>WriteApplyStatusAsync</c>) 몸통에서 INSERT 표 이름(서로 다른 것) 목록.</summary>
    internal static List<string> WriterTables(string writerSource)
    {
        var s = writerSource.Replace("\r\n", "\n");
        var head = s.IndexOf("Task<bool> WriteApplyStatusAsync(", StringComparison.Ordinal);
        if (head < 0) return new List<string>();
        var next = s.IndexOf("\n    public ", head + 1, StringComparison.Ordinal);
        var body = next < 0 ? s[head..] : s[head..next];
        return WriterInsertRx.Matches(body).Select(m => m.Groups["t"].Value).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>F-1 쪽 「success」 읽는 표 이름(서로 다른 것) 목록.</summary>
    internal static List<string> ReaderTables(string readerSource) =>
        ReaderSuccessRx.Matches(readerSource.Replace("\r\n", "\n"))
            .Select(m => m.Groups["t"].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>② 판정. 빈 목록 = 초록.</summary>
    internal static List<string> JudgeSameTable(string orchestratorSource, string writerSource, string readerSource)
    {
        var why = new List<string>();
        if (!orchestratorSource.Contains("_statusWriter.WriteApplyStatusAsync(", StringComparison.Ordinal))
            why.Add("성공 기록 함수가 WatchdogStatusWriter.WriteApplyStatusAsync 를 부르지 않는다");
        var w = WriterTables(writerSource);
        var r = ReaderTables(readerSource);
        if (w.Count != 1) why.Add($"워치독 기록 표 수 = {w.Count} [{string.Join(",", w)}] (1 이어야 함)");
        if (r.Count != 1) why.Add($"F-1 읽는 표 수 = {r.Count} [{string.Join(",", r)}] (1 이어야 함)");
        if (w.Count == 1 && r.Count == 1 && !string.Equals(w[0], r[0], StringComparison.Ordinal))
            why.Add($"표 이름 갈림: 워치독 {w[0]} ≠ F-1 {r[0]}");
        return why;
    }

    // ═══════════════════════════════════════════════════════════════
    // 사본 만들기 — 메모리 문자열만
    // ═══════════════════════════════════════════════════════════════

    private static string[] SplitKeep(string s) => s.Replace("\r\n", "\n").Split('\n');

    private static int FindLine(string[] lines, Regex rx)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("//", StringComparison.Ordinal)) continue;
            if (rx.IsMatch(t)) return i;
        }
        return -1;
    }

    /// <summary>성공 기록 줄을 표식 지움 줄 바로 앞으로 옮긴 사본.</summary>
    internal static string MoveSuccessBeforeMarker(string src)
    {
        var lines = SplitKeep(src).ToList();
        var s = FindLine(lines.ToArray(), SuccessRecordRx);
        var m = FindLine(lines.ToArray(), MarkerDeleteRx);
        Assert.True(s >= 0 && m >= 0, "사본 차림 불가 — 원본에 두 줄 중 하나가 없다.");
        var line = lines[s];
        lines.RemoveAt(s);
        if (s < m) m--;
        lines.Insert(m, line);
        return string.Join("\n", lines);
    }

    /// <summary>성공 기록 줄을 지운 사본.</summary>
    internal static string DeleteSuccess(string src)
    {
        var lines = SplitKeep(src).ToList();
        var s = FindLine(lines.ToArray(), SuccessRecordRx);
        Assert.True(s >= 0, "사본 차림 불가 — 원본에 성공 기록 줄이 없다.");
        lines.RemoveAt(s);
        return string.Join("\n", lines);
    }

    /// <summary>성공 기록 줄을 한 번 더 넣은 사본.</summary>
    internal static string DuplicateSuccess(string src)
    {
        var lines = SplitKeep(src).ToList();
        var s = FindLine(lines.ToArray(), SuccessRecordRx);
        Assert.True(s >= 0, "사본 차림 불가 — 원본에 성공 기록 줄이 없다.");
        lines.Insert(s + 1, lines[s]);
        return string.Join("\n", lines);
    }

    // ═══════════════════════════════════════════════════════════════
    // 파일 읽기
    // ═══════════════════════════════════════════════════════════════

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }

    private static string ReadRel(string rel)
    {
        var path = Path.Combine(RepoRoot(), rel.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"소스 파일이 없다: {rel}");
        return File.ReadAllText(path);
    }

    // ═══════════════════════════════════════════════════════════════
    // 시험 — 원본 초록 · 사본 빨강 (같은 실행 안)
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "D6-① 원본: 교체 표식 지움 뒤 성공 기록 정확히 1번 · 사이 흐름 끊김 0 (초록)")]
    public void D6_1_Original_SuccessRecordAfterMarker()
    {
        var why = JudgeSuccessAfterMarker(ReadRel(OrchestratorRel));
        Assert.True(why.Count == 0, "D6-①: " + string.Join(" / ", why));
    }

    [Fact(DisplayName = "D6-①대조a 🔴 성공 기록 줄을 표식 지움 앞으로 옮긴 사본 = 빨강")]
    public void D6_1_Control_MovedBeforeMarker_IsRed()
    {
        var copy = MoveSuccessBeforeMarker(ReadRel(OrchestratorRel));
        Assert.NotEmpty(JudgeSuccessAfterMarker(copy));
    }

    [Fact(DisplayName = "D6-①대조b 🔴 성공 기록 줄을 지운 사본 = 빨강")]
    public void D6_1_Control_Deleted_IsRed()
    {
        var copy = DeleteSuccess(ReadRel(OrchestratorRel));
        Assert.NotEmpty(JudgeSuccessAfterMarker(copy));
    }

    [Fact(DisplayName = "D6-①대조c 🔴 성공 기록 줄을 두 번 넣은 사본 = 빨강 (정확히 1번)")]
    public void D6_1_Control_Duplicated_IsRed()
    {
        var copy = DuplicateSuccess(ReadRel(OrchestratorRel));
        Assert.NotEmpty(JudgeSuccessAfterMarker(copy));
    }

    [Fact(DisplayName = "D6-② 원본: 워치독 성공 기록 표 = F-1 옮기기가 읽는 표 (초록 · F-1 코드 있어야 · 첫 실행 = 합류본)")]
    public void D6_2_Original_SameTable()
    {
        var why = JudgeSameTable(ReadRel(OrchestratorRel), ReadRel(WriterRel), ReadRel(ReaderRel));
        Assert.True(why.Count == 0, "D6-②: " + string.Join(" / ", why));
    }

    [Fact(DisplayName = "D6-②대조 🔴 F-1 쪽 표 이름을 바꾼 사본 = 빨강")]
    public void D6_2_Control_ReaderTableRenamed_IsRed()
    {
        var orch = ReadRel(OrchestratorRel);
        var writer = ReadRel(WriterRel);
        var w = WriterTables(writer);
        Assert.True(w.Count == 1, "D6-②대조: 차림 불가 — 워치독 기록 표를 하나로 못 읽었다.");
        // F-1 글자가 없는 바닥(갈래 D 단독)에서도 대조가 「읽는 표 다름」 으로 서도록 읽는 줄 꼴을 사본에 직접 만든다.
        var renamedReader = "SELECT applied_version FROM " + w[0] + "_renamed WHERE result = 'success'";
        Assert.NotEmpty(JudgeSameTable(orch, writer, renamedReader));
        // 양성 대조 — 같은 꼴에 같은 표 이름이면 초록이어야 판정기가 산 것.
        var sameReader = "SELECT applied_version FROM " + w[0] + " WHERE result = 'success'";
        Assert.Empty(JudgeSameTable(orch, writer, sameReader));
    }
}
