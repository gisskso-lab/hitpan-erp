using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-49 ~ G-55</b> — 단가 참고값이 <b>6화면 전부</b>에 배선돼 있다 (20260820작4 · 설계2 C안).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 확정 (2026-08-20 · C안)</b>:
/// <i>"단가는 모든 워크플로우 명세서 작성시(발주,판매,반품,견적,수주,판매) 직접 작성이 가능하되,
/// 마우스 커서 갖다대면 업체특별단가·최종단가·표준단가·혹은 상품특별단가를 볼 수 있도록"</i>
/// + <i>"업체 특별단가 우선"</i>(= 자동 채움).
/// </para>
///
/// <para>
/// 🔴 <b>이 시험이 지키는 것은 "한 곳만 하고 됐다 하지 않는 것"</b>이다(설계2 §6 G-3).
/// 6화면은 <b>서로 다른 그리드 6개</b>라 한 곳을 고쳐도 나머지 다섯은 그대로다.
/// 사람이 눈으로 세면 반드시 빠진다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 브라우저에서 말풍선이 실제로 뜨는지는 검사하지 못한다.
/// 그것은 실측(설계2 §6 G-2)의 몫이다. 여기 초록불은 <b>"배선이 빠진 화면은 없다"</b> 까지다.
/// 🔴 이걸 <i>"특별단가 연결 끝났다"</i> 로 적지 마라 — 고쳤다와 갔다는 다르다.
/// </para>
/// </remarks>
public sealed class PriceHintWiringGateTests
{
    /// <summary>사장님이 부르신 6화면 — 발주·매입·반품·견적·수주·판매.</summary>
    private static readonly (string Path, bool IsPurchase)[] Grids =
    {
        ("src/HitPan.Web/Components/Purchase/PurchaseOrderGrid.razor",   true),   // 발주
        ("src/HitPan.Web/Components/Purchase/PurchaseReceiptGrid.razor", true),   // 매입
        ("src/HitPan.Web/Components/Purchase/PurchaseReturnGrid.razor",  true),   // 반품
        ("src/HitPan.Web/Components/Sales/QuotationGrid.razor",          false),  // 견적
        ("src/HitPan.Web/Components/Sales/SalesOrderGrid.razor",         false),  // 수주
        ("src/HitPan.Web/Components/Sales/DeliveryGrid.razor",           false),  // 판매
    };

    private static string RepoRoot()
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

