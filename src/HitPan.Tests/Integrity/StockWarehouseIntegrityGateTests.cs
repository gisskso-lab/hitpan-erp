using System;
using System.Linq;
using System.Threading.Tasks;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260903작18 W1 — <b>재고 정합성 검사의 창고축 게이트.</b>
///
/// <para>
/// 사장님 오더: <i>"창고로 인해 재고가 안맞는거라면 … 마스타에서 창고1 = 재고 몆개,
/// 창고2 = 재고 몆개 이렇게 관리하면 되잖아"</i> →
/// <i>"그럼 수불부와, 마스터 재고가 안맞을 일이 없을거 같은데???"</i>
/// </para>
///
/// <para>
/// 🔴 <b>사장님 말씀이 맞았다.</b> 구조는 이미 창고별이다
/// (<c>item_stock UNIQUE (tenant_id, item_id, warehouse_id)</c> ·
///  <c>stock_ledger.warehouse_id NOT NULL</c>).
/// 틀린 것은 데이터가 아니라 <b><c>CheckIntegrityAsync</c> 의 재고 검사식</b>이다 —
/// JOIN 에 <c>warehouse_id</c> 가 빠져 <b>창고 1곳의 재고를 원장 전체합과 비교</b>한다.
/// 그래서 창고에 나뉘어 있을 뿐 멀쩡한 재고가 "불일치" 로 잡힌다(거짓 경보).
/// </para>
///
/// <para>
/// 🔴 <b>이 게이트는 봉합보다 먼저 작성됐다</b>(작18 W1, SoD — 봉합자가 짜지 않는다).
/// 봉합 전에 돌리면 <b>반드시 FAIL 이어야 정상</b>이다. 이 시점에 초록불이면 그 게이트는 가짜다.
/// </para>
///
/// <para>
/// 🔴 실제 <c>FinanceService.CheckIntegrityAsync</c> 를 실 DB 에 물려 부른다.
/// SQL 을 베껴 적어 글자로 검사하면 코드가 바뀌어도 계속 초록불이 된다 — 가짜 게이트 누적 22번.
/// </para>
///
/// <para>⚠️ TEMPORARY 표만 쓴다 — 실제 표는 가리기만 하고 안 건드린다(헌법 #39).</para>
/// </summary>
public sealed class StockWarehouseIntegrityGateTests
{
    private const string Tid = "GATE-WH903";
    private const string ItemMulti = "ITEM-MULTI";   // 창고 2곳에 나뉜 품목
    private const string ItemSingle = "ITEM-SINGLE"; // 창고 1곳만 쓰는 품목
    private const string WhMain = "WH-MAIN";
    private const string WhSub = "WH-SUB";

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    private const string CheckName = "stock vs ledger 정합성";

    /// <summary>
    /// 🔴 G-WH1 — <b>창고에 나뉘어 있어도 재고가 맞으면 「이상 없음」이어야 한다.</b>
    ///
    /// <para>
    /// 이 게이트가 작18 의 존재 이유다. 씨앗은 <b>창고별로 완벽히 맞는</b> 데이터다:
    /// 본창고 14(원장 in 14) · 부창고 1(원장 in 1). 창고별로 보면 차이 0.
    /// </para>
    /// <para>
    /// 봉합 전에는 검사식이 창고를 안 갈라 <b>본창고 14 vs 원장 전체 15</b>,
    /// <b>부창고 1 vs 원장 전체 15</b> 로 비교해 2건을 불일치로 뱉는다 ⇒ 이 게이트는 FAIL.
    /// 봉합 후에는 창고별로 갈려 0건 ⇒ PASS.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GWH1_창고별로_맞으면_불일치가_없다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GWH1_창고별로_맞으면_불일치가_없다)); return; }
        using var db = FreshDb();
        SeedWarehouseSplitButBalanced(db);

        var report = await NewService(db).CheckIntegrityAsync(Tid);
        var stock = FindStockCheck(report);

