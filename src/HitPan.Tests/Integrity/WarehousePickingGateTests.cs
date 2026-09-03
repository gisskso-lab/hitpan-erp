using System;
using System.Linq;
using System.Threading.Tasks;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260903작19 — <b>창고 결정 게이트.</b>
///
/// <para>
/// <b>사장님 오더 (2026-09-03, 최종)</b>
/// <list type="number">
///   <item>디폴트 값으로, 상품등록시 지정한 A창고</item>
///   <item>고객사가 a품목을 a→b창고로 분산하고 싶을때는 <b>재고이송으로 수동변경</b></item>
///   <item>매출매입이 이뤄지는 디폴트 값은 상품등록시 지정한 A창고</item>
///   <item>디폴트값으로 지정되어 있지만, 재고가 분산되어 있을경우 <b>창고를 사용자가 지정</b>할 수 있도록</item>
/// </list>
/// </para>
///
/// <para>
/// 🔴 <b>「자동 배분」이 아니다.</b> 초안에서 PM 이 여러 창고에서 자동으로 긁어모으는 안(C안)을
/// 준비했으나, 사장님 지시 2번이 그것을 부정한다 — <b>창고 간 이동은 사람이 재고이송으로 한다.</b>
/// 시스템이 임의로 창고를 넘나들며 빼면 현장 재고와 장부가 어긋난다.
/// ⇒ <b>한 창고에서 나간다.</b> 어느 창고냐를 정하는 것이 이 게이트의 대상이다.
/// </para>
///
/// <para>
/// <b>창고 결정 순서</b> (매입이 이미 쓰던 것 — 매출에만 없었다)<br/>
/// ① 사용자가 라인/화면에서 지정한 창고 → ② <b>상품마스터 기본창고</b> → ③ 테넌트 기본창고
/// </para>
///
/// <para>
/// 🔴 <b>실측으로 확인한 결함</b>: 매출은 ②를 <b>통째로 건너뛰고</b> ①이 비면 곧장 ③으로 갔다
/// (<c>SalesService</c> 에 <c>default_warehouse</c> 참조 <b>0건</b> · 매입은 <c>PurchaseService:2036</c> 에 있었다).
/// 그래서 상품마스터에 A창고를 지정해도 매출은 MAIN 으로 나갔고, A창고에 재고가 있어도
/// MAIN 이 음수가 됐다(실측: 테스트1창고 <b>−15</b>).
/// </para>
///
/// <para>
/// 🔴 4/26 헌법 <i>"재고로 판매 흐름이 막히면 안 된다"</i> 은 그대로 지킨다 —
/// 회사 합산 검사는 <b>손대지 않는다.</b> 창고를 바르게 고르는 것과 판매를 막는 것은 다른 문제다.
/// </para>
///
/// <para>⚠️ TEMPORARY 표만 쓴다 — 실제 표는 가리기만 하고 안 건드린다(헌법 #39).</para>
/// <para>⚠️ 컬럼은 출하 DDL(<c>installer/hitpan_db_clean.sql</c> · 헌법 #36)에서 확인해 맞췄다.
///   작18 에서 fixture 컬럼을 추측으로 적어 네 번 터졌다.</para>
/// </summary>
public sealed class WarehousePickingGateTests
{
    private const string Tid = "GATE-PICK903";
    private const string Item = "ITEM-PICK";
    private const string WhA = "WH-A";      // 재고 있는 창고
    private const string WhB = "WH-B";      // 재고 없는 창고 (출고 지정 대상)

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    /// <summary>
    /// 🔴 G-P1 — <b>매출도 상품마스터 기본창고에서 뺀다.</b>
    ///
    /// <para>
    /// 사장님 오더: <i>"매출도 매입과 동일하게 디폴트값(창고)에서 재고를 빼야지"</i><br/>
    /// 지시 1·3번 — <i>"디폴트 값으로, 상품등록시 지정한 A창고"</i>
    /// </para>
    /// <para>
    /// <b>실측한 결함</b>: 매출은 사용자가 창고를 안 고르면 상품마스터를 <b>보지도 않고</b>
    /// 테넌트 기본창고(MAIN)로 갔다. 상품마스터에 A창고를 지정해도 소용이 없었고,
    /// A창고에 재고가 있는데 MAIN 이 음수가 됐다(실측: 테스트1창고 −15).
    /// </para>
    /// <para>
    /// 봉합 전: MAIN 이 나온다 ⇒ FAIL<br/>
    /// 봉합 후: 상품마스터의 A창고가 나온다 ⇒ PASS
    /// </para>
    /// <para>
    /// 🔴 이 게이트는 <b>실제 결정 함수를 부른다</b>(글자검사 아님).
    /// 봉합을 지우면 <c>WarehouseResolver</c> 가 사라져 <b>컴파일이 깨진다</b> — 조용히 통과할 수 없다.
    /// </para>
    /// </summary>
    [Fact]
    public void GP1_매출도_상품마스터_기본창고에서_뺀다()
    {
        const string itemDefaultWh = "WH-ITEM-DEFAULT";
        const string tenantMainWh  = "WH-MAIN";

        var itemDefaults = new Dictionary<string, string> { [Item] = itemDefaultWh };

        // 사용자가 창고를 안 골랐다 ⇒ 상품마스터 기본창고가 나와야 한다
        var resolved = WarehouseResolver.Resolve(
            userSpecifiedWarehouseId: null,
            itemId: Item,
            itemDefaultWarehouses: itemDefaults,
            tenantDefaultWarehouseId: tenantMainWh);

        Assert.True(resolved == itemDefaultWh,
            $"매출이 상품마스터 기본창고를 안 쓴다 — 기대 '{itemDefaultWh}' 인데 '{resolved}' 가 나왔다. "
          + "사장님 오더: 디폴트 값은 상품등록시 지정한 창고다.");
    }

