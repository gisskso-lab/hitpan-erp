using System;
using System.Data;
using System.Threading.Tasks;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260827작1 W3 — <b>매입반품 목록 정합성 게이트.</b>
///
/// <para>
/// 사장님 실측 반려(1.3.21): <i>"매입목록과 반품목록 정합성이 안맞음"</i>.
/// 원인은 <b>세 화면의 반품 필터가 제각각</b>이었다는 것.
/// </para>
///
/// <para>
/// 같은 데이터(confirmed 2 · canceled 1 · draft 1)·같은 30일 창에서 실측한 결과:
/// <list type="bullet">
///   <item>반품현황(집계) <c>status='confirmed'</c> → <b>2건</b> 120,000</item>
///   <item>반품목록 <c>GetReturnsAsync</c> 필터없음 → <b>4건</b> 260,000 ← 이 자리가 문제였다</item>
///   <item>매입목록 「반품」표기 <c>status&lt;&gt;'canceled'</c> → <b>3건</b> 200,000</item>
/// </list>
/// 취소한 반품이 목록에 살아있는 반품처럼 남아, 담당자가 이미 되돌린 건을 또 처리할 수 있었다.
/// </para>
///
/// <para>
/// 🔴 <b>이 게이트는 실제 <c>PurchaseService.GetReturnsAsync</c> 를 부른다.</b>
/// SQL 을 베껴 적어 글자로 검사하면 <b>코드가 바뀌어도 계속 초록</b>이 된다 — 그건 가짜다.
/// 가짜 게이트가 이 레포에 이미 19번 누적됐다. 진짜 서비스에 진짜 DB 를 물려
/// <b>나오는 건수</b>로 판정한다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 화면에 「반품」 글자가 뜨는지는 못 잰다.
/// 서비스가 내려보내는 <b>행 수</b>까지가 범위다.
/// 또한 <b>날짜 기준 불일치</b>(매입일 vs 반품일)는 여기서 재지 않는다 —
/// 그건 20260827작1 §8 사장님 결재 대기 사항이다.
/// </para>
/// </summary>
public sealed class PurchaseReturnListParityGateTests
{
    private const string Tid = "GATE-P827-TENANT";
    private static readonly DateTime From = new(2026, 7, 28);
    private static readonly DateTime To = new(2026, 8, 27);

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    /// <summary>
    /// 🔴 G-P827-1 — <b>핵심.</b> 취소된 반품은 목록에 뜨면 안 된다.
    /// confirmed 2 + draft 1 = <b>3건</b>이어야 하고, canceled 1 건은 빠져야 한다.
    /// (draft 는 작성중이라 <b>남긴다</b> — 담당자가 이어서 고쳐야 하므로.)
    /// </summary>
    [Fact]
    public async Task GP827_1_취소된_반품은_목록에서_빠진다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        var rows = await NewService(db).GetReturnsAsync(Tid, From, To);

        Assert.DoesNotContain(rows, r =>
            string.Equals(r.Status, "canceled", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, rows.Count);
    }

    /// <summary>
    /// 🔴 G-P827-2 — <b>정합성 본안.</b> 반품목록 건수 == 매입목록 「반품」표기 대상 건수.
    /// 매입목록은 <c>status &lt;&gt; 'canceled'</c> 로 표기하므로, 목록도 같은 모수여야
    /// 두 화면 숫자가 맞는다. 이 둘이 어긋나면 사장님이 보신 그 증상이다.
    /// </summary>
    [Fact]
    public async Task GP827_2_반품목록과_매입목록_반품표기_모수가_같다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        var listCount = (await NewService(db).GetReturnsAsync(Tid, From, To)).Count;

        // 매입목록 ReturnStatus 서브쿼리와 **같은 조건**으로 센다
        // (PurchaseService.cs ReturnStatus: is_deleted=0 AND status<>'canceled')
        var markCount = Convert.ToInt32(Scalar(db, $"""
            SELECT COUNT(*) FROM purchase_returns
             WHERE tenant_id='{Tid}' AND is_deleted=0 AND status<>'canceled'
               AND return_date BETWEEN '2026-07-28' AND '2026-08-27';
            """));

