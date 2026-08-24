using System.Data;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-65 ~ G-69</b> — BOM 그리드의 <b>단계</b>와 <b>생산가능 수량</b> (20260825작1 W5·W6).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 지시 (8/25)</b>
/// <list type="bullet">
/// <item>W5 — <i>"BOM 그리드에 버전 의미가 반제품→반반제품→…→완제품과 같은 상품의 버전 아님?
/// 볼트너트(1차반제품)-볼트너트오링(완제품)인데 V1로 나옴"</i></item>
/// <item>W6 — <i>"BOM그리드에 반제품 혹은 자재의 수량에 맞춰 생산가능 수가 나와야 함"</i></item>
/// </list>
/// </para>
///
/// <para>
/// 🔴 <b>V1 이 나온 것은 고장이 아니었다.</b> <c>bom_version</c> 은 <b>문서 개정 회차</b>(완성품별 MAX+1)라
/// 볼트너트와 볼트너트오링은 <b>서로 다른 완성품</b>이므로 각각 첫 등록이면 <b>둘 다 필연적으로 v1</b> 이다.
/// 제조 단계는 <b>스키마에 아예 없었다.</b> 그래서 <b>계산으로</b> 만든다 — DB 변경 0.
/// </para>
///
/// <para>
/// 🔴 <b>이 게이트는 실제 <c>BomService.GetListAsync</c> 를 부른다.</b> 알고리즘을 베껴 적어
/// 검사하면 <b>코드가 바뀌어도 시험은 계속 초록</b>이 된다 — 그건 가짜다.
/// 진짜 서비스에 진짜 DB 를 물려 <b>나오는 값</b>으로 판정한다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 화면에 「1차 반제품」 이라는 <b>글자가 뜨는지</b>는 못 잰다.
/// 서비스가 내려보내는 <c>BomLevel</c>·<c>ProducibleQty</c> 숫자까지가 범위다.
/// </para>
/// </remarks>
public sealed class BomLevelProducibleGateTests
{
    private const string Tid = "GATE-W56-TENANT";

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // ────────────────────────────────────────────────────────────────────────────
    //  W5 — 제조 단계
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-65 — <b>사장님 사례 그대로.</b> 볼트너트(1차 반제품) → 볼트너트오링(완제품).
    /// 개정(<c>BomVersion</c>)은 둘 다 1 이지만 <b>단계는 2 와 3 으로 갈려야</b> 한다.
    /// </summary>
    [Fact]
    public async Task G65_볼트너트는_2단계_볼트너트오링은_3단계()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedBoltNutCase(db);

        var list = await NewService(db).GetListAsync(Tid);

        var bn = list.Single(x => x.ProductItemId == "W5-BN");
        var bno = list.Single(x => x.ProductItemId == "W5-BNO");

        // 🔴 사장님이 보신 것 — 개정은 둘 다 1 이다. 이건 고장이 아니다.
        Assert.Equal(1, bn.BomVersion);
        Assert.Equal(1, bno.BomVersion);

