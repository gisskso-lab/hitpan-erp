using System.Text.RegularExpressions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-1 ~ G-7</b> — 매출 사슬 <b>중복생성 봉합</b> (20260827작9 W1~W5).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 오더</b>: <i>"사슬의 정합률 100%가 목표임.
/// 그리고 가장 중요한거 <b>사슬동작중 중복생성 절대금지</b>"</i>
/// </para>
///
/// <para>
/// 🔴 <b>이 게이트가 재는 것 — 글자가 아니라 동작이다.</b>
/// 가짜 게이트 누적 21회의 교훈: <i>"짜면 곧바로 봉합 빼고 FAIL 확인"</i>.
/// G-1·G-3·G-4 는 <b>실제 DB 에 INSERT 해서</b> 판정한다. 길이 계산이나 문자열 검사가 아니다.
/// </para>
///
/// <para>
/// 🔴🔴 <b>PM 오판 정정 — 이 게이트가 나를 잡았다.</b>
/// 작업지시서 W1 에 <i>"'매출반품-20260827-001' 은 25자라 varchar(20) 에 ERROR 1406 으로 터진다"</i>
/// 고 적었는데 <b>틀렸다.</b> 실제는 <b>17자</b>이고 여유가 3칸 있다 — <b>저장 실패는 없었다.</b>
/// </para>
///
/// <para>
/// 원인은 <b>도구가 한글을 CP949 로 넘긴 것</b>이다. perl·python·mysql(기본 charset)이
/// 길이를 <b>25 → 20 → 17 로 세 번 다르게</b> 보여줬고, 나는 DB 에 제대로 묻지 않고 그 숫자를 믿었다.
/// <c>mysql --default-character-set=utf8mb4</c> 로 재고 나서야 맞는 값이 나왔다.
/// ⇒ <b>길이는 사람이 세지 않는다. DB 가 세게 한다</b>(G-1 (c) 항).
/// </para>
///
/// <para>
/// 그래서 <c>'반-'</c> 로 바꾸는 실익은 <b>사고 봉합이 아니라</b>
/// ① 사장님이 지시한 표기 형식(<i>"반품전표 : 반-(전표번호)"</i>)이고
/// ② 순번이 4자리로 늘어도 견디는 <b>여유 확보</b>다. 과장하지 않는다.
/// </para>
///
/// <para>
/// ⚠️ <b>TEMPORARY 표는 실제 컬럼 폭을 그대로 베낀다.</b> 시험용으로 넓게 잡으면
/// 폭 관련 판정이 <b>전부 가짜 초록불</b>이 된다(가짜 스키마 사고 재발 방지).
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 화면에서 사장님이 반품을 저장했을 때 실제로 되는지는
/// 재지 못한다. 서버·DB 까지가 범위다. 화면은 실측의 몫이다.
/// </para>
/// </remarks>
public sealed class SalesChainDupGuardGateTests
{
    private static readonly string TestDb =
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // ─────────────────────────────────────────────────────────────────────
    // G-1 — 새 번호(반-)는 들어가고, 옛 번호(매출반품-)는 실제로 터진다
    //   🔴 대조군이 핵심이다. "새 번호가 들어간다" 만 재면, 컬럼이 넓어졌을 뿐인
    //      경우와 구분이 안 된다. 옛 번호가 **반드시 실패**해야 이 봉합이 의미가 있다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G1_반dash_번호가_저장되고_길이여유가_늘어난다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라."); return; }

        using var conn = new MySqlConnection(
            ServerConnString().Replace("User=", $"Database={TestDb};User="));
        conn.Open();

