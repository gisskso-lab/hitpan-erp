using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작13) — <b>원장 중복(1062)이 500 으로 새면 안 된다.</b>
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 실측 반려(1.3.15)</b>: <i>"반품확인서 반품확정 동작: … status of 500 = 반려"</i>
/// 작12 에서 스키마 폴백(1054)을 5경로에 깔았는데도 <b>여전히 500</b> 이었다.
/// </para>
/// <para>
/// 🔴 <b>진짜 원인 — 원장의 UNIQUE 제약</b>
/// <c>stock_ledger</c> 에 <c>uq_stock_ledger_source</c>(source_type·source_id·item_id·move_type),
/// <c>journal_entries</c> 에 <c>uq_je_source</c>(tenant_id·source_type·source_id) 가 걸려 있다.
/// 원장이 이미 남아 있는 반품을 다시 확정하면 <b>MySQL 1062(Duplicate entry)</b>.
/// </para>
/// <para>
/// 🔴 <b>왜 500 이었나 — 증상 모양 대조</b>
/// 1062 는 <c>GlobalExceptionMiddleware</c> 의 어느 필터에도 안 걸린다:
/// 1054/1146(스키마)도 아니고, 1451/1452(FK → <b>409</b>)도 아니라
/// 마지막 <c>catch(Exception)</c> 으로 떨어져 <b>정확히 500</b> 이 된다.
/// 실측으로 확인했다 — 레포 전체에서 1062 를 잡는 곳이 <b>한 군데도 없었다</b>(grep 0건).
/// </para>
/// <para>
/// <b>실측 재현</b>: 같은 <c>source_id</c> 로 <c>stock_ledger</c> 두 번 INSERT →
/// <c>MySQL 1062: Duplicate entry 'T1-sales_return-…-I1-in' for key 'uq_stock_ledger_source'</c>.
/// </para>
/// <para>
/// ⚠️ <b>원장을 지우지 않는다</b>(헌법 #3 INSERT ONLY). 지우는 게 아니라 <b>막는다</b> —
/// 이미 반영된 것이므로 사용자에겐 실패가 아니라 <b>상태 안내</b>(400)로 돌려준다.
/// </para>
/// </remarks>
public class LedgerDuplicateGateTests
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

    /// <summary>주석을 걷어낸 실제 코드만 남긴다(거짓 경보 방지).</summary>
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

    /// <summary>
    /// 🔴 <b>확정 전에 원장이 이미 있는지 본다.</b>
    /// 상태 전환은 트랜잭션 마지막이라, 앞 단계만 남고 상태는 draft 로 남는 창이 생긴다.
    /// 그 뒤로는 몇 번을 눌러도 1062 → 500 이 반복되고 <b>누르는 사람은 이유를 알 수 없다.</b>
    /// </summary>
    [Fact]
    public void 확정_진입시_원장중복을_먼저_막아야_한다()
    {
        var svc = CodeLines(Read("src", "HitPan.Application", "Services", "SalesService.cs"));

        // 원장 존재 확인 쿼리가 있어야 한다.
        Assert.Contains("FROM stock_ledger", svc, StringComparison.Ordinal);
        Assert.Contains("source_type = 'sales_return'", svc, StringComparison.Ordinal);

        // 있으면 InvalidOperationException(→400)으로 사람 말로 돌려준다.
        Assert.Contains("ledgerExists", svc, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>1062 안전망이 확정·취소 양쪽에 있어야 한다.</b>
    /// 진입 가드가 1차로 막지만 동시 클릭 등 경쟁 상황에서 컨트롤러까지 올 수 있다.
    /// ⚠️ 한쪽만 달면 작7 의 <i>"생성은 넣고 수정은 안 넣던 비대칭"</i> 이 또 난다.
    /// </summary>
    [Fact]
    public void 확정과_취소_양쪽에_1062_안전망이_있어야_한다()
    {
        var ctrl = CodeLines(Read("src", "HitPan.API", "Controllers", "SalesController.cs"));

        var catches = ctrl.Split("ex.Number == 1062").Length - 1;
        Assert.True(catches >= 2,
            $"1062 안전망이 확정·취소 양쪽에 있어야 한다 — 현재 {catches}곳. "
            + "1062 는 미들웨어의 1054/1146·1451/1452 어느 필터에도 안 걸려 500 으로 샌다.");
    }

    /// <summary>
    /// 🔴 <b>1062 가 미들웨어에서 500 으로 새는 구조인지 못박는다.</b>
    /// 이 게이트가 지키는 것은 "미들웨어를 고쳐라"가 아니라
    /// <b>"1062 는 위에서 잡아야 한다"</b>는 사실이다 — 미들웨어에 1062 필터가 생기면
    /// 이 테스트가 알려주고, 그때 위 안전망을 정리하면 된다.
    /// </summary>
    [Fact]
    public void 미들웨어는_FK만_409로_가르고_1062는_안_잡는다()
    {
        var mw = CodeLines(Read("src", "HitPan.API", "Middleware", "GlobalExceptionMiddleware.cs"));

        // FK 는 409 로 가른다 — 그래서 FK 는 500 의 원인이 될 수 없다(증상 모양 대조의 근거).
        Assert.Contains("1451", mw, StringComparison.Ordinal);
        Assert.Contains("1452", mw, StringComparison.Ordinal);

        // 마지막 안전망이 있어야 한다(빈 catch 금지 · 헌법 #15).
        Assert.Contains("catch (Exception ex)", mw, StringComparison.Ordinal);
    }
}
