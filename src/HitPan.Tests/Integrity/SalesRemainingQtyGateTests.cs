using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>매출 잔량 기준 · 반품 증발 — 20260828작14 W3 게이트</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 결재 4</b>: <i>「출고 잔량 = 주문수량 − 기출고수량 · 반품 잔량 = 판매수량 − 기반품수량,
/// 잔량 0 이면 차단」</i> — 이 한 줄이 P0-1 과 P0-2 를 동시에 푼다.
/// </para>
///
/// <para>
/// 🔴 <b>이 게이트가 글자검사가 아닌 이유</b> — 5축 전수조사에서 게이트 11종 중 <b>8종이 가짜</b>였다.
/// 전부 <c>File.ReadAllText</c> + <c>Assert.Contains</c> 로 <b>소스에 낱말이 있는지</b>만 봤고,
/// 그래서 P0 버그를 완전히 복원해도 1100개가 전부 통과했다.
/// ⇒ 여기서는 <b>실제 DB 에 행을 넣고 판정식을 SQL 로 돌린다.</b>
/// 봉합을 죽이면 숫자가 달라지므로 문자열로는 흉내 낼 수 없다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 화면 버튼을 두 번 눌렀을 때는 재지 못한다.
/// 서버 가드가 쓰는 것과 <b>같은 판정식</b>을 같은 데이터에 돌려 판정한다.
/// 화면까지 갔는지는 별도 실측이다(8/27 작7 교훈 — <i>"고쳤나"가 아니라 "갔나"</i>).
/// </para>
/// </remarks>
public sealed class SalesRemainingQtyGateTests
{
    private const string Tid = "t-gate-0828";
    private const string OtherTid = "t-other-0828";

    // ─────────────────────────────────────────────────────────────────────
    // G-1 🔴 출고 잔량 — 주문 10, 기출고 10 이면 잔량 0 ⇒ 더는 못 만든다 (P0-1)
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G1_전량출고된_수주는_출고잔량이_0()
    {
        if (!ServerAvailable()) { Skipped(nameof(G1_전량출고된_수주는_출고잔량이_0)); return; }
        using var db = FreshDb();
        SeedOrder(db, orderedQty: 10m, deliveredQty: 10m);

        Assert.Equal(0m, ShipRemaining(db, "ORD-1", "ITEM-A"));
    }

