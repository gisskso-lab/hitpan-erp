using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작6) 매출반품(반품확인서) 판매관리 분리 + 파손 로스.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 실측 반려(2026-08-25, 1.3.9): <i>"반품현황메뉴 생성안됨 = 반려"</i> · <i>"확인안됨 = 반려"</i>
/// </para>
/// <para>
/// 🔴 <b>사장님이 정리해 주신 업무 구분.</b>
/// </para>
/// <list type="bullet">
/// <item><b>매출 반품확인</b> — 고객사가 반품한 것 → 재고 <b>+</b> · 미수 <b>−</b></item>
/// <item><b>매입 반품처리</b> — 내가 공급처로 보낼 것 → 재고 <b>−</b> · 지급 <b>−</b></item>
/// </list>
/// <para>
/// 백엔드는 이 구분을 이미 지키고 있었다. <b>화면만 매입관리에 묶여 있었다</b> —
/// 판매 담당자가 매입 메뉴로 들어가 콤보를 바꿔야 자기 업무를 볼 수 있었고,
/// 매출반품 현황은 <b>아예 없었다</b>(RT_* 4종이 purchase_returns 하드코딩).
/// </para>
/// <para>
/// 🔴 <b>파손 로스</b> — 사장님 정의: <i>"파손이면 로스로 정의, 파손이 아니면 재입고(재고반영)"</i>.
/// 종전엔 확정하면 <b>무조건 재고를 늘렸다.</b> 파손품이 재고로 잡히면 현장에서 세는 숫자와 어긋난다.
/// <b>줄 단위</b>인 이유는 한 반품에 정상품과 파손품이 섞여 오기 때문이다.
/// <b>판정은 고객사가 한다</b> — 사장님: <i>"로스판정 기준은 고객사가 정하는거지, 너가 왜 정해."</i>
/// </para>
/// <para>
/// ⚠️ <b>매입은 건드리지 않는다.</b> 작업 중 한 번 매입 화면을 고쳤다가 사장님께 적발돼 되돌렸다.
/// 지금은 대메뉴 영역별 수정보완 중이고, 이번 차수는 <b>판매</b> 영역이다.
/// </para>
/// </remarks>
public class SalesReturnSeparationGateTests
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
                       && !t.StartsWith("///", StringComparison.Ordinal)
                       && !t.StartsWith("@*", StringComparison.Ordinal)
                       && !t.StartsWith("--", StringComparison.Ordinal);
            }));

    private static string SalesService() =>
        CodeLines(Read("src", "HitPan.Application", "Services", "SalesService.cs"));

    private static string ReportService() =>
        CodeLines(Read("src", "HitPan.Application", "Services", "ReportService.cs"));

    // ───────────────────────────────────────────────────────────────
    // 🔴 반려 ② — 반품확인현황 메뉴가 없었다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>20260825작15 — 반품확인 메뉴가 되살아나면 안 된다</b>(사장님 결재).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님 오더: <i>"반품확정 메뉴를 아애 빼 … 반품확인메뉴에 대한 <b>모든걸 다 지워</b>"</i>
    /// 대체 경로는 거래명세서 <b>확정취소</b>(재고·회계 역행 원장 자동 발행) 또는 <b>마이너스 전표</b>다.
    /// </para>
    /// <para>
    /// ⚠️ 종전 이 자리에는 <c>판매관리_메뉴에_반품확인서와_반품확인현황이_있어야_한다</c> 가
    /// <b>메뉴가 있어야 한다</b>고 강제하고 있었다. 결재로 뺐으므로 <b>정반대로 뒤집는다</b> —
    /// 안 그러면 게이트가 제거를 막는다.
    /// </para>
    /// <para>
    /// 🔴 <b>서버 로직·화면·API 는 남아 있다.</b> 이 게이트가 지키는 것은 <b>메뉴 진입점</b> 뿐이다.
    /// 아래 SQL·서버 게이트들은 그대로 살아 있어, 되살릴 때 기능이 온전한지 보증한다.
    /// </para>
    /// </remarks>
    [Fact]
    public void 사이드바에_반품확인_메뉴가_되살아나면_안_된다()
    {
        var sidebar = CodeLines(Read("src", "HitPan.Web", "Layout", "Sidebar.razor"));

        Assert.DoesNotContain("/sales-return-status", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkDocumentKind.SalesReturn", sidebar, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>매출반품 현황 SQL 4종이 있는가.</b>
    /// 종전엔 RT_* 4종이 purchase_returns 하드코딩이라 매출은 집계 자체가 없었다.
    /// </summary>
    [Fact]
    public void 매출반품현황_SQL_4종이_있어야_한다()
    {
        var svc = ReportService();

        foreach (var name in new[] { "SR_BY_PERIOD", "SR_BY_PARTNER", "SR_BY_ITEM", "SR_BY_EMPLOYEE" })
        {
            Assert.Contains(name, svc, StringComparison.Ordinal);
        }

        Assert.Contains("GetSalesReturnReportAsync", svc, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>매출반품 현황이 확정분만 집계하는가.</b> 20260825작3 결재 계승.
    /// 재고·회계가 confirmed 에만 반응하므로 현황 숫자도 같은 잣대여야 한다(헌법 #6).
    /// </summary>
    [Fact]
    public void 매출반품현황은_확정분만_집계해야_한다()
    {
        var svc = ReportService();

        var wheres = svc.Split('\n')
            .Where(l => l.Contains("WHERE", StringComparison.Ordinal)
                        && l.Contains("sr.tenant_id", StringComparison.Ordinal))
            .ToList();

        Assert.True(wheres.Count >= 4,
            $"매출반품 SQL 들의 WHERE 를 찾아야 한다 (찾은 수: {wheres.Count})");

        foreach (var w in wheres)
        {
            Assert.Contains("sr.status = 'confirmed'", w, StringComparison.Ordinal);
        }
    }

    // ───────────────────────────────────────────────────────────────
    // 🔴 파손 로스 — 재고에 넣지 않는다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>로스는 재고에 안 들어가는가.</b>
    /// 종전엔 파손이든 아니든 무조건 재고를 늘렸다.
    /// </summary>
    [Fact]
    public void 파손_로스는_재고에_반영하지_않아야_한다()
    {
        var svc = SalesService();

        // 🔴 20260825작15 — 리터럴 `it.is_loss ?? 0` 요구를 걷어낸다.
        //   그 캐스팅이 바로 **반품확정 500 의 원인**이었다:
        //   MySqlConnector 가 TINYINT(1) 을 Boolean 으로 주는데 `(int)` 로 받아
        //   RuntimeBinderException → 미들웨어 마지막 catch → 500.
        //   ⇒ 안전 판정(IsLossValue)으로 바꿨으므로 글자가 아니라 **동작**을 검사한다.
        //   (작11·작12 에서도 리터럴 게이트가 봉합을 막았다 — 같은 자리다.)
        var filters = svc.Split("IsLossValue").Length - 1;
        Assert.True(filters >= 3,
            $"확정·취소 두 경로 모두 로스를 걸러야 한다 (현재 {filters}회, 정의 포함). " +
            "확정에서 안 넣은 재고를 취소에서 빼면 재고가 마이너스로 어긋난다.");
    }

    /// <summary>
    /// 🔴 <b>로스여도 매출·미수 차감은 그대로인가.</b>
    /// 물건은 못 쓰지만 <b>고객에게 돈은 돌려준다</b> — 여기까지 빼면 미수가 안 맞는다.
    /// </summary>
    [Fact]
    public void 로스여도_매출과_미수_차감은_유지돼야_한다()
    {
        var svc = SalesService();

        Assert.Contains("total_sales - @Amount", svc, StringComparison.Ordinal);
        Assert.Contains("sales_return_confirmed", svc, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>로스 표시가 저장·복원되는가.</b>
    /// 저장은 되는데 다시 열 때 사라지면 사용자는 체크를 또 해야 한다.
    /// </summary>
    [Fact]
    public void 로스_표시가_저장되고_복원돼야_한다()
    {
        var svc = SalesService();

        // 🔴 20260825작12 — 글자 그대로("is_loss)")를 요구하던 것을 걷어낸다.
        //   마이그(DB-108) 안 들어간 DB 에서 500 이 나서, 컬럼 유무에 따라
        //   INSERT 컬럼목록·SELECT 를 갈라 넣도록 바뀌었다($"...{lossCol}").
        //   ⇒ 리터럴 검사는 **봉합을 막는 게이트**가 된다(작11 에서 겪은 그 자리다).
        //   지킬 것은 글자가 아니라 **로스가 저장·복원된다**는 동작이다.
        Assert.Contains("IsLoss = it.IsLoss", svc, StringComparison.Ordinal);   // 저장 파라미터
        Assert.Contains("AS IsLoss", svc, StringComparison.Ordinal);            // 복원 SELECT
        Assert.Contains("is_loss", svc, StringComparison.Ordinal);              // 컬럼 자체는 살아 있다

        var page = CodeLines(Read("src", "HitPan.Web", "Pages", "Sales", "SalesReturnPage.razor.cs"));
        Assert.Contains("isLoss = l.IsLoss", page, StringComparison.Ordinal);
        Assert.Contains("IsLoss = it.IsLoss", page, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────────────────────────────
    // 🔴 사유는 고객사가 정한다 — 우리가 코드를 깔지 않는다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>매출 그리드의 반품사유가 자율 입력인가.</b>
    /// 사장님 지시: <i>"콤보박스로 두지 말고 텍스트박스로 자율적 기입"</i>.
    /// 우리가 사유 코드를 미리 정하지 않는다(헌법 #11 과 같은 축).
    /// </summary>
    [Fact]
    public void 매출반품_사유는_자율입력이어야_한다()
    {
        var grid = CodeLines(Read("src", "HitPan.Web", "Components", "Sales", "SalesReturnGrid.razor"));

        Assert.False(grid.Contains("MudSelectItem", StringComparison.Ordinal),
            "매출반품 사유를 우리가 정한 코드 목록으로 고정하면 안 된다. 고객사가 쓴 말이 목록이 된다.");

        Assert.Contains("SearchReasonAsync", grid, StringComparison.Ordinal);
        Assert.Contains("CoerceValue=\"true\"", grid, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────────────────────────────
    // ⚠️ 매입 무접촉 — 이번 차수는 판매 영역이다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>매입 반품 화면이 그대로인가.</b>
    /// 작업 중 한 번 갈아엎었다가 되돌렸다. 매입은 이번 영역이 아니다.
    /// </summary>
    [Fact]
    public void 매입_반품화면은_건드리지_않아야_한다()
    {
        var purchase = CodeLines(Read("src", "HitPan.Web", "Pages", "Purchase", "ReturnPage.razor"));

        Assert.Contains("_returnType", purchase, StringComparison.Ordinal);
        Assert.Contains("purchase_return", purchase, StringComparison.Ordinal);

        Assert.False(purchase.Contains("/sales-returns", StringComparison.Ordinal),
            "매출 경로는 매출 전용 화면에 있어야 한다. 매입 화면에 얹으면 두 업무가 다시 섞인다.");
    }
}
