using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260827작6 — <b>사슬 정합성 검사가 틀린 데이터를 실제로 찾아내는가.</b>
///
/// <para>
/// 사장님 지시(2026-08-27):
/// <i>"ERP의 핵심인 입력과, 입력이 연결된 각 도구의 사슬과 조회의 사슬들의 정합성에 집중해.
/// 데이터가 맞아야 하고, 혹시나 데이터가 틀릴 경우, 빠르게 틀린 데이터를 발견할 수 있어야 해"</i>
/// </para>
///
/// <para>
/// 🔴 <b>왜 필요한가</b> — 사장님이 든 예: <i>"1000가지 매입전표 중 100가지 반품전표를 보고
/// 회계나 재고가 안 맞아서 찾아야 되는 상황. 전표번호 대조기능이 없다면 일일이 전표로
/// 숫자들을 다 찾아봐야 되잖아."</i> · <i>"실무에서 몇십만 건 전표가 발행될텐데
/// 연결사슬에 대한 전표번호가 없다면 이건 <b>AI도 못 찾음</b>."</i>
/// </para>
///
/// <para>
/// 종전 <c>CheckIntegrityAsync</c> 8항목은 <b>재고·마스터만</b> 봤다. 회계 장부와
/// 매입↔반품 사슬은 <b>한 건도 안 봤다</b>(실측: journal_entries 참조 0건).
/// ⇒ 장부가 틀어져도 사슬이 끊겨도 <b>계속 초록불</b>이었다.
/// </para>
///
/// <para>
/// 🔴 <b>이 게이트는 「검사가 있나」가 아니라 「틀린 걸 잡나」를 잰다.</b>
/// 일부러 깨진 데이터를 넣고 <b>FAIL 이 나오는지</b> 확인한다.
/// 넣지 않았을 때 OK 가 나오는 <b>대조군</b>도 함께 잰다 —
/// 대조군이 없으면 "전부 FAIL" 로 짜도 통과한다.
/// </para>
///
/// <para>⚠️ TEMPORARY 표만 쓴다 — 실제 표는 가리기만 하고 안 건드린다(헌법 #39).</para>
/// </summary>
public sealed class ChainIntegrityCheckGateTests
{
    private const string Tid = "GATE-CI827";

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    /// <summary>
    /// 🔴 G-CI0 — <b>대조군.</b> 깨끗한 데이터면 사슬 검사가 전부 OK 다.
    /// 이게 없으면 "무조건 FAIL" 로 짜도 아래 시험들이 통과한다.
    /// </summary>
    [Fact]
    public async Task GCI0_대조군_깨끗하면_사슬검사가_OK()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedBalanced(db);

        var r = await NewService(db).CheckIntegrityAsync(Tid);