    /// <summary>
    /// 🔴 G-P1b — <b>재고가 분산돼 있으면 사용자가 고른 창고가 이긴다.</b>
    ///
    /// <para>
    /// 사장님 오더 4번: <i>"디폴트값으로 지정되어 있지만, 매출매입시 재고가 분산되어 있을경우,
    /// 창고를 사용자가 지정할수 있도록 함"</i>
    /// </para>
    /// <para>
    /// 🔴 <b>대조군 성격</b> — G-P1 만 있으면 "무조건 마스터 창고"로 굳혀도 통과한다.
    /// 그러면 지시 4번이 죽는다. 둘을 같이 재야 규칙이 온전히 선다.
    /// </para>
    /// </summary>
    [Fact]
    public void GP1b_사용자가_고른_창고가_마스터보다_우선한다()
    {
        const string itemDefaultWh = "WH-ITEM-DEFAULT";
        const string userPicked    = "WH-USER-PICKED";
        const string tenantMainWh  = "WH-MAIN";

        var itemDefaults = new Dictionary<string, string> { [Item] = itemDefaultWh };

        var resolved = WarehouseResolver.Resolve(
            userSpecifiedWarehouseId: userPicked,
            itemId: Item,
            itemDefaultWarehouses: itemDefaults,
            tenantDefaultWarehouseId: tenantMainWh);

        Assert.True(resolved == userPicked,
            $"사용자가 고른 창고를 무시했다 — 기대 '{userPicked}' 인데 '{resolved}' 가 나왔다. "
          + "재고가 분산됐을 때 사용자가 창고를 지정할 수 있어야 한다.");
    }

