using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260827작4 — <b>회계장부(합계잔액시산표) 게이트.</b>
///
/// <para>
/// 사장님 오더: <i>"13개의 모든 돈 흐름이 모이는 회계장부가 필요한데,
/// 회계에선 이게 핵심인데, 이 기능이 빠졌네"</i>
/// </para>
///
/// <para>
/// 🔴 <b>실제 <c>FinanceService.GetTrialBalanceAsync</c> 를 실 DB 에 물려 부른다.</b>
/// SQL 을 베껴 적어 글자로 검사하면 코드가 바뀌어도 계속 초록불이 된다 — 그건 가짜다.
/// (이 레포에 가짜 게이트가 19번 누적됐고, 직전 작3 에서도 <b>모수만 재는 게이트</b>가
/// 전부 초록불인데 사장님이 반려하셨다.)
/// </para>
/// </summary>
public sealed class TrialBalanceGateTests
{
    private const string Tid = "GATE-TB827";
    private static readonly DateTime From = new(2026, 8, 1);
    private static readonly DateTime To = new(2026, 8, 31);

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    /// <summary>
    /// 🔴 G-TB1 — <b>검산이 실제로 맞는다.</b> 복식부기는 차변합 = 대변합.
    /// 이게 안 맞으면 장부가 틀렸다는 뜻이라, 시산표의 존재 이유가 이 한 줄이다.
    /// </summary>
    [Fact]
    public async Task GTB1_차변합과_대변합이_맞는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        var tb = await NewService(db).GetTrialBalanceAsync(Tid, From, To);