    private static string Read(string relative)
    {
        var path = Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>주석을 걷어낸 코드만 남긴다 — 설명문에 걸려 헛통과하지 않게.</summary>
    private static string CodeOnly(string src)
    {
        var noRazorComment = Regex.Replace(src, @"@\*.*?\*@", "", RegexOptions.Singleline);
        return string.Join('\n', noRazorComment
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal)
                     && !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    /// <summary>
    /// 🔴 <b>G-49 — 수문장.</b> 6화면 <b>전부</b>가 단가칸을 <c>PriceHintCell</c> 로 쓴다.
    /// 하나라도 빠지면 그 화면만 말풍선이 안 뜬다.
    /// </summary>
    [Fact]
    public void G49_여섯화면_전부_단가칸이_PriceHintCell_이다()
    {
        foreach (var (path, _) in Grids)
        {
            var code = CodeOnly(Read(path));
            Assert.True(code.Contains("<PriceHintCell", StringComparison.Ordinal),
                $"{path} 의 단가칸이 PriceHintCell 이 아니다 — 이 화면만 말풍선이 안 뜬다.");
        }
    }

    /// <summary>
    /// 🔴 G-50 — 단가칸에 옛 <c>MudNumericField</c> 가 <b>남아 있지 않다.</b>
    /// (바꾼 줄 옆에 옛 줄이 남으면 화면에 칸이 둘 뜬다)
    /// </summary>
    [Fact]
    public void G50_단가칸에_옛_MudNumericField_가_남아있지_않다()
    {
        foreach (var (path, _) in Grids)
        {
            var code = CodeOnly(Read(path));
            Assert.False(
                Regex.IsMatch(code, @"MudNumericField[^>]*Value=""context\.UnitPrice"""),
                $"{path} 에 옛 단가칸이 남아 있다.");
        }
    }

    /// <summary>
    /// 🔴 <b>G-51 — 판 값과 산 값이 섞이지 않는다.</b>
    /// 매입계열(발주·매입·반품)은 <c>IsPurchase="true"</c>, 판매계열(견적·수주·판매)은 <c>false</c>.
    /// ⚠️ 섞이면 <b>매입 화면에 판 가격이 뜬다</b>(설계2 §4-6).
    /// </summary>
    [Fact]
    public void G51_매입_판매_최종단가_출처가_갈린다()
    {
        foreach (var (path, isPurchase) in Grids)
        {
            var code = CodeOnly(Read(path));
            var expected = isPurchase ? "IsPurchase=\"true\"" : "IsPurchase=\"false\"";
            Assert.True(code.Contains(expected, StringComparison.Ordinal),
                $"{path} 는 {expected} 여야 한다 — 판 값과 산 값은 다른 금액이다.");
        }
    }

    /// <summary>
    /// 🔴 <b>G-52 — 업체특별단가 자동 채움</b>(C안). 6화면 전부가 품목 선택 때 참고값을 묻고
    /// <c>PartnerSpecialPrice</c> 가 있으면 그것으로 덮는다.
    /// </summary>
    [Fact]
    public void G52_여섯화면_전부_업체특별단가로_자동채운다()
    {
        foreach (var (path, _) in Grids)
        {
            // 견적만 코드비하인드(.razor.cs)에 로직이 있다.
            var src = path.EndsWith("QuotationGrid.razor", StringComparison.Ordinal)
                ? Read(path + ".cs")
                : Read(path);
            var code = CodeOnly(src);

            Assert.True(code.Contains("HintService.GetAsync", StringComparison.Ordinal),
                $"{path} 가 참고값을 묻지 않는다 — 업체특별단가가 안 들어온다.");
            Assert.True(code.Contains("PartnerSpecialPrice", StringComparison.Ordinal),
                $"{path} 가 업체특별단가를 안 쓴다 (C안 위반).");
        }
    }

    /// <summary>
    /// 🔴 <b>G-53 — 상품특별단가는 자동 채움에 끼지 않는다</b>(설계2 §4-4).
    /// 사장님 판정: <i>"상품 특별단가는 존재 자체가 큰 의미가 없네"</i>
    /// ⚠️ 여기 끼워 넣으면 <b>우선순위 다툼</b>이 되살아나고, 그것이 초안이 폐기된 이유다.
    /// </summary>
    [Fact]
    public void G53_상품특별단가는_자동채움에_안낀다()
    {
        foreach (var (path, _) in Grids)
        {
            var src = path.EndsWith("QuotationGrid.razor", StringComparison.Ordinal)
                ? Read(path + ".cs")
                : Read(path);
            var code = CodeOnly(src);

            Assert.False(
                Regex.IsMatch(code, @"UnitPrice\s*=\s*[^;]*ItemSpecialPrice"),
                $"{path} 가 상품특별단가를 단가에 넣고 있다 — 자동 적용 축은 업체특별단가 하나뿐이다.");
        }
    }

    /// <summary>
    /// 🔴 <b>G-54 — 값이 없으면 0 이 아니라 표시하지 않는다</b>(설계2 §6 G-8).
    /// <c>decimal?</c> 을 <c>decimal</c> 로 바꾸면 <c>null</c> 이 0 이 되어
    /// <b>진짜 0원과 구별이 안 된다.</b>
    /// </summary>
    [Fact]
    public void G54_참고값은_null_을_유지한다()
    {
        var model = CodeOnly(Read("src/HitPan.Web/Models/DeliveryModels.cs"));
        var start = model.IndexOf("class PriceHint", StringComparison.Ordinal);
        Assert.True(start > 0, "PriceHint 모델이 있어야 한다");
        var body = model[start..];

        foreach (var prop in new[]
                 { "PartnerSpecialPrice", "LastPrice", "StdPrice", "ItemSpecialPrice" })
        {
            Assert.True(
                Regex.IsMatch(body, $@"decimal\?\s+{prop}"),
                $"PriceHint.{prop} 은 decimal? 여야 한다 — 0 으로 그리면 진짜 0원과 구별이 안 된다.");
        }
    }

    /// <summary>
    /// G-55 — 6화면의 페이지가 그리드에 <c>PartnerId</c> 를 넘긴다.
    /// 안 넘기면 <b>빌드는 되는데 말풍선이 조용히 안 뜬다.</b>
    /// </summary>
    [Fact]
    public void G55_여섯페이지_전부_PartnerId_를_넘긴다()
    {
        var pages = new[]
        {
            ("src/HitPan.Web/Pages/Purchase/PurchaseOrderPage.razor",   "PurchaseOrderGrid"),
            ("src/HitPan.Web/Pages/Purchase/PurchaseReceiptPage.razor", "PurchaseReceiptGrid"),
            ("src/HitPan.Web/Pages/Purchase/ReturnPage.razor",          "PurchaseReturnGrid"),
            ("src/HitPan.Web/Pages/Sales/QuotationPage.razor",          "QuotationGrid"),
            ("src/HitPan.Web/Pages/Sales/SalesOrderPage.razor",         "SalesOrderGrid"),
            ("src/HitPan.Web/Pages/Sales/DeliveryPage.razor",           "DeliveryGrid"),
        };

        foreach (var (path, tag) in pages)
        {
            var code = CodeOnly(Read(path));
            var m = Regex.Match(code, $@"<{tag}\b[^>]*>", RegexOptions.Singleline);
            Assert.True(m.Success, $"{path} 에서 <{tag}> 를 찾아야 한다");
            Assert.True(m.Value.Contains("PartnerId", StringComparison.Ordinal),
                $"{path} 가 {tag} 에 PartnerId 를 안 넘긴다 — 말풍선이 조용히 안 뜬다.");
        }
    }
}