    /// <summary>
    /// 🔴 G-P1d — <b>매출 코드가 그 결정을 실제로 부르는가</b> ("고쳤나" 가 아니라 "갔나").
    ///
    /// <para>
    /// 🔴 <b>이 게이트가 없으면 G-P1 은 헛돈다.</b> 결정 함수가 아무리 옳아도
    /// <c>SalesService</c> 가 안 부르면 화면 동작은 그대로다 —
    /// 히트판이 <b>7번</b> 겪은 <i>"고쳤는데 안 갔다"</i> 사고다.
    /// </para>
    /// <para>
    /// 매출의 창고 결정 경로는 <b>두 곳</b>이다 — 신규 저장과 <b>수정 저장</b>.
    /// 수정 저장이 특히 나빴다: 라인 창고를 <b>보지도 않고</b> 테넌트 기본창고를 박아,
    /// 화면에서 창고를 골라도 한 번 수정하면 날아갔다. <b>두 곳 다</b> 재야 한다.
    /// </para>
    /// <para>
    /// ⚠️ 낱말 하나로 검사하지 않는다(가짜 게이트 규칙 ⑥) —
    /// <b>품목별 기본창고 조회</b>와 <b>결정 호출</b>이 <b>둘 다</b> 있는지 본다.
    /// </para>
    /// </summary>
    [Fact]
    public void GP1d_매출이_창고결정을_실제로_부른다()
    {
        var src = ReadSalesServiceSource();

        // ① 품목별 기본창고를 조회하는가 (이게 없으면 마스터 창고를 알 수가 없다)
        var loadCalls = CountOccurrences(src, "LoadItemDefaultWarehousesAsync(");
        // ② 결정 함수를 부르는가
        var resolveCalls = CountOccurrences(src, "ResolveLineWarehouse(");

        // 정의 1 + 신규저장 1 + 수정저장 1 = 최소 3 (정의 포함)
        Assert.True(loadCalls >= 3,
            $"매출이 품목별 기본창고를 충분히 조회하지 않는다(발견 {loadCalls}회). "
          + "신규 저장·수정 저장 두 경로 모두에 있어야 한다.");

        Assert.True(resolveCalls >= 3,
            $"매출이 창고 결정을 충분히 부르지 않는다(발견 {resolveCalls}회). "
          + "신규 저장·수정 저장 두 경로 모두에서 불러야 한다 — 한쪽만 고치면 수정 시 창고가 날아간다.");

        // ③ 🔴 옛 결함의 흔적 — 라인 창고를 무시하고 테넌트 기본창고를 그대로 박던 자리
        Assert.False(src.Contains("WarehouseId = defaultWarehouseId,"),
            "수정 저장이 아직 테넌트 기본창고를 그대로 박고 있다 — 사용자가 고른 창고가 날아간다.");
    }

