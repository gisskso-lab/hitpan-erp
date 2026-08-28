using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260827작3 — <b>매입↔반품 연결 「표기」 게이트.</b>
///
/// <para>
/// 사장님 실측 반려(1.3.24): <i>"반품시 매입전표 어떤항목을 불러와서 반품을 했는지,
/// 반품과 매입 연결에 대한 반품목록 그리드, 매입전표목록 그리드에 표기를 정확하게 해!!!"</i>
/// </para>
///
/// <para>
/// 🔴 <b>왜 직전 게이트 6건이 전부 초록불인데 사장님은 반려하셨나</b> — 그 게이트들은
/// <b>건수(모수)만</b> 봤다. "몇 건이 나오는가" 는 맞았지만 <b>"어느 매입에서 나온
/// 반품인지 화면에 보이는가" 는 아무도 안 봤다.</b> <c>receipt_id</c> 는 DB 에 진작
/// 있었는데 DTO·SELECT 에 없어 <b>화면까지 값이 가지 않았다.</b>
/// ⇒ <b>서비스가 값을 내려보내는지 + 화면이 그 값을 렌더하는지</b> 둘 다 본다.
/// </para>
/// </summary>
public sealed class PurchaseReturnLinkDisplayGateTests
{
    private const string Tid = "GATE-LINK827";
    private static readonly DateTime From = new(2026, 8, 1);
    private static readonly DateTime To = new(2026, 8, 31);

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    /// <summary>
    /// 🔴 G-L1 — 반품목록이 <b>원 매입전표번호를 실제로 내려준다.</b>
    /// 값이 안 내려오면 화면은 표기할 것이 없다(이번 반려의 뿌리).
    /// </summary>
    [Fact]
    public async Task GL1_반품목록이_원매입전표를_내려준다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        var rows = await NewService(db).GetReturnsAsync(Tid, From, To);

