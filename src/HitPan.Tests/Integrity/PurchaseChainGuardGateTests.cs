using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260827작7 — <b>매입라인 사슬 가드가 실제로 막는가.</b>
///
/// <para>
/// 사장님 지시(2026-08-27):
/// <i>"가장 중요한건, 사슬로 이어진 건이 중복으로 발행되거나 사슬에 영향을 주는,
/// 즉 정합성에 영향을 주는 것들을 <b>모두 차단</b>하자는거야."</i>
/// <i>"삭제된 건은 해당 사슬에서 같이 삭제 되던가, 아니면 <b>해당 사슬로 인해 삭제가
/// 불가하다고 메시지를 띄우면</b> 되."</i>
/// </para>
///
/// <para>
/// 🔴 판정 기준은 사장님이 정하셨다 — <b>원장이 움직였느냐.</b>
/// 임시저장(draft)은 재고·회계 무영향이라 지울 수 있고,
/// 확정(confirmed)은 이미 움직였으므로 사슬 어디서도 못 지운다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 게이트는 실제 서비스 메서드를 부르지 않는다</b> — 그 시험은 실 DB 와
/// 트랜잭션이 필요해 <c>MoneyFlowJournalGateTests</c> 축에서 따로 다룬다.
/// 여기서는 <b>가드가 코드에 실재하고 올바른 축을 보는지</b>를 잰다.
/// 🔴 그래서 <b>낱말 하나로 검사하지 않는다</b> — 같은 이름이 주석·정의·호출에 다 살기 때문에
/// (가짜 게이트 20번 누적), <b>메서드 본문을 잘라내</b> 그 안에서만 본다.
/// </para>
/// </summary>
public sealed class PurchaseChainGuardGateTests
{
    /// <summary>
    /// 🔴 G-PG1 — <b>매입명세서 삭제가 자식 반품을 본다.</b>
    /// 종전엔 draft 만 보고 반품을 안 봐서, draft 매입에 반품을 붙인 뒤 매입을
    /// hard DELETE 하면 <b>부모 없는 반품</b>이 남았다(*"AI도 못 찾는"* 상태).
    /// </summary>
    [Fact]
    public void GPG1_매입삭제가_자식반품을_본다()
    {
        var body = Body("DeletePurchaseReceiptAsync");

        Assert.Contains("purchase_returns", body);
        // 살아있는 것만 막아야 한다 — 취소분까지 막으면 영영 못 지운다
        Assert.Contains("status <> 'canceled'", body);
        // 🔴 어느 전표인지 번호를 알려주는가 (사장님 지시)
        Assert.Contains("return_no", body);
    }

    /// <summary>
    /// 🔴 G-PG2 — <b>매입명세서 삭제가 원장을 본다.</b> 판정 기준 = 원장이 움직였느냐.
    /// </summary>
    [Fact]
    public void GPG2_매입삭제가_원장을_본다()
    {
        var body = Body("DeletePurchaseReceiptAsync");

        Assert.Contains("stock_ledger", body);
        Assert.Contains("journal_entries", body);
    }

    /// <summary>
    /// 🔴 G-PG3 — <b>반품 삭제가 원장을 본다.</b> 매입과 대칭이어야 한다.
    /// </summary>
    [Fact]
    public void GPG3_반품삭제가_원장을_본다()
    {
        var body = Body("DeletePurchaseReturnAsync");

        Assert.Contains("stock_ledger", body);
        Assert.Contains("journal_entries", body);
        // is_deleted 조건 보강 — 매출쪽엔 있는데 매입쪽만 빠져 있었다
        Assert.Contains("is_deleted=0", body);
    }

    /// <summary>
    /// 🔴 G-PG4 — <b>발주 삭제가 status 를 본다.</b>
    /// 종전엔 <c>is_deleted</c> 만 봐서 <c>received</c>(입고완료) 발주도 지워졌다.
    /// 주석은 *"발주서 draft 삭제"* 인데 <b>코드가 draft 를 강제하지 않았다.</b>
    /// </summary>
    [Fact]
    public void GPG4_발주삭제가_상태를_본다()
    {
        var body = Body("DeletePurchaseOrderAsync");

        // 🔴 status 를 실제로 **판정에 쓰는지** — SELECT 만 하고 안 쓰던 게 종전 결함이다
        Assert.Contains("row.Status", body);
        Assert.Contains("draft", body);
        // 어느 매입명세서 때문인지 알려주는가
        Assert.Contains("receipt_no", body);
    }