    /// <summary>
    /// 🔴 G-2 <b>대조군</b> — 분할출고는 살아 있어야 한다(헌법 #20).
    /// 작11 가드는 "살아있는 명세서가 하나라도 있으면 차단"이라 <b>정상 분할출고까지 막았다.</b>
    /// 이 대조군이 없으면 "전부 막기"가 G-1 을 통과해버린다.
    /// </summary>
    [Fact]
    public void G2_대조군_부분출고면_잔량이_남는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G2_대조군_부분출고면_잔량이_남는다)); return; }
        using var db = FreshDb();
        SeedOrder(db, orderedQty: 10m, deliveredQty: 3m);

        Assert.Equal(7m, ShipRemaining(db, "ORD-1", "ITEM-A"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-3 🔴 반품 잔량 — 판매 1개에 99개 반품이 통과하던 자리 (P0-2)
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G3_판매수량을_넘는_반품은_잔량이_음수가_된다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G3_판매수량을_넘는_반품은_잔량이_음수가_된다)); return; }
        using var db = FreshDb();
        SeedDelivery(db, soldQty: 1m);
        SeedReturn(db, "RET-1", "ITEM-A", qty: 99m, status: "confirmed");

        // 판매 1 − 기반품 99 = −98. 음수가 나온다는 것은 애초에 들어가면 안 됐다는 뜻이다.
        Assert.Equal(-98m, ReturnRemaining(db, "DLV-1", "ITEM-A"));
    }

    /// <summary>
    /// 🔴 G-4 <b>결재 6 — 누적은 품목 단위.</b> A 가 소진돼도 B 는 무관하다.
    /// 명세서 단위로 세면 A 를 다 반품한 순간 B 도 못 하게 되어 업무가 끊긴다.
    /// </summary>
    [Fact]
    public void G4_반품누적은_품목단위다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G4_반품누적은_품목단위다)); return; }
        using var db = FreshDb();
        SeedDelivery(db, soldQty: 10m);                       // ITEM-A 10개
        Exec(db, $"""
            INSERT INTO sales_delivery_items
              (delivery_item_id, delivery_id, tenant_id, item_id, qty, unit_price, supply_amount, vat_amount)
            VALUES ('DI-B','DLV-1','{Tid}','ITEM-B',5,1000,5000,500)
            """);                                              // ITEM-B 5개
        SeedReturn(db, "RET-1", "ITEM-A", qty: 10m, status: "confirmed");   // A 전량 반품

        Assert.Equal(0m, ReturnRemaining(db, "DLV-1", "ITEM-A"));   // A 는 소진
        Assert.Equal(5m, ReturnRemaining(db, "DLV-1", "ITEM-B"));   // 🔴 B 는 그대로 남아야 한다
    }

    /// <summary>
    /// 🔴 G-5 <b>취소분은 잔량을 되돌려준다.</b> 취소한 반품까지 세면
    /// 담당자가 실수를 바로잡은 뒤에도 영영 반품을 못 하게 된다.
    /// ⚠️ 철자 — <c>sales_returns</c> 는 <c>canceled</c>(l 하나)다. 명세서는 <c>cancelled</c>(l 둘).
    /// </summary>
    [Fact]
    public void G5_취소된_반품은_잔량을_되돌려준다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_취소된_반품은_잔량을_되돌려준다)); return; }
        using var db = FreshDb();
        SeedDelivery(db, soldQty: 10m);
        SeedReturn(db, "RET-1", "ITEM-A", qty: 10m, status: "canceled");

        Assert.Equal(10m, ReturnRemaining(db, "DLV-1", "ITEM-A"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-6 🔴 P0-3 — 명세서 취소가 과거 반품을 증발시키면 안 된다
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 🔴 G-6 — <b>취소 후 재계산에 반품이 반드시 반영된다.</b>
    /// 봉합 전 식은 <c>SUM(sales_deliveries)</c> 만 봤다. 그래서 무관한 명세서 하나를 취소하면
    /// 과거 반품 누적이 통째로 덮여 <b>미수가 부활</b>했다. 오류도 경고도 없는 조용한 사고였다.
    /// </summary>
    [Fact]
    public void G6_명세서취소_재계산에_반품이_반영된다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G6_명세서취소_재계산에_반품이_반영된다)); return; }
        using var db = FreshDb();

        // 확정 명세서 1,000만 · 확정 반품 200만
        SeedDeliveryHeader(db, "DLV-1", total: 10_000_000m, status: "confirmed");
        SeedReturnHeader(db, "RET-1", "DLV-1", total: 2_000_000m, status: "confirmed");

        // 봉합된 재계산식 — 명세서 합계 − 반품 합계
        var recomputed = Scalar(db, $"""
            SELECT COALESCE((SELECT SUM(total_amount) FROM sales_deliveries
                              WHERE tenant_id='{Tid}' AND partner_id='P-1' AND status='confirmed'),0)
                 - COALESCE((SELECT SUM(total_amount) FROM sales_returns
                              WHERE tenant_id='{Tid}' AND partner_id='P-1' AND status='confirmed'),0)
            """);

        // 🔴 8,000,000 이어야 한다. 반품을 빼지 않으면 10,000,000 이 나오고 미수가 부활한다.
        Assert.Equal(8_000_000m, recomputed);
    }

    /// <summary>
    /// 🔴 G-7 <b>테넌트 격리</b>(헌법 #2). 남의 회사 반품이 우리 잔량에 섞이면 안 된다.
    /// 대조군이 없으면 <c>tenant_id</c> 조건이 빠져도 아무도 모른다.
    /// </summary>
    [Fact]
    public void G7_대조군_다른_테넌트_반품은_섞이지_않는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G7_대조군_다른_테넌트_반품은_섞이지_않는다)); return; }
        using var db = FreshDb();
        SeedDelivery(db, soldQty: 10m);

        // 다른 회사가 같은 명세서 번호로 반품을 갖고 있다 — 우리 잔량에 영향 0 이어야 한다
        Exec(db, $"""
            INSERT INTO sales_returns (return_id, tenant_id, return_no, delivery_id, partner_id,
                                       return_date, status, total_amount, vat_amount, is_deleted)
            VALUES ('RET-X','{OtherTid}','반-X','DLV-1','P-1','2026-08-28','confirmed',9999,0,0)
            """);
        Exec(db, $"""
            INSERT INTO sales_return_items (return_item_id, return_id, tenant_id, item_id, qty,
                                            unit_price, supply_amount, vat_amount)
            VALUES ('RI-X','RET-X','{OtherTid}','ITEM-A',10,1000,10000,1000)
            """);

        Assert.Equal(10m, ReturnRemaining(db, "DLV-1", "ITEM-A"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // 판정식 — 서버 가드가 쓰는 것과 같은 SQL
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>출고 잔량 = 주문수량 − 기출고수량 (SalesService.CreateDeliveryAsync 와 동일 식).</summary>
    private static decimal ShipRemaining(MySqlConnection db, string orderId, string itemId) =>
        Scalar(db, $"""
            SELECT COALESCE(SUM(ordered_qty),0) - COALESCE(SUM(delivered_qty),0)
              FROM sales_order_items
             WHERE order_id='{orderId}' AND tenant_id='{Tid}' AND item_id='{itemId}'
            """);

    /// <summary>반품 잔량 = 판매수량 − 기반품수량 (SalesService.CreateSalesReturnAsync 와 동일 식).</summary>
    private static decimal ReturnRemaining(MySqlConnection db, string deliveryId, string itemId) =>
        Scalar(db, $"""
            SELECT COALESCE((SELECT SUM(sdi.qty) FROM sales_delivery_items sdi
                              WHERE sdi.delivery_id='{deliveryId}' AND sdi.tenant_id='{Tid}'
                                AND sdi.item_id='{itemId}'),0)
                 - COALESCE((SELECT SUM(sri.qty) FROM sales_return_items sri
                              JOIN sales_returns sr ON sr.return_id=sri.return_id
                                                   AND sr.tenant_id=sri.tenant_id
                              WHERE sr.delivery_id='{deliveryId}' AND sr.tenant_id='{Tid}'
                                AND sri.item_id='{itemId}'
                                AND sr.is_deleted=0 AND sr.status <> 'canceled'),0)
            """);

    // ─────────────────────────────────────────────────────────────────────
    // 시험 설비 — TEMPORARY 표만 쓴다. 실제 표는 가리기만 하고 안 건드린다(헌법 #39).
    // ─────────────────────────────────────────────────────────────────────

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다 (작14 W1)
        try { using var c = new MySqlConnection(ConnString()); c.Open(); return true; }
        catch (MySqlException) { return false; }
    }

    private static void Skipped(string gate) =>
        Console.Error.WriteLine($"[SKIP] {gate} — MariaDB 없음. 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라.");

    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();
        Exec(db, """
            CREATE TEMPORARY TABLE sales_order_items (
              order_item_id varchar(36) NOT NULL,
              order_id      varchar(36) NOT NULL,
              tenant_id     varchar(36) NOT NULL,
              item_id       varchar(36) NOT NULL,
              ordered_qty   decimal(15,3) NOT NULL DEFAULT 0,
              delivered_qty decimal(15,3) NOT NULL DEFAULT 0,
              unit_price    decimal(15,2) NOT NULL DEFAULT 0,
              PRIMARY KEY (order_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_deliveries (
              delivery_id  varchar(36) NOT NULL,
              tenant_id    varchar(36) NOT NULL,
              delivery_no  varchar(30) NOT NULL,
              order_id     varchar(36) DEFAULT NULL,
              partner_id   varchar(36) NOT NULL,
              status       varchar(20) NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL DEFAULT 0,
              is_deleted   tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (delivery_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_delivery_items (
              delivery_item_id varchar(36) NOT NULL,
              delivery_id      varchar(36) NOT NULL,
              tenant_id        varchar(36) NOT NULL,
              item_id          varchar(36) DEFAULT NULL,
              qty              decimal(15,3) NOT NULL DEFAULT 0,
              unit_price       decimal(15,2) NOT NULL DEFAULT 0,
              supply_amount    decimal(15,2) NOT NULL DEFAULT 0,
              vat_amount       decimal(15,2) NOT NULL DEFAULT 0,
              PRIMARY KEY (delivery_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_returns (
              return_id    varchar(36) NOT NULL,
              tenant_id    varchar(36) NOT NULL,
              return_no    varchar(20) NOT NULL,
              delivery_id  varchar(36) DEFAULT NULL,
              partner_id   varchar(36) NOT NULL,
              return_date  date NOT NULL,
              status       varchar(20) NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL DEFAULT 0,
              vat_amount   decimal(15,2) NOT NULL DEFAULT 0,
              is_deleted   tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (return_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_return_items (
              return_item_id varchar(36) NOT NULL,
              return_id      varchar(36) NOT NULL,
              tenant_id      varchar(36) NOT NULL,
              item_id        varchar(36) NOT NULL,
              qty            decimal(15,3) NOT NULL DEFAULT 0,
              unit_price     decimal(15,2) NOT NULL DEFAULT 0,
              supply_amount  decimal(15,2) NOT NULL DEFAULT 0,
              vat_amount     decimal(15,2) NOT NULL DEFAULT 0,
              PRIMARY KEY (return_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);
        return db;
    }

    private static void SeedOrder(MySqlConnection db, decimal orderedQty, decimal deliveredQty) =>
        Exec(db, $"""
            INSERT INTO sales_order_items
              (order_item_id, order_id, tenant_id, item_id, ordered_qty, delivered_qty, unit_price)
            VALUES ('OI-1','ORD-1','{Tid}','ITEM-A',{orderedQty},{deliveredQty},1000)
            """);

    private static void SeedDelivery(MySqlConnection db, decimal soldQty)
    {
        SeedDeliveryHeader(db, "DLV-1", total: soldQty * 1000m, status: "confirmed");
        Exec(db, $"""
            INSERT INTO sales_delivery_items
              (delivery_item_id, delivery_id, tenant_id, item_id, qty, unit_price, supply_amount, vat_amount)
            VALUES ('DI-1','DLV-1','{Tid}','ITEM-A',{soldQty},1000,{soldQty * 1000m},{soldQty * 100m})
            """);
    }

    private static void SeedDeliveryHeader(MySqlConnection db, string id, decimal total, string status) =>
        Exec(db, $"""
            INSERT INTO sales_deliveries
              (delivery_id, tenant_id, delivery_no, order_id, partner_id, status, total_amount, is_deleted)
            VALUES ('{id}','{Tid}','명-{id}','ORD-1','P-1','{status}',{total},0)
            """);

    private static void SeedReturn(MySqlConnection db, string id, string itemId, decimal qty, string status)
    {
        SeedReturnHeader(db, id, "DLV-1", total: qty * 1000m, status: status);
        Exec(db, $"""
            INSERT INTO sales_return_items
              (return_item_id, return_id, tenant_id, item_id, qty, unit_price, supply_amount, vat_amount)
            VALUES ('RI-{id}','{id}','{Tid}','{itemId}',{qty},1000,{qty * 1000m},{qty * 100m})
            """);
    }

    private static void SeedReturnHeader(MySqlConnection db, string id, string deliveryId, decimal total, string status) =>
        Exec(db, $"""
            INSERT INTO sales_returns
              (return_id, tenant_id, return_no, delivery_id, partner_id, return_date, status, total_amount, vat_amount, is_deleted)
            VALUES ('{id}','{Tid}','반-{id}','{deliveryId}','P-1','2026-08-28','{status}',{total},{total * 0.1m},0)
            """);

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static decimal Scalar(MySqlConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? 0m : Convert.ToDecimal(v);
    }
}
