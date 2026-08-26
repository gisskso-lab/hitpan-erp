using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 20260826작5 — 매입명세서 목록의 「반품」 표기 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 사장님 지시(2026-08-26, 1.3.20 실측 후): <i>"매입명세서 목록에 반품처리된 전표는
/// 상태처리를 「반품」이라고 표기할것. 전표에 전부 매입확정이라고만 나옴"</i>
/// </para>
/// <para>
/// 반품확정은 <c>purchase_receipts</c> 를 <b>한 줄도 UPDATE 하지 않는다</b> —
/// 반품 전후가 <b>바이트 동일</b>이다(20260825작16 에서 예고된 자리).
/// 그래서 저장된 값이 없고, <c>purchase_returns</c> 를 <b>조회 시점에 되짚어</b> 채운다.
/// </para>
/// <para>
/// ⚠️ 이 게이트가 지키는 것은 <b>세 가지</b>다:
/// <list type="number">
///   <item><b>매입확정 표기를 지우지 않는다</b> — 반품했다고 매입이 취소된 게 아니다.
///         <c>status</c> 를 덮으면 매입확정 사실이 사라져 원장·부가세가 어긋난다.</item>
///   <item><b>취소·삭제분은 빼고 센다</b> — 되돌린 반품이 「반품」으로 남으면 오인한다.</item>
///   <item><b>JOIN 이 아니라 서브쿼리</b> — JOIN 하면 반품서가 둘일 때 목록 행이 늘어난다.</item>
/// </list>
/// </para>
/// </remarks>
public class PurchaseReceiptReturnStatusGateTests
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

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

    /// <summary>GetReceiptsAsync 의 base SQL 만 잘라낸다.</summary>
    private static string ReceiptListSql()
    {
        var src = Read("src", "HitPan.Application", "Services", "PurchaseService.cs");

        var idx = src.IndexOf("pr.receipt_id AS ReceiptId", StringComparison.Ordinal);
        Assert.True(idx >= 0, "매입명세서 목록 SQL 을 찾아야 한다");

        var end = src.IndexOf("\"\"\";", idx, StringComparison.Ordinal);
        Assert.True(end > idx, "SQL 끝을 찾아야 한다");

        return src[idx..end];
    }

    /// <summary>목록 SQL 이 반품 상태를 실제로 뽑아야 한다.</summary>
    [Fact]
    public void 매입명세서_목록은_반품상태를_조회해야_한다()
    {
        var sql = ReceiptListSql();

        Assert.Contains("ReturnStatus", sql);
        Assert.Contains("purchase_returns", sql);
    }

    /// <summary>
    /// 🔴 취소·삭제된 반품은 「반품」으로 세지 않아야 한다.
    /// 되돌린 반품이 남아 있으면 담당자가 살아있는 것으로 오인한다.
    /// </summary>
    [Fact]
    public void 취소되거나_삭제된_반품은_세지_않아야_한다()
    {
        var sql = ReceiptListSql();

        var sub = Regex.Match(sql, @"\(SELECT\s+MIN\(r\.status\).*?\)\s*AS\s+ReturnStatus",
            RegexOptions.Singleline);
        Assert.True(sub.Success, "ReturnStatus 서브쿼리를 찾아야 한다");

        var body = sub.Value;
        Assert.Contains("is_deleted = 0", body);
        Assert.Contains("canceled", body);
    }

    /// <summary>
    /// 🔴 JOIN 이 아니라 상관 서브쿼리여야 한다 — JOIN 하면 반품서가 둘 이상일 때
    /// 매입명세서 행이 <b>늘어난다</b>(같은 전표가 목록에 두 번 뜬다).
    /// </summary>
    [Fact]
    public void 반품조회는_행을_늘리는_조인이_아니어야_한다()
    {
        var sql = ReceiptListSql();

        Assert.DoesNotContain("JOIN purchase_returns", sql);
    }

    /// <summary>
    /// 🔴 매입확정 표기를 지우면 안 된다 — 반품했다고 매입이 취소된 게 아니다.
    /// 두 축을 각각 보여준다.
    /// </summary>
    [Fact]
    public void 화면은_매입확정과_반품을_함께_보여야_한다()
    {
        var razor = Read("src", "HitPan.Web", "Components", "Purchase", "PurchaseReceiptList.razor");

        // 매입확정 칩이 살아 있어야 한다.
        Assert.Contains("매입확정", razor);

        // 반품 표기가 있어야 한다.
        Assert.Contains("ReturnStatus", razor);

        // 확정과 작성중을 구분해야 한다 — 하나로 뭉치면 아직 확정 안 한 반품이
        // 이미 처리된 것처럼 보여 담당자가 확정을 건너뛴다.
        Assert.Contains("\"confirmed\"", razor);
        Assert.Contains("\"draft\"", razor);
    }
}
