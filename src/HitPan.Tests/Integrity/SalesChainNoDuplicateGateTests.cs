using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>매출 워크플로우 사슬 — 중복생성 절대금지</b> (20260827작11).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님</b>: <i>"가장 중요한건, 매출판매 영역에 워크플로우 사슬에 대한 정합성이야."</i> ·
/// <i>"사슬동작중 중복생성 절대금지"</i>
/// </para>
///
/// <para>
/// 🔴 <b>W1 — 수주 1건으로 거래명세서 2장이 나올 수 있었다.</b>
/// <c>ConvertOrderToDeliveryAsync</c> 의 방어는 <c>deliveryItems.Count == 0</c> 하나뿐이었는데,
/// 그건 <c>delivered_qty</c> 가 올라간 뒤에만 듣는다.
/// 그런데 <c>delivered_qty</c> 는 <b>확정</b>(<c>ConfirmDeliveryAsync</c>)에서만 증가한다.
/// ⇒ 명세서를 만들고 <b>확정하기 전(draft)</b> 에 다시 전환하면 가드를 그대로 통과한다.
/// 둘 다 확정하면 <b>재고가 2배 출고</b>된다.
/// </para>
///
/// <para>
/// 🔴 <b>매입은 이 사고를 이미 겪고 봉합했다</b>(<c>PurchaseService.cs:726-747</c>) —
/// <i>"발주 1건으로 매입 2장을 만들 수 있었다(재고 2배 입고)"</i>.
/// <b>매출만 그 봉합을 안 받았다.</b> 같은 구멍이 그대로 남아 있었다.
/// </para>
///
/// <para>
/// 🔴 <b>W2 — 계산서 중복 가드에 <c>tenant_id</c> 가 없었다.</b>
/// 남의 회사 계산서가 걸려 우리 발행이 막히거나, 우리 것이 안 걸릴 수 있었다(헌법 #2).
/// 게다가 주석은 DB UNIQUE 가 지킨다고 적었는데 <b>그 UNIQUE 는 DDL 에 없다</b>(16차 ERROR 1901 회수).
/// ⇒ 실제 방어는 SELECT 하나뿐이었고, 그 하나가 새고 있었다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 화면에서 버튼을 두 번 눌렀을 때를 재지 못한다.
/// 서버 가드가 쓰는 것과 <b>같은 SQL</b> 을 같은 조건으로 돌려 판정한다.
/// </para>
/// </remarks>
public sealed class SalesChainNoDuplicateGateTests
{
    private static readonly string TestDb =
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // ─────────────────────────────────────────────────────────────────────
    // G-1 🔴 본 판정 — draft 명세서가 있으면 재전환이 막힌다
    //   봉합 전/후 두 가드를 같은 데이터에 돌려 결과가 갈리는 것으로 판정한다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G1_확정전_draft명세서가_있으면_재전환이_막힌다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라."); return; }

        using var conn = new MySqlConnection(
            ServerConnString().Replace("User=", $"Database={TestDb};User="));
        conn.Open();
        Seed(conn);

        // 수주 1건(10개 주문) → 명세서 1장을 draft 로 만들었다. 아직 확정 안 했으므로 delivered_qty=0.
        Exec(conn, "INSERT INTO t_sd VALUES ('d1','t1','o1','명-20260827-001','draft',0)");

        // ── 옛 방어(Count==0 만): delivered_qty 가 0 이라 미출고 수량이 남아 있다 ⇒ 통과해버린다
        var remainingQty = Scalar(conn,
            "SELECT COUNT(*) FROM t_soi WHERE order_id='o1' AND tenant_id='t1' AND (ordered_qty - delivered_qty) > 0");
        Assert.Equal(1, remainingQty);   // 🔴 옛 가드는 여기서 "전환 가능" 으로 본다

        // ── 새 방어(살아있는 명세서를 센다): 막는다
        var existing = ScalarNullable(conn, """
            SELECT delivery_no FROM t_sd
             WHERE order_id='o1' AND tenant_id='t1'
               AND status <> 'cancelled' AND is_deleted = 0
             ORDER BY delivery_no LIMIT 1
            """);
        Assert.Equal("명-20260827-001", existing);   // 🟢 새 가드는 잡는다

        // 🔴 대조군 — 두 판정이 갈려야 이 봉합이 실재한다
        Assert.True(remainingQty > 0 && existing is not null,
            "옛 가드는 통과시키고 새 가드는 막아야 한다 — 그 차이가 이번 봉합이다");
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-2 — 취소된 명세서는 막지 않는다 (과잉봉합 방지)
    //   취소했으면 다시 만들 수 있어야 한다. 매입과 같은 정책.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G2_취소된_명세서는_재전환을_막지_않는다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다."); return; }

        using var conn = new MySqlConnection(
            ServerConnString().Replace("User=", $"Database={TestDb};User="));
        conn.Open();
        Seed(conn);

        Exec(conn, "INSERT INTO t_sd VALUES ('d1','t1','o1','명-20260827-001','cancelled',0)");
        Exec(conn, "INSERT INTO t_sd VALUES ('d2','t1','o1','명-20260827-002','draft',1)");  // 삭제분