        var a1 = Assert.Single(rows, r => r.ReturnNo == "매반-A1");
        Assert.Equal("RA", a1.ReceiptId);
        Assert.Equal("매입-A", a1.ReceiptNo);
    }

    /// <summary>
    /// 🔴 G-L2 — 매입목록이 <b>반품전표번호를 전부</b> 내려준다.
    /// 부분반품을 나눠서 하면 둘 이상이다 — 하나만 주면 나머지가 숨는다.
    /// </summary>
    [Fact]
    public async Task GL2_매입목록이_반품전표를_전부_내려준다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        var rows = await NewService(db).GetReceiptsAsync(Tid, From, To);

        var ra = Assert.Single(rows, r => r.ReceiptNo == "매입-A");
        Assert.NotNull(ra.ReturnNos);
        Assert.Contains("매반-A1", ra.ReturnNos);
        Assert.Contains("매반-A2", ra.ReturnNos);   // 🔴 둘째가 숨으면 FAIL
    }

    /// <summary>
    /// 🔴 G-L3 — <b>대조군.</b> 반품이 <b>없는</b> 매입은 반품전표가 비어야 한다.
    /// 이게 없으면 "전부 채우기" 로도 G-L2 가 통과한다.
    /// </summary>
    [Fact]
    public async Task GL3_대조군_반품없는_매입은_비어있다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        var rows = await NewService(db).GetReceiptsAsync(Tid, From, To);

        var rb = Assert.Single(rows, r => r.ReceiptNo == "매입-B");
        Assert.True(string.IsNullOrWhiteSpace(rb.ReturnNos),
            "반품이 없는 매입에 반품전표가 붙으면 안 된다");
    }

    /// <summary>
    /// 🔴 G-L4 — 매입명세서 없이 <b>직접 작성한 반품</b>은 원매입이 비어야 한다.
    /// ⚠️ 그래도 <b>목록에서 사라지면 안 된다</b>(LEFT JOIN 이어야 하는 이유).
    /// </summary>
    [Fact]
    public async Task GL4_직접작성_반품도_목록에_남는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        var rows = await NewService(db).GetReturnsAsync(Tid, From, To);

        var c = Assert.Single(rows, r => r.ReturnNo == "매반-C");
        Assert.True(string.IsNullOrWhiteSpace(c.ReceiptNo));
    }

    /// <summary>
    /// 🔴 G-L5 — <b>화면이 그 값을 실제로 렌더하는가.</b>
    /// 서비스가 내려줘도 그리드가 안 그리면 사장님 눈엔 여전히 없다 —
    /// 이번 반려가 정확히 그 자리였다.
    /// </summary>
    [Fact]
    public void GL5_두_그리드가_연결값을_렌더한다()
    {
        var ret = Read("src", "HitPan.Web", "Components", "Purchase", "PurchaseReturnList.razor");
        Assert.Contains("원 매입전표", ret);
        Assert.Contains("context.ReceiptNo", ret);
        Assert.Contains("직접작성", ret);          // 빈칸 대신 사유를 말해준다

        var rcp = Read("src", "HitPan.Web", "Components", "Purchase", "PurchaseReceiptList.razor");
        Assert.Contains("반품전표", rcp);
        Assert.Contains("context.ReturnNos", rcp);
    }

    /// <summary>
    /// 🔴 G-L6 — <b>전표별 장부(4번 반려)</b> 가 두 현황 화면에 실재한다.
    /// 사장님: <i>"전체 장부가 없네?? 그래서 확인이 안되네?"</i>
    /// </summary>
    [Fact]
    public void GL6_현황화면에_전표별_장부가_있다()
    {
        var rs = Read("src", "HitPan.Web", "Pages", "Purchase", "ReturnStatusPage.razor");
        Assert.Contains("전표별 반품장부", rs);
        Assert.Contains("_docRows", rs);
        Assert.Contains("원 매입전표", rs);

        var ps = Read("src", "HitPan.Web", "Pages", "Purchase", "PurchaseStatusPage.razor");
        Assert.Contains("전표별 매입장부", ps);
        Assert.Contains("_docRows", ps);
        Assert.Contains("반품전표", ps);
    }

    // ────────────────────────────────────────────────────────────────────────

    private static PurchaseService NewService(System.Data.IDbConnection db) =>
        new(null!, null!, db, null!);

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !Directory.Exists(Path.Combine(dir, "src")); i++)
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        var all = new[] { dir }.Concat(parts).ToArray();
        return File.ReadAllText(Path.Combine(all));
    }

    /// <summary>매입 2건(A=부분반품 2건 · B=반품없음) + 반품 3건(A1·A2·직접작성 C).</summary>
    private static void Seed(MySqlConnection db)
    {
        Exec(db, $"""
            INSERT INTO purchase_receipts
              (receipt_id,tenant_id,receipt_no,partner_id,receipt_date,source_type,status,total_amount,vat_amount,created_at)
            VALUES
              ('RA','{Tid}','매입-A','P1','2026-08-20','manual','confirmed',100000,10000,NOW()),
              ('RB','{Tid}','매입-B','P1','2026-08-21','manual','confirmed',200000,20000,NOW());
            """);
        Exec(db, $"""
            INSERT INTO purchase_returns
              (return_id,tenant_id,return_no,receipt_id,partner_id,return_date,return_type,status,
               total_amount,vat_amount,is_deleted,created_at)
            VALUES
              ('XA1','{Tid}','매반-A1','RA','P1','2026-08-22','normal','confirmed',30000,3000,0,NOW()),
              ('XA2','{Tid}','매반-A2','RA','P1','2026-08-23','normal','confirmed',20000,2000,0,NOW()),
              ('XC', '{Tid}','매반-C', NULL,'P1','2026-08-24','normal','confirmed', 5000, 500,0,NOW());
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
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다 (작14 W1)
        try { using var c = new MySqlConnection(ConnString()); c.Open(); return true; }
        catch (MySqlException) { return false; }
    }

    /// <summary>TEMPORARY 표만 쓴다 — 실제 표는 가리기만 하고 안 건드린다(헌법 #39).</summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_receipts (
              receipt_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL, receipt_no varchar(50) NOT NULL,
              po_id varchar(36) NULL, partner_id varchar(36) NOT NULL,
              receipt_date date NOT NULL, source_type varchar(30) NOT NULL,
              status varchar(20) NULL, total_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL, memo longtext NULL,
              created_at datetime NOT NULL, created_by varchar(36) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_returns (
              return_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL, return_no varchar(50) NOT NULL,
              receipt_id varchar(36) NULL, partner_id varchar(36) NOT NULL,
              return_date date NOT NULL, return_type varchar(30) NULL,
              status varchar(20) NULL, total_amount decimal(15,2) NULL,
              vat_amount decimal(15,2) NULL, memo longtext NULL,
              created_at datetime NULL, updated_at datetime NULL,
              is_deleted tinyint(1) NOT NULL DEFAULT 0, created_by varchar(36) NULL,
              return_reason varchar(50) NULL, return_reason_memo longtext NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE partners (
              partner_id varchar(36) NOT NULL, tenant_id varchar(36) NOT NULL,
              partner_name varchar(200) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE employees (
              user_id varchar(36) NOT NULL, tenant_id varchar(36) NOT NULL,
              emp_name varchar(100) NULL
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