        Assert.Equal("OK", stock.Status);
        Assert.Null(stock.Detail);
    }

    /// <summary>
    /// 🔴 G-WH2 — <b>대조군: 진짜로 어긋나면 여전히 잡아내야 한다.</b>
    ///
    /// <para>
    /// 봉합이 "창고를 갈라 본다" 가 아니라 "검사를 무르게 한다" 가 되어버리면,
    /// 진짜 불일치까지 놓친다. 그건 봉합이 아니라 <b>눈을 가린 것</b>이다.
    /// 본창고 재고를 99 로 두고 원장은 14 만 넣어 <b>같은 창고 안에서</b> 어긋뜨린다.
    /// </para>
    /// <para>🔴 이 게이트는 봉합 전/후 <b>둘 다 PASS</b> 여야 한다.</para>
    /// </summary>
    [Fact]
    public async Task GWH2_같은창고에서_진짜로_어긋나면_잡는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GWH2_같은창고에서_진짜로_어긋나면_잡는다)); return; }
        using var db = FreshDb();
        SeedRealMismatchInSameWarehouse(db);

        var report = await NewService(db).CheckIntegrityAsync(Tid);
        var stock = FindStockCheck(report);

        Assert.Equal("WARN", stock.Status);
        Assert.NotNull(stock.Detail);
    }

    /// <summary>
    /// 🔴 G-WH3 — <b>창고를 하나만 쓰는 품목은 봉합 전후 판정이 같아야 한다.</b>
    ///
    /// <para>
    /// 봉합의 부작용 반증. 창고 1곳뿐이면 JOIN 이 1:1 이라 결과가 달라질 이유가 없다.
    /// 실측에서 <c>test1234</c> 의 15품목 중 13품목이 이 경우였다 —
    /// 여기가 바뀌면 봉합이 애먼 것을 건드린 것이다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GWH3_단일창고_품목은_영향받지_않는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GWH3_단일창고_품목은_영향받지_않는다)); return; }
        using var db = FreshDb();
        SeedSingleWarehouseBalanced(db);

        var report = await NewService(db).CheckIntegrityAsync(Tid);
        var stock = FindStockCheck(report);

        Assert.Equal("OK", stock.Status);
    }

    /// <summary>
    /// 🔴 G-WH5 — <b>두 창고의 재고가 <u>같은 수</u>여도 창고별로 갈려야 한다.</b>
    ///
    /// <para>
    /// 🔴 이 게이트는 <b>무력화 시험이 뚫려서</b> 추가됐다.
    /// G-WH1 의 씨앗은 본 14 · 부 1 로 <b>수가 달라서</b>, <c>GROUP BY</c> 에
    /// <c>warehouse_id</c> 를 도로 빼도 두 행이 안 뭉쳐 게이트가 통과해버렸다.
    /// (JOIN 만 지웠을 땐 잡았지만, GROUP BY 만 지운 것은 못 잡았다.)
    /// </para>
    /// <para>
    /// 실측된 「볼트너트오링」이 바로 이 경우다 — 테스트1창고 2 · 기본창고 2 로 <b>같은 수</b>라
    /// <c>GROUP BY s.item_id, s.current_qty</c> 가 두 창고를 <b>한 줄로 뭉쳐버린다</b>.
    /// 씨앗도 그대로 맞춘다: 두 창고 각 2, 원장도 창고별 in 2.
    /// </para>
    /// <para>⇒ <c>GROUP BY</c> 의 <c>warehouse_id</c> 를 빼면 이 게이트가 <b>반드시 FAIL</b> 해야 한다.</para>
    /// </summary>
    [Fact]
    public async Task GWH5_두창고_재고가_같아도_창고별로_갈린다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GWH5_두창고_재고가_같아도_창고별로_갈린다)); return; }
        using var db = FreshDb();
        SeedEqualQtyAcrossWarehouses(db);

        var report = await NewService(db).CheckIntegrityAsync(Tid);
        var stock = FindStockCheck(report);

        Assert.Equal("OK", stock.Status);
        Assert.Null(stock.Detail);
    }

    /// <summary>
    /// 🔴 G-WH4 — <b>다른 검사 14종이 회귀하지 않았다.</b>
    ///
    /// <para>
    /// 재고 검사 하나를 고치면서 같은 메서드의 다른 검사를 깨뜨리면 안 된다.
    /// 검사 항목이 통째로 사라지는 사고(항목 수 감소)도 여기서 잡는다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GWH4_다른_검사항목이_사라지지_않는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(GWH4_다른_검사항목이_사라지지_않는다)); return; }
        using var db = FreshDb();
        SeedWarehouseSplitButBalanced(db);

        var report = await NewService(db).CheckIntegrityAsync(Tid);

        // 실측 기준 15개 항목. 줄어들면 검사가 조용히 빠진 것이다.
        Assert.True(report.Items.Count >= 15,
            $"검사 항목이 {report.Items.Count}개로 줄었다 — 검사가 조용히 빠졌는지 확인하라");
        Assert.Contains(report.Items, i => i.CheckName == CheckName);
        Assert.Contains(report.Items, i => i.CheckName == "음수 재고");
        Assert.Contains(report.Items, i => i.CheckName == "item_stock 누락");
    }

    // ────────────────────────────────────────────────────────────
    // 헬퍼
    // ────────────────────────────────────────────────────────────

    private static IntegrityItem FindStockCheck(DataIntegrityReport report)
    {
        var hit = report.Items.FirstOrDefault(i => i.CheckName == CheckName);
        Assert.True(hit is not null,
            $"검사 항목 '{CheckName}' 이 사라졌다. 항목: {string.Join(", ", report.Items.Select(i => i.CheckName))}");
        return hit!;
    }

    private static FinanceService NewService(System.Data.IDbConnection db) => new(db, null!, null!);

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
        // ⚠️ GuidFormat=None 필수 — 빠지면 char(36) 컬럼이 Guid 로 와서 string DTO 매핑이
        //   500 으로 터진다(2026-08-12 양식템플릿 사고). ConnectionStringGuidGuardTests 가 지킨다.
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "AllowUserVariables=true;GuidFormat=None;Connection Timeout=5;";
    }

    /// <summary>TEMPORARY 표만 만든다 — 실제 표는 가리기만 하고 안 건드린다(헌법 #39).</summary>
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
              qty_out decimal(15,3) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        // 🔴 CheckIntegrityAsync 는 15개 검사를 한 번에 돈다. 표가 하나라도 없으면
        //   재고 검사에 닿기도 전에 터진다 — 실제로 items.purchase_price 누락으로 한 번 터졌다.
        //   ⇒ 이 게이트가 재는 것은 재고 검사지만, **메서드 전체가 돌 수 있게** 갖춰준다.
        Exec(db, """
            CREATE TEMPORARY TABLE items (
              item_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              item_name varchar(100) NOT NULL,
              purchase_price decimal(15,2) NOT NULL DEFAULT 0,
              sale_price decimal(15,2) NOT NULL DEFAULT 0,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              is_active tinyint(1) NOT NULL DEFAULT 1
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE purchase_order_items (
              po_item_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              ordered_qty decimal(15,3) NOT NULL DEFAULT 0,
              received_qty decimal(15,3) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE sales_order_items (
              so_item_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              ordered_qty decimal(15,3) NOT NULL DEFAULT 0,
              delivered_qty decimal(15,3) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE bom_items (
              bom_item_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              material_item_id varchar(36) NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE approval_doc_lines (
              line_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              approver_id varchar(36) NULL,
              is_active tinyint(1) NOT NULL DEFAULT 1
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE employees (
              employee_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              employee_name varchar(100) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE journal_lines (
              line_id bigint(20) NOT NULL AUTO_INCREMENT PRIMARY KEY,
              entry_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              account_code varchar(10) NOT NULL,
              debit_amount decimal(15,2) NOT NULL DEFAULT 0,
              credit_amount decimal(15,2) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE journal_entries (
              entry_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              source_type varchar(30) NULL,
              source_id varchar(80) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE accounts (
              account_code varchar(10) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              account_name varchar(100) NOT NULL,
              PRIMARY KEY (account_code, tenant_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        // ⚠️ 컬럼은 출하 DDL(installer/hitpan_db_clean.sql · 헌법 #36 진실원)에서 확인해 맞췄다.
        //   추측으로 적었다가 is_deleted·purchase_price 누락으로 두 번 터졌다.
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_receipts (
              receipt_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              po_id varchar(36) NULL,
              source_type varchar(20) NOT NULL DEFAULT 'po',
              status varchar(20) NOT NULL DEFAULT 'draft',
              is_deleted tinyint(1) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE purchase_returns (
              return_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              receipt_id varchar(36) NULL,
              status varchar(20) NOT NULL DEFAULT 'draft',
              is_deleted tinyint(1) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE purchase_orders (
              po_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              status varchar(20) NOT NULL DEFAULT 'draft'
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE purchase_receipt_items (
              receipt_item_id varchar(36) NOT NULL PRIMARY KEY,
              receipt_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              qty decimal(15,3) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE purchase_return_items (
              return_item_id varchar(36) NOT NULL PRIMARY KEY,
              return_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              qty decimal(15,3) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        Exec(db, """
            CREATE TEMPORARY TABLE sales_returns (
              return_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              status varchar(20) NOT NULL DEFAULT 'draft',
              is_deleted tinyint(1) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        return db;
    }

    /// <summary>
    /// 🔴 핵심 씨앗 — <b>창고에 나뉘어 있지만 창고별로는 완벽히 맞는</b> 재고.
    /// 실측된 「볼트너트」와 같은 모양이다(본 14 · 부 1).
    /// </summary>
    private static void SeedWarehouseSplitButBalanced(MySqlConnection db)
    {
        SeedItems(db);

        // 마스터: 본창고 14 · 부창고 1
        Exec(db, $"""
            INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty) VALUES
              ('S1','{Tid}','{ItemMulti}','{WhMain}', 14),
              ('S2','{Tid}','{ItemMulti}','{WhSub}',   1);
            """);

        // 원장: 본창고 in 14 · 부창고 in 1  ⇒ 창고별로 보면 차이 0
        Exec(db, $"""
            INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out) VALUES
              ('{Tid}','{ItemMulti}','{WhMain}','2026-09-01','2026-09','in','purchase_receipt','R1', 14, 0),
              ('{Tid}','{ItemMulti}','{WhSub}' ,'2026-09-01','2026-09','in','transfer'        ,'T1',  1, 0);
            """);
    }

    /// <summary>
    /// 🔴 두 창고의 재고가 <b>같은 수</b>인 씨앗 — 실측된 「볼트너트오링」(테스트1창고 2 · 기본창고 2).
    /// <c>GROUP BY s.item_id, s.current_qty</c> 는 이 둘을 <b>한 줄로 뭉쳐</b>
    /// 재고 2 를 원장 전체 4 와 비교한다 ⇒ 멀쩡한데 불일치로 잡힌다.
    /// </summary>
    private static void SeedEqualQtyAcrossWarehouses(MySqlConnection db)
    {
        SeedItems(db);
        Exec(db, $"""
            INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty) VALUES
              ('S1','{Tid}','{ItemMulti}','{WhMain}', 2),
              ('S2','{Tid}','{ItemMulti}','{WhSub}' , 2);
            """);
        Exec(db, $"""
            INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out) VALUES
              ('{Tid}','{ItemMulti}','{WhMain}','2026-09-01','2026-09','in','purchase_receipt','R1', 2, 0),
              ('{Tid}','{ItemMulti}','{WhSub}' ,'2026-09-01','2026-09','in','transfer'        ,'T1', 2, 0);
            """);
    }

    /// <summary>대조군 — <b>같은 창고 안에서</b> 진짜로 어긋난 재고(본창고 99 vs 원장 14).</summary>
    private static void SeedRealMismatchInSameWarehouse(MySqlConnection db)
    {
        SeedItems(db);
        Exec(db, $"""
            INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty) VALUES
              ('S1','{Tid}','{ItemMulti}','{WhMain}', 99);
            """);
        Exec(db, $"""
            INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out) VALUES
              ('{Tid}','{ItemMulti}','{WhMain}','2026-09-01','2026-09','in','purchase_receipt','R1', 14, 0);
            """);
    }

    /// <summary>창고를 하나만 쓰는 품목 — 봉합 전후 판정이 같아야 한다.</summary>
    private static void SeedSingleWarehouseBalanced(MySqlConnection db)
    {
        SeedItems(db);
        Exec(db, $"""
            INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty) VALUES
              ('S1','{Tid}','{ItemSingle}','{WhMain}', 20);
            """);
        Exec(db, $"""
            INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out) VALUES
              ('{Tid}','{ItemSingle}','{WhMain}','2026-09-01','2026-09','in','purchase_receipt','R1', 25, 0),
              ('{Tid}','{ItemSingle}','{WhMain}','2026-09-02','2026-09','out','sales_delivery' ,'D1',  0, 5);
            """);
    }

    private static void SeedItems(MySqlConnection db) => Exec(db, $"""
        INSERT INTO items (item_id, tenant_id, item_name, purchase_price, sale_price) VALUES
          ('{ItemMulti}' ,'{Tid}','창고분산품목', 800, 1000),
          ('{ItemSingle}','{Tid}','단일창고품목', 800, 1000);
        """);

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        cmd.ExecuteNonQuery();
    }
}