        foreach (var name in ChainChecks)
        {
            var item = Assert.Single(r.Items, x => x.CheckName == name);
            Assert.True(item.Status == "OK",
                $"깨끗한 데이터인데 [{name}] 이 {item.Status} 다 — 검사가 과잉이면 매번 빨간불이라 아무도 안 본다. Detail={item.Detail}");
        }
    }

    /// <summary>
    /// 🔴 G-CI1 — <b>차·대가 안 맞으면 잡는다.</b> 복식부기 검산이 장부 신뢰의 근거다.
    /// </summary>
    [Fact]
    public async Task GCI1_차대불일치를_잡는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedBalanced(db);
        // 차변만 있는 전표 — 대변이 없다
        Exec(db, $"""
            INSERT INTO journal_entries (entry_id,tenant_id,entry_no,entry_date,ym,description,source_type,is_confirmed,created_at)
            VALUES ('BAD1','{Tid}','JE-BAD','2026-08-20','2026-08','고의 불균형','manual',1,NOW(6));
            """);
        Exec(db, $"""
            INSERT INTO journal_lines (entry_id,tenant_id,account_code,debit_amount,credit_amount,created_at)
            VALUES ('BAD1','{Tid}','10800',50000,0,NOW(6));
            """);

        var r = await NewService(db).CheckIntegrityAsync(Tid);

        Assert.Equal("FAIL", Assert.Single(r.Items, x => x.CheckName == "차변합 = 대변합").Status);
    }

    /// <summary>
    /// 🔴 G-CI2 — <b>상쇄 함정.</b> 두 전표가 서로 상쇄해 <b>총합은 0</b> 인데
    /// 개별 전표는 둘 다 깨진 경우. 총합만 재는 검사는 <b>이걸 못 잡는다</b> —
    /// 그래서 「전표별 차·대 균형」이 따로 있어야 한다.
    /// 🔴 실 DB 로 실측해 확인한 함정이다(총합 0, 전표별 2건).
    /// </summary>
    [Fact]
    public async Task GCI2_상쇄되어_총합이_0이어도_전표별로_잡는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedBalanced(db);
        Exec(db, $"""
            INSERT INTO journal_entries (entry_id,tenant_id,entry_no,entry_date,ym,description,source_type,is_confirmed,created_at)
            VALUES ('BAD1','{Tid}','JE-B1','2026-08-20','2026-08','차변만','manual',1,NOW(6)),
                   ('BAD2','{Tid}','JE-B2','2026-08-20','2026-08','대변만','manual',1,NOW(6));
            """);
        Exec(db, $"""
            INSERT INTO journal_lines (entry_id,tenant_id,account_code,debit_amount,credit_amount,created_at)
            VALUES ('BAD1','{Tid}','10800',50000,0,NOW(6)),
                   ('BAD2','{Tid}','25500',0,50000,NOW(6));
            """);

        var r = await NewService(db).CheckIntegrityAsync(Tid);

        // 총합은 0 이라 ①은 OK 로 지나간다 — 그게 이 함정의 핵심이다
        Assert.Equal("OK", Assert.Single(r.Items, x => x.CheckName == "차변합 = 대변합").Status);
        // ②가 잡아야 한다
        var perEntry = Assert.Single(r.Items, x => x.CheckName == "전표별 차·대 균형");
        Assert.Equal("FAIL", perEntry.Status);
        Assert.Contains("2", perEntry.Detail ?? "");
    }

    /// <summary>
    /// 🔴 G-CI3 — <b>확정했는데 기표 안 된 전표를 잡는다.</b>
    /// 재고는 움직였는데 장부에 없는 상태 — <b>재고와 회계가 갈리는</b> 대표 원인이다.
    /// </summary>
    [Fact]
    public async Task GCI3_확정전표_기표누락을_잡는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedBalanced(db);
        // 확정 매입인데 분개가 없다
        Exec(db, $"""
            INSERT INTO purchase_receipts (receipt_id,tenant_id,receipt_no,partner_id,receipt_date,status,total_amount,vat_amount)
            VALUES ('R-NOJE','{Tid}','매-20260820-001','P1','2026-08-20','confirmed',100000,10000);
            """);

        var r = await NewService(db).CheckIntegrityAsync(Tid);

        Assert.Equal("FAIL", Assert.Single(r.Items, x => x.CheckName == "확정전표 기표 누락").Status);
    }

    /// <summary>
    /// 🔴 G-CI4 — <b>원 매입이 사라진 반품을 잡는다.</b>
    /// 사슬이 끊기면 「반품전표」 대조가 불가능해진다 — 수십만 건에서 추적 불가.
    /// </summary>
    [Fact]
    public async Task GCI4_사슬끊긴_반품을_잡는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedBalanced(db);
        // 있지도 않은 매입전표를 가리키는 반품
        Exec(db, $"""
            INSERT INTO purchase_returns (return_id,tenant_id,return_no,receipt_id,partner_id,return_date,status,is_deleted)
            VALUES ('RT-ORPHAN','{Tid}','매반-20260820-001','없는매입','P1','2026-08-20','draft',0);
            """);

        var r = await NewService(db).CheckIntegrityAsync(Tid);

        Assert.Equal("FAIL", Assert.Single(r.Items, x => x.CheckName == "반품↔매입 사슬").Status);
    }

    /// <summary>
    /// 🔴 G-CI5 — <b>매입보다 많이 반품한 건을 잡는다.</b>
    /// 100개 받아 120개 반품은 있을 수 없다 — 재고가 음수로 가거나 매입액이 마이너스가 된다.
    /// </summary>
    [Fact]
    public async Task GCI5_초과반품을_잡는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedBalanced(db);

        Exec(db, $"""
            INSERT INTO purchase_receipts (receipt_id,tenant_id,receipt_no,partner_id,receipt_date,status,total_amount,vat_amount)
            VALUES ('R-100','{Tid}','매-20260820-002','P1','2026-08-20','draft',100000,10000);
            """);
        Exec(db, $"""
            INSERT INTO purchase_receipt_items (receipt_item_id,receipt_id,tenant_id,item_id,qty,unit_price,supply_amount,vat_amount)
            VALUES ('RI-1','R-100','{Tid}','I1',100,1000,100000,10000);
            """);
        // 100개 받았는데 120개 반품
        Exec(db, $"""
            INSERT INTO purchase_returns (return_id,tenant_id,return_no,receipt_id,partner_id,return_date,status,is_deleted)
            VALUES ('RT-OVER','{Tid}','매반-20260820-002','R-100','P1','2026-08-20','draft',0);
            """);
        Exec(db, $"""
            INSERT INTO purchase_return_items (return_item_id,return_id,tenant_id,item_id,qty,unit_price,supply_amount,vat_amount)
            VALUES ('RTI-1','RT-OVER','{Tid}','I1',120,1000,120000,12000);
            """);

        var r = await NewService(db).CheckIntegrityAsync(Tid);

        Assert.Equal("FAIL", Assert.Single(r.Items, x => x.CheckName == "반품수량 ≤ 매입수량").Status);
    }

    // ────────────────────────────────────────────────────────────────────

    private static readonly string[] ChainChecks =
    {
        "차변합 = 대변합", "전표별 차·대 균형", "계정과목 참조",
        "확정전표 기표 누락", "반품↔매입 사슬", "매입↔발주 사슬", "반품수량 ≤ 매입수량",
    };

    private static FinanceService NewService(IDbConnection db) => new(db, null!, null!);

    /// <summary>판매 1건이 차·대 맞게 기표된 깨끗한 상태.</summary>
    private static void SeedBalanced(MySqlConnection db)
    {
        Exec(db, $"""
            INSERT INTO accounts (account_code,tenant_id,account_name,account_type,sort_order) VALUES
              ('10800','{Tid}','외상매출금','asset',1), ('25500','{Tid}','부가세예수금','liability',2),
              ('40100','{Tid}','상품매출','revenue',3);
            """);
        Exec(db, $"""
            INSERT INTO journal_entries (entry_id,tenant_id,entry_no,entry_date,ym,description,source_type,is_confirmed,created_at)
            VALUES ('E1','{Tid}','JE-001','2026-08-10','2026-08','판매','sales','S1',1,NOW(6));
            """.Replace(",'S1',1,", ",1,"));   // source_id 컬럼 순서 방어
        Exec(db, $"""
            INSERT INTO journal_lines (entry_id,tenant_id,account_code,debit_amount,credit_amount,created_at)
            VALUES ('E1','{Tid}','10800',110000,0,NOW(6)),
                   ('E1','{Tid}','40100',0,100000,NOW(6)),
                   ('E1','{Tid}','25500',0,10000,NOW(6));
            """);
    }

    private static void Skipped() =>
        Console.Error.WriteLine("[SKIP] MariaDB 없음 — 안 돌았다. 초록불을 검증으로 읽지 마라.");

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
        try { using var c = new MySqlConnection(ConnString()); c.Open(); return true; }
        catch (MySqlException) { return false; }
    }

    /// <summary>TEMPORARY 표만 쓴다 — 실제 표는 가리기만 하고 안 건드린다(헌법 #39).</summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();

        Exec(db, """
            CREATE TEMPORARY TABLE accounts (
              account_code varchar(10) NOT NULL, tenant_id varchar(36) NOT NULL,
              account_name varchar(100) NOT NULL, account_type varchar(20) NOT NULL,
              parent_code varchar(10) NULL, is_active tinyint(1) NOT NULL DEFAULT 1,
              sort_order int NOT NULL DEFAULT 0,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              PRIMARY KEY (account_code, tenant_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE journal_entries (
              entry_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              entry_no varchar(32) NOT NULL, entry_date date NOT NULL, ym varchar(7) NOT NULL,
              description varchar(255) NULL, source_type varchar(30) NOT NULL DEFAULT 'manual',
              source_id varchar(36) NULL, is_confirmed tinyint(1) NOT NULL DEFAULT 1,
              confirmed_at datetime(6) NULL, confirmed_by varchar(36) NULL,
              created_at datetime(6) NOT NULL, created_by varchar(36) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE journal_lines (
              line_id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
              entry_id varchar(36) NOT NULL, tenant_id varchar(36) NOT NULL,
              account_code varchar(10) NOT NULL,
              debit_amount decimal(15,2) NOT NULL DEFAULT 0,
              credit_amount decimal(15,2) NOT NULL DEFAULT 0,
              partner_id varchar(36) NULL, memo varchar(255) NULL,
              created_at datetime(6) NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE item_stock (
              stock_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL, warehouse_id varchar(36) NOT NULL DEFAULT 'default',
              current_qty decimal(15,3) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE stock_ledger (
              ledger_id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
              tenant_id varchar(36) NOT NULL, item_id varchar(36) NOT NULL,
              warehouse_id varchar(36) NOT NULL,
              qty_in decimal(15,3) NOT NULL DEFAULT 0, qty_out decimal(15,3) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE items (
              item_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              item_name varchar(100) NULL, purchase_price decimal(15,2) NOT NULL DEFAULT 0,
              sale_price decimal(15,2) NOT NULL DEFAULT 0, is_deleted tinyint(1) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_orders (
              po_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              po_no varchar(30) NULL, status varchar(20) NOT NULL DEFAULT 'draft'
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_order_items (
              po_item_id varchar(36) NOT NULL PRIMARY KEY, po_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL, item_id varchar(36) NOT NULL,
              ordered_qty decimal(15,3) NOT NULL DEFAULT 0,
              received_qty decimal(15,3) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_receipts (
              receipt_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              receipt_no varchar(30) NULL, po_id varchar(36) NULL, partner_id varchar(36) NULL,
              receipt_date date NOT NULL, status varchar(20) NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL DEFAULT 0,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_receipt_items (
              receipt_item_id varchar(36) NOT NULL PRIMARY KEY, receipt_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL, item_id varchar(36) NOT NULL,
              qty decimal(15,3) NOT NULL DEFAULT 0, unit_price decimal(15,2) NOT NULL DEFAULT 0,
              supply_amount decimal(15,2) NOT NULL DEFAULT 0,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_returns (
              return_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              return_no varchar(30) NULL, receipt_id varchar(36) NULL, partner_id varchar(36) NULL,
              return_date date NOT NULL, status varchar(20) NOT NULL DEFAULT 'draft',
              is_deleted tinyint(1) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_return_items (
              return_item_id varchar(36) NOT NULL PRIMARY KEY, return_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL, item_id varchar(36) NOT NULL,
              qty decimal(15,3) NOT NULL DEFAULT 0, unit_price decimal(15,2) NOT NULL DEFAULT 0,
              supply_amount decimal(15,2) NOT NULL DEFAULT 0,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE sales_order_items (
              so_item_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              ordered_qty decimal(15,3) NOT NULL DEFAULT 0,
              delivered_qty decimal(15,3) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE bom_items (
              bom_item_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              material_item_id varchar(36) NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE approval_doc_lines (
              line_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              approver_id varchar(36) NULL, is_active tinyint(1) NOT NULL DEFAULT 1
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE employees (
              employee_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              user_id varchar(36) NULL, emp_name varchar(50) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        return db;
    }

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
