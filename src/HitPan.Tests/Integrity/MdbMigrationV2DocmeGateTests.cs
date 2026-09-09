using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>MdbMigrationV2DocmeGate</b> — 레거시 메모(DOCME) → 일일보고서(hr_reports) 매핑 판정 + 배선 (20260909작22 갈래 D).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 이 게이트를 짰나</b><br/>
/// 사장님 9/8 ③ <i>"상담이력, 메모이력은 일일보고서에 작성자, 내용만 살려서 이관하고 … 결재 완료된 건으로 보관"</i>.
/// DOCME 242,106행을 (사원,날짜) 9,167장으로 접는 규칙(날짜 유효성·줄 모양·묶음 키·멱등 키·제목)이 서비스 안에 묻히면
/// 아무도 값을 넣어 보지 못한다. 작22 D 가 그 규칙을 <see cref="LegacyDocmeMapping"/> 순수함수로 뺐고, 이 게이트는 그 함수를 <b>실제로 부른다.</b>
/// </para>
///
/// <para>
/// 🔴 <b>케이스 값은 MDB 실측(선행검증서 §2-3 · 9/9 읽기전용 재확인)에서 뽑았다.</b>
/// 연도 <c>0000</c> 3행(<c>00000000</c>·<c>00000109</c>·<c>00000929</c>) · 시각 6자 아님 302행(<c>"0000"</c>·한 칸 공백) ·
/// 시각 6자는 전부 숫자(콜론 0건) · 공지 346행 · 사원 빈값 135행.
/// </para>
///
/// <para>
/// 🔴 <b>두 층으로 나뉜다</b><br/>
/// G10-1~G10-5 = <b>판정 함수</b>를 값으로 검사한다. 함수를 되돌리면 빨간불.<br/>
/// W15 = <b>배선</b>을 검사한다 — <c>hr_reports</c> 잡이 <c>events</c> 잡 <b>뒤</b>에 등록됐는지, 결과 필드가 합산되는지,
/// DB-119 와 출하 DDL(#36)에 컬럼·UNIQUE·시드가 있는지(작21 W8·W9 와 같은 모양).
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — MDB 실물을 읽어 DB 에 넣는 종단(9,167장 · 2회 = 1회 · 관리자 목록 API)은 못 잰다.
/// 그것은 <c>hitpan_e2e</c> 실측(작업지시서 §5 G-DM)의 몫이다.
/// </para>
/// </remarks>
public sealed class MdbMigrationV2DocmeGateTests
{
    private static string Sha256Hex(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s ?? string.Empty)));

    // ────────────────────────────────────────────────────────────────────────────
    //  G10-1 — IsValidDate : 8자리 숫자 · 연도 ≠ 0000 · 실제 날짜
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G10-1 — 연도 <c>0000</c>·자릿수 불량·없는 날짜·빈값은 무효, 정상 8자리는 유효.
    /// <para>무력화: <c>TryParseDate</c> 의 <c>StartsWith("0000")</c> 검사를 지우면 <c>00001231</c> 이 유효로 바뀌어 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("20260220", true)]
    [InlineData("20000918", true)]
    [InlineData("00001231", false)]
    [InlineData("00000000", false)]
    [InlineData("2026022", false)]
    [InlineData("202602201", false)]
    [InlineData("20261340", false)]
    [InlineData("2026-02-20", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void G10_1_IsValidDate(string? meDate, bool expected)
    {
        Assert.Equal(expected, LegacyDocmeMapping.IsValidDate(meDate));
    }

    [Fact]
    public void G10_1_TryParseDate_는_실제_날짜를_돌려준다()
    {
        Assert.True(LegacyDocmeMapping.TryParseDate("20260220", out var d));
        Assert.Equal(new DateTime(2026, 2, 20), d);
        Assert.False(LegacyDocmeMapping.TryParseDate("00000109", out _));
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G10-2 — Line : HH:MM [구분] d1..d5 · 빈 칸 생략 · --:-- · (공지)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G10-2 — 정상 줄: <c>09:30 [상담] 가 나</c>. 빈 칸은 건너뛰고 공백 하나로 잇는다.
    /// <para>무력화: <c>AppendPart</c> 의 빈값 건너뛰기를 지우면 <c>가  나</c>(공백 둘)가 되어 빨간불.</para>
    /// </summary>
    [Fact]
    public void G10_2_Line_정상()
    {
        Assert.Equal("09:30 [상담] 가 나", LegacyDocmeMapping.Line("093015", "상담", "가", "", "나", "", "", 0));
    }

    /// <summary>🔴 G10-2 — 시각 빈값·<c>9:30</c>·<c>0000</c>(실측 4자)·한 칸 공백은 <c>--:--</c> 로 시작한다.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("9:30")]
    [InlineData("0000")]
    [InlineData(" ")]
    [InlineData("09301a")]
    public void G10_2_Line_시각_못읽으면_자리표시(string? meTime)
    {
        var line = LegacyDocmeMapping.Line(meTime, "상담", "가", null, null, null, null, 0);
        Assert.StartsWith("--:-- ", line, StringComparison.Ordinal);
        Assert.Equal("--:--", LegacyDocmeMapping.FormatTime(meTime));
    }

    /// <summary>🔴 G10-2 — 공지(<c>ME_NOTICE≠0</c>)면 줄 끝 <c> (공지)</c>, 0 이면 없다.</summary>
    [Fact]
    public void G10_2_Line_공지_표기()
    {
        Assert.EndsWith(" (공지)", LegacyDocmeMapping.Line("142621", "상담", "가", null, null, null, null, 1), StringComparison.Ordinal);
        Assert.EndsWith(" (공지)", LegacyDocmeMapping.Line("142621", "상담", "가", null, null, null, null, -1), StringComparison.Ordinal);
        Assert.DoesNotContain("(공지)", LegacyDocmeMapping.Line("142621", "상담", "가", null, null, null, null, 0));
    }

    /// <summary>🔴 G10-2 — 구분이 빈값이면 <c>[</c> 자체가 없다(<c>[기타]</c> 를 지어내지 않는다).</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void G10_2_Line_구분_빈값이면_괄호_없음(string? gubun)
    {
        var line = LegacyDocmeMapping.Line("093015", gubun, "가", null, null, null, null, 0);
        Assert.Equal("09:30 가", line);
        Assert.DoesNotContain("[", line);
    }

    /// <summary>🔴 G10-2 — 레거시 Text 40 의 뒤 공백(실측 102행)은 TRIM 되어 칸 사이 공백이 하나다. 다섯 칸 전부 채워도 순서가 유지된다.</summary>
    [Fact]
    public void G10_2_Line_뒤공백_TRIM_및_다섯칸_순서()
    {
        var line = LegacyDocmeMapping.Line("101010", " 입출 ", "a   ", "  b", "c", "d ", " e", 0);
        Assert.Equal("10:10 [입출] a b c d e", line);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G10-3 — SourceId : ≤ 80자 · 결정적 · 사원명 공백 무시
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G10-3 — <c>docme-{yyyyMMdd}-{sha8}</c> 는 80자 이하(<c>hr_reports.source_id varchar(80)</c>), 같은 입력이면 같은 값,
    /// 사원명 앞뒤 공백(<c>" 홍길동 "</c> vs <c>"홍길동"</c>)은 같은 값, 다른 사원·다른 날짜는 다른 값.
    /// <para>무력화: <c>SourceId</c> 안의 <c>Trim()</c> 을 지우면 공백 차이가 다른 값이 되어 빨간불.</para>
    /// </summary>
    [Fact]
    public void G10_3_SourceId_길이_결정성_공백무시()
    {
        var a = LegacyDocmeMapping.SourceId("20260220", "홍길동", Sha256Hex);
        var b = LegacyDocmeMapping.SourceId("20260220", " 홍길동 ", Sha256Hex);
        var c = LegacyDocmeMapping.SourceId("20260220", "홍길동", Sha256Hex);
        var otherName = LegacyDocmeMapping.SourceId("20260220", "김철수", Sha256Hex);
        var otherDate = LegacyDocmeMapping.SourceId("20260221", "홍길동", Sha256Hex);
        var empty = LegacyDocmeMapping.SourceId("20260220", "", Sha256Hex);

        Assert.True(a.Length <= 80, $"source_id 는 80자 이하여야 한다: {a.Length}");
        Assert.StartsWith("docme-20260220-", a, StringComparison.Ordinal);
        Assert.Equal(8, a.Length - "docme-20260220-".Length);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.NotEqual(a, otherName);
        Assert.NotEqual(a, otherDate);
        Assert.NotEqual(a, empty);
        Assert.Equal(empty, LegacyDocmeMapping.SourceId("20260220", "   ", Sha256Hex));
    }

    /// <summary>🔴 G10-3 — 묶음 키도 같은 TRIM 이라, 공백만 다른 사원명은 한 묶음이고 빈값은 <c>""</c> 로 모인다.</summary>
    [Fact]
    public void G10_3_ReportKey_공백_TRIM_빈값()
    {
        Assert.Equal(LegacyDocmeMapping.ReportKey("홍길동", "20260220"), LegacyDocmeMapping.ReportKey(" 홍길동 ", "20260220"));
        Assert.Equal(("", "20260220"), LegacyDocmeMapping.ReportKey(null, "20260220"));
        Assert.Equal(("", "20260220"), LegacyDocmeMapping.ReportKey("   ", "20260220"));
        Assert.NotEqual(LegacyDocmeMapping.ReportKey("홍길동", "20260220"), LegacyDocmeMapping.ReportKey("홍길동", "20260221"));
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G10-4 — Title : WorkReportService.BuildTitle 규칙과 같은 문자열
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G10-4 — 제목은 <c>일일보고서 2026-02-20 홍길동</c> 모양. 이름 없으면 <c>일일보고서 2026-02-20</c>.
    /// 기대값은 <c>WorkReportService.BuildTitle(null, "daily", d, d, name)</c> 을 <b>리플렉션으로 실제 호출</b>해 얻는다 —
    /// 화면이 만드는 제목과 한 글자라도 다르면 빨간불(복붙이 아니라 대조).
    /// <para>무력화: <c>Title</c> 의 <c>label + " " + period</c> 순서를 바꾸면 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("홍길동")]
    [InlineData("레거시이관")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void G10_4_Title_은_BuildTitle_과_같다(string? employeeName)
    {
        var date = new DateTime(2026, 2, 20);
        var expected = InvokeBuildTitle(date, employeeName);
        var actual = LegacyDocmeMapping.Title(date, employeeName);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void G10_4_Title_모양_고정()
    {
        var date = new DateTime(2026, 2, 20);
        Assert.Equal("일일보고서 2026-02-20 홍길동", LegacyDocmeMapping.Title(date, "홍길동"));
        Assert.Equal("일일보고서 2026-02-20", LegacyDocmeMapping.Title(date, null));
        Assert.True(LegacyDocmeMapping.Title(date, new string('가', 300)).Length <= 200, "title 은 200자 이하(varchar(200))");
    }

    private static string InvokeBuildTitle(DateTime date, string? employeeName)
    {
        var method = typeof(WorkReportService).GetMethod("BuildTitle", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(method is not null, "WorkReportService.BuildTitle(private static) 이 있어야 한다 — 이름이 바뀌었으면 이 게이트와 LegacyDocmeMapping.Title 을 같이 본다");
        var result = method!.Invoke(null, new object?[] { null, "daily", date, date, employeeName });
        Assert.True(result is string, "BuildTitle 은 string 을 돌려준다");
        return (string)result!;
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G10-5 — OriginalAuthorLine : 미매칭 사원 자리표시의 첫 줄
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>🔴 G10-5 — <c>원작성자: {이름}</c> · 빈값은 <c>원작성자: (없음)</c>.</summary>
    [Fact]
    public void G10_5_OriginalAuthorLine()
    {
        Assert.Equal("원작성자: 010-0000-0000", LegacyDocmeMapping.OriginalAuthorLine(" 010-0000-0000 "));
        Assert.Equal("원작성자: (없음)", LegacyDocmeMapping.OriginalAuthorLine(null));
        Assert.Equal("원작성자: (없음)", LegacyDocmeMapping.OriginalAuthorLine("  "));
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  W15 — 배선 : hr_reports 잡(events 뒤) · 결과 합산 · DB-119 · 출하 DDL
    // ────────────────────────────────────────────────────────────────────────────

    private const string ServiceFile = "MdbMigrationService.cs";

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")), "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>주석 줄을 걷어낸 코드만 남긴다 — 설명문에 적힌 코드 모양에 걸려 헛통과·헛실패하지 않게.</summary>
    private static string StripComments(string source)
        => string.Join('\n', source.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//", StringComparison.Ordinal)
                && !t.StartsWith("--", StringComparison.Ordinal)
                && !t.StartsWith("*", StringComparison.Ordinal);
        }));

    private static string ServiceSource()
        => StripComments(ReadSource("src", "HitPan.Application", "Services", ServiceFile));

    /// <summary>메서드 본문 한 덩어리만 잘라낸다 — 정의(<c>&gt; 메서드명(</c>)부터 다음 <c>private</c> 멤버 직전까지(작21 게이트와 같은 방식).</summary>
    private static string MethodBody(string source, string methodName)
    {
        var start = source.IndexOf($"> {methodName}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{ServiceFile} 에 {methodName} 정의가 있어야 한다");
        var end = source.IndexOf("\n    private ", start + methodName.Length, StringComparison.Ordinal);
        if (end < 0) end = source.Length;
        return source.Substring(start, end - start);
    }

    /// <summary>
    /// 🔴 W15-a — <c>MigrateCoreAsync</c> 안에서 <c>RunTableStepAsync("hr_reports"</c> 가 <c>RunTableStepAsync("events"</c> <b>뒤</b>에 있고,
    /// 그 잡이 <c>MigrateDailyReportsAsync(</c> 를 부르며 <c>result.DailyReports</c> 에 담는다(작21 W8 이 순서를 재는 방식).
    /// <para>무력화: 잡 등록을 지우거나 <c>events</c> 앞으로 옮기면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W15a_hr_reports_잡은_events_잡_뒤에_등록된다()
    {
        var src = ServiceSource();
        var core = MethodBody(src, "MigrateCoreAsync");
        var events = core.IndexOf("RunTableStepAsync(\"events\"", StringComparison.Ordinal);
        var daily = core.IndexOf("RunTableStepAsync(\"hr_reports\"", StringComparison.Ordinal);
        Assert.True(events >= 0, "events 잡 등록이 있어야 한다(기준점)");
        Assert.True(daily >= 0, "hr_reports 잡 등록이 MigrateCoreAsync 에 있어야 한다");
        Assert.True(events < daily, "hr_reports 잡은 events 잡 뒤에 등록돼야 한다");

        var job = core.Substring(daily, Math.Min(600, core.Length - daily));
        Assert.Contains("result.DailyReports = await MigrateDailyReportsAsync(", job);
        Assert.Contains("mdbFile: \"POTHER\"", job);
    }

    /// <summary>
    /// 🔴 W15-b — <c>MigrateDailyReportsAsync</c> 본문이 순수함수를 <i>그 자리에서</i> 부르고(<c>Line</c>·<c>TryParseDate</c>·<c>SourceId</c>·<c>Title</c>·<c>OriginalAuthorLine</c>),
    /// <c>INSERT IGNORE INTO hr_reports</c> 로 넣으며, 미매칭은 <c>EnsureLegacyFallbackEmployeeAsync(</c> 로 간다.
    /// <para>무력화: 서비스가 줄 모양을 직접 문자열 보간으로 만들게 되돌리면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W15b_MigrateDailyReportsAsync_는_순수함수를_부르고_INSERT_IGNORE_한다()
    {
        var body = MethodBody(ServiceSource(), "MigrateDailyReportsAsync");
        Assert.Contains("FROM DOCME", body);
        Assert.Contains("LegacyDocmeMapping.TryParseDate(", body);
        Assert.Contains("LegacyDocmeMapping.ReportKey(", body);
        Assert.Contains("LegacyDocmeMapping.Line(", body);
        Assert.Contains("LegacyDocmeMapping.SourceId(", body);
        Assert.Contains("LegacyDocmeMapping.Title(", body);
        Assert.Contains("LegacyDocmeMapping.OriginalAuthorLine(", body);
        Assert.Contains("EnsureLegacyFallbackEmployeeAsync(", body);
        Assert.Contains("INSERT IGNORE INTO hr_reports", body);
        Assert.Contains("ComputeSourceHash(", body);
    }

    /// <summary>
    /// 🔴 W15-c — 결과: <c>MdbMigrationResult.DailyReports</c> 프로퍼티가 있고(리플렉션) <b>값으로</b> <c>Total</c> 에 합산되며 <c>ToString</c> 에 실린다.
    /// <c>MigrationJobResult.DailyReports</c>(잡 상태 DTO)도 있어야 화면까지 간다("고쳤다 ≠ 갔다").
    /// <para>무력화: <c>Total</c> 식에서 <c>+ DailyReports</c> 를 빼면 빨간불(글자가 아니라 7 ≠ 0 으로).</para>
    /// </summary>
    [Fact]
    public void W15c_결과_DailyReports_합산_및_잡DTO()
    {
        var prop = typeof(MdbMigrationResult).GetProperty("DailyReports", BindingFlags.Public | BindingFlags.Instance);
        Assert.True(prop is not null && prop.PropertyType == typeof(int), "MdbMigrationResult.DailyReports(int) 가 있어야 한다");

        var r = new MdbMigrationResult();
        prop!.SetValue(r, 7);
        Assert.Equal(7, r.Total);
        Assert.Contains("일일보고서:7", r.ToString());

        var jobProp = typeof(MigrationJobResult).GetProperty("DailyReports", BindingFlags.Public | BindingFlags.Instance);
        Assert.True(jobProp is not null && jobProp.PropertyType == typeof(int), "MigrationJobResult.DailyReports(int) 가 있어야 한다 — 없으면 화면에 안 간다");
    }

    /// <summary>
    /// 🔴 W15-d — DDL: <c>DB-119_hr_reports_source.sql</c> 이 있고 컬럼 3 + UNIQUE + 멱등 관용구를 담으며,
    /// 출하 DDL(<c>installer/hitpan_db_clean.sql</c>, 헌법 #36)의 <c>hr_reports</c> 에 같은 컬럼·UNIQUE 와 시드 <c>('DB-119','clean-ddl',1)</c> 이 있다(작21 W9 모양).
    /// <para>무력화: DB-119 를 지우거나 clean DDL 의 UNIQUE 를 빼거나 시드를 빼면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W15d_DB119_및_출하DDL_편입()
    {
        var ddl = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-119_hr_reports_source.sql");
        foreach (var col in new[] { "source_type", "source_id", "migrated_source_hash", "uq_hr_reports_source" })
            Assert.Contains(col, ddl);
        Assert.Contains("information_schema", ddl);   // 멱등 관용구
        Assert.Contains("table_name = 'hr_reports'", ddl);

        var clean = ReadSource("installer", "hitpan_db_clean.sql");
        var start = clean.IndexOf("CREATE TABLE `hr_reports`", StringComparison.Ordinal);
        Assert.True(start >= 0, "출하 DDL 에 hr_reports 가 있어야 한다");
        var table = clean.Substring(start, clean.IndexOf("ENGINE=", start, StringComparison.Ordinal) - start);
        Assert.Contains("`source_type` varchar(30)", table);
        Assert.Contains("`source_id` varchar(80)", table);
        Assert.Contains("`migrated_source_hash` char(64)", table);
        Assert.Contains("UNIQUE KEY `uq_hr_reports_source` (`tenant_id`,`source_type`,`source_id`)", table);
        Assert.Contains("('DB-119','clean-ddl',1)", clean);
    }
}
