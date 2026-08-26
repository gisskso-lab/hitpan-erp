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
///   <item><b>반품 확정건은 상태가 「반품」으로 바뀐다</b> — 사장님:
///         <i>"전표 상태가 변경되면 됨. 이 방법이 가장 쉬움"</i>.
///         단 <b>DB 의 <c>status</c> 는 안 건드린다</b> — 바뀌는 건 화면 표기뿐이다.</item>
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
    /// 🔴 반품 확정건은 상태 칸이 <b>「반품」으로 바뀌어야</b> 한다.
    /// </summary>
    /// <remarks>
    /// 사장님(2026-08-26): <i>"반품 했다고 매입이 지워지는게 아니라,
    /// <b>전표 상태가 변경되면 됨. 이 방법이 가장 쉬움</b>"</i>
    ///
    /// <para>
    /// ⚠️ <b>PM 이 한 번 틀린 자리다.</b> 처음 지시(<i>"상태처리를 「반품」이라고 표기할것"</i>)는
    /// <b>상태를 바꾸라</b>는 말인데, PM 이 <i>"매입확정 사실이 사라지면 안 된다"</i> 는 걱정을 앞세워
    /// <b>칩을 나란히 붙이는</b> 다른 물건을 만들었다. 그 걱정은 <b>DB</b> 얘기였고
    /// 사장님은 <b>화면 표기</b>를 말씀하신 것이라, 섞으면 안 되는 둘을 섞은 것이다.
    /// </para>
    /// <para>
    /// ⇒ <b>상태 칸은 하나다.</b> 칩을 둘로 늘리면 화면이 복잡해진다(히트판은 쉬움으로 이겼다).
    /// DB 의 <c>status</c> 는 <c>'confirmed'</c> 그대로 두고 <b>보이는 표기만</b> 바꾼다.
    /// </para>
    /// </remarks>
    /// <summary>
    /// 반품 <b>확정</b>건은 직전 상태가 무엇이든 상태 칸이 「반품」이어야 한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 사장님(2026-08-26): <i>"매입확정이던 임시저장상태이던, <b>최종 상태는 반품</b>이잖아.
    /// 그럼 반품으로 상태변경이 맞지."</i> · <i>"그래야 <b>워크플로우 정합성</b>에도 맞지"</i>
    /// </para>
    /// <para>
    /// 🔴 <b>왜 확정된 반품만인가</b> — 사장님이 정리해 주신 데이터 흐름:
    /// <code>
    /// 매입확정 → 재고 +  · 지급금 +
    /// 반품     → 재고 −  · 지급금 −
    /// </code>
    /// 전표상 마이너스 전표를 끊지는 않지만 <b>데이터 흐름상 마이너스 처리</b>가 들어가
    /// 정합성이 맞춰진다. 그런데 그 마이너스는 <b>반품확정 시점에만</b> 일어난다
    /// (<c>ConfirmPurchaseReturnAsync</c> 는 <c>draft</c> 만 확정 대상으로 받고, 원장 반영은
    /// confirmed 에만 — 헌법 #6).
    /// </para>
    /// <para>
    /// ⇒ 작성만 해둔(<c>draft</c>) 반품을 「반품」으로 표시하면 <b>숫자는 안 빠졌는데 화면은 반품</b>이라
    /// 오히려 정합성이 깨진다. <b>돈과 재고가 실제로 움직인 건만</b> 「반품」이다.
    /// </para>
    /// </remarks>
    [Fact]
    public void 반품확정건은_직전상태와_무관하게_반품으로_보여야_한다()
    {
        var razor = Read("src", "HitPan.Web", "Components", "Purchase", "PurchaseReceiptList.razor");

        // ① 반품 확정을 먼저 판정해야 한다 — 매입확정보다 앞에 와야 상태가 「반품」으로 바뀐다.
        //    매입확정이 앞에 오면 확정된 반품건도 「매입확정」으로 보인다.
        var returnIdx = razor.IndexOf("ReturnStatus == \"confirmed\"", StringComparison.Ordinal);
        var purchaseIdx = razor.IndexOf("IsConfirmed(context)", StringComparison.Ordinal);

        Assert.True(returnIdx >= 0, "반품 확정 판정이 있어야 한다");
        Assert.True(purchaseIdx >= 0, "매입확정 판정이 있어야 한다");
        Assert.True(returnIdx < purchaseIdx,
            "반품 판정이 매입확정보다 뒤에 있다 — 그러면 반품건도 「매입확정」으로 보인다.\n"
            + "사장님: \"매입확정이던 임시저장상태이던, 최종 상태는 반품이잖아.\"");

        // ② 상태 칸은 하나다. 반품칩과 매입확정칩이 나란히 붙으면 안 된다.
        Assert.DoesNotContain("Class=\"ml-1\">반품<", razor);

        // ③ 매입확정 표기 자체는 남아 있어야 한다 — 반품 아닌 전표가 볼 상태다.
        Assert.Contains("매입확정", razor);
    }
}