        Assert.Equal(220000m, tb.TotalDebit);
        Assert.Equal(220000m, tb.TotalCredit);
        Assert.True(tb.IsBalanced);
    }

    /// <summary>
    /// 🔴 G-TB2 — <b>잔액 방향이 계정 성격을 따른다.</b>
    /// 자산·비용은 (차변−대변), 부채·자본·수익은 (대변−차변).
    /// 이걸 틀리면 자산이 음수로 나오는 등 장부가 통째로 뒤집힌다.
    /// </summary>
    [Fact]
    public async Task GTB2_잔액방향이_계정성격을_따른다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        var tb = await NewService(db).GetTrialBalanceAsync(Tid, From, To);

        // 현금(자산) — 차변 110,000 만 있다 ⇒ +110,000
        var cash = Assert.Single(tb.Rows, r => r.AccountCode == "10100");
        Assert.Equal(110000m, cash.Balance);

        // 상품매출(수익) — 대변 110,000 만 있다 ⇒ +110,000 (음수로 뒤집히면 FAIL)
        var rev = Assert.Single(tb.Rows, r => r.AccountCode == "40100");
        Assert.Equal(110000m, rev.Balance);
    }

    /// <summary>
    /// 🔴 G-TB3 — <b>대조군.</b> 팔고(차변) 받아서(대변) 상계된 계정은 <b>잔액 0</b>.
    /// 이게 없으면 "그냥 차변 다 더하기" 같은 엉터리 구현도 G-TB2 를 통과한다.
    /// </summary>
    [Fact]
    public async Task GTB3_대조군_상계된_계정은_잔액이_0()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        var tb = await NewService(db).GetTrialBalanceAsync(Tid, From, To);

        // 외상매출금: 차변 110,000(판매) + 대변 110,000(회수) ⇒ 0
        var ar = Assert.Single(tb.Rows, r => r.AccountCode == "10800");
        Assert.Equal(110000m, ar.DebitTotal);
        Assert.Equal(110000m, ar.CreditTotal);
        Assert.Equal(0m, ar.Balance);
    }

    /// <summary>
    /// 🔴 G-TB4 — <b>계정과목에 없는 코드로 기표된 분개도 사라지면 안 된다.</b>
    /// INNER JOIN 으로 짜면 그 줄이 통째로 빠져 <b>검산이 거짓으로 깨진다</b>
    /// (차변합≠대변합 이 되어 "장부가 틀렸다" 는 잘못된 경고가 뜬다).
    /// </summary>
    [Fact]
    public async Task GTB4_미등록_계정도_시산표에_남는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        // 계정과목에 등록되지 않은 코드로 한 쌍을 더 넣는다
        Exec(db, $"""
            INSERT INTO journal_entries
              (entry_id,tenant_id,entry_no,entry_date,ym,description,source_type,is_confirmed,created_at)
            VALUES ('E9','{Tid}','JE-009','2026-08-15','2026-08','미등록계정 시험','manual',1,NOW());
            """);
        Exec(db, $"""
            INSERT INTO journal_lines (entry_id,tenant_id,account_code,debit_amount,credit_amount)
            VALUES ('E9','{Tid}','99999',5000,0), ('E9','{Tid}','10100',0,5000);
            """);

        var tb = await NewService(db).GetTrialBalanceAsync(Tid, From, To);

        Assert.Contains(tb.Rows, r => r.AccountCode == "99999");
        Assert.True(tb.IsBalanced, "미등록 계정이 빠지면 검산이 거짓으로 깨진다");
    }

    /// <summary>
    /// 🔴 G-TB5 — <b>기간 밖 전표는 안 들어온다.</b>
    /// 기간 필터가 죽으면 시산표가 전 기간을 다 긁어와 숫자도 틀리고 화면도 죽는다.
    /// </summary>
    [Fact]
    public async Task GTB5_기간_밖_전표는_제외된다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        Exec(db, $"""
            INSERT INTO journal_entries
              (entry_id,tenant_id,entry_no,entry_date,ym,description,source_type,is_confirmed,created_at)
            VALUES ('E7','{Tid}','JE-007','2026-07-15','2026-07','7월 전표','manual',1,NOW());
            """);
        Exec(db, $"""
            INSERT INTO journal_lines (entry_id,tenant_id,account_code,debit_amount,credit_amount)
            VALUES ('E7','{Tid}','10100',999999,0), ('E7','{Tid}','40100',0,999999);
            """);

        var tb = await NewService(db).GetTrialBalanceAsync(Tid, From, To);

        // 8월만 봤으므로 7월 999,999 는 안 섞여야 한다
        Assert.Equal(220000m, tb.TotalDebit);
    }

    /// <summary>
    /// 🔴 G-TB6 — <b>다른 테넌트 전표가 섞이면 안 된다</b>(헌법 #2).
    /// 회계장부는 돈이라 격리가 깨지면 곧바로 사고다.
    /// </summary>
    [Fact]
    public async Task GTB6_다른_테넌트는_안_섞인다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        Exec(db, """
            INSERT INTO journal_entries
              (entry_id,tenant_id,entry_no,entry_date,ym,description,source_type,is_confirmed,created_at)
            VALUES ('EX','OTHER-TENANT','JE-X','2026-08-10','2026-08','남의 회사','manual',1,NOW());
            """);
        Exec(db, """
            INSERT INTO journal_lines (entry_id,tenant_id,account_code,debit_amount,credit_amount)
            VALUES ('EX','OTHER-TENANT','10100',777777,0);
            """);

        var tb = await NewService(db).GetTrialBalanceAsync(Tid, From, To);

        Assert.Equal(220000m, tb.TotalDebit);
        Assert.True(tb.IsBalanced);
    }

    /// <summary>
    /// 🔴 G-TB7 — <b>화면이 실재하고 검산 결과를 렌더한다.</b>
    /// 서비스가 값을 내려줘도 화면이 안 그리면 사장님 눈엔 없는 것이다 —
    /// 직전 작3 반려가 정확히 그 자리였다.
    /// </summary>
    [Fact]
    public void GTB7_화면이_검산결과를_렌더한다()
    {
        var page = Read("src", "HitPan.Web", "Pages", "Finance", "TrialBalancePage.razor");

        Assert.Contains("@page \"/accounting/ledger\"", page);
        Assert.Contains("IsBalanced", page);
        Assert.Contains("차변과 대변이 맞지 않습니다", page);   // 불일치를 말해주는가
        Assert.Contains("UnpostedCount", page);                  // 미기표 안내가 있는가
        Assert.Contains("_data is null", page);                  // 실패와 0건을 구분하는가

        var side = Read("src", "HitPan.Web", "Layout", "Sidebar.razor");
        Assert.Contains("/accounting/ledger", side);
    }

    // ────────────────────────────────────────────────────────────────────────

    private static FinanceService NewService(System.Data.IDbConnection db) =>
        new(db, null!, null!);

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !Directory.Exists(Path.Combine(dir, "src")); i++)
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        var all = new[] { dir }.Concat(parts).ToArray();
        return File.ReadAllText(Path.Combine(all));
    }

    /// <summary>
    /// 판매(외상매출금/상품매출) + 회수(현금/외상매출금). 차변합 = 대변합 = 220,000.
    /// </summary>
    private static void Seed(MySqlConnection db)
    {
        Exec(db, $"""
            INSERT INTO accounts (account_code,tenant_id,account_name,account_type,sort_order)
            VALUES ('10100','{Tid}','현금','asset',1),
                   ('10800','{Tid}','외상매출금','asset',2),
                   ('40100','{Tid}','상품매출','revenue',4);
            """);
        Exec(db, $"""
            INSERT INTO journal_entries
              (entry_id,tenant_id,entry_no,entry_date,ym,description,source_type,is_confirmed,created_at)
            VALUES ('E1','{Tid}','JE-001','2026-08-10','2026-08','상품 판매','sales_delivery',1,NOW()),
                   ('E2','{Tid}','JE-002','2026-08-12','2026-08','대금 회수','manual',1,NOW());
            """);
        Exec(db, $"""
            INSERT INTO journal_lines (entry_id,tenant_id,account_code,debit_amount,credit_amount)
            VALUES ('E1','{Tid}','10800',110000,0), ('E1','{Tid}','40100',0,110000),
                   ('E2','{Tid}','10100',110000,0), ('E2','{Tid}','10800',0,110000);
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
              account_code varchar(10) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              account_name varchar(100) NOT NULL,
              account_type varchar(20) NOT NULL,
              parent_code varchar(10) NULL,
              is_active tinyint(1) NOT NULL DEFAULT 1,
              sort_order int(11) NOT NULL DEFAULT 0,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              PRIMARY KEY (account_code, tenant_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE journal_entries (
              entry_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              entry_no varchar(32) NOT NULL,
              entry_date date NOT NULL,
              ym varchar(7) NOT NULL,
              description varchar(200) NOT NULL,
              source_type varchar(30) NOT NULL DEFAULT 'manual',
              source_id varchar(36) NULL,
              is_confirmed tinyint(1) NOT NULL DEFAULT 0,
              confirmed_at datetime(6) NULL,
              confirmed_by varchar(36) NULL,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              created_by varchar(36) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE journal_lines (
              line_id bigint(20) NOT NULL AUTO_INCREMENT PRIMARY KEY,
              entry_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              account_code varchar(10) NOT NULL,
              debit_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              credit_amount decimal(15,2) NOT NULL DEFAULT 0.00,
              partner_id varchar(36) NULL,
              memo varchar(200) NULL,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              source_id varchar(80) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        return db;
    }

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        cmd.ExecuteNonQuery();
    }
}
