using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260827작4 — <b>모든 돈의 흐름이 회계장부 하나로 모이는가.</b>
///
/// <para>
/// 사장님 오더(2026-08-27): <i>"수금, 지급, 경비, 급여등 모든 돈의 흐름을
/// 회계장부 하나로 모두 모여서 정합하도록 배선작업할것."</i>
/// </para>
///
/// <para>
/// 종전엔 이 넷이 <b>분개를 한 줄도 안 만들었다</b>(전수조사 §1-2). 화면·저장은 되는데
/// 회계로 넘기는 문이 없었다. 이 게이트는 그 문이 실제로 열렸는지를 잰다.
/// </para>
///
/// <para>
/// 🔴 <b>실제 서비스를 실 DB 에 물려 부른다.</b> SQL 을 베껴 적어 글자로 검사하면
/// 코드가 바뀌어도 계속 초록불이 된다 — 그게 이 레포에 19번 누적된 가짜 게이트다.
/// 여기서는 <c>CollectionService.CreateCollectionAsync</c> 등을 진짜로 부르고,
/// <b>DB 에 실제로 꽂힌 분개 행</b>으로 판정한다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 화면에 숫자가 뜨는지는 못 잰다. 서비스가 DB 에
/// 남기는 것까지가 범위다. <b>게시본 화면 확인은 사장님 실측이 필요하다</b>
/// (직전 작3 반려가 정확히 그 자리였다 — 게이트는 초록불인데 화면이 안 갔다).
/// </para>
///
/// <para>
/// ⚠️ TEMPORARY 표만 쓴다 — 실제 표는 가리기만 하고 안 건드린다(헌법 #39).
/// </para>
/// </summary>
public sealed class MoneyFlowJournalGateTests
{
    private const string Tid = "GATE-MF827";
    private const string Pid = "P-001";
    private static readonly DateTime D = new(2026, 8, 15);

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // ════════════════════════════════════════════════════════════════════
    // G-MF1~4 — 넷이 실제로 기표되는가 (본안)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 G-MF1 — <b>수금하면 분개가 생긴다.</b> 차변 현금 / 대변 외상매출금.
    /// 받을 돈이 줄고 현금이 느는 게 수금이다. 방향이 뒤집히면 매출채권이 늘어난다.
    /// </summary>
    [Fact]
    public async Task GMF1_수금하면_분개가_생긴다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();

        var id = await NewCollection(db).CreateCollectionAsync(
            new CreateCollectionRequest
            {
                PartnerId = Pid,
                CollectionDate = D,
                Amount = 50000m,
                CollectionMethod = "cash",
            }, Tid, "U1");

        var lines = Lines(db, "collection", id);

        Assert.Equal(2, lines.Count);
        // 차변 현금 50,000
        Assert.Single(lines, l => l.Code == "10100" && l.Debit == 50000m && l.Credit == 0m);
        // 대변 외상매출금 50,000
        Assert.Single(lines, l => l.Code == "10800" && l.Credit == 50000m && l.Debit == 0m);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    /// <summary>
    /// 🔴 G-MF2 — <b>지급하면 분개가 생긴다.</b> 차변 외상매입금 / 대변 보통예금.
    /// 수금의 정확한 반대다.
    /// </summary>
    [Fact]
    public async Task GMF2_지급하면_분개가_생긴다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();

        var id = await NewCollection(db).CreatePaymentAsync(
            new CreatePaymentRequest
            {
                PartnerId = Pid,
                PaymentDate = D,
                Amount = 30000m,
                PaymentMethod = "bank_transfer",
                PaymentType = "payment",
            }, Tid, "U1");

        var lines = Lines(db, "payment", id);

        Assert.Equal(2, lines.Count);
        Assert.Single(lines, l => l.Code == "23200" && l.Debit == 30000m);   // 외상매입금 감소
        Assert.Single(lines, l => l.Code == "10300" && l.Credit == 30000m);  // 보통예금 감소
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    /// <summary>
    /// 🔴 G-MF3 — <b>경비를 승인하면 분개가 생긴다.</b> 분류가 계정으로 매핑된다.
    /// </summary>
    [Fact]
    public async Task GMF3_경비승인하면_분개가_생긴다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();

        var id = SeedExpense(db, "교통비", 11000m, 0m, "cash");
        await NewFinance(db).ApproveExpenseAsync(id, Tid, "approved");

        var lines = Lines(db, "expense", id);

