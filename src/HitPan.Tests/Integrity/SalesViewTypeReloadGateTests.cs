using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작2) 판매관리 조회유형 재조회 게이트 — 6화면.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 실측(2026-08-25): <i>"견적현황·수주현황·판매현황·판매순위표·판매수익성분석·판매통계
/// = 모두 조회유형 동작이 이상함. 조회유형 콤보박스 동작시, 그리드 첫칸에 반영된 데이터가
/// 아닌 날짜로만 나옴."</i>
/// </para>
/// <para>
/// 🔴 <b>원인은 화면의 재조회 한 줄이 없던 것이다.</b>
/// 프론트 콤보값·전송·컨트롤러 수신·서비스 SQL 분기는 전부 정상이었다.
/// <c>@bind-Value="_viewType"</c> 만 걸려 있어 조회는 「조회」 버튼과 최초 진입에서만 돌았다.
/// 그래서 헤더(<c>GetLabelHeader()</c>)는 <c>_viewType</c> 을 직접 읽어 즉시 바뀌는데
/// 그리드(<c>_reportData</c>)는 최초 로드된 기간별(날짜) 그대로 남아 있었다.
/// </para>
/// <para>
/// ⚠️ <b>이 게이트의 한계를 분명히 한다.</b> Blazor 콤보 클릭을 단위시험에서 재현할 수 없어
/// 이 시험은 <b>배선이 끊겼는지</b>를 본다. 봉합(<c>ValueChanged</c> 연결 · 핸들러의
/// <c>SearchAsync()</c> 호출)을 지우면 반드시 FAIL 한다.
/// <b>최종 판정은 사장님 화면 실측이다</b> — 개발PC 통과는 검증이 아니다.
/// </para>
/// </remarks>
public class SalesViewTypeReloadGateTests
{
    /// <summary>조회유형 콤보를 가진 판매관리 6화면.</summary>
    public static TheoryData<string> Pages => new()
    {
        "QuotationStatusPage.razor",
        "SalesOrderStatusPage.razor",
        "SalesSummaryPage.razor",
        "SalesRankingPage.razor",
        "SalesProfitabilityPage.razor",
        "SalesStatisticsPage.razor",
    };

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

    private static string ReadPage(string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "HitPan.Web", "Pages", "Sales", fileName);
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
                       && !t.StartsWith("@*", StringComparison.Ordinal)
                       && !t.StartsWith("///", StringComparison.Ordinal);
            }));

    // ───────────────────────────────────────────────────────────────
    // 🔴 사고 — 콤보를 바꿔도 서버를 다시 부르지 않았다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>조회유형 콤보가 재조회에 연결돼 있는가.</b>
    /// <c>@bind-Value</c> 만 쓰면 값만 바뀌고 조회가 안 돈다 — 그게 이 사고였다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pages))]
    public void 조회유형_콤보는_재조회_핸들러에_연결돼야_한다(string page)
    {
        var code = CodeLines(ReadPage(page));

        Assert.False(code.Contains("@bind-Value=\"_viewType\"", StringComparison.Ordinal),
            $"{page}: 조회유형에 @bind-Value 를 쓰면 값만 바뀌고 재조회가 안 돈다. " +
            "Value + ValueChanged 로 연결해야 한다.");

        Assert.Contains("ValueChanged=\"OnViewTypeChangedAsync\"", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>핸들러가 실제로 재조회를 부르는가.</b>
    /// 핸들러만 있고 <c>SearchAsync()</c> 를 안 부르면 껍데기다 — 가짜 봉합 차단.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pages))]
    public void 조회유형_핸들러는_SearchAsync_를_불러야_한다(string page)
    {
        var code = CodeLines(ReadPage(page));

        var at = code.IndexOf("private async Task OnViewTypeChangedAsync", StringComparison.Ordinal);
        Assert.True(at >= 0, $"{page}: OnViewTypeChangedAsync 핸들러가 있어야 한다");

        var close = code.IndexOf("\n    }", at, StringComparison.Ordinal);
        Assert.True(close > at, $"{page}: OnViewTypeChangedAsync 본문을 찾아야 한다");
        var body = code[at..close];

        Assert.Contains("_viewType = value", body, StringComparison.Ordinal);
        Assert.Contains("SearchAsync()", body);
    }

    /// <summary>
    /// 🔴 <b>조회유형이 바뀌면 검색어를 비우는가.</b>
    /// 품목별에서 친 품명이 업체별로 바꿔도 남으면 엉뚱한 필터가 되어 <b>0건</b>이 된다.
    /// 재조회만 붙이면 이 문제가 오히려 드러나므로 함께 지킨다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pages))]
    public void 조회유형이_바뀌면_검색어를_비워야_한다(string page)
    {
        var code = CodeLines(ReadPage(page));

        var at = code.IndexOf("private async Task OnViewTypeChangedAsync", StringComparison.Ordinal);
        Assert.True(at >= 0, $"{page}: OnViewTypeChangedAsync 핸들러가 있어야 한다");

        var close = code.IndexOf("\n    }", at, StringComparison.Ordinal);
        var body = code[at..close];

        Assert.Contains("_searchText = \"\"", body, StringComparison.Ordinal);
    }
}