        // 🟢 단계는 갈린다 — 이것이 사장님이 원하신 값이다.
        Assert.Equal(2, bn.BomLevel);   // 자재로만 만든다 → 1차 반제품
        Assert.Equal(3, bno.BomLevel);  // 그 반제품을 쓴다 → 그 위 단계
    }

    /// <summary>
    /// 🔴 G-66 — <b>순환참조가 있어도 멈추지 않는다.</b>
    /// 손상된 데이터로 목록 화면이 굳으면 P0 다. 값이 무엇이든 <b>끝나는 것</b>이 합격이다.
    /// </summary>
    [Fact]
    public async Task G66_순환참조_BOM에도_화면이_안_멈춘다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();

        // A 를 만들려면 B 가 필요하고, B 를 만들려면 A 가 필요하다 (손상 데이터)
        SeedItem(db, "CYC-A", "semi_finished");
        SeedItem(db, "CYC-B", "semi_finished");
        SeedBom(db, "CYC-BOM-A", "CYC-A", ver: 1);
        SeedBomItem(db, "CYC-BI-A", "CYC-BOM-A", "CYC-B", qty: 1);
        SeedBom(db, "CYC-BOM-B", "CYC-B", ver: 1);
        SeedBomItem(db, "CYC-BI-B", "CYC-BOM-B", "CYC-A", qty: 1);

        var run = NewService(db).GetListAsync(Tid);
        var done = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.True(ReferenceEquals(done, run),
            "순환참조 BOM 에서 목록 조회가 10초 안에 안 끝났다 — 화면이 굳는다");
        Assert.Equal(2, (await run).Count);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  W6 — 생산가능 수량
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🟢 G-67 — <b>가장 모자란 자재가 생산 수량을 정한다.</b>
    /// A: 재고 100 ÷ 2개 = 50 · B: 재고 30 ÷ 5개 = 6 ⇒ <b>6</b>.
    /// </summary>
    [Fact]
    public async Task G67_가장_모자란_자재가_생산가능수를_정한다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedTwoMaterialBom(db);

        var bom = (await NewService(db).GetListAsync(Tid)).Single(x => x.BomId == "W6-BOM");

        Assert.Equal(6m, bom.ProducibleQty);
    }

    /// <summary>
    /// 🔴 G-68 — <b>창고를 나눠 넣어도 부풀지 않는다.</b>
    /// <c>item_stock</c> 은 창고 단위라 단순 조인하면 같은 자재가 여러 줄로 붙어
    /// <b>자재 수와 원가가 뻥튀기</b>된다. 창고합산 서브쿼리가 살아 있는지 잰다.
    /// </summary>
    [Fact]
    public async Task G68_창고를_나눠도_자재수와_원가가_안_부푼다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedTwoMaterialBom(db);   // 자재 A 를 MAIN 60 · SUB 40 두 창고에 나눠 넣는다

        var bom = (await NewService(db).GetListAsync(Tid)).Single(x => x.BomId == "W6-BOM");

        Assert.Equal(2, bom.MaterialCount);      // 창고가 2개여도 자재는 2종이다
        Assert.Equal(450m, bom.TotalCost);       // 2*100 + 5*50. 단순 조인이면 650 이 된다
        Assert.Equal(6m, bom.ProducibleQty);     // 합산 100 으로 봐야 나오는 값
    }

    /// <summary>
    /// 🟢 G-69 — <b>경계값.</b> 자재가 없는 BOM · 재고 행이 아예 없는 자재 · 로스율.
    /// <c>NULLIF</c>/<c>COALESCE</c> 방어가 빠지면 여기서 NULL 이 새거나 0 나눗셈이 난다.
    /// </summary>
    [Fact]
    public async Task G69_자재0개_재고없음_로스율_경계값()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();

        SeedItem(db, "W6-PROD", "finished");
        SeedItem(db, "W6-MAT-B", "material", price: 50);
        SeedStock(db, "W6-S-B", "W6-MAT-B", "MAIN", 30);

        SeedBom(db, "BOM-EMPTY", "W6-PROD", ver: 1);               // 자재 0개

        SeedItem(db, "W6-MAT-C", "material", price: 10);           // 재고 행 자체가 없다
        SeedBom(db, "BOM-NOSTOCK", "W6-PROD", ver: 2);
        SeedBomItem(db, "BI-C", "BOM-NOSTOCK", "W6-MAT-C", qty: 1);

        SeedBom(db, "BOM-LOSS", "W6-PROD", ver: 3);                // 로스율 10%
        SeedBomItem(db, "BI-L", "BOM-LOSS", "W6-MAT-B", qty: 5, lossRate: 10);

        var list = await NewService(db).GetListAsync(Tid);

        Assert.Equal(0m, list.Single(x => x.BomId == "BOM-EMPTY").ProducibleQty);
        Assert.Equal(0m, list.Single(x => x.BomId == "BOM-NOSTOCK").ProducibleQty);
        Assert.Equal(5m, list.Single(x => x.BomId == "BOM-LOSS").ProducibleQty);  // 30 / 5.5 = 5.45 → 5
    }

    // ── 준비 ────────────────────────────────────────────────────────────────────

    /// <summary>사장님 사례: 볼트·너트(자재) → 볼트너트(반제품) → 볼트너트오링(완제품).</summary>
    private static void SeedBoltNutCase(MySqlConnection db)
    {
        SeedItem(db, "W5-BOLT", "material", price: 100);
        SeedItem(db, "W5-NUT", "material", price: 50);
        SeedItem(db, "W5-ORING", "material", price: 30);
        SeedItem(db, "W5-BN", "semi_finished");
        SeedItem(db, "W5-BNO", "finished");

        SeedBom(db, "W5-BOM-BN", "W5-BN", ver: 1);
        SeedBomItem(db, "W5-BI-1", "W5-BOM-BN", "W5-BOLT", qty: 1);
        SeedBomItem(db, "W5-BI-2", "W5-BOM-BN", "W5-NUT", qty: 1);

        SeedBom(db, "W5-BOM-BNO", "W5-BNO", ver: 1);
        SeedBomItem(db, "W5-BI-3", "W5-BOM-BNO", "W5-BN", qty: 1);
        SeedBomItem(db, "W5-BI-4", "W5-BOM-BNO", "W5-ORING", qty: 1);
    }

    /// <summary>자재 A(1개당 2개, 재고 100=60+40 두 창고) · 자재 B(1개당 5개, 재고 30).</summary>
    private static void SeedTwoMaterialBom(MySqlConnection db)
    {
        SeedItem(db, "W6-PROD", "finished");
        SeedItem(db, "W6-MAT-A", "material", price: 100);
        SeedItem(db, "W6-MAT-B", "material", price: 50);

        SeedBom(db, "W6-BOM", "W6-PROD", ver: 1);
        SeedBomItem(db, "W6-BI-A", "W6-BOM", "W6-MAT-A", qty: 2);
        SeedBomItem(db, "W6-BI-B", "W6-BOM", "W6-MAT-B", qty: 5);

        SeedStock(db, "W6-S-A1", "W6-MAT-A", "MAIN", 60);
        SeedStock(db, "W6-S-A2", "W6-MAT-A", "SUB", 40);
        SeedStock(db, "W6-S-B", "W6-MAT-B", "MAIN", 30);
    }

    private static void SeedItem(MySqlConnection db, string id, string type, decimal price = 0) =>
        Exec(db, $"""
            INSERT INTO items (item_id, tenant_id, item_name, item_type, purchase_price, is_active, is_deleted)
            VALUES ('{id}', '{Tid}', '{id}', '{type}', {price}, 1, 0);
            """);

    private static void SeedBom(MySqlConnection db, string bomId, string productId, int ver) =>
        Exec(db, $"""
            INSERT INTO bom_headers (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active, created_at)
            VALUES ('{bomId}', '{Tid}', '{productId}', '{bomId}', {ver}, 1, 1, NOW(6));
            """);

    private static void SeedBomItem(MySqlConnection db, string id, string bomId,
                                    string materialId, decimal qty, decimal lossRate = 0) =>
        Exec(db, $"""
            INSERT INTO bom_items (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, loss_rate)
            VALUES ('{id}', '{bomId}', '{Tid}', 1, '{materialId}', {qty}, {lossRate});
            """);

    private static void SeedStock(MySqlConnection db, string id, string itemId,
                                  string warehouse, decimal qty) =>
        Exec(db, $"""
            INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty)
            VALUES ('{id}', '{Tid}', '{itemId}', '{warehouse}', {qty});
            """);

    // ── 배선 ────────────────────────────────────────────────────────────────────

    /// <summary>🔴 <b>진짜 서비스</b>를 만든다 — 알고리즘을 베껴 적지 않는다.</summary>
    private static BomService NewService(IDbConnection db) =>
        new(db, new Mock<IEventPublisher>().Object, new Mock<IAuditService>().Object);

    private static void Skipped() =>
        Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라.");

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "Hitpan2025!";
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private static bool ServerAvailable()
    {
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

    /// <summary>
    /// 시험용 <c>TEMPORARY</c> 표 4개. 커넥션이 닫히면 서버가 알아서 지운다 —
    /// 이 DB 의 실제 표는 <b>가리기만 하고 건드리지 않는다</b>(헌법 #39).
    /// </summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();
        Exec(db, """
            CREATE TEMPORARY TABLE items (
              item_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              item_name varchar(200) NOT NULL,
              item_type longtext NOT NULL,
              purchase_price decimal(15,2) NULL,
              cost_price decimal(15,2) NULL,
              is_active tinyint(1) NOT NULL DEFAULT 1,
              is_deleted tinyint(1) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE bom_headers (
              bom_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              product_item_id varchar(36) NOT NULL,
              bom_name varchar(100) NOT NULL,
              bom_version int NOT NULL DEFAULT 1,
              is_default tinyint(1) NOT NULL DEFAULT 1,
              is_active tinyint(1) NOT NULL DEFAULT 1,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE bom_items (
              bom_item_id varchar(36) NOT NULL PRIMARY KEY,
              bom_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              seq_no int NOT NULL DEFAULT 1,
              material_item_id varchar(36) NOT NULL,
              qty decimal(10,2) NOT NULL DEFAULT 1,
              loss_rate decimal(5,2) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE item_stock (
              stock_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              warehouse_id varchar(36) NOT NULL DEFAULT 'MAIN',
              current_qty decimal(10,2) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        return db;
    }

    private static void Exec(IDbConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