        Assert.Equal(2, lines.Count);
        Assert.Single(lines, l => l.Code == "81200" && l.Debit == 11000m);   // 여비교통비
        Assert.Single(lines, l => l.Code == "10100" && l.Credit == 11000m);  // 현금
    }

    /// <summary>
    /// 🔴 G-MF4 — <b>급여를 지급 처리하면 3줄 분개가 생긴다.</b>
    /// 차변 급여(총지급) / 대변 예수금(공제) + 예금(실지급).
    /// 총지급액과 실수령액의 차액은 회사가 대신 보관했다 나라에 내는 돈이라 부채다.
    /// 이걸 안 나누면 인건비가 과소계상되고 원천세 신고와 장부가 안 맞는다.
    /// </summary>
    [Fact]
    public async Task GMF4_급여지급하면_3줄분개가_생긴다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();

        var id = SeedPayroll(db, gross: 3000000m, deduct: 300000m);
        await NewPayroll(db).MarkPaidAsync(Tid, "U1", id, D);

        var lines = Lines(db, "payroll", id);

        Assert.Equal(3, lines.Count);
        Assert.Single(lines, l => l.Code == "80100" && l.Debit == 3000000m);   // 급여 총액
        Assert.Single(lines, l => l.Code == "25400" && l.Credit == 300000m);   // 예수금
        Assert.Single(lines, l => l.Code == "10300" && l.Credit == 2700000m);  // 실지급
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    // ════════════════════════════════════════════════════════════════════
    // G-MF5~8 — 반증·대조군 (이게 없으면 위 넷은 "전부 기표"로도 통과한다)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 G-MF5 — <b>대조군: 반려한 경비는 기표되지 않는다.</b>
    /// 이게 없으면 "승인이든 반려든 무조건 기표" 같은 엉터리 구현도 G-MF3 을 통과한다.
    /// 반려된 경비가 비용으로 남으면 손익이 틀어진다.
    /// </summary>
    [Fact]
    public async Task GMF5_대조군_반려경비는_기표되지_않는다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();

        var id = SeedExpense(db, "접대비", 50000m, 0m, "card");
        await NewFinance(db).ApproveExpenseAsync(id, Tid, "rejected");

        Assert.Empty(Lines(db, "expense", id));
    }

    /// <summary>
    /// 🔴 G-MF6 — <b>멱등: 두 번 승인해도 분개는 한 벌뿐.</b>
    /// 두 번 눌렀다고 비용이 두 배로 잡히면 장부가 통째로 틀린다.
    /// </summary>
    [Fact]
    public async Task GMF6_두번_승인해도_분개는_한벌()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();

        var id = SeedExpense(db, "통신비", 22000m, 0m, "bank_transfer");
        var svc = NewFinance(db);
        await svc.ApproveExpenseAsync(id, Tid, "approved");
        await svc.ApproveExpenseAsync(id, Tid, "approved");

        Assert.Equal(2, Lines(db, "expense", id).Count);   // 4줄이면 이중기표
        Assert.Equal(1, Entries(db, "expense", id));
    }

    /// <summary>
    /// 🔴 G-MF7 — <b>카드 경비는 미지급금으로 간다.</b>
    /// 카드는 그 자리에서 현금이 안 나가고 결제일에 빠진다 — 그 시차를 미지급금이 잡는다.
    /// 현금으로 잡으면 있지도 않은 현금 유출이 장부에 남는다.
    /// </summary>
    [Fact]
    public async Task GMF7_카드경비는_미지급금으로_간다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();

        var id = SeedExpense(db, "소모품비", 40000m, 0m, "card");
        await NewFinance(db).ApproveExpenseAsync(id, Tid, "approved");

        var lines = Lines(db, "expense", id);
        Assert.Single(lines, l => l.Code == "82500" && l.Debit == 40000m);    // 소모품비
        Assert.Single(lines, l => l.Code == "25300" && l.Credit == 40000m);   // 미지급금
        Assert.DoesNotContain(lines, l => l.Code == "10100");                 // 현금이면 틀렸다
    }

    /// <summary>
    /// 🔴 G-MF8 — <b>모르는 분류는 추측하지 않고 잡비로 간다.</b>
    /// 틀린 계정에 넣는 것보다 잡비에 모아 사람이 재분류하는 편이 안전하다.
    /// 특히 접대비는 세무 한도가 따로라, 다른 계정에 잘못 들어가면 신고가 틀어진다.
    /// </summary>
    [Fact]
    public async Task GMF8_모르는_분류는_잡비로_간다()
    {
        if (!ServerAvailable()) { Skipped(); return; }
        using var db = FreshDb();

        var id = SeedExpense(db, "듣도보도못한분류", 7000m, 0m, "cash");
        await NewFinance(db).ApproveExpenseAsync(id, Tid, "approved");

        Assert.Single(Lines(db, "expense", id), l => l.Code == "84100" && l.Debit == 7000m);
    }

    /// <summary>
    /// 🔴 G-MF9 — <b>계정과목이 실제로 존재한다.</b>
    /// 기표는 <c>journal_lines → accounts</c> FK 를 탄다. 계정이 없으면 FK 1452 로 죽는다.
    /// DB-111 이 심는 코드와 <c>AutoJournalHelper</c> 상수가 어긋나면 여기서 걸린다.
    /// </summary>
    [Fact]
    public void GMF9_기표에_쓰는_계정이_마이그에_들어있다()
    {
        var sql = Read("src", "HitPan.API", "Migrations", "SQL", "DB-111_chart_of_accounts_expand.sql");

        // AutoJournalHelper 가 실제로 쓰는 코드 — 하나라도 빠지면 그 업무 기표가 죽는다
        foreach (var code in new[] { "10100", "10300", "25300", "25400", "80100",
                                     "81100", "81200", "81300", "81400", "82500", "84100" })
        {
            Assert.Contains($"'{code}'", sql);
        }

        // 프로비저너(신규 테넌트 경로)와도 같아야 한다 — 갈리면 한쪽 고객만 죽는다
        var prov = Read("src", "HitPan.API", "Services", "CompanyBootstrapProvisioner.cs");
        foreach (var code in new[] { "10100", "25400", "80100", "84100" })
        {
            Assert.Contains($"\"{code}\"", prov);
        }
    }

    // ────────────────────────────────────────────────────────────────────

    private sealed record Line(string Code, decimal Debit, decimal Credit);

    private static System.Collections.Generic.List<Line> Lines(
        MySqlConnection db, string sourceType, string sourceId)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT l.account_code, l.debit_amount, l.credit_amount
              FROM journal_lines l
              JOIN journal_entries e ON e.entry_id = l.entry_id
             WHERE e.tenant_id = @t AND e.source_type = @st AND e.source_id = @sid
            """;
        cmd.Parameters.AddWithValue("@t", Tid);
        cmd.Parameters.AddWithValue("@st", sourceType);
        cmd.Parameters.AddWithValue("@sid", sourceId);
        var list = new System.Collections.Generic.List<Line>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Line(r.GetString(0), r.GetDecimal(1), r.GetDecimal(2)));
        return list;
    }

    private static int Entries(MySqlConnection db, string sourceType, string sourceId)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM journal_entries WHERE tenant_id=@t AND source_type=@st AND source_id=@sid";
        cmd.Parameters.AddWithValue("@t", Tid);
        cmd.Parameters.AddWithValue("@st", sourceType);
        cmd.Parameters.AddWithValue("@sid", sourceId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static CollectionService NewCollection(IDbConnection db) => new(db, new NullAudit());
    private static FinanceService NewFinance(IDbConnection db) => new(db, new NullAudit(), null!);
    private static PayrollService NewPayroll(IDbConnection db) => new(db, null);

    private static string SeedExpense(MySqlConnection db, string category,
        decimal amount, decimal vat, string method)
    {
        var id = Guid.NewGuid().ToString();
        Exec(db, $"""
            INSERT INTO expenses (expense_id,tenant_id,expense_date,employee_id,category,description,
                                  amount,vat_amount,payment_method,receipt_yn,approval_status,is_active)
            VALUES ('{id}','{Tid}','2026-08-15','E1','{category}','시험',
                    {amount},{vat},'{method}',0,'pending',1);
            """);
        return id;
    }

    private static string SeedPayroll(MySqlConnection db, decimal gross, decimal deduct)
    {
        var id = Guid.NewGuid().ToString();
        Exec(db, $"""
            INSERT INTO payroll_slips (slip_id,tenant_id,employee_id,pay_year,pay_month,
                                       total_payment,total_deduct,net_payment,status)
            VALUES ('{id}','{Tid}','E1',2026,8,{gross},{deduct},{gross - deduct},'confirmed');
            """);
        return id;
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !Directory.Exists(Path.Combine(dir, "src")); i++)
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        var all = new[] { dir }.Concat(parts).ToArray();
        return File.ReadAllText(Path.Combine(all));
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
            CREATE TEMPORARY TABLE accounts (
              account_code varchar(10) NOT NULL, tenant_id varchar(36) NOT NULL,
              account_name varchar(100) NOT NULL, account_type varchar(20) NOT NULL,
              parent_code varchar(10) NULL, is_active tinyint(1) NOT NULL DEFAULT 1,
              sort_order int(11) NOT NULL DEFAULT 0,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              PRIMARY KEY (account_code, tenant_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE journal_entries (
              entry_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              entry_no varchar(30) NOT NULL, entry_date date NOT NULL, ym varchar(7) NOT NULL,
              description varchar(255) NULL, source_type varchar(30) NULL, source_id varchar(36) NULL,
              is_confirmed tinyint(1) NOT NULL DEFAULT 1, confirmed_at datetime(6) NULL,
              confirmed_by varchar(36) NULL, created_at datetime(6) NOT NULL,
              created_by varchar(36) NULL
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
            CREATE TEMPORARY TABLE collections (
              collection_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              partner_id varchar(36) NULL, collection_date date NOT NULL,
              amount decimal(15,2) NOT NULL, collection_method varchar(20) NOT NULL DEFAULT 'cash',
              ref_doc_type varchar(20) NULL, ref_doc_id varchar(36) NULL, memo varchar(255) NULL,
              is_active tinyint(1) NOT NULL DEFAULT 1, created_by varchar(36) NULL,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE payments (
              payment_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              partner_id varchar(36) NULL, payment_date date NOT NULL,
              amount decimal(15,2) NOT NULL, payment_method varchar(20) NOT NULL DEFAULT 'cash',
              payment_type varchar(20) NOT NULL DEFAULT 'payment', ref_order_id varchar(36) NULL,
              memo varchar(255) NULL, is_active tinyint(1) NOT NULL DEFAULT 1,
              created_by varchar(36) NULL,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE partner_balance (
              balance_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              partner_id varchar(36) NOT NULL, total_sales decimal(15,2) NOT NULL DEFAULT 0,
              total_receipt decimal(15,2) NOT NULL DEFAULT 0,
              total_purchase decimal(15,2) NOT NULL DEFAULT 0,
              total_payment decimal(15,2) NOT NULL DEFAULT 0,
              last_updated_at datetime(6) NULL,
              UNIQUE KEY uk_pb (tenant_id, partner_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE expenses (
              expense_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              expense_date date NOT NULL, employee_id varchar(36) NULL, category varchar(50) NULL,
              description varchar(255) NULL, amount decimal(15,2) NOT NULL DEFAULT 0,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0,
              payment_method varchar(20) NOT NULL DEFAULT 'card',
              receipt_yn tinyint(1) NOT NULL DEFAULT 0,
              approval_status varchar(20) NOT NULL DEFAULT 'pending',
              approval_id varchar(36) NULL, memo varchar(255) NULL,
              is_active tinyint(1) NOT NULL DEFAULT 1, created_by varchar(36) NULL,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              updated_at datetime(6) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE payroll_slips (
              slip_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              employee_id varchar(36) NOT NULL, pay_year int NOT NULL, pay_month int NOT NULL,
              pay_date date NULL, total_payment decimal(15,2) NOT NULL DEFAULT 0,
              total_deduct decimal(15,2) NOT NULL DEFAULT 0,
              net_payment decimal(15,2) NOT NULL DEFAULT 0,
              status varchar(20) NOT NULL DEFAULT 'draft',
              confirmed_by varchar(36) NULL, confirmed_at datetime(6) NULL,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              updated_at datetime(6) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE employees (
              employee_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              user_id varchar(36) NULL, emp_name varchar(50) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        // ⚠️ 실 스키마와 컬럼명이 같아야 한다 — `year_month`(char 6) · `status`.
        //   ym/is_closed 로 잘못 만들었더니 월마감 검사가 "Unknown column 'status'" 로 죽었다.
        //   가짜 스키마로 시험하면 시험만 통과하고 실물은 죽는다.
        Exec(db, """
            CREATE TEMPORARY TABLE monthly_closing (
              closing_id varchar(36) NOT NULL PRIMARY KEY, tenant_id varchar(36) NOT NULL,
              `year_month` char(6) NOT NULL, status varchar(20) NOT NULL DEFAULT 'open'
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        // 계정과목 — DB-111 이 심는 것과 같은 코드
        Exec(db, $"""
            INSERT INTO accounts (account_code,tenant_id,account_name,account_type,sort_order) VALUES
              ('10100','{Tid}','현금','asset',1),      ('10300','{Tid}','보통예금','asset',2),
              ('10800','{Tid}','외상매출금','asset',3), ('23200','{Tid}','외상매입금','liability',4),
              ('25300','{Tid}','미지급금','liability',5),('25400','{Tid}','예수금','liability',6),
              ('80100','{Tid}','급여','expense',7),     ('81100','{Tid}','복리후생비','expense',8),
              ('81200','{Tid}','여비교통비','expense',9),('81300','{Tid}','접대비','expense',10),
              ('81400','{Tid}','통신비','expense',11),  ('82500','{Tid}','소모품비','expense',12),
              ('84100','{Tid}','잡비','expense',13);
            """);
        Exec(db, $"INSERT INTO employees (employee_id,tenant_id,user_id,emp_name) VALUES ('E1','{Tid}','U1','시험직원');");

        return db;
    }

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private sealed class NullAudit : HitPan.Application.Interfaces.IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            IDbTransaction? tx = null,
            System.Threading.CancellationToken ct = default) => Task.CompletedTask;
    }
}
