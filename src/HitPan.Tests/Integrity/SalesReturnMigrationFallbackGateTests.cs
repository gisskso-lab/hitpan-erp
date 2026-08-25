using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작12) — <b>마이그 안 들어간 DB 에서도 매출반품 전 경로가 살아야 한다.</b>
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 실측(1.3.14)</b>: <i>"매출에서 반품 불러와, 반품확정 하려니 오류뜸 — 500"</i>
/// 그리고 <i>"개발PC에서 <b>지금 재현이 되고있는데</b> 무슨소리야"</i>.
/// </para>
/// <para>
/// 🔴 <b>작10 이 못 막은 이유 — 읽는 자리만 막고 쓰는 자리를 안 막았다.</b>
/// 작10 은 <i>"확정 시 500"</i> 을 <c>ConfirmSalesReturnAsync</c> 안으로 좁혀 읽고
/// 확정·취소의 <b>SELECT 2곳</b>만 폴백을 걸었다. 그런데 사용자는 확정 버튼을 누르기 <b>전에</b>
/// 상세조회·저장을 먼저 지나간다 ⇒ 마이그(DB-108) 안 들어간 DB 에서는
/// 문서를 <b>여는 순간</b>·<b>저장하는 순간</b> 1054 로 죽었다.
/// </para>
/// <para>
/// 🔴 <b>증상 모양이 맞다</b> — <c>MySqlException(1054)</c> 는 <c>GlobalExceptionMiddleware</c> 에서
/// FK(1451/1452 → 409) 필터도 <c>InvalidOperationException</c>(→400) 필터도 통과하지 못하고
/// 마지막 <c>catch(Exception)</c> 으로 떨어져 <b>정확히 500</b> 이 된다.
/// DB 실측: <c>hitpan_e2e.sales_return_items</c> 에 <c>is_loss</c> <b>없음</b>,
/// <c>SELECT sri.is_loss</c> → <c>ERROR 1054</c> 재현 확인.
/// </para>
/// <para>
/// ⚠️ 이 폴백은 <b>마이그 대체가 아니다.</b> 근본 해결은 DB-108 적용이고,
/// 폴백은 마이그가 늦게 도착한 DB 에서도 <b>흐름을 안 끊기</b> 위한 것이다(헌법 #20).
/// </para>
/// </remarks>
public class SalesReturnMigrationFallbackGateTests
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

    private static string SalesService() =>
        Read("src", "HitPan.Application", "Services", "SalesService.cs");

    private static string SalesController() =>
        Read("src", "HitPan.API", "Controllers", "SalesController.cs");

    /// <summary>
    /// 🔴 <b><c>is_loss</c> 를 쓰는 모든 SQL 이 스키마 확인을 거쳐야 한다.</b>
    /// 🔴 <b>낱말 하나로 검사하지 않는다</b> — 실제 SQL 문자열에 <c>is_loss</c> 가 박힌 자리를 센다.
    /// </summary>
    [Fact]
    public void is_loss_를_쓰는_SQL_은_전부_폴백을_거쳐야_한다()
    {
        var svc = SalesService();

        // 폴백 판정 함수가 살아 있어야 한다.
        Assert.Contains("HasSalesReturnLossColumnAsync", svc, StringComparison.Ordinal);

        // 🔴 핵심: SQL 안에 is_loss 가 **조건 없이 박힌** 자리가 남아 있으면 안 된다.
        //   조건부로 조립하면 "{lossCol}" · "{lossSelect}" · "{lossColU}" 같은 보간이 들어간다.
        //   아래 3가지는 그런 조립 없이 SQL 에 직접 박은 형태다.
        Assert.DoesNotContain("sri.is_loss AS IsLoss", svc, StringComparison.Ordinal);
        Assert.DoesNotContain("warehouse_id, is_loss)", svc, StringComparison.Ordinal);
        Assert.DoesNotContain("@Wh, @IsLoss)", svc, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>확정·취소만이 아니라 조회·저장 경로도 폴백을 거쳐야 한다.</b>
    /// 작10 의 누락이 정확히 여기였다 — 5개 경로 중 2개만 막았다.
    /// </summary>
    [Fact]
    public void 조회와_저장_경로에도_폴백이_적용돼야_한다()
    {
        var svc = SalesService();

        // HasSalesReturnLossColumnAsync 호출이 최소 5곳(상세조회·생성·수정·확정·취소)이어야 한다.
        var calls = svc.Split("HasSalesReturnLossColumnAsync").Length - 1;

        // 정의 1 + 호출 5 = 6 이상. (정의부 시그니처도 같은 이름을 포함한다)
        Assert.True(calls >= 6,
            $"is_loss 폴백이 5개 경로 전부에 걸려야 한다 — 현재 등장 {calls}회(정의 포함). "
            + "작10 처럼 일부 경로만 막으면 나머지에서 그대로 500 이 난다.");
    }

    /// <summary>
    /// 🔴 <b>컨트롤러가 스키마 예외를 사람 말로 돌려줘야 한다.</b>
    /// 1054/1146 을 안 잡으면 미들웨어 마지막 <c>catch</c> 로 떨어져
    /// <c>{"error":"서버 오류가 발생했습니다"}</c> <b>500</b> 만 뜬다 — 원인이 화면에 안 드러난다.
    /// </summary>
    [Fact]
    public void 반품_조회_저장_확정_취소가_스키마예외를_안내해야_한다()
    {
        var ctrl = SalesController();

        foreach (var route in new[]
                 {
                     "returns/{id}\")",          // GET 상세
                     "returns\")",               // POST 생성
                     "returns/{id}/confirm\")",  // POST 확정
                     "returns/{id}/cancel\")",   // POST 취소
                 })
        {
            var at = ctrl.IndexOf(route, StringComparison.Ordinal);
            Assert.True(at > 0, $"{route} 엔드포인트가 있어야 한다");
        }

        // 1054/1146 catch 가 최소 5곳(GET·POST·PUT·confirm·cancel)에 있어야 한다.
        var catches = ctrl.Split("ex.Number is 1054 or 1146").Length - 1;
        Assert.True(catches >= 5,
            $"스키마 예외 안내가 반품 전 경로에 있어야 한다 — 현재 {catches}곳. "
            + "작10 은 confirm·cancel 2곳에만 달았고, 사용자가 먼저 지나가는 조회·저장이 500 이었다.");
    }
}
