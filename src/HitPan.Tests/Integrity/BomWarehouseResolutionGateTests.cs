using System.Data;
using HitPan.Application.DTOs.Bom;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-B1 ~ G-B3</b> — BOM 생산의 <b>창고결정</b> (20260904작1).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 이 게이트를 짰나</b><br/>
/// 20260903작19 에서 <b>매출</b>의 창고결정 3단을 봉합했다
/// (① 사용자지정 → ② 상품마스터 기본창고 → ③ 테넌트 MAIN, <see cref="WarehouseResolver"/>).
/// 그때 <b>BOM 생산은 재지 않았다.</b> 안 잰 것을 "괜찮다" 로 넘기면
/// 매출에서 겪은 사고를 형태만 바꿔 반복한다.
/// </para>
///
/// <para>
/// 🔴 <b>실측한 결함</b> (정적 판정 → 이 게이트로 실증)<br/>
/// <see cref="BomService.AssembleAsync"/> 는 창고를 <c>warehouses</c> 표에서
/// <c>wh_code='MAIN'</c> 우선으로 <b>한 번 고르고 끝</b>이다(BomService.cs:528-536).
/// <c>items.default_warehouse_id</c>(DB-105) 를 <b>조회조차 하지 않는다</b> —
/// 낱말 등장 횟수: 매입 5 · 매출 4 · <b>BOM 0</b>.
/// </para>
///
/// <para>
/// ⇒ 상품마스터에 A창고를 지정해도 BOM 생산은 MAIN 에서 자재를 빼려 한다.
/// <b>매출이 어제까지 갖고 있던 병증과 같은 모양이다</b>(테스트1창고 −15 를 만든 그 경로).
/// </para>
///
/// <para>
/// 🔴 <b>이 게이트는 진짜 <see cref="BomService"/> 를 부른다.</b> 알고리즘을 베껴 적으면
/// 코드가 바뀌어도 시험은 계속 초록이 된다 — 그건 가짜다(누적 24번 겪었다).
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 생산 화면에 창고 칸이 <b>뜨는지</b>는 못 잰다.
/// 서비스가 실제로 어느 창고에서 빼고 어디에 넣는지까지가 범위다.
/// </para>
/// </remarks>
public sealed class BomWarehouseResolutionGateTests
{
    private const string Tid = "GATE-BOMWH-TENANT";
    private const string Uid = "GATE-BOMWH-USER";

    private const string Product = "BOMWH-PRODUCT";   // 완제품
    private const string Material = "BOMWH-MATERIAL"; // 자재
    private const string BomId = "BOMWH-BOM-1";