        Assert.Equal(markCount, listCount);
    }

    /// <summary>
    /// 🔴 G-P827-3 — <b>대조군.</b> 게이트가 "전부 막기"로도 통과해버리면 가짜다.
    /// draft 는 <b>반드시 남아야</b> 한다. 이게 없으면 <c>status='confirmed'</c> 로
    /// 과하게 조여도 위 두 시험이 통과해버린다.
    /// </summary>
    [Fact]
    public async Task GP827_3_대조군_작성중_반품은_목록에_남는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);

        var rows = await NewService(db).GetReturnsAsync(Tid, From, To);

        Assert.Contains(rows, r =>
            string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 🔴 G-P827-4 — <b>회귀.</b> 20260826작2 의 500 재발 방지.
    /// 날짜를 <b>줄 때</b>만 터졌던 자리다(raw string 이어붙이기 → <c>0AND</c>).
    /// 날짜 유/무 두 경로가 모두 살아있어야 한다.
    /// </summary>
    [Fact]
    public async Task GP827_4_날짜_있든_없든_터지지_않는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        Seed(db);
        var svc = NewService(db);

        var withDates = await svc.GetReturnsAsync(Tid, From, To);   // 500 이 나던 경로
        var noDates = await svc.GetReturnsAsync(Tid, null, null);

        Assert.Equal(3, withDates.Count);
        Assert.Equal(3, noDates.Count);
    }

    /// <summary>
    /// 🔴 G-P827-5 — <b>§8-B 「반품포함」.</b> 끄면 종전 그대로, 켜면 <b>날짜창 밖 매입</b>이
    /// 함께 나와야 한다. 7월 매입(<c>R3</c>)을 8월에 반품(<c>X3</c>)한 건이 그 대상이다.
    /// </summary>
    [Fact]
    public async Task GP827_5_반품포함을_켜면_날짜창_밖_매입도_나온다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedReceipts(db);
        Seed(db);
        var svc = NewService(db);

        var off = await svc.GetReceiptsAsync(Tid, From, To, null, default, false);
        var on = await svc.GetReceiptsAsync(Tid, From, To, null, default, true);

        // 끄면 8월 매입만(R1·R2·R4) — 7월 매입 R3 은 창 밖
        Assert.DoesNotContain(off, r => r.ReceiptNo == "매입-003");
        // 켜면 R3 이 합류한다(8월에 반품이 일어났으므로)
        Assert.Contains(on, r => r.ReceiptNo == "매입-003");
        Assert.True(on.Count > off.Count, "반품포함이 켜지면 건수가 늘어야 한다");
    }

    /// <summary>
    /// 🔴 G-P827-6 — <b>대조군.</b> 「반품포함」을 켜도 <b>반품이 없는</b> 창 밖 매입은
    /// 들어오면 안 된다. 이게 없으면 "그냥 날짜를 다 무시하기" 로도 위 시험이 통과한다.
    /// </summary>
    [Fact]
    public async Task GP827_6_대조군_반품없는_창밖_매입은_안_들어온다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();
        SeedReceipts(db);
        Seed(db);

        // R9 = 7월 매입인데 반품이 **없다** ⇒ 켜도 나오면 안 된다
        Exec(db, $"""
            INSERT INTO purchase_receipts
              (receipt_id,tenant_id,receipt_no,partner_id,receipt_date,source_type,status,total_amount,vat_amount,created_at)
            VALUES ('R9','{Tid}','매입-009','P1','2026-07-02','manual','confirmed',900000,90000,NOW());
            """);

        var on = await NewService(db).GetReceiptsAsync(Tid, From, To, null, default, true);

        Assert.DoesNotContain(on, r => r.ReceiptNo == "매입-009");
    }

    // ────────────────────────────────────────────────────────────────────────
    //  헬퍼
    // ────────────────────────────────────────────────────────────────────────

    /// <remarks>
    /// <c>GetReturnsAsync</c> 는 <c>_db</c> 만 쓴다(실측 확인). 나머지 의존성은
    /// 이 경로에서 호출되지 않으므로 <c>null!</c> 로 둔다 — 시험이 무엇을 재는지
    /// 좁혀 두는 편이 가짜 초록불보다 낫다.
    /// </remarks>
    private static PurchaseService NewService(IDbConnection db) =>
        new(null!, null!, db, null!);

    /// <summary>
    /// 매입 4건. <c>R3</c> 만 <b>7월</b>(=30일 창 밖) — §8-B 시험의 주인공이다.
    /// </summary>
    private static void SeedReceipts(MySqlConnection db)
    {
        Exec(db, $"""
            INSERT INTO purchase_receipts
              (receipt_id,tenant_id,receipt_no,partner_id,receipt_date,source_type,status,total_amount,vat_amount,created_at)
            VALUES
              ('R1','{Tid}','매입-001','P1','2026-08-20','manual','confirmed',100000,10000,NOW()),
              ('R2','{Tid}','매입-002','P1','2026-08-20','manual','confirmed',200000,20000,NOW()),
              ('R3','{Tid}','매입-003','P1','2026-07-01','manual','confirmed',300000,30000,NOW()),
              ('R4','{Tid}','매입-004','P1','2026-08-22','manual','confirmed',400000,40000,NOW());
            """);
    }

    /// <summary>
    /// confirmed 2 · canceled 1 · draft 1. 사장님 증상 재현에 쓴 것과 같은 모양.
    /// </summary>
    private static void Seed(MySqlConnection db)
    {
        Exec(db, $"""
            INSERT INTO purchase_returns
              (return_id,tenant_id,return_no,receipt_id,partner_id,return_date,
               return_type,status,total_amount,vat_amount,is_deleted,created_at,created_by)
            VALUES
              ('X1','{Tid}','매반-001','R1','P1','2026-08-21','normal','confirmed',50000,5000,0,NOW(),'u1'),
              ('X2','{Tid}','매반-002','R2','P1','2026-08-21','normal','canceled', 60000,6000,0,NOW(),'u1'),
              ('X3','{Tid}','매반-003','R3','P1','2026-08-25','normal','confirmed',70000,7000,0,NOW(),'u1'),
              ('X4','{Tid}','매반-004','R4','P1','2026-08-23','normal','draft',    80000,8000,0,NOW(),'u1');
            """);
    }

    private static void Skipped() =>
        Console.Error.WriteLine(
            "[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라.");

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        // 🔴 비밀번호를 코드에 적지 않는다 (기존 게이트 관례).
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다 (작14 W1)
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
    /// 시험용 <c>TEMPORARY</c> 표. 커넥션이 닫히면 서버가 지운다 —
    /// 이 DB 의 실제 표는 <b>가리기만 하고 건드리지 않는다</b>(헌법 #39).
    /// </summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_returns (
              return_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              return_no varchar(50) NOT NULL,
              receipt_id varchar(36) NULL,
              partner_id varchar(36) NOT NULL,
              return_date date NOT NULL,
              return_type varchar(30) NULL,
              status varchar(20) NULL,
              total_amount decimal(15,2) NULL,
              vat_amount decimal(15,2) NULL,
              memo longtext NULL,
              created_at datetime NULL,
              updated_at datetime NULL,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              created_by varchar(36) NULL,
              return_reason varchar(50) NULL,
              return_reason_memo longtext NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_receipts (
              receipt_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              receipt_no varchar(50) NOT NULL,
              po_id varchar(36) NULL,
              partner_id varchar(36) NOT NULL,
              receipt_date date NOT NULL,
              source_type varchar(30) NOT NULL,
              status varchar(20) NULL,
              total_amount decimal(15,2) NOT NULL,
              vat_amount decimal(15,2) NOT NULL,
              memo longtext NULL,
              created_at datetime NOT NULL,
              created_by varchar(36) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE partners (
              partner_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              partner_name varchar(200) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE employees (
              user_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
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

    private static object? Scalar(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        return cmd.ExecuteScalar();
    }
}