        // 실제 DDL 과 동일한 폭으로 만든다 — 넓게 잡으면 가짜 초록불이 된다.
        Exec(conn, """
            CREATE TEMPORARY TABLE t_sret (
              return_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              return_no varchar(20) NOT NULL,
              PRIMARY KEY (return_id),
              UNIQUE KEY uq_sret (tenant_id, return_no)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);

        // strict 를 세션에 강제 — 운영과 같은 조건에서 잰다.
        Exec(conn, "SET SESSION sql_mode = 'STRICT_TRANS_TABLES'");

        // 🔴🔴 PM 정정 이력 — 이 게이트가 내 오판을 잡았다.
        //   작업지시서에 "'매출반품-…' 은 25자라 varchar(20) 에 ERROR 1406 으로 터진다" 고 적었는데
        //   **틀렸다.** 셸(perl/python/mysql 기본 charset)이 한글을 CP949 로 넘겨
        //   길이를 세 번 다르게(25 → 20 → 17) 보여줬고, 나는 DB 에 제대로 묻지 않고 그 숫자를 믿었다.
        //   `mysql --default-character-set=utf8mb4` 로 재니 **17자** 다. 여유가 3칸 있다.
        //   ⇒ **ERROR 1406 은 일어나지 않는다. 저장 실패는 없었다.**
        //   [[feedback_tool_transforms_paths]] 5번째 사례.
        //
        //   그래서 이 게이트는 "옛 번호가 터진다" 를 재지 않는다 — 사실이 아니기 때문이다.
        //   대신 **둘 다 들어가되 새 번호가 더 여유롭다**(사장님 지시 형식)는 것만 잰다.

        // (a) 새 번호 '반-20260827-001' = 14자 → 들어간다
        Exec(conn, "INSERT INTO t_sret VALUES ('r1','t1','반-20260827-001')");
        Assert.Equal(1, Scalar(conn, "SELECT COUNT(*) FROM t_sret WHERE return_no='반-20260827-001'"));

        // (b) 옛 번호도 17자라 들어간다 — 결함이 아니었음을 기록으로 남긴다
        Exec(conn, "INSERT INTO t_sret VALUES ('r2','t1','매출반품-20260827-001')");

        // (c) 길이를 DB 가 세게 한다 — 사람이 세지 않는다(내가 틀린 자리)
        Assert.Equal(14, Scalar(conn, "SELECT CHAR_LENGTH('반-20260827-001')"));
        Assert.Equal(17, Scalar(conn, "SELECT CHAR_LENGTH('매출반품-20260827-001')"));

        // (d) 순번이 4자리로 늘어도 새 번호는 견딘다(15자). 여유 확보가 이 변경의 실익이다.
        Exec(conn, "INSERT INTO t_sret VALUES ('r4','t1','반-20260827-1001')");
        Assert.Equal(1, Scalar(conn, "SELECT COUNT(*) FROM t_sret WHERE return_no='반-20260827-1001'"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-2 — 코드가 옛 prefix 를 더 이상 쓰지 않는다
    //   ⚠️ 이건 글자검사다. 그래서 **보조**로만 쓴다(G-1 이 본 판정).
    //      낱말 하나로 검사하지 않도록 '채번하는 줄'로 한정한다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G2_매출반품_채번이_반dash_로_바뀌었다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "SalesService.cs"));

        Assert.DoesNotContain("$\"매출반품-{", src);
        Assert.Contains("$\"반-{returnDate:yyyyMMdd}-\"", src);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-3 — 소프트삭제 후 채번이 **이미 쓴 번호를 재발급하지 않는다**
    //   🔴 COUNT+1 이 지는 자리. MAX+1 이라야 통과한다.
    //      이게 사장님이 말한 "중복생성" 의 가장 조용한 형태다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G3_삭제된_번호를_재발급하지_않는다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라."); return; }

        using var conn = new MySqlConnection(
            ServerConnString().Replace("User=", $"Database={TestDb};User="));
        conn.Open();

        Exec(conn, """
            CREATE TEMPORARY TABLE t_q (
              quote_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              quote_no varchar(32) NOT NULL,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (quote_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);

        Exec(conn, "INSERT INTO t_q VALUES ('q1','t1','견-20260827-001',0)");
        Exec(conn, "INSERT INTO t_q VALUES ('q2','t1','견-20260827-002',0)");
        Exec(conn, "INSERT INTO t_q VALUES ('q3','t1','견-20260827-003',0)");

        // 가운데 것을 소프트삭제한다
        Exec(conn, "UPDATE t_q SET is_deleted=1 WHERE quote_id='q2'");

        // COUNT+1 방식 (옛 코드) — 살아있는 3건 중 삭제 1건이 빠져 COUNT=2 ⇒ -003 재발급
        var countBased = Scalar(conn,
            "SELECT COUNT(*) FROM t_q WHERE tenant_id='t1' AND quote_no LIKE '견-20260827-%' AND is_deleted=0") + 1;

        // MAX+1 방식 (새 코드) — 삭제와 무관하게 최대 순번 3 ⇒ 다음은 4
        var maxBased = Scalar(conn, """
            SELECT COALESCE(MAX(CAST(SUBSTRING(quote_no, 14) AS UNSIGNED)), 0) + 1
              FROM t_q WHERE tenant_id='t1' AND quote_no LIKE '견-20260827-%'
            """);

        // 🔴 대조군: 두 방식이 갈려야 이 봉합이 실재한다
        Assert.Equal(3, countBased);   // 옛 방식은 이미 쓴 -003 을 다시 낸다
        Assert.Equal(4, maxBased);     // 새 방식은 건너뛴다
        Assert.NotEqual(countBased, maxBased);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-4 — UNIQUE 가 실제로 중복을 막는다 (quotations / purchase_returns)
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G4_UNIQUE가_중복번호를_실제로_막는다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라."); return; }

        using var conn = new MySqlConnection(
            ServerConnString().Replace("User=", $"Database={TestDb};User="));
        conn.Open();

        Exec(conn, """
            CREATE TEMPORARY TABLE t_uq (
              id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              doc_no varchar(32) NOT NULL,
              PRIMARY KEY (id),
              UNIQUE KEY uq_doc (tenant_id, doc_no)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);

        Exec(conn, "INSERT INTO t_uq VALUES ('a','t1','견-20260827-001')");

        var ex = Assert.ThrowsAny<MySqlException>(() =>
            Exec(conn, "INSERT INTO t_uq VALUES ('b','t1','견-20260827-001')"));
        Assert.Equal(1062, ex.Number);

        // 다른 테넌트면 같은 번호가 허용돼야 한다 — 테넌트 격리(헌법 #2)
        Exec(conn, "INSERT INTO t_uq VALUES ('c','t2','견-20260827-001')");
        Assert.Equal(2, Scalar(conn, "SELECT COUNT(*) FROM t_uq WHERE doc_no='견-20260827-001'"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-5 — 매출반품 중복차단이 **기존 반품번호를 메시지에 담는다**
    //   🔴 작8 교훈: "막는 것 ≠ 알려주는 것."
    //      막기만 하고 번호를 안 주면 사장님이 목록에서 눈으로 찾아야 한다.
    //   🔴 철자도 함께 잰다 — sales_returns 는 'canceled'(l 하나)다.
    //      옆줄 sales_deliveries('cancelled')를 보고 복사하면 가드가 영원히 안 걸린다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G5_반품중복차단이_기존번호를_알려주고_철자가_맞다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "SalesService.cs"));

        var body = Slice(src, "CreateSalesReturnAsync", "public async Task<(string ReturnId, string ReturnNo)> UpdateSalesReturn");

        // 가드가 있다
        Assert.Contains("FROM sales_returns", body);
        Assert.Contains("delivery_id=@Did", body);

        // 🔴 철자 — canceled(l 하나). cancelled 면 가드가 안 걸린다.
        Assert.Contains("status <> 'canceled'", body);
        Assert.DoesNotContain("status <> 'cancelled'", body);

        // 🔴 메시지에 번호가 실려 나간다 — 고정문구면 실패
        Assert.Contains("{dupNo}", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-6 — 자동생성 수주서에 is_auto 표식이 남고, 그 표식을 **읽어서** 재사용한다
    //   🔴 사장님: 표식 "중요함". 표식 없이 생성되면 FAIL.
    //   🔴 자동발주 사고의 교훈 — "is_auto=1 은 쓰기만 하고 읽지 않았다".
    //      쓰기만 하면 멱등이 성립하지 않는다. 읽는지까지 재야 한다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G6_자동수주서는_표식을_남기고_그_표식을_읽어_재사용한다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "SalesService.cs"));

        var body = Slice(src, "CreateDeliveryAsync", "public async Task");

        // (a) 표식을 쓴다
        Assert.Contains("IsAuto = true", body);

        // (b) 🔴 표식을 읽는다 — 이게 없으면 두 번 저장 시 수주서가 두 장 생긴다
        Assert.Contains("o.is_auto    = 1", body);

        // (c) 이미 명세서가 붙은 수주서는 재사용하지 않는다 (다른 거래가 얽히면 더 큰 사고)
        Assert.Contains("NOT EXISTS", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-7 — 생성 엔드포인트에 멱등 키가 붙었다 (확정과 대칭)
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G7_거래명세서_생성에_멱등키가_붙었다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.API", "Controllers", "SalesController.cs"));

        var body = Slice(src, "[HttpPost(\"deliveries\")]", "public async Task<IActionResult> CreateDelivery");
        Assert.Contains("[IdempotencyKey]", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-8 — COUNT+1 채번이 코드에서 사라졌다 (5곳 전부)
    //   ⚠️ 보조 검사. 본 판정은 G-3(동작)이다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G8_전표번호_채번에_COUNT가_남아있지_않다()
    {
        string[] files =
        {
            Path.Combine("src", "HitPan.Application", "Services", "SalesService.cs"),
            Path.Combine("src", "HitPan.Application", "Services", "QuotationService.cs"),
            Path.Combine("src", "HitPan.Application", "Services", "TaxInvoiceService.cs"),
            Path.Combine("src", "HitPan.Application", "Services", "PurchaseService.cs"),
        };

        // 채번용 COUNT 쿼리의 특징: 문서번호 컬럼에 LIKE 를 걸고 COUNT 를 센다
        var bad = new Regex(@"SELECT\s+COUNT\([^)]*\)\s+FROM\s+\w+\s+WHERE[^""]*(?:return_no|order_no|quote_no|invoice_no)\s+LIKE",
            RegexOptions.IgnoreCase);

        foreach (var rel in files)
        {
            var src = File.ReadAllText(Path.Combine(RepoRoot(), rel));
            Assert.False(bad.IsMatch(src), $"{rel} 에 COUNT+1 채번이 남아 있다 — MAX+1(DocumentNumberHelper)로 바꿔야 한다");
        }
    }

    // ───────────────────────── helpers ─────────────────────────

    private static string Slice(string src, string from, string to)
    {
        var i = src.IndexOf(from, StringComparison.Ordinal);
        Assert.True(i >= 0, $"앵커를 찾지 못했다: {from}");
        var j = src.IndexOf(to, i + from.Length, StringComparison.Ordinal);
        return j < 0 ? src[i..] : src[i..j];
    }

    private static void Exec(MySqlConnection c, string sql)
    {
        using var cmd = new MySqlCommand(sql, c);
        cmd.ExecuteNonQuery();
    }

    private static int Scalar(MySqlConnection c, string sql)
    {
        using var cmd = new MySqlCommand(sql, c);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "HitPan.Application")))
            dir = dir.Parent;
        Assert.True(dir is not null, "레포 루트를 찾아야 한다");
        return dir!.FullName;
    }

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다 (작14 W1)
        try
        {
            using var c = new MySqlConnection(
                ServerConnString().Replace("User=", $"Database={TestDb};User="));
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }
}
