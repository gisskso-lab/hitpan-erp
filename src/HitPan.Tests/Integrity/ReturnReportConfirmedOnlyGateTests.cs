using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작3) 반품현황 확정분만 집계 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 지시(2026-08-25): <i>"통계, 현황, 분석, 순위표 등 데이터 분석은 하나도 안되고 있어."</i>
/// 그 원인 중 하나가 <b>반품 집계가 틀린 것</b>이었다.
/// </para>
/// <para>
/// 🔴 <b>실측으로 확인한 두 가지 사고.</b>
/// </para>
/// <list type="number">
/// <item>
/// <b>매입반품 — 필터가 헛돌았다.</b> 실제 기록값은 <c>canceled</c>(L하나)인데
/// (<c>PurchaseService</c> 취소 UPDATE) 리포트는 <c>status &lt;&gt; 'cancelled'</c>(L둘)로
/// 걸렀다. 두 문자열은 절대 같지 않으므로 조건이 <b>항상 참</b>이 되어
/// 취소분이 100% 집계에 남았다.
/// </item>
/// <item>
/// <b>매출반품 — 상태 조건이 아예 없었다.</b> 특히 판매종합현황(반품포함)에서
/// 취소·미확정 반품이 <b>음수로 매출에서 계속 차감</b>되어 매출이 과소 집계됐다.
/// </item>
/// </list>
/// <para>
/// 🔴 <b>봉합 방식 — 부정(<c>&lt;&gt;</c>)이 아니라 양성(<c>=</c>) 비교.</b>
/// 사장님 결재(2026-08-25): 철자는 건드리지 않고 리포트만 고친다.
/// <c>status = 'confirmed'</c> 로 쓰면 <c>canceled</c>/<c>cancelled</c> 혼재와 무관하고
/// draft(미확정) 도 함께 빠진다. 재고·회계가 confirmed 에만 반응하므로
/// 현황 숫자도 같은 잣대여야 한다(헌법 #6).
/// </para>
/// <para>
/// ⚠️ 이 게이트는 SQL 상수의 <b>WHERE 절 구성</b>을 본다. 봉합을 되돌리면 반드시 FAIL 한다.
/// <b>최종 판정은 사장님 화면 실측</b> — 취소한 반품이 현황에서 빠지는지 눈으로 확인해야 한다.
/// </para>
/// </remarks>
public class ReturnReportConfirmedOnlyGateTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadReportService()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "HitPan.Application", "Services", "ReportService.cs");
        Assert.True(File.Exists(path), $"{path} 가 있어야 한다");
        return File.ReadAllText(path);
    }

    /// <summary>주석 줄을 걸러낸 실제 코드만 남긴다(거짓 경보 방지).</summary>
    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return t.Length > 0
                       && !t.StartsWith("//", StringComparison.Ordinal)
                       && !t.StartsWith("*", StringComparison.Ordinal)
                       && !t.StartsWith("/*", StringComparison.Ordinal)
                       && !t.StartsWith("///", StringComparison.Ordinal);
            }));

    /// <summary>반품 테이블을 조회하는 WHERE 줄만 뽑는다.</summary>
    private static List<string> ReturnWhereLines(string alias) =>
        CodeLines(ReadReportService()).Split('\n')
            .Where(l => l.Contains("WHERE", StringComparison.Ordinal)
                        && l.Contains($"{alias}.tenant_id", StringComparison.Ordinal))
            .ToList();

    // ───────────────────────────────────────────────────────────────
    // 🔴 사고 ① — 매입반품: 철자가 어긋나 필터가 헛돌았다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>매입반품 4종이 확정분만 집계하는가.</b>
    /// 종전 <c>&lt;&gt; 'cancelled'</c> 는 실제 기록값 <c>canceled</c> 와 절대 안 맞아 무의미했다.
    /// </summary>
    [Fact]
    public void 매입반품현황은_확정분만_집계해야_한다()
    {
        var wheres = ReturnWhereLines("rt");

        Assert.True(wheres.Count >= 4,
            $"매입반품 SQL 4종의 WHERE 를 찾아야 한다 (찾은 수: {wheres.Count})");

        foreach (var w in wheres)
        {
            Assert.Contains("rt.status = 'confirmed'", w, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 🔴 <b>헛도는 부정 비교로 되돌아가지 않았는가.</b>
    /// 철자 혼재 상황에서 <c>&lt;&gt;</c> 비교는 조용히 항상 참이 된다 — 재발 차단.
    /// </summary>
    [Fact]
    public void 반품집계는_철자에_의존하는_부정비교를_쓰면_안_된다()
    {
        foreach (var alias in new[] { "rt", "sr" })
        {
            foreach (var w in ReturnWhereLines(alias))
            {
                Assert.False(w.Contains($"{alias}.status <>", StringComparison.Ordinal),
                    $"{alias}: 반품 집계에 부정(<>) 비교를 쓰면 철자 혼재(canceled/cancelled)에 " +
                    "조용히 무력화된다. status = 'confirmed' 양성 비교를 써야 한다.");
            }
        }
    }

    // ───────────────────────────────────────────────────────────────
    // 🔴 사고 ② — 매출반품: 상태 조건이 아예 없었다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>매출반품이 확정분만 집계하는가.</b>
    /// 매출반품현황과 판매종합현황(반품포함) 두 곳 모두 조건이 없었다.
    /// 특히 종합현황에서는 취소분이 <b>음수로 매출에서 차감</b>됐다.
    /// </summary>
    [Fact]
    public void 매출반품집계는_확정분만_집계해야_한다()
    {
        var wheres = ReturnWhereLines("sr");

        Assert.True(wheres.Count >= 2,
            $"매출반품 SQL 2종(매출반품현황·판매종합현황)의 WHERE 를 찾아야 한다 (찾은 수: {wheres.Count})");

        foreach (var w in wheres)
        {
            Assert.Contains("sr.status = 'confirmed'", w, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 🔴 <b>판매종합현황의 매출 쪽은 건드리지 않았는가.</b>
    /// <c>sales_deliveries</c> 는 기록·조회가 <c>cancelled</c> 로 정상 매칭된다.
    /// 반품만 고치라는 결재였으므로 멀쩡한 필터를 깨뜨리지 않았음을 지킨다.
    /// </summary>
    [Fact]
    public void 판매종합현황의_매출측_필터는_그대로여야_한다()
    {
        var code = CodeLines(ReadReportService());

        Assert.Contains("sd.status <> 'cancelled'", code, StringComparison.Ordinal);
    }
}