        var existing = ScalarNullable(conn, """
            SELECT delivery_no FROM t_sd
             WHERE order_id='o1' AND tenant_id='t1'
               AND status <> 'cancelled' AND is_deleted = 0
             ORDER BY delivery_no LIMIT 1
            """);

        Assert.Null(existing);   // 취소분·삭제분뿐이면 다시 만들 수 있다
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-3 — 남의 회사 명세서는 우리 전환을 막지 않는다 (헌법 #2)
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G3_남의회사_명세서는_우리_전환을_막지_않는다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다."); return; }

        using var conn = new MySqlConnection(
            ServerConnString().Replace("User=", $"Database={TestDb};User="));
        conn.Open();
        Seed(conn);

        // 다른 테넌트가 같은 order_id 로 명세서를 갖고 있다
        Exec(conn, "INSERT INTO t_sd VALUES ('dx','t2','o1','명-20260827-001','draft',0)");

        var existing = ScalarNullable(conn, """
            SELECT delivery_no FROM t_sd
             WHERE order_id='o1' AND tenant_id='t1'
               AND status <> 'cancelled' AND is_deleted = 0
             ORDER BY delivery_no LIMIT 1
            """);

        Assert.Null(existing);   // 우리(t1) 것은 없으므로 막히면 안 된다
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-4 — 가드가 코드에 실제로 있고, 번호를 알려준다
    //   🔴 작8 교훈: 막는 것 ≠ 알려주는 것.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G4_수주_재전환_차단이_기존_명세서번호를_알려준다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "SalesService.cs"));

        var body = Slice(src, "ConvertOrderToDeliveryAsync", "public Task<List<PartnerSearchDto>> SearchPartnersAsync");

        Assert.Contains("FROM sales_deliveries", body);
        Assert.Contains("order_id = @OrderId", body);
        Assert.Contains("tenant_id = @TenantId", body);      // 헌법 #2
        Assert.Contains("status <> 'cancelled'", body);      // 철자: deliveries 는 l 둘
        Assert.Contains("{existingDeliveryNo}", body);       // 번호를 담아 알려준다

        // 🔴🔴 이 검사가 처음엔 **가짜였다.** 가드를 `if (false && ...)` 로 죽였는데도 통과했다 —
        //   문자열이 파일에 그대로 남아 있기 때문이다(게이트 체크리스트 ⑧).
        //   ⇒ 조건이 **살아서 던지는지**까지 본다. 죽은 조건이면 잡는다.
        Assert.DoesNotContain("false &&", body);
        Assert.DoesNotContain("if (false", body);

        // 가드가 SELECT 결과로 판정하는지 — 상수로 바뀌면 잡힌다
        Assert.Contains("if (existingDeliveryNo is not null)", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-5 — 계산서 중복 가드에 tenant_id 가 있고, 번호를 알려준다
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G5_계산서_중복차단에_테넌트조건과_번호가_있다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "TaxInvoiceService.cs"));

        var body = Slice(src, "2) 중복 발행 차단", "// 3) 계산서 번호 생성");

        // 🔴 종전엔 tenant_id 가 없었다 — 남의 회사 것이 걸리거나 우리 것이 안 걸렸다
        Assert.Contains("tenant_id = @TenantId", body);

        // 🔴 종전엔 invoice_id 를 뽑아놓고 메시지엔 안 썼다
        Assert.Contains("SELECT invoice_no", body);
        Assert.Contains("{existing}", body);
    }

    // ───────────────────────── helpers ─────────────────────────

    /// <summary>수주 1건(10개 주문·0개 출고). 명세서는 각 시험이 직접 넣는다.</summary>
    private static void Seed(MySqlConnection c)
    {
        Exec(c, "DROP TEMPORARY TABLE IF EXISTS t_sd");
        Exec(c, "DROP TEMPORARY TABLE IF EXISTS t_soi");

        Exec(c, """
            CREATE TEMPORARY TABLE t_soi (
              order_item_id varchar(36) NOT NULL, order_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              ordered_qty decimal(15,4) NOT NULL, delivered_qty decimal(15,4) NOT NULL,
              PRIMARY KEY (order_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);
        Exec(c, """
            CREATE TEMPORARY TABLE t_sd (
              delivery_id varchar(36) NOT NULL, tenant_id varchar(36) NOT NULL,
              order_id varchar(36) DEFAULT NULL, delivery_no varchar(32) NOT NULL,
              status varchar(20) NOT NULL, is_deleted tinyint(1) NOT NULL DEFAULT 0,
              PRIMARY KEY (delivery_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);

        // 10개 주문, 아직 한 개도 출고 안 됨 (확정 전이라 delivered_qty=0)
        Exec(c, "INSERT INTO t_soi VALUES ('oi1','o1','t1',10,0)");
    }

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

    private static string? ScalarNullable(MySqlConnection c, string sql)
    {
        using var cmd = new MySqlCommand(sql, c);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? null : v.ToString();
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
        try
        {
            using var c = new MySqlConnection(
                ServerConnString().Replace("User=", $"Database={TestDb};User="));
            c.Open();
            return true;
        }
        catch (MySqlException) { return false; }
    }
}