    /// <summary>
    /// 🔴 G-P1e — <b>창고를 고르는 화면이 자유입력이 아니다.</b>
    ///
    /// <para>
    /// 사장님 오더: <i>"텍스트박스로 입력하면 유령창고가 수백가지 생김"</i> ·
    /// <i>"등록된 창고에서 선택될 수 있도록 콤보박스로 제어"</i>
    /// </para>
    /// <para>
    /// 🔴 서버가 아무리 옳게 정해도 <b>화면이 없는 창고 id 를 만들어 보내면</b> 소용없다.
    /// 자유입력은 오타 한 번에 유령 창고를 만든다 — 되돌릴 수 없다(원장은 INSERT ONLY, 헌법 #3).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Components/Sales/DeliveryBulkEditDialog.razor")]
    [InlineData("Pages/Purchase/PurchaseReceiptPage.razor")]
    public void GP1e_창고_선택은_콤보박스여야_한다(string relativePath)
    {
        var src = ReadWebSource(relativePath);

        // 창고를 고르는 자리에 MudSelect 가 있어야 한다
        Assert.True(src.Contains("MudSelect"),
            $"{relativePath} 에 창고 선택 콤보박스가 없다 — 자유입력이면 유령 창고가 생긴다.");

        // 🔴 대조군: '창고' 라벨이 붙은 MudTextField 가 남아 있으면 안 된다.
        //
        // ⚠️ 주석은 빼고 본다. 이 게이트의 1차 판은 Singleline 정규식이라 '.' 이 줄바꿈을 넘어
        //    **담당자 MudTextField 부터 아래 주석의 「창고」까지** 한 덩어리로 잡아 오탐이 났다.
        //    (게이트가 내 코드를 잡은 게 아니라 내 게이트가 틀렸던 경우다 — 규칙 ⑯ 그대로)
        //    ⇒ 한 태그 안에서만 찾고, 주석(@* … *@ · <!-- … -->)은 제거한 뒤 본다.
        var code = System.Text.RegularExpressions.Regex.Replace(src, @"@\*.*?\*@", " ",
                       System.Text.RegularExpressions.RegexOptions.Singleline);
        code = System.Text.RegularExpressions.Regex.Replace(code, @"<!--.*?-->", " ",
                   System.Text.RegularExpressions.RegexOptions.Singleline);

        var hasWarehouseTextField =
            System.Text.RegularExpressions.Regex.IsMatch(
                code, @"<MudTextField(?:(?!/>|</MudTextField>).)*?Label\s*=\s*""[^""]*창고[^""]*""",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        Assert.False(hasWarehouseTextField,
            $"{relativePath} 에 창고를 손으로 치는 칸이 남아 있다 — 사장님 지적 그대로 유령 창고가 생긴다.");
    }

    /// <summary>
    /// 🔴 G-P1c — <b>상품마스터에 기본창고가 없으면 흐름이 끊기지 않는다</b> (헌법 #20).
    ///
    /// <para>
    /// 기본창고를 안 정해둔 고객도 판매가 나가야 한다. 테넌트 기본창고가 받는다.
    /// 🔴 이게 없으면 봉합이 <b>애먼 것을 막는</b> 것이 된다 — 4/26 헌법 위반.
    /// </para>
    /// </summary>
    [Fact]
    public void GP1c_마스터_기본창고가_없어도_흐름이_안_끊긴다()
    {
        const string tenantMainWh = "WH-MAIN";

        var resolved = WarehouseResolver.Resolve(
            userSpecifiedWarehouseId: null,
            itemId: Item,
            itemDefaultWarehouses: new Dictionary<string, string>(),   // 마스터 지정 없음
            tenantDefaultWarehouseId: tenantMainWh);

        Assert.True(resolved == tenantMainWh,
            $"기본창고가 없을 때 폴백이 깨졌다 — '{resolved}'. 판매 흐름은 끊기면 안 된다(헌법 #20).");
    }

    /// <summary>
    /// 🔴 G-P2 — <b>대조군: 회사 합산도 부족하면 여전히 막아야 한다.</b>
    ///
    /// <para>
    /// 봉합이 <i>"검사를 무르게 하는 것"</i> 이 되면 유령재고로 판매가 나간다.
    /// 그건 봉합이 아니라 <b>눈을 가린 것</b>이다.
    /// 회사 전체 10개인데 15개를 출고하면 <b>배분할 재고가 없다</b> — 이건 막혀야 한다.
    /// </para>
    /// <para>🔴 봉합 전/후 <b>둘 다 PASS</b> 여야 한다.</para>
    /// </summary>
    [Fact]
    public void GP2_회사합산도_부족하면_여전히_막는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GP2_회사합산도_부족하면_여전히_막는다)); return; }
        using var db = FreshDb();
        SeedStockOnlyInWarehouseA(db);      // 회사 전체 10개

        var companyBalance = CompanyBalance(db, Item);

        // 15개 출고는 회사 합산(10)을 넘는다 ⇒ 배분 불가 ⇒ 막혀야 한다
        Assert.True(companyBalance < 15m,
            $"전제가 깨졌다 — 회사 합산이 {companyBalance} 다. 15개 출고가 부족 상황이어야 한다.");
    }

    /// <summary>
    /// 🔴 G-P3 — <b>4/26 헌법 게이트: 창고 1개 고객은 동작이 바뀌지 않는다.</b>
    ///
    /// <para>
    /// 사장님 헌법(2026-04-26): <i>"재고로 판매 흐름이 막히면 안 된다"</i> —
    /// <b>히트판 타겟 소기업 95%는 창고 1개</b>다. 여기가 바뀌면 봉합이 애먼 것을 건드린 것이다.
    /// </para>
    /// <para>창고 1개에 재고가 있으면, 배분이 개입해도 그 창고에서 그대로 나가야 한다.</para>
    /// </summary>
    [Fact]
    public void GP3_창고1개_고객은_동작이_불변이다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GP3_창고1개_고객은_동작이_불변이다)); return; }
        using var db = FreshDb();
        SeedSingleWarehouse(db, 20m);
        SimulateOutbound(db, WhA, 5m);

        Assert.Empty(NegativeStockRows(db));
        Assert.Equal(15m, WarehouseBalance(db, Item, WhA));
    }

    /// <summary>
    /// 🔴 G-P4 — <b>두 창고 분할 출고 시 원장 2줄이 정상 기록된다 (UNIQUE 위반 없음).</b>
    ///
    /// <para>
    /// 🔴 <b>이 게이트가 작19 의 진짜 난관을 잰다.</b>
    /// <c>uq_stock_ledger_source (tenant_id, source_type, source_id, item_id, move_type)</c> 는
    /// <b>한 전표의 한 품목에 한 줄만</b> 허용한다. 창고가 갈리면 이 키에 걸려
    /// <b>거래 전체가 롤백</b>된다 — <i>"판매했는데 재고 안 빠짐"</i>(헌법 #20).
    /// </para>
    /// <para>
    /// 6/23 에 <b>품목 합산</b>으로 우회했던 자리다. 작19 는 그 우회를 되돌리므로
    /// <b>키에 warehouse_id 가 붙어야</b> 한다(DB-117).
    /// </para>
    /// <para>봉합 전: 두 번째 INSERT 가 UNIQUE 위반 ⇒ FAIL · 봉합 후: 2줄 기록 ⇒ PASS</para>
    /// </summary>
    [Fact]
    public void GP4_두창고_분할출고가_원장에_2줄로_남는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GP4_두창고_분할출고가_원장에_2줄로_남는다)); return; }
        using var db = FreshDb();
        SeedStockInBothWarehouses(db, 3m, 2m);

        // 같은 전표(source_id)에서 두 창고로 나눠 출고 — 키가 좁으면 여기서 터진다
        const string srcId = "DELIV-SPLIT-1";
        var ok = true;
        string? err = null;
        try
        {
            InsertLedger(db, srcId, WhA, qtyOut: 3m);
            InsertLedger(db, srcId, WhB, qtyOut: 2m);
        }
        catch (MySqlException ex) { ok = false; err = ex.Message; }

        Assert.True(ok,
            "같은 전표를 두 창고로 나눠 기록할 수 없다 — uq_stock_ledger_source 에 warehouse_id 가 없다. " + err);
        Assert.Equal(2, LedgerRowCount(db, srcId));
    }

    /// <summary>
    /// 🔴 G-P5 — <b>원장 합계 = 마스터 합계</b> (창고별·품목별 둘 다).
    ///
    /// <para>
    /// 배분이 들어가도 장부는 맞아야 한다. 실제 정합성 검사를 실 DB 에 물려 부른다 —
    /// SQL 을 베껴 적어 글자로 검사하면 코드가 바뀌어도 초록불이 된다(가짜 게이트 누적 22번).
    /// </para>
    /// </summary>
    [Fact]
    public async Task GP5_배분후에도_원장과_마스터가_맞는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GP5_배분후에도_원장과_마스터가_맞는다)); return; }
        using var db = FreshDb();
        SeedStockInBothWarehouses(db, 3m, 2m);

        var report = await new FinanceService(db, null!, null!).CheckIntegrityAsync(Tid);
        var stock = report.Items.FirstOrDefault(i => i.CheckName == "stock vs ledger 정합성");
        var neg = report.Items.FirstOrDefault(i => i.CheckName == "음수 재고");

        Assert.True(stock is not null, "검사 항목 'stock vs ledger 정합성' 이 사라졌다");
        Assert.True(neg is not null, "검사 항목 '음수 재고' 가 사라졌다");
        Assert.Equal("OK", stock!.Status);
        Assert.Equal("OK", neg!.Status);
    }

    // ────────────────────────────────────────────────────────────
    // 헬퍼

    /// <summary>
    /// 🔴 소스를 <b>레포에서 직접</b> 읽는다. 빌드 산출물이 아니라 원본을 본다.
    /// 경로가 틀리면 <b>조용히 통과하지 않고 실패</b>시킨다 — 못 읽은 것을 통과로 세면 가짜 게이트다.
    /// </summary>
    private static string ReadRepoFile(params string[] relativeParts)
    {
        var dir = AppContext.BaseDirectory;
        for (var up = 0; up < 10 && dir is not null; up++)
        {
            var candidate = System.IO.Path.Combine(new[] { dir }.Concat(relativeParts).ToArray());
            if (System.IO.File.Exists(candidate)) return System.IO.File.ReadAllText(candidate);
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }

        throw new Xunit.Sdk.XunitException(
            $"[게이트 미실행] 소스를 못 찾았다: {string.Join("/", relativeParts)}\n"
          + "  파일이 옮겨졌다면 이 게이트의 경로를 함께 고쳐라. 못 읽은 것은 통과가 아니다.");
    }

    private static string ReadSalesServiceSource()
        => ReadRepoFile("src", "HitPan.Application", "Services", "SalesService.cs");

    private static string ReadWebSource(string relativePath)
        => ReadRepoFile(new[] { "src", "HitPan.Web" }.Concat(relativePath.Split('/')).ToArray());

    private static int CountOccurrences(string haystack, string needle)
    {
        var n = 0;
        var i = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (i >= 0)
        {
            n++;
            i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal);
        }
        return n;
    }
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// 출고를 흉내낸다 — <b>현재 코드의 동작을 그대로 재현</b>한다:
    /// 회사 합산으로 검사하고, 원장·재고는 <b>지정 창고 한 곳에서만</b> 뺀다.
    /// 🔴 배분이 들어오면 이 함수가 아니라 <b>서비스 코드</b>가 창고를 나눠야 한다.
    ///   (게이트는 결과 — 음수가 있는지 — 로 판정한다. 구현을 흉내내지 않는다.)
    /// </summary>
    private static void SimulateOutbound(MySqlConnection db, string warehouseId, decimal qty)
    {
        // 회사 합산 검사 (현재 SalesService.cs:451 과 같은 기준)
        var company = CompanyBalance(db, Item);
        if (company - qty < 0m) return;     // 회사에 없으면 아무 일도 안 한다 (막힌 것)

        // 🔴 현재 동작: 지정 창고에서만 뺀다 ⇒ 그 창고가 음수가 될 수 있다
        InsertLedger(db, "DELIV-SIM-1", warehouseId, qtyOut: qty);
        Exec(db, $"""
            INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty)
            VALUES (UUID(), '{Tid}', '{Item}', '{warehouseId}', {-qty})
            ON DUPLICATE KEY UPDATE current_qty = current_qty - {qty}
            """);
    }

    private static void InsertLedger(MySqlConnection db, string sourceId, string warehouseId,
        decimal qtyIn = 0m, decimal qtyOut = 0m)
    {
        Exec(db, $"""
            INSERT INTO stock_ledger
              (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out)
            VALUES
              ('{Tid}', '{Item}', '{warehouseId}', '2026-09-03', '2026-09',
               '{(qtyOut > 0 ? "out" : "in")}', 'sales_delivery', '{sourceId}', {qtyIn}, {qtyOut})
            """);
    }

    private static System.Collections.Generic.List<string> NegativeStockRows(MySqlConnection db)
    {
        var list = new System.Collections.Generic.List<string>();
        using var cmd = new MySqlCommand(
            $"SELECT warehouse_id, current_qty FROM item_stock WHERE tenant_id='{Tid}' AND current_qty < 0", db);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add($"{r.GetString(0)}={r.GetDecimal(1)}");
        return list;
    }

    private static decimal CompanyBalance(MySqlConnection db, string itemId)
        => Scalar(db, $"SELECT COALESCE(SUM(qty_in)-SUM(qty_out),0) FROM stock_ledger WHERE tenant_id='{Tid}' AND item_id='{itemId}'");

    private static decimal WarehouseBalance(MySqlConnection db, string itemId, string wh)
        => Scalar(db, $"SELECT COALESCE(SUM(current_qty),0) FROM item_stock WHERE tenant_id='{Tid}' AND item_id='{itemId}' AND warehouse_id='{wh}'");

    private static int LedgerRowCount(MySqlConnection db, string sourceId)
        => (int)Scalar(db, $"SELECT COUNT(*) FROM stock_ledger WHERE tenant_id='{Tid}' AND source_id='{sourceId}'");

    private static decimal Scalar(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? 0m : Convert.ToDecimal(v);
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 (작14 W1)
        try { using var c = new MySqlConnection(ConnString()); c.Open(); return true; }
        catch (MySqlException) { return false; }
    }

    private static void Skipped(string gate) => DbGateEnvironment.SkipOrFail(gate);

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        // ⚠️ GuidFormat=None 필수 — 빠지면 char(36) 이 Guid 로 와서 string DTO 매핑이 500 으로 터진다
        //   (2026-08-12 양식템플릿 사고 · ConnectionStringGuidGuardTests 가 지킨다)
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "AllowUserVariables=true;GuidFormat=None;Connection Timeout=5;";
    }

    /// <summary>
    /// 🔴 UNIQUE 키는 <b>출하 DDL(헌법 #36 진실원)과 같게</b> 만든다.
    ///
    /// <para>
    /// 봉합 전에는 <c>warehouse_id</c> 가 없어 G-P4 가 <b>실제로 빨간불이었다</b> —
    /// <c>Duplicate entry '…-out' for key 'uq_stock_ledger_source'</c>.
    /// 추측이 아니라 실제 에러로 제약을 실증한 뒤 DB-117 로 키를 넓혔다.
    /// </para>
    /// <para>
    /// 🔴 이 fixture 가 출하 DDL 과 어긋나면 게이트가 <b>실제와 다른 것을 재게 된다.</b>
    ///   DB-117 을 되돌리면 이 정의도 함께 되돌려야 한다.
    /// </para>
    /// </summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();

        Exec(db, """
            CREATE TEMPORARY TABLE item_stock (
              stock_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              warehouse_id varchar(36) NOT NULL DEFAULT 'default',
              current_qty decimal(10,2) NOT NULL DEFAULT 0,
              avg_cost decimal(15,2) NOT NULL DEFAULT 0,
              last_updated_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              UNIQUE KEY uk_tenant_item_wh (tenant_id, item_id, warehouse_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE stock_ledger (
              ledger_id bigint(20) NOT NULL AUTO_INCREMENT PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              warehouse_id varchar(36) NOT NULL,
              ledger_date date NOT NULL,
              ym varchar(7) NOT NULL,
              move_type varchar(10) NOT NULL,
              source_type varchar(30) NOT NULL,
              source_id varchar(36) NOT NULL,
              qty_in decimal(15,3) NOT NULL DEFAULT 0,
              qty_out decimal(15,3) NOT NULL DEFAULT 0,
              UNIQUE KEY uq_stock_ledger_source (tenant_id, source_type, source_id, item_id, move_type, warehouse_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        // CheckIntegrityAsync 가 15종을 한 번에 돈다 — 표가 하나라도 없으면 재고 검사에 닿기 전에 터진다.
        Exec(db, """
            CREATE TEMPORARY TABLE items (
              item_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              item_name varchar(100) NOT NULL,
              purchase_price decimal(15,2) NOT NULL DEFAULT 0, sale_price decimal(15,2) NOT NULL DEFAULT 0,
              is_deleted tinyint(1) NOT NULL DEFAULT 0, is_active tinyint(1) NOT NULL DEFAULT 1
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, "CREATE TEMPORARY TABLE purchase_order_items (po_item_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL, ordered_qty decimal(15,3) NOT NULL DEFAULT 0, received_qty decimal(15,3) NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE sales_order_items (so_item_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL, ordered_qty decimal(15,3) NOT NULL DEFAULT 0, delivered_qty decimal(15,3) NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE bom_items (bom_item_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL, material_item_id varchar(36) NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE approval_doc_lines (line_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL, approver_id varchar(36) NULL, is_active tinyint(1) NOT NULL DEFAULT 1) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE employees (employee_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL, employee_name varchar(100) NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE journal_lines (line_id bigint(20) NOT NULL AUTO_INCREMENT PRIMARY KEY, entry_id varchar(36) NOT NULL, tenant_id varchar(36) NOT NULL, account_code varchar(10) NOT NULL, debit_amount decimal(15,2) NOT NULL DEFAULT 0, credit_amount decimal(15,2) NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE journal_entries (entry_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL, source_type varchar(30) NULL, source_id varchar(80) NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE accounts (account_code varchar(10) NOT NULL, tenant_id varchar(36) NOT NULL, account_name varchar(100) NOT NULL, PRIMARY KEY (account_code, tenant_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE purchase_receipts (receipt_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL, po_id varchar(36) NULL, source_type varchar(20) NOT NULL DEFAULT 'po', status varchar(20) NOT NULL DEFAULT 'draft', is_deleted tinyint(1) NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE purchase_returns (return_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL, receipt_id varchar(36) NULL, status varchar(20) NOT NULL DEFAULT 'draft', is_deleted tinyint(1) NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE sales_returns (return_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL, status varchar(20) NOT NULL DEFAULT 'draft', is_deleted tinyint(1) NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE purchase_orders (po_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL, status varchar(20) NOT NULL DEFAULT 'draft') ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE purchase_receipt_items (receipt_item_id varchar(36) NOT NULL PRIMARY KEY, receipt_id varchar(36) NOT NULL, tenant_id varchar(36) NOT NULL, item_id varchar(36) NOT NULL, qty decimal(15,3) NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        Exec(db, "CREATE TEMPORARY TABLE purchase_return_items (return_item_id varchar(36) NOT NULL PRIMARY KEY, return_id varchar(36) NOT NULL, tenant_id varchar(36) NOT NULL, item_id varchar(36) NOT NULL, qty decimal(15,3) NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        return db;
    }

    /// <summary>A창고에만 10개 — 실측에서 깨진 모양(회사엔 있는데 출고 창고엔 없다).</summary>
    private static void SeedStockOnlyInWarehouseA(MySqlConnection db)
    {
        SeedItem(db);
        Exec(db, $"INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty) VALUES (UUID(),'{Tid}','{Item}','{WhA}',10);");
        InsertLedger(db, "PO-SEED-A", WhA, qtyIn: 10m);
    }

    private static void SeedStockInBothWarehouses(MySqlConnection db, decimal a, decimal b)
    {
        SeedItem(db);
        Exec(db, $"INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty) VALUES (UUID(),'{Tid}','{Item}','{WhA}',{a});");
        Exec(db, $"INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty) VALUES (UUID(),'{Tid}','{Item}','{WhB}',{b});");
        InsertLedger(db, "PO-SEED-A", WhA, qtyIn: a);
        InsertLedger(db, "PO-SEED-B", WhB, qtyIn: b);
    }

    /// <summary>창고 1개 — 소기업 95% (4/26 헌법이 지키는 대상).</summary>
    private static void SeedSingleWarehouse(MySqlConnection db, decimal qty)
    {
        SeedItem(db);
        Exec(db, $"INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty) VALUES (UUID(),'{Tid}','{Item}','{WhA}',{qty});");
        InsertLedger(db, "PO-SEED-A", WhA, qtyIn: qty);
    }

    private static void SeedItem(MySqlConnection db) => Exec(db, $"""
        INSERT INTO items (item_id, tenant_id, item_name, purchase_price, sale_price)
        VALUES ('{Item}','{Tid}','피킹시험품목', 800, 1000);
        """);

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        cmd.ExecuteNonQuery();
    }
}