    /// <summary>
    /// 🔴 G-PG5 — <b>반품 직접작성에 중복·상태 가드가 있다.</b>
    /// 전환 경로에는 <c>FOR UPDATE</c> 가드가 있는데 <b>직접작성 경로가 그걸 통째로
    /// 우회</b>하고 있었다 — 같은 매입에 반품을 무한히 만들 수 있었다.
    /// </summary>
    [Fact]
    public void GPG5_반품직접작성에_중복가드가_있다()
    {
        var body = Body("CreatePurchaseReturnAsync");

        // 원 매입 상태를 보는가 — draft 매입에 반품이 붙던 자리
        Assert.Contains("purchase_receipts", body);
        Assert.Contains("confirmed", body);
        // 중복 반품을 막는가
        Assert.Contains("purchase_returns", body);
        Assert.Contains("status <> 'canceled'", body);
        // 🔴 헤더·라인이 한 트랜잭션인가 — 유령 헤더가 남던 자리
        Assert.Contains("BeginTransaction", body);
        Assert.Contains("Rollback", body);
    }

    /// <summary>
    /// 🔴 G-PG6 — <b>발주→매입 중복 전환 가드가 올바른 방향이다.</b>
    /// 종전 <c>status != 'Confirmed'</c> 는 ①대문자라 collation 이 바뀌면 무력화되고
    /// ②<b>방향이 반대</b>라 이미 확정된 매입이 있어도 다시 전환됐다(재고 2배 입고).
    /// </summary>
    [Fact]
    public void GPG6_발주매입_중복전환_가드가_올바르다()
    {
        // 🔴 **주석을 걷어내고 코드만 본다.**
        //   내가 옛 결함을 주석에 인용해 뒀더니 그 문구가 잡혀 이 시험이 FAIL 했다.
        //   같은 글자가 **다른 목적**으로 파일에 산다 — 게이트가 늘 빠지는 함정이다.
        //   ⇒ 주석 줄(`//`·`--`)을 제거한 뒤 판정한다.
        var body = CodeOnly(Body("ConvertOrderToReceiptAsync"));

        // 🔴 대문자 'Confirmed' 가 **코드에** 남아 있으면 안 된다
        Assert.DoesNotContain("!= 'Confirmed'", body);
        Assert.DoesNotContain("status != 'confirmed'", body);
        // 취소분만 빼고 전부 세는 방향이어야 한다
        Assert.Contains("status <> 'cancelled'", body);
        Assert.Contains("receipt_no", body);
    }

    /// <summary>
    /// 🔴 G-PG7 — <b>재고현황 조회에 테넌트 조인이 있다</b>(헌법 #2).
    /// 종전 <c>JOIN items i ON i.item_id = s.item_id</c> 는 <c>tenant_id</c> 가 없어
    /// <b>다른 회사 품목과 교차 조인</b>됐다 — 남의 품명 노출 + 행 곱하기.
    /// </summary>
    [Fact]
    public void GPG7_재고현황에_테넌트조인이_있다()
    {
        var body = Body("GetBalanceAsync", "StockService.cs");

        Assert.Contains("JOIN items i ON i.item_id = s.item_id AND i.tenant_id = s.tenant_id", body);
    }

    /// <summary>
    /// 🔴 G-PG8 — <b>대조군.</b> 있지도 않은 문구는 안 잡혀야 한다.
    /// 이게 없으면 <c>Contains</c> 가 늘 참인 엉터리 검사도 위 시험을 통과한다.
    /// </summary>
    [Fact]
    public void GPG8_대조군_없는문구는_안잡힌다()
    {
        var body = Body("DeletePurchaseReceiptAsync");
        Assert.DoesNotContain("절대_없는_문구_XYZZY", body);
    }

