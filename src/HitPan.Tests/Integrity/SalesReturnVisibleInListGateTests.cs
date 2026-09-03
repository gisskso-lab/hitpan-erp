using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>마이너스 전표가 목록에 「보이는가」 게이트 — 20260903작16</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>사장님 실측(9/3)</b>: <i>"매출반품 명세서가 판매목록조회에 안뜸"</i> → <i>"전표를 못찾겠음"</i>
/// → <i>"매출반품메뉴를 없애고, 마이너스 전표(반품)과 그 마이너스 전표와 연결되는
/// 원 전표의 사슬을 <b>가독성 있게 붙여 놓으라</b>고 했잖아."</i>
/// </para>
///
/// <para>
/// 🔴 <b>작15 가 반만 했다.</b> 저장은 <c>sales_returns</c> 로 정확히 갔는데
/// <b>목록은 <c>sales_deliveries</c> 만 읽었다.</b> 매출반품 화면은 8/25 결재로
/// <c>@page</c> 가 주석 처리돼 있어 <b>열 문이 하나도 없었다</b> — 저장은 되는데 아무 데서도 안 보였다.
/// </para>
///
/// <para>
/// 🔴 <b>작15 게이트 8개가 전부 「저장 경로」만 쟀다.</b> 그래서 9건 다 통과하고도 반려가 났다.
/// [[project_fixed_vs_delivered_gap]] <b>9차</b> — 질문은 언제나 <i>"고쳤나"</i> 가 아니라 <b>"보이나"</b>.
/// ⇒ 이 게이트는 <b>목록이 반품을 실제로 읽어서 원전표에 매다는가</b> 를 잰다.
/// </para>
/// </remarks>
public sealed class SalesReturnVisibleInListGateTests
{
    private static string Dialog =>
        Path.Combine(RepoRoot(), "src", "HitPan.Web", "Components", "Sales", "SalesListDialog.razor");

    private static string SalesServiceCs =>
        Path.Combine(RepoRoot(), "src", "HitPan.Application", "Services", "SalesService.cs");