    private const string WhMain = "BOMWH-MAIN";  // 테넌트 기본창고 (MAIN)
    private const string WhA = "BOMWH-A";        // 상품마스터가 가리키는 창고 — 자재가 여기 있다

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // ────────────────────────────────────────────────────────────────────────────
    //  G-B1 — 자재가 상품마스터 기본창고에 있으면 생산이 되어야 한다
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-B1 — <b>자재가 상품마스터 기본창고(A)에 있으면 생산이 된다.</b>
    ///
    /// <para>
    /// 사장님 오더(9/3, 매출에 적용한 것과 같은 규칙):
    /// <i>"디폴트 값으로, 상품등록시 지정한 A창고"</i>
    /// </para>
    /// <para>
    /// <b>상황</b>: 자재 100개가 <b>A창고</b>에만 있다. MAIN 에는 0개.
    /// 자재의 상품마스터 기본창고 = A창고.
    /// </para>
    /// <para>
    /// 봉합 전: MAIN 에서 빼려다 <b>"자재 재고 부족"</b> 으로 생산 전체가 실패 ⇒ FAIL<br/>
    /// 봉합 후: A창고에서 빠져 생산 성공 ⇒ PASS
    /// </para>
    /// <para>
    /// 🔴 <b>이것이 실제 업무를 막는다</b> — 창고를 나눠 쓰는 고객은 BOM 생산을 아예 못 한다(헌법 #20).
    /// </para>
    /// </summary>
    [Fact]
    public async Task GB1_자재가_상품마스터_기본창고에_있으면_생산이_된다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GB1_자재가_상품마스터_기본창고에_있으면_생산이_된다)); return; }

        using var db = FreshDb();
        SeedMasters(db);
        // 자재는 A창고에만 100개. MAIN 에는 행조차 없다.
        SeedStock(db, Material, WhA, 100m);
        // 자재의 상품마스터 기본창고 = A창고
        SetItemDefaultWarehouse(db, Material, WhA);

        var svc = NewService(db);

        var ex = await Record.ExceptionAsync(() => svc.AssembleAsync(
            new BomAssembleDto { BomId = BomId, ProduceQty = 10m, Memo = "G-B1" },
            Tid, Uid));

        Assert.True(ex is null,
            "자재가 상품마스터 기본창고(A창고)에 있는데 생산이 실패했다 — "
          + $"BOM 생산이 A창고를 안 보고 MAIN 에서 빼려 한 것이다. 실제 예외: {ex?.Message}");
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G-B2 — 자재가 실제로 A창고에서 빠졌는가 ("됐다" 가 아니라 "어디서 뺐나")
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-B2 — <b>자재는 A창고에서 빠지고, 원장도 A창고로 남는다.</b>
    ///
    /// <para>
    /// 🔴 <b>G-B1 만으로는 부족하다.</b> "MAIN 에도 재고를 넣어두면" G-B1 은 통과한다 —
    /// 그러면 <b>엉뚱한 창고에서 빼고도 초록불</b>이 된다. 어느 창고에서 뺐는지를 따로 재야 한다.
    /// </para>
    /// <para>
    /// <b>상황</b>: 자재가 A창고 100 · MAIN 100 <b>양쪽에</b> 있다. 마스터 기본창고는 A.<br/>
    /// 기대: A 가 90 으로 줄고 MAIN 은 100 그대로.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GB2_자재는_마스터_기본창고에서_빠진다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GB2_자재는_마스터_기본창고에서_빠진다)); return; }

        using var db = FreshDb();
        SeedMasters(db);
        SeedStock(db, Material, WhA, 100m);
        SeedStock(db, Material, WhMain, 100m);
        SetItemDefaultWarehouse(db, Material, WhA);

        var svc = NewService(db);
        await svc.AssembleAsync(
            new BomAssembleDto { BomId = BomId, ProduceQty = 10m, Memo = "G-B2" },
            Tid, Uid);

        var qtyA = QtyOf(db, Material, WhA);
        var qtyMain = QtyOf(db, Material, WhMain);

        // 🔴 두 값을 한 번에 판정한다 — 앞 Assert 에서 멈추면 "어디서 뺐는지" 가 안 보인다.
        //    A 가 안 줄었다는 사실만으로는 MAIN 을 건드렸는지 알 수 없고,
        //    그 둘을 같이 봐야 "엉뚱한 창고에서 뺐다" 가 증거가 된다.
        Assert.True(qtyA == 90m && qtyMain == 100m,
            $"자재를 마스터 기본창고(A)에서 빼지 않았다 — A창고 기대 90/실제 {qtyA}, "
          + $"MAIN 기대 100/실제 {qtyMain}. "
          + "BOM 생산이 상품마스터 기본창고를 보지 않고 테넌트 MAIN 에서 뺀다.");
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G-B4 — 창고가 여러 개라고 자재가 더 빠지면 안 된다 (과차감)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-B4 — <b>창고 수가 늘어도 빠지는 자재 총량은 같아야 한다.</b>
    ///
    /// <para>
    /// 🔴 <b>G-B2 를 짜다 발견한 별개 결함이다.</b> 10개 생산인데 자재가 <b>20개</b> 빠졌다.
    /// 창고가 3개면 30개가 빠진다 — <b>창고를 늘릴수록 자재가 더 녹는다.</b>
    /// </para>
    /// <para>
    /// <b>원인</b>(SQL 로 확증): <c>GetAsync</c> 의 자재 조회가
    /// <c>LEFT JOIN item_stock ON tenant_id, item_id</c> 뿐이라 <b>창고 필터도 합산도 없다</b>
    /// (BomService.cs:222). 자재가 2창고에 있으면 BOM 자재 1건이 <b>2행</b>이 되고,
    /// <c>AssembleAsync</c> 가 그 목록을 그대로 돌며(:565) <b>같은 자재를 두 번 차감</b>한다.
    /// </para>
    /// <para>
    /// 🔴 <b>이것은 창고결정(G-B1·G-B2)과 다른 축이다.</b> 창고를 옳게 골라도
    /// 목록이 부풀면 여전히 과차감한다 — 그래서 따로 잰다.
    /// </para>
    /// <para>
    /// ⚠️ 원장·완제품도 같은 목록을 쓰므로 <b>원가와 분개도 함께 부푼다</b>(회계 오염).
    /// </para>
    /// </summary>
    [Fact]
    public async Task GB4_창고가_여러개라고_자재가_더_빠지지_않는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GB4_창고가_여러개라고_자재가_더_빠지지_않는다)); return; }

        using var db = FreshDb();
        SeedMasters(db);
        // 자재 총 200 = A 100 + MAIN 100. 10개 생산이면 자재는 10 만 빠져야 한다.
        SeedStock(db, Material, WhA, 100m);
        SeedStock(db, Material, WhMain, 100m);

        var svc = NewService(db);
        await svc.AssembleAsync(
            new BomAssembleDto { BomId = BomId, ProduceQty = 10m, Memo = "G-B4" },
            Tid, Uid);

        var total = QtyOf(db, Material, WhA) + QtyOf(db, Material, WhMain);

        Assert.True(total == 190m,
            $"창고가 2개라고 자재가 더 빠졌다 — 총 재고 기대 190(200−10) 인데 {total} 다. "
          + "BOM 자재 목록이 창고 수만큼 부풀어 같은 자재를 여러 번 차감한다(BomService.cs:222 조인).");
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G-B3 — 대조군: 마스터 기본창고가 없으면 흐름이 끊기지 않는다 (헌법 #20)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-B3 — <b>대조군.</b> 상품마스터에 기본창고를 안 정해둔 고객도 생산이 된다.
    ///
    /// <para>
    /// 🔴 G-B1·G-B2 만 있으면 <b>"무조건 마스터 창고"</b> 로 굳혀도 통과한다.
    /// 그러면 기본창고를 안 쓰는 고객(대다수)의 생산이 죽는다 — 헌법 #20 위반.
    /// ③ 테넌트 MAIN 폴백이 살아 있는지 같이 재야 규칙이 온전히 선다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GB3_마스터_기본창고가_없어도_생산이_안_끊긴다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GB3_마스터_기본창고가_없어도_생산이_안_끊긴다)); return; }

        using var db = FreshDb();
        SeedMasters(db);
        // 자재는 MAIN 에만 있고, 마스터 기본창고는 지정하지 않았다(NULL).
        SeedStock(db, Material, WhMain, 100m);

        var svc = NewService(db);

        var ex = await Record.ExceptionAsync(() => svc.AssembleAsync(
            new BomAssembleDto { BomId = BomId, ProduceQty = 10m, Memo = "G-B3" },
            Tid, Uid));

        Assert.True(ex is null,
            "상품마스터에 기본창고를 안 정해둔 고객의 생산이 끊겼다 — "
          + $"③ 테넌트 MAIN 폴백이 죽었다(헌법 #20). 실제 예외: {ex?.Message}");

        Assert.True(QtyOf(db, Material, WhMain) == 90m,
            "폴백 경로에서 MAIN 자재가 안 빠졌다.");
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G-B5 — 창고를 안 쓰는 고객 (대다수) — 사장님 지시 2026-09-04
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-B5 — <b>창고를 안 쓰는 고객도 재고가 정상으로 돌아간다.</b>
    ///
    /// <para>
    /// <b>사장님 지시 (2026-09-04)</b><br/>
    /// <i>"창고를 별도로 선택하지 않을시는 사용하지 않고도 재고가 정상적으로 돌아가도록"</i><br/>
    /// <i>"사실 대부분 고객사들은 창고를 사용하지 않을거야. 도소매 업체들은 대부분
    /// 사업장에서 상품을 보관하고 출고하기 때문이야."</i><br/>
    /// <i>"미지정 그게 상품마스터의 기본 디폴트 값이야. 창고는 물류까지 돌리는
    /// 중형이상의 유통업장을 운영하는 사장들에게 필요한 <b>기능</b>이야."</i>
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>창고는 「옵션」이 아니라 「기능」이다</b> (사장님 정정).<br/>
    /// 옵션이라 부르면 <b>부가물로 취급해 대충 다뤄도 된다</b>고 읽힌다. 기능이므로
    /// 중형 이상 유통업장에서 <b>제대로 서 있어야</b> 하고(G-B1·G-B2·G-B4),
    /// <b>동시에</b> 안 쓰는 다수는 그 존재를 몰라도 재고가 정상으로 돌아야 한다(이 게이트).
    /// 둘 중 하나만 만족하면 실패다.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>이것이 다수 경로다.</b> G-B1·G-B2·G-B4 가 재는 다창고는 <b>중형 이상 소수</b>고,
    /// 이 시험이 재는 모양이 <b>대다수 도소매 고객</b>이다. 다수가 깨지면 소수를 고친 의미가 없다.
    /// </para>
    ///
    /// <para>
    /// <b>실제 고객 모양</b> — 창고가 "0개" 가 아니다. 회사 생성 시
    /// <c>CompanyBootstrapProvisioner</c> 가 <c>MAIN</c> 기본창고 1행을 만든다(:280-287).
    /// 사용자는 그 존재를 모르고, 상품등록 화면의 「기본 창고」 는 <b>「미지정」</b> 으로 둔다
    /// (<c>ItemDetail.razor:103</c> — 안내문 <i>"비워두면 회사 기본창고로 입고됩니다"</i>).
    /// ⇒ <b>창고 1개 + 마스터 기본창고 전부 NULL</b> 이 다수 고객의 상태다.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>G-B3 과 무엇이 다른가</b>: G-B3 은 창고가 <b>2개</b> 있는 상태에서 미지정을 잰다.
    /// 이 시험은 창고가 <b>MAIN 하나뿐</b>이다 — 다수 고객의 실제 조건이고,
    /// 폴백이 "고를 게 없을 때" 도 옳게 도는지는 따로 재야 한다.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>생산됐다로 끝내지 않는다</b> — 자재가 빠지고, 완제품이 들어오고,
    /// <b>원장(<c>stock_ledger</c>)이 그 창고로 남는지</b>까지 본다.
    /// 원장과 재고가 다른 창고를 가리키면 재고 정합성 검사가 거짓 경보를 낸다(9/3 작18 자리).
    /// </para>
    /// </summary>
    [Fact]
    public async Task GB5_창고를_안_쓰는_고객도_재고가_정상으로_돈다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GB5_창고를_안_쓰는_고객도_재고가_정상으로_돈다)); return; }

        using var db = FreshDb();
        SeedSingleWarehouseMasters(db);          // 창고는 MAIN 하나뿐
        SeedStock(db, Material, WhMain, 100m);   // 자재 100 (마스터 기본창고는 NULL = 「미지정」)

        var svc = NewService(db);
        var ex = await Record.ExceptionAsync(() => svc.AssembleAsync(
            new BomAssembleDto { BomId = BomId, ProduceQty = 10m, Memo = "G-B5" },
            Tid, Uid));

        Assert.True(ex is null,
            "창고를 안 쓰는 고객(대다수)의 BOM 생산이 끊겼다 — "
          + $"창고 기능을 안 건드린 고객이 피해를 봤다. 실제 예외: {ex?.Message}");

        // ① 자재가 빠졌나
        var matQty = QtyOf(db, Material, WhMain);
        // ② 완제품이 들어왔나
        var prodQty = QtyOf(db, Product, WhMain);
        // ③ 원장이 같은 창고로 남았나 (재고와 원장이 갈리면 정합성 검사가 거짓 경보)
        var ledgerOther = LedgerRowsNotIn(db, WhMain);

        Assert.True(matQty == 90m && prodQty == 10m && ledgerOther == 0,
            $"창고 미사용 고객의 재고가 정상으로 안 돈다 — "
          + $"자재 기대 90/실제 {matQty}, 완제품 기대 10/실제 {prodQty}, "
          + $"MAIN 아닌 창고의 원장 줄 기대 0/실제 {ledgerOther}.");
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  받침
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>🔴 <b>진짜 서비스</b>를 만든다 — 알고리즘을 베껴 적지 않는다.</summary>
    private static BomService NewService(IDbConnection db) =>
        new(db, new Mock<IEventPublisher>().Object, new Mock<IAuditService>().Object);

    private static void Skipped(string gate) => DbGateEnvironment.SkipOrFail(gate);

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        // 🔴 비밀번호를 코드에 적지 않는다 — 기존 게이트 관례를 따른다.
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 실패로 드러난다
        try
        {
            using var c = new MySqlConnection(ConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    private static decimal QtyOf(MySqlConnection db, string itemId, string warehouseId)
    {
        using var cmd = new MySqlCommand(
            "SELECT COALESCE(SUM(current_qty),0) FROM item_stock "
          + "WHERE tenant_id=@t AND item_id=@i AND warehouse_id=@w", db);
        cmd.Parameters.AddWithValue("@t", Tid);
        cmd.Parameters.AddWithValue("@i", itemId);
        cmd.Parameters.AddWithValue("@w", warehouseId);
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    /// <summary>
    /// MAIN 창고가 아닌 곳에 남은 원장 줄 수. 창고 미사용 고객이라면 <b>0</b> 이어야 한다.
    /// </summary>
    private static int LedgerRowsNotIn(MySqlConnection db, string warehouseId)
    {
        using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@t AND warehouse_id <> @w", db);
        cmd.Parameters.AddWithValue("@t", Tid);
        cmd.Parameters.AddWithValue("@w", warehouseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// 🔴 <b>창고를 안 쓰는 고객</b> — 회사 생성 시 자동으로 만들어지는 <c>MAIN</c> 1개뿐이고
    /// 상품마스터의 기본창고는 <b>「미지정」(NULL)</b> 이다.
    /// <c>CompanyBootstrapProvisioner:280-287</c> 이 실제로 만드는 모양 그대로다.
    /// </summary>
    private static void SeedSingleWarehouseMasters(MySqlConnection db)
    {
        Exec(db, $"""
            INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at)
            VALUES ('{WhMain}','{Tid}','MAIN','기본창고','normal',1, NOW(6), NOW(6));
            """);

        Exec(db, $"""
            INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit,
                               purchase_price, sale_price, is_deleted, is_active, created_at, updated_at)
            VALUES ('{Product}','{Tid}','BOMWH-P','생산완제품','finished','EA', 0, 5000, 0, 1, NOW(6), NOW(6)),
                   ('{Material}','{Tid}','BOMWH-M','생산자재','material','EA', 100, 0, 0, 1, NOW(6), NOW(6));
            """);

        Exec(db, $"""
            INSERT INTO bom_headers (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active)
            VALUES ('{BomId}','{Tid}','{Product}','생산완제품 BOM', 1, 1, 1);
            """);

        Exec(db, $"""
            INSERT INTO bom_items (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate)
            VALUES (UUID(),'{BomId}','{Tid}', 1, '{Material}', 1, 'EA', 0);
            """);
    }

    private static void SetItemDefaultWarehouse(MySqlConnection db, string itemId, string warehouseId) =>
        Exec(db, $"UPDATE items SET default_warehouse_id='{warehouseId}' WHERE item_id='{itemId}' AND tenant_id='{Tid}';");

    private static void SeedStock(MySqlConnection db, string itemId, string warehouseId, decimal qty) =>
        Exec(db, $"""
            INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost)
            VALUES (UUID(), '{Tid}', '{itemId}', '{warehouseId}', {qty}, 100);
            """);

    /// <summary>완제품 1개 = 자재 1개. 창고 2개(MAIN·A).</summary>
    private static void SeedMasters(MySqlConnection db)
    {
        // 🔴 NOT NULL 이고 기본값 없는 컬럼을 실제 스키마에서 확인해 채운다
        //    (wh_type·created_at·updated_at · items 의 item_code·unit).
        //    빠뜨리면 "Field ... doesn't have a default value" 로 죽고, 그것을
        //    창고 결함으로 오독하게 된다.
        Exec(db, $"""
            INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at)
            VALUES ('{WhMain}','{Tid}','MAIN','기본창고','normal',1, NOW(6), NOW(6)),
                   ('{WhA}','{Tid}','WH-A','A창고','normal',1, NOW(6), NOW(6));
            """);

        Exec(db, $"""
            INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit,
                               purchase_price, sale_price, is_deleted, is_active, created_at, updated_at)
            VALUES ('{Product}','{Tid}','BOMWH-P','생산완제품','finished','EA', 0, 5000, 0, 1, NOW(6), NOW(6)),
                   ('{Material}','{Tid}','BOMWH-M','생산자재','material','EA', 100, 0, 0, 1, NOW(6), NOW(6));
            """);

        Exec(db, $"""
            INSERT INTO bom_headers (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active)
            VALUES ('{BomId}','{Tid}','{Product}','생산완제품 BOM', 1, 1, 1);
            """);

        Exec(db, $"""
            INSERT INTO bom_items (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate)
            VALUES (UUID(),'{BomId}','{Tid}', 1, '{Material}', 1, 'EA', 0);
            """);
    }

    /// <summary>
    /// 🔴 <b>TEMPORARY 표</b> — 이 DB 의 실제 표는 가리기만 하고 건드리지 않는다(헌법 #39).
    /// 커넥션이 닫히면 서버가 알아서 지운다.
    /// </summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();

        // 🔴 표 정의를 손으로 베끼지 않는다 — 실제 표의 DDL 을 SHOW CREATE TABLE 로 읽어
        //    TEMPORARY 로 바꿔 만든다. 손으로 적으면 컬럼 하나 빠질 때마다 게이트가
        //    'Unknown column' 으로 죽고, 그걸 "창고 결함 재현" 으로 오독하게 된다
        //    (실제로 1차 bom_name · 2차 i.spec 로 두 번 그렇게 났다).
        //
        //    ⚠️ CREATE TEMPORARY TABLE x LIKE x 는 MariaDB 가 거부한다("Not unique table/alias")
        //       — 같은 이름이라 LIKE 의 원본을 임시표 자신으로 해석한다. 그래서 DDL 을 직접 가져온다.
        //
        //    TEMPORARY 라 같은 이름의 실제 표를 이 커넥션에서만 가리며, 커넥션이 닫히면 사라진다.
        //    실제 표는 읽지도 쓰지도 않는다(헌법 #39).
        foreach (var t in new[]
                 {
                     "warehouses", "items", "item_stock", "stock_ledger", "stock_adjust_logs",
                     "bom_headers", "bom_items", "bom_cost_cache",
                     "journal_entries", "journal_lines", "accounts",
                 })
        {
            Exec(db, $"CREATE TEMPORARY TABLE {t} {TableDefinitionOf(db, t)};");
        }

        return db;
    }

    /// <summary>
    /// 실제 표의 정의를 <c>SHOW CREATE TABLE</c> 로 읽어 <c>CREATE TABLE `x`</c> 머리만 떼어낸다.
    /// 남는 것은 <c>(컬럼들…) ENGINE=…</c> 이라 <c>CREATE TEMPORARY TABLE x</c> 뒤에 그대로 붙는다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>FK 는 지운다</b> — TEMPORARY 표는 FK 를 가질 수 없고, 참조 대상이 실제 표로 걸리면
    /// 시험 데이터가 실제 표를 건드리게 된다(헌법 #39 위반). 창고결정을 재는 데 FK 는 필요 없다.
    /// </remarks>
    private static string TableDefinitionOf(MySqlConnection db, string table)
    {
        using var cmd = new MySqlCommand($"SHOW CREATE TABLE `{table}`;", db);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException($"표 정의를 못 읽었다: {table}");
        var ddl = r.GetString(1);
        r.Close();

        var head = ddl.IndexOf('(');
        var body = ddl[head..];

        // FK 절 제거 — 줄 단위로 걸러낸다.
        var kept = body
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // FK 를 빼면서 남은 꼬리 쉼표를 정리한다 (")" 직전 줄의 끝 쉼표).
        for (var i = kept.Count - 1; i >= 0; i--)
        {
            var t = kept[i].TrimEnd();
            if (t.StartsWith(")")) continue;
            if (t.EndsWith(",")) kept[i] = t[..^1];
            break;
        }

        return string.Join("\n", kept);
    }

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        cmd.ExecuteNonQuery();
    }
}
