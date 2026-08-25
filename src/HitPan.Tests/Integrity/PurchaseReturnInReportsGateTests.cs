using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작17) 매입 리포트 반품 반영 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 사장님 지시(2026-08-25): <i>"반품은 조회전용이 아니라 워크플로우의 한 축이지. 따라서
/// 매입목록이나, 매입관련 현황·분석·통계자료·순위표 에도 반영되야 하고, 회계·재고에도 자동반영되야 함."</i>
/// <i>"워크플로우 흐름에 따라 데이터 재고·금액의 정합성은 무조건 맞아야됨."</i>
/// </para>
/// <para>
/// 🔴 <b>왜 유형 추가가 아니라 SQL 자체를 바꿨나.</b> 사장님 원문:
/// <i>"불량을 매입했다고 쳐도, 불량을 매출로 잡지 않고, 매입 수량에 딱 떨어지게 매출을 잡고,
/// 창고에 재고를 0으로 맞추고 판매를 하는게 아니잖아."</i> ·
/// <i>"불량100개가 들어왓다고 해도, 100개를 반품하고 재주문 하겠지."</i>
/// ⇒ 불량 100 매입 → 100 반품 → 100 재주문이면 <b>실제로 산 건 100개</b>다.
/// 반품을 안 빼면 <b>200개 산 것처럼</b> 보인다.
/// </para>
/// <para>
/// ⚠️ 나는 처음에 판매쪽이 「판매종합현황(반품포함)」을 별도 유형으로 뒀다는 이유로
/// 매입도 유형 추가로 가려 했다. 사장님이 바로잡으셨다 — <b>"매입을 매출에 맞춰서 하지 않잖아."</b>
/// <b>매입은 매입 논리로 판단한다.</b>
/// </para>
/// <para>
/// ⚠️ <b>실측 근거</b> — 이 봉합은 로컬 MariaDB 에 사장님 시나리오를 넣고 20종을 전부 실행해
/// 확인했다(매입현황 200개/20만 → 100개/10만, 부가세 매입세액 2만 → 1만,
/// 미지급금 22만 → 11만, YoY 전년 30만 → 20만).
/// 이 시험은 <b>배선이 되돌려지는 것</b>을 막는다. <b>최종 판정은 사장님 실측이다.</b>
/// </para>
/// </remarks>
public class PurchaseReturnInReportsGateTests
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

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"{path} 가 있어야 한다");
        return File.ReadAllText(path);
    }

    private static string ReportService() =>
        Read("src", "HitPan.Application", "Services", "ReportService.cs");

    /// <summary>
    /// <paramref name="name"/> SQL 상수의 본문만 잘라낸다.
    /// 🔴 <b>구간을 자르는 이유</b> — 파일 전체에서 문자열을 세면
    /// 다른 상수의 낱말이 거짓 초록불을 만든다(판매 SQL 에도 return 이 나온다).
    /// </summary>
    private static string SqlBody(string source, string name)
    {
        var marker = $"private const string {name} = \"\"\"";
        var at = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{name} 상수가 있어야 한다");

        var end = source.IndexOf("\"\"\";", at + marker.Length, StringComparison.Ordinal);
        Assert.True(end > at, $"{name} 본문 끝을 찾아야 한다");

        return source[(at + marker.Length)..end];
    }

    /// <summary>반품이 반영돼야 하는 매입 리포트 SQL — 현황 5 + 순위 5 + 통계 8 = 18종.</summary>
    public static TheoryData<string> ReturnAwareSql => new()
    {
        // 매입현황 (PR_PRICE 는 단가 최저/최고/평균이라 반품 개념이 안 맞아 제외)
        "PR_BY_PERIOD", "PR_BY_PARTNER", "PR_BY_ITEM", "PR_MONTHLY", "PR_PARTNER_YEARLY",
        // 매입순위표
        "PRR_BY_PARTNER", "PRR_BY_ITEM", "PRR_BY_PERIOD", "PRR_BY_REGION", "PRR_BY_EMPLOYEE",
        // 매입통계 — 월별 4
        "PRSTATS_ITEM_MONTHLY", "PRSTATS_PARTNER_MONTHLY",
        "PRSTATS_EMPLOYEE_MONTHLY", "PRSTATS_REGION_MONTHLY",
        // 매입통계 — 전년동기 4
        "PRSTATS_YOY_ITEM", "PRSTATS_YOY_PARTNER",
        "PRSTATS_YOY_EMPLOYEE", "PRSTATS_YOY_REGION",
    };

    /// <summary>
    /// 🔴 <b>매입 리포트가 반품을 빼야 한다.</b>
    /// 반품 테이블을 아예 안 보면 <b>반품을 전액 되돌린 업체도 1위로 남는다.</b>
    /// </summary>
    [Theory]
    [MemberData(nameof(ReturnAwareSql))]
    public void 매입_리포트가_반품을_차감해야_한다(string name)
    {
        var sql = SqlBody(ReportService(), name);

        Assert.Contains("purchase_returns", sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>확정한 반품만 빼야 한다</b>(헌법 #6 — 원장은 confirmed 시점에만).
    /// 임시저장 반품이 매입액을 깎으면 <b>확정도 안 한 일로 숫자가 줄어든다.</b>
    /// 지운 반품(<c>is_deleted=1</c>)도 마찬가지다.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReturnAwareSql))]
    public void 확정된_반품만_차감해야_한다(string name)
    {
        var sql = SqlBody(ReportService(), name);

        Assert.Contains("rt.status = 'confirmed'", sql, StringComparison.Ordinal);
        Assert.Contains("rt.is_deleted = 0", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>반품 쪽 날짜 축은 <c>return_date</c> 여야 한다.</b>
    /// 매입은 <c>receipt_date</c>, 반품은 <c>return_date</c> 다.
    /// 축을 섞으면 <b>4월에 사서 5월에 반품한 건이 엉뚱한 달에서 빠진다.</b>
    /// </summary>
    [Theory]
    [MemberData(nameof(ReturnAwareSql))]
    public void 반품_날짜축은_return_date_여야_한다(string name)
    {
        var sql = SqlBody(ReportService(), name);

        Assert.Contains("rt.return_date", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>반품 브랜치의 <u>모든</u> 집계 컬럼이 음수여야 한다.</b>
    /// 부호를 빠뜨리면 반품이 <b>매입을 더 늘린다</b> — 정반대가 된다.
    /// <para>
    /// ⚠️ <b>반증에서 배웠다</b> — 처음엔 "마이너스가 하나라도 있으면 통과" 로 짰다.
    /// 그런데 <c>rti.qty</c> 하나의 부호만 떼도 <b>다른 컬럼에 마이너스가 남아 초록불이 나왔다.</b>
    /// 낱말 하나로 검사하면 안 된다 — <b>집계 컬럼 수만큼 부호가 있는지 센다.</b>
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ReturnAwareSql))]
    public void 반품_금액은_음수로_합산돼야_한다(string name)
    {
        var sql = SqlBody(ReportService(), name);

        var at = sql.IndexOf("UNION ALL", StringComparison.Ordinal);
        Assert.True(at > 0, $"{name}: UNION ALL 이 있어야 한다");

        var returnBranch = sql[at..];

        // 반품 브랜치의 집계 함수 호출 수와, 그중 앞에 마이너스가 붙은 수를 각각 센다.
        var aggregates = Regex.Matches(returnBranch, @"(?<!-)\s(COALESCE\(SUM\(|COUNT\()").Count;
        var negated = Regex.Matches(returnBranch, @"-\s*(COALESCE\(SUM\(|COUNT\()").Count;

        Assert.True(negated > 0, $"{name}: 반품 브랜치에 음수 합산이 없다.");
        Assert.True(aggregates == 0,
            $"{name}: 반품 브랜치에 부호 없는 집계가 {aggregates}개 남아 있다(음수 {negated}개). " +
            "하나라도 양수면 그 컬럼만 반품이 매입을 늘린다.");
    }

    /// <summary>
    /// 🔴 <b>전년동기 대비는 당해·전년 <u>양쪽</u> 반품을 빼야 한다.</b>
    /// 한쪽만 빼면 <b>증감률이 거짓</b>이 된다 — 전년이 부풀려져 실제보다 많이 줄어든 것처럼 보인다.
    /// ⚠️ 실측으로 확인했다: 전년 매입 30만 · 반품 10만 ⇒ 전년 <b>20만</b> 으로 나온다(30만 아님).
    /// </summary>
    [Theory]
    [InlineData("PRSTATS_YOY_ITEM")]
    [InlineData("PRSTATS_YOY_PARTNER")]
    [InlineData("PRSTATS_YOY_EMPLOYEE")]
    [InlineData("PRSTATS_YOY_REGION")]
    public void 전년동기대비는_전년_반품도_빼야_한다(string name)
    {
        var sql = SqlBody(ReportService(), name);

        var at = sql.IndexOf("UNION ALL", StringComparison.Ordinal);
        Assert.True(at > 0, $"{name}: UNION ALL 이 있어야 한다");
        var returnBranch = sql[at..];

        // 반품 브랜치가 전년 구간(@FromPrev~@ToPrev)도 집계하는가.
        Assert.Contains("@FromPrev", returnBranch, StringComparison.Ordinal);
        Assert.Contains("@ToPrev", returnBranch, StringComparison.Ordinal);

        // WHERE 가 두 구간을 다 받는가 — 당해만 받으면 전년 CASE 가 영영 0 이다.
        Assert.Contains("OR rt.return_date BETWEEN", returnBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>전량 반품된 건이 목록에서 사라지면 안 된다.</b>
    /// 순액도 0, 건수도 0(매입 1 + 반품 −1)이 되어 <c>HAVING</c> 을 못 넘고 조용히 없어졌다.
    /// <para>
    /// 사장님 헌법: <i>"목록에서 숨기면 사라지는 게 아니라 안 보이는 채로 쌓인다."</i>
    /// <b>"샀다가 다 물렸다"는 것도 사실이고, 0원으로 보이는 게 맞다.</b>
    /// </para>
    /// ⚠️ 실측으로 확인했다 — 고치기 전엔 전량반품 품목이 화면에서 통째로 빠졌다.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReturnAwareSql))]
    public void 전량반품_건이_HAVING_에_걸려_사라지면_안_된다(string name)
    {
        var sql = SqlBody(ReportService(), name);

        var having = sql.IndexOf("HAVING", StringComparison.Ordinal);
        if (having < 0) return;   // HAVING 이 없으면 애초에 거를 일이 없다.

        var line = sql[having..];
        var eol = line.IndexOf("ORDER BY", StringComparison.Ordinal);
        if (eol > 0) line = line[..eol];

        Assert.True(line.Contains("ABS(", StringComparison.Ordinal),
            $"{name}: HAVING 이 절대값 기준이어야 한다. " +
            "순액/건수가 상쇄돼 0 이 되면 전량 반품된 건이 목록에서 통째로 사라진다.");
    }

    /// <summary>
    /// ⚠️ <b>매입단가 변동현황은 반품을 빼지 않는다 — 일부러 그렇다.</b>
    /// 최저·최고·평균 <b>단가</b>라 "반품분을 뺀 단가" 라는 게 성립하지 않는다.
    /// 나중에 누가 일관성을 이유로 여기까지 손대는 것을 막는다.
    /// </summary>
    [Fact]
    public void 매입단가_변동현황은_반품을_빼지_않는다()
    {
        var sql = SqlBody(ReportService(), "PR_PRICE");

        Assert.DoesNotContain("purchase_returns", sql, StringComparison.Ordinal);
        Assert.Contains("MIN(pri.unit_price)", sql, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────────────────────────
    // 회계·자금 — 법령이 걸린 자리
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>부가세 신고에서 반품분 매입세액이 빠져야 한다.</b>
    /// 안 빼면 <b>안 산 물건의 매입세액을 국세청에 공제 신청</b>하게 된다.
    /// ⚠️ 실측: 불량 100 매입 → 100 반품 → 100 재주문에서 매입세액 2만 → <b>1만</b>.
    /// </summary>
    [Fact]
    public void 부가세_신고가_반품분_매입세액을_빼야_한다()
    {
        var fin = Read("src", "HitPan.Application", "Services", "FinanceService.cs");

        var at = fin.IndexOf("GetVatSummaryAsync", StringComparison.Ordinal);
        Assert.True(at >= 0, "GetVatSummaryAsync 가 있어야 한다");

        var body = fin[at..Math.Min(fin.Length, at + 2600)];
        Assert.Contains("purchase_returns", body, StringComparison.Ordinal);
        Assert.Contains("rt.status = 'confirmed'", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>미지급금에서 반품분이 빠져야 한다.</b> 돌려준 물건 값은 <b>줄 돈이 아니다.</b>
    /// ⚠️ 같은 병을 2026-06-23 에 지급(payments) 축에서 이미 겪었다
    /// (<i>"미지급이 지급해도 안 줄어 영구 과대 계상"</i>). 이번엔 반품 축에서 재발한 것이다.
    /// </summary>
    [Fact]
    public void 미지급금이_반품분을_빼야_한다()
    {
        var fin = Read("src", "HitPan.Application", "Services", "FinanceService.cs");

        var at = fin.IndexOf("SELECT 'payable'", StringComparison.Ordinal);
        Assert.True(at >= 0, "payable KPI 가 있어야 한다");

        var body = fin[at..Math.Min(fin.Length, at + 1200)];
        Assert.Contains("purchase_returns", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>미지급 aging 도 전표별로 반품분을 빼야 한다.</b>
    /// 안 빼면 <b>돌려준 물건 값이 계속 미지급으로 늙어간다</b>(90일 초과로 넘어간다).
    /// </summary>
    [Fact]
    public void 미지급_aging_이_반품분을_빼야_한다()
    {
        var col = Read("src", "HitPan.Application", "Services", "CollectionService.cs");

        Assert.Contains("purchase_returns", col, StringComparison.Ordinal);
        Assert.Contains("IFNULL(ret.returned, 0)", col, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>매입매출장에는 반품이 <u>행으로</u> 떠야 한다.</b>
    /// 여기는 집계가 아니라 <b>전표 목록</b>이다. 빼서 없애면
    /// <b>"매입은 있는데 돌려준 기록이 없는" 장부</b>가 되어 세무사·거래처가 대사할 수 없다.
    /// </summary>
    [Fact]
    public void 매입매출장에_반품이_행으로_떠야_한다()
    {
        var fin = Read("src", "HitPan.Application", "Services", "FinanceService.cs");

        Assert.Contains("'매입반품' AS DocType", fin, StringComparison.Ordinal);
        Assert.Contains("returnSql", fin, StringComparison.Ordinal);
    }
}