    // ─────────────────────────────────────────────────────────────────────
    // G-1 🔴 목록이 반품을 **읽는다**  ← 사장님이 겪은 그 자리
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G1_판매목록이_반품을_읽는다()
    {
        var src = ReadStripped(Dialog);

        Assert.True(
            LiveCallCount(src, "GetSalesReturnListAsync") > 0,
            "판매 목록이 매출반품을 읽지 않는다. sales_deliveries 만 읽으면 "
            + "반품은 다른 표에 있으므로 **아무 데도 안 보인다**(사장님 실측 9/3).");
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-2 🔴 사슬 — 반품이 **원전표 아래에** 붙는다 (결재: 가독성 있게)
    //
    //   단순히 목록에 섞어 날짜순으로 뿌리면 사슬이 안 보인다.
    //   원전표 id 로 묶어 그 바로 다음 줄에 끼워 넣어야 한다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G2_반품이_원전표_아래에_붙는다()
    {
        var src = ReadStripped(Dialog);

        Assert.True(
            Regex.IsMatch(src, @"SourceDeliveryId"),
            "반품이 어느 원전표에 매달리는지(SourceDeliveryId)가 없다. 사슬을 만들 수 없다.");

        // 원전표 id 로 묶어서(GroupBy) 그 아래에 끼워 넣는(AddRange) 구조인가
        Assert.True(
            Regex.IsMatch(src, @"GroupBy\s*\(") && Regex.IsMatch(src, @"AddRange\s*\("),
            "반품을 원전표별로 묶어 그 아래에 끼워 넣는 코드가 없다. "
            + "날짜순으로 섞어 뿌리면 어느 판매의 반품인지 눈으로 못 읽는다(결재: 가독성).");
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-3 🔴 서버가 사슬 링크를 준다 — 목록 SQL 에 delivery_id 가 실려야 한다
    //
    //   상세는 진작 주고 있었는데 **목록만 빠져 있었다.**
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G3_반품목록_SQL이_원전표링크를_싣는다()
    {
        var src = ReadStripped(SalesServiceCs);

        var m = Regex.Match(
            src,
            @"GetSalesReturnsAsync.*?FROM\s+sales_returns",
            RegexOptions.Singleline);

        Assert.True(m.Success, "GetSalesReturnsAsync 의 목록 SQL 을 찾지 못했다.");

        Assert.True(
            Regex.IsMatch(m.Value, @"sr\.delivery_id\s+AS\s+DeliveryId"),
            "매출반품 목록 SQL 이 원 거래명세서 링크(sr.delivery_id)를 안 싣는다. "
            + "화면이 사슬을 만들 재료를 못 받는다 — 상세는 주는데 목록만 빠져 있던 자리다.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-4 🔴 반품 줄은 (−) 로 보인다 (마이너스 전표 = 사장님 어휘)
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G4_반품줄은_마이너스로_보인다()
    {
        var src = ReadStripped(Dialog);

        Assert.True(
            Regex.IsMatch(src, @"TotalAmount\s*=\s*-\s*Math\.Abs\s*\("),
            "반품 줄 금액이 (−)가 아니다. 사장님이 「마이너스 전표」라 부르는 그 표기다 — "
            + "양수로 보이면 판매와 구분이 안 된다.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-5 🔴 반품 줄에 판매 동작이 걸리면 안 된다
    //
    //   반품이 confirmed 면 [계산서 발행]·[판매확정] 이 켜져서
    //   **반품 건에 판매 계산서를 끊게 된다.**
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G5_반품줄에는_판매동작이_안걸린다()
    {
        var src = ReadStripped(Dialog);

        // 체크 대상을 고르는 조건들이 모두 반품을 빼고 있는가
        var pickers = Regex.Matches(src, @"x\.IsChecked\s*&&[^;\r\n]{0,120}")
                           .Select(m => m.Value)
                           .ToList();

        Assert.True(pickers.Count > 0, "체크 대상 판정식을 찾지 못했다.");

        var leaky = pickers.Where(p => !p.Contains("!x.IsReturn")).ToList();

        Assert.True(
            leaky.Count == 0,
            "반품 줄을 빼지 않는 판정식이 있다 — 반품에 판매확정·계산서발행이 걸린다:\n  "
            + string.Join("\n  ", leaky));
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-6 🔴 반품 줄 상태는 반품 어휘로 — 「판매완료」가 뜨면 정반대로 읽힌다
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G6_반품줄_상태는_반품어휘다()
    {
        var src = ReadStripped(Dialog);

        Assert.True(
            Regex.IsMatch(src, @"반품완료") && Regex.IsMatch(src, @"반품확정"),
            "반품 줄에 반품 어휘(반품완료·반품확정)가 없다. "
            + "판매와 같은 사전을 쓰면 반품에 「판매완료」가 떠서 **정반대로 읽힌다**.");

        // 철자 함정 — sales_returns 는 canceled(l 하나), sales_deliveries 는 cancelled(l 둘)
        Assert.True(
            Regex.IsMatch(src, @"""canceled""") && Regex.IsMatch(src, @"""cancelled"""),
            "취소 철자 두 가지를 다 받지 않는다. sales_returns 는 canceled(l 하나), "
            + "sales_deliveries 는 cancelled(l 둘) — 한쪽만 받으면 취소 반품이 원문 그대로 뜬다.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 도우미
    // ─────────────────────────────────────────────────────────────────────

    private static string ReadStripped(string path)
    {
        Assert.True(File.Exists(path), $"파일이 없다: {path}");
        var src = File.ReadAllText(path);

        src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        src = Regex.Replace(src, @"^\s*//.*?$", " ", RegexOptions.Multiline);
        src = Regex.Replace(src, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        return src;
    }

    /// <summary>정의·주석을 뺀 <b>살아있는 호출</b> 수.</summary>
    private static int LiveCallCount(string strippedSrc, string name)
    {
        var calls = Regex.Matches(strippedSrc, Regex.Escape(name) + @"\s*\(").Count;
        var defs = Regex.Matches(
            strippedSrc,
            @"(?:private|public|internal|protected|async|Task|void)[^\n;{]{0,80}?" + Regex.Escape(name) + @"\s*\(").Count;
        return calls - defs;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