    /// <summary>
    /// 🔴 G-PG10 — <b>가드가 죽어 있지 않다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>이 시험을 왜 만들었나 — 내 게이트가 가짜였다(누적 21번째).</b>
    /// 위 시험들을 짜고 나서 반증으로 <c>if (blockingReturns.Count &gt; 0)</c> 를
    /// <c>if (false &amp;&amp; blockingReturns.Count &gt; 0)</c> 로 죽였는데
    /// <b>9건이 전부 통과했다.</b> 낱말(`purchase_returns`·`return_no`)은 그대로 남기 때문이다.
    ///
    /// <para>
    /// ⇒ 글자검사만으로는 <b>죽은 가드를 못 본다.</b> 조건을 무력화하는 상투수단
    /// (<c>if (false</c>, <c>&amp;&amp; false</c>, <c>|| true</c>)이 매입라인 코드에
    /// <b>한 건도 없어야</b> 한다.
    /// </para>
    /// </remarks>
    [Fact]
    public void GPG10_가드가_죽어있지_않다()
    {
        foreach (var file in new[] { "PurchaseService.cs", "StockService.cs" })
        {
            var code = CodeOnly(TestSource.Read("src", "HitPan.Application", "Services", file));

            Assert.DoesNotContain("if (false", code);
            Assert.DoesNotContain("&& false", code);
            Assert.DoesNotContain("|| true", code);
            Assert.DoesNotContain("if (true ||", code);
        }
    }

    /// <summary>
    /// 🔴 G-PG9 — <b>화면 표시가 한글이다</b>(사장님 지시 *"화면상에는 한글로!!"*).
    /// 사장님이 정한 상태 정의가 그대로 들어 있어야 한다.
    /// </summary>
    [Fact]
    public void GPG9_상태표시가_한글이다()
    {
        var src = TestSource.Read("src", "HitPan.Application", "DTOs", "Purchase", "PurchaseStatusLabels.cs");

        Assert.Contains("임시저장", src);   // 매입만 잡힌 상태
        Assert.Contains("입고완료", src);   // 매입확정
        Assert.Contains("반품중", src);     // 반품서 작성
        Assert.Contains("반품확정", src);   // 반품확정
        // 철자 두 갈래를 **둘 다** 「취소」로 받는가 (canceled / cancelled)
        Assert.Contains("[\"canceled\"] = \"취소\"", src);
        Assert.Contains("[\"cancelled\"] = \"취소\"", src);
    }

    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 주석 줄(<c>//</c>, SQL <c>--</c>)을 걷어낸다.
    /// </summary>
    /// <remarks>
    /// 🔴 옛 결함을 주석에 인용해 두면 그 문구가 검사에 걸려 <b>거짓 FAIL</b> 이 난다.
    /// 반대로 봉합을 지워도 주석에 낱말이 남아 <b>거짓 PASS</b> 가 나기도 한다.
    /// 둘 다 같은 뿌리 — <b>같은 글자가 다른 목적으로 파일에 산다.</b>
    /// </remarks>
    private static string CodeOnly(string src)
    {
        var lines = src.Split('\n')
            .Select(l => l.TrimStart())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal)
                     && !l.StartsWith("--", StringComparison.Ordinal)
                     && !l.StartsWith("///", StringComparison.Ordinal));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 🔴 메서드 <b>본문만</b> 잘라 돌려준다.
    /// 파일 전체를 검사하면 다른 메서드의 낱말이 잡혀 <b>봉합을 빼도 통과</b>한다
    /// (가짜 게이트 누적 20번의 주된 원인).
    /// </summary>
    private static string Body(string methodName, string file = "PurchaseService.cs")
    {
        var src = file == "StockService.cs"
            ? TestSource.Read("src", "HitPan.Application", "Services", "StockService.cs")
            : TestSource.Read("src", "HitPan.Application", "Services", "PurchaseService.cs");

        var idx = src.IndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"{methodName} 을 찾지 못했다 — 이름이 바뀌었으면 이 게이트도 고쳐야 한다.");

        // 다음 메서드 선언 전까지를 본문으로 본다 (public/private 선언 앵커)
        var rest = src[idx..];
        var next = rest.IndexOf("\n    public ", StringComparison.Ordinal);
        var next2 = rest.IndexOf("\n    private ", StringComparison.Ordinal);
        var end = next < 0 ? next2 : (next2 < 0 ? next : Math.Min(next, next2));
        return end > 0 ? rest[..end] : rest;
    }
}

internal static class TestSource
{
    public static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !System.IO.Directory.Exists(System.IO.Path.Combine(dir, "src")); i++)
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, ".."));
        var all = new List<string> { dir };
        all.AddRange(parts);
        return System.IO.File.ReadAllText(System.IO.Path.Combine(all.ToArray()));
    }
}
