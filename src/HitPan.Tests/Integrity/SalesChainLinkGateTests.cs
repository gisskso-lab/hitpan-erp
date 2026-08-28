using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-1 ~ G-3</b> — 거래명세서를 <b>수정해도 수주 사슬이 안 끊긴다</b> (20260827작10 W1).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇이 났나</b> — <c>UpdateDeliveryAsync</c> 가 라인을 전량 DELETE 하고 재INSERT 하면서
/// <c>order_item_id</c> 에 <b>하드코딩 NULL</b> 을 넣었다.
/// ⇒ <b>draft 거래명세서를 한 번만 수정 저장하면 수주라인 링크가 영구히 끊겼다.</b>
/// 생성 경로는 값을 넣는데 <b>수정 경로만</b> NULL 로 덮는 비대칭이었다.
/// </para>
///
/// <para>
/// 🔴🔴 <b>이게 단순 데이터 손실이 아닌 이유 — 가드가 뚫린다.</b>
/// <c>DeleteSalesOrderAsync</c> 의 <i>"판매전환된 라인 차단"</i> 가드가 <c>order_item_id</c> 로 판정한다.
/// 링크가 NULL 이면 그 COUNT 가 <b>0</b> 이라
/// <b>이미 출고된 수주서가 삭제 가능해진다.</b> 그래서 G-2 가 이 게이트의 본 판정이다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 화면을 고치는 방식이 아닌가</b> — <c>DeliveryItemDto</c> 에 <c>OrderItemId</c> 가 없고
/// 조회 SQL 도 <c>order_item_id</c> 를 한 번도 읽지 않는다.
/// <b>화면은 받은 적 없는 값을 되돌려줄 수 없다.</b> 그래서 <b>서버가 보존</b>한다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 실제 화면에서 저장 버튼을 눌렀을 때를 재지 못한다.
/// 서버 로직이 쓰는 것과 <b>같은 SQL</b> 을 같은 순서로 돌려 판정한다. 화면은 실측의 몫이다.
/// </para>
/// </remarks>
public sealed class SalesChainLinkGateTests
{
    private static readonly string TestDb =
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // ─────────────────────────────────────────────────────────────────────
    // G-1 — 수정(DELETE→INSERT) 후에도 order_item_id 가 살아 있다
    //   봉합 있음 / 없음 두 방식을 같은 데이터에 각각 돌려 결과가 갈리는 것으로 판정한다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G1_명세서를_수정해도_수주라인_링크가_살아있다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라."); return; }

        using var conn = new MySqlConnection(
            ServerConnString().Replace("User=", $"Database={TestDb};User="));
        conn.Open();
        Seed(conn);

        // ── 봉합 없는 방식(옛 코드): DELETE 후 order_item_id 에 NULL 하드코딩
        Exec(conn, "DELETE FROM t_di WHERE delivery_id='d1'");
        Exec(conn, """
            INSERT INTO t_di (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, qty)
            VALUES ('new1','d1','t1',NULL,'i1',5)
            """);
        var afterOld = ScalarNullable(conn, "SELECT order_item_id FROM t_di WHERE delivery_id='d1'");
        Assert.Null(afterOld);   // 옛 방식은 링크가 끊긴다 — 이게 사고였다

        // ── 봉합 있는 방식(새 코드): 지우기 전에 읽어두고 되붙인다
        Seed(conn);
        var kept = ScalarNullable(conn,
            "SELECT order_item_id FROM t_di WHERE delivery_item_id='di1' AND order_item_id IS NOT NULL");
        Assert.Equal("oi1", kept);   // DELETE 전에 읽어둔다

        Exec(conn, "DELETE FROM t_di WHERE delivery_id='d1'");
        Exec(conn, $"""
            INSERT INTO t_di (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, qty)
            VALUES ('new2','d1','t1','{kept}','i1',5)
            """);
        var afterNew = ScalarNullable(conn, "SELECT order_item_id FROM t_di WHERE delivery_id='d1'");

        // 🔴 대조군 — 두 방식이 갈려야 이 봉합이 실재한다
        Assert.Equal("oi1", afterNew);
        Assert.NotEqual(afterOld, afterNew);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-2 — 🔴 본 판정: 수정 후에도 **수주 삭제 가드가 막는다**
    //   W1 의 진짜 피해는 데이터가 아니라 가드 무력화다.
    //   가드가 쓰는 것과 같은 SQL 을 그대로 돌린다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G2_수정후에도_출고된수주서_삭제가드가_막는다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라."); return; }

        using var conn = new MySqlConnection(
            ServerConnString().Replace("User=", $"Database={TestDb};User="));
        conn.Open();

        // DeleteSalesOrderAsync 의 가드와 동일한 SQL (SalesService.cs:1625-1637)
        const string guardSql = """
            SELECT COUNT(*)
              FROM t_di di
              JOIN t_sd sd ON sd.delivery_id = di.delivery_id AND sd.tenant_id = di.tenant_id
             WHERE di.order_item_id IN (SELECT order_item_id FROM t_soi WHERE order_id='o1' AND tenant_id='t1')
               AND di.tenant_id = 't1'
               AND sd.status <> 'cancelled'
            """;

        // ── 봉합 없는 방식: 수정하면 링크가 NULL 이 되고 가드가 0 을 센다
        Seed(conn);
        Assert.Equal(1, Scalar(conn, guardSql));   // 수정 전에는 막는다
        Exec(conn, "DELETE FROM t_di WHERE delivery_id='d1'");
        Exec(conn, "INSERT INTO t_di (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, qty) VALUES ('n1','d1','t1',NULL,'i1',5)");
        var guardOld = Scalar(conn, guardSql);

        // 🔴 이것이 사고다 — 이미 출고된 수주서가 삭제 가능해진다
        Assert.Equal(0, guardOld);

        // ── 봉합 있는 방식: 링크가 보존되어 가드가 계속 막는다
        Seed(conn);
        var kept = ScalarNullable(conn, "SELECT order_item_id FROM t_di WHERE delivery_item_id='di1' AND order_item_id IS NOT NULL");
        Exec(conn, "DELETE FROM t_di WHERE delivery_id='d1'");
        Exec(conn, $"INSERT INTO t_di (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, qty) VALUES ('n2','d1','t1','{kept}','i1',5)");
        var guardNew = Scalar(conn, guardSql);

        Assert.Equal(1, guardNew);            // 여전히 막는다
        Assert.NotEqual(guardOld, guardNew);  // 대조군
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-3 — 새로 추가한 라인은 order_item_id 가 NULL 인 게 정상 (과잉봉합 방지)
    //   없던 사슬을 지어내면 그게 더 나쁘다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G3_새로추가한_라인은_링크가_비어있는게_정상이다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "SalesService.cs"));

        var body = Slice(src, "public async Task UpdateDeliveryAsync", "public async Task DeleteDeliveryAsync");

        // 사전에서 못 찾으면 null 이 그대로 들어간다 — TryGetValue 로 조회하고 억지로 채우지 않는다
        Assert.Contains("keepOrderItemIds.TryGetValue", body);
        Assert.Contains("OrderItemId = keptOrderItemId", body);

        // 🔴 NULL 하드코딩이 사라졌다
        Assert.DoesNotContain("@TenantId, NULL, @ItemId", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-4 — 견적→수주 전환이 quotation_id 를 저장한다
    //   종전엔 memo 자유텍스트가 유일한 연결이라, 수주서를 수정하면 소멸했다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G4_견적을_수주로_전환하면_quotation_id_가_저장된다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "QuotationService.cs"));

        var body = Slice(src, "INSERT INTO sales_orders", "// 수주 품목 생성");

        Assert.Contains("quotation_id", body);
        Assert.Contains("QuotationId = quoteId", body);

        // 🔴 memo 는 지우지 않는다 — 사람이 읽는 흔적은 그대로 두고 축만 추가한다
        Assert.Contains("견적서 전환:", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-5 — converted_order_id 에 order_**id** 가 들어간다 (종전엔 order_no)
    //   컬럼명과 내용이 어긋나 있었다. varchar(36) 이라 에러가 안 나 아무도 못 봤다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G5_converted_order_id_에_번호가_아니라_id_가_들어간다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "QuotationService.cs"));

        var body = Slice(src, "UPDATE quotations SET status = 'converted'", "return orderNo;");

        Assert.Contains("converted_order_id = @OrderId", body);
        Assert.DoesNotContain("converted_order_id = @OrderNo", body);
        Assert.Contains("OrderId = orderId", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-6 — 🔴 중복전환 차단 메시지가 **UUID 가 아니라 번호**를 보여준다
    //   W2 로 converted_order_id 가 id 가 됐으니, 그대로 뿌리면 사장님이 UUID 를 받는다.
    //   작8 교훈: 막는 것 ≠ 알려주는 것. 막으면서 "어느 수주로 갔는지" 를 줘야 한다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G6_중복전환_차단이_UUID가_아니라_수주번호를_알려준다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "QuotationService.cs"));

        // 앵커는 차단 블록 **시작**부터 잡는다 — 되짚는 SELECT 가 메시지보다 먼저 나온다.
        var body = Slice(src, "if (quote.Status == \"converted\")", "// 수주 문서번호 채번");

        // 번호로 되짚어서 보여준다
        Assert.Contains("SELECT order_no FROM sales_orders", body);
        Assert.Contains("convertedNo", body);
        Assert.Contains("이미 수주로 전환된 견적서입니다", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-7 — 수주 자동생성 Y/N 이 **사장님 지시 문구 그대로** 뜨고, 취소하면 저장이 멈춘다
    //   🔴 PM 정정: 작10 착수 시 "묻지 않고 조용히 만든다" 고 적었는데 틀렸다.
    //      Y/N 은 20260825작5 에 이미 있었다 — 서버만 보고 화면을 안 봤다.
    //      남은 것은 문구 하나였다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G7_수주자동생성_확인문구가_지시대로이고_취소하면_멈춘다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Web", "Pages", "Sales", "DeliveryPage.razor"));

        var body = Slice(src, "수주서 자동 생성", "try");

        // 사장님 지시 문구 그대로
        Assert.Contains("수주서가 없는 거래입니다. 수주서를 자동 생성합니다.", body);

        // 🔴 알림이 아니라 **확인**이다 — 취소하면 저장이 멈춘다(헌법: 100% 자동은 없다)
        Assert.Contains("proceed != true", body);
        Assert.Contains("return;", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-8 — 자동생성 수주서에 is_auto 표식이 남는다 (사장님 "중요함")
    //   레거시 실데이터와 반드시 구분돼야 한다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G8_자동생성_수주서에_is_auto_표식이_남는다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "SalesService.cs"));

        // 앵커는 정의 서명으로 잡는다(호출부가 아니라) — 게이트 교훈 ⑪
        var body = Slice(src,
            "public async Task<(string Id, string DocumentNumber, string? AutoCreatedOrderNo)> CreateDeliveryAsync",
            "public async Task ConfirmDeliveryAsync");

        Assert.Contains("IsAuto = true", body);
    }

    // ───────────────────────── helpers ─────────────────────────

    /// <summary>수주 1건 + 명세서 1건 + 링크된 라인 1건. TEMPORARY 라 커넥션이 닫히면 사라진다.</summary>
    private static void Seed(MySqlConnection c)
    {
        Exec(c, "DROP TEMPORARY TABLE IF EXISTS t_di");
        Exec(c, "DROP TEMPORARY TABLE IF EXISTS t_sd");
        Exec(c, "DROP TEMPORARY TABLE IF EXISTS t_soi");

        Exec(c, """
            CREATE TEMPORARY TABLE t_soi (
              order_item_id varchar(36) NOT NULL, order_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL, PRIMARY KEY (order_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);
        Exec(c, """
            CREATE TEMPORARY TABLE t_sd (
              delivery_id varchar(36) NOT NULL, tenant_id varchar(36) NOT NULL,
              status varchar(20) NOT NULL, PRIMARY KEY (delivery_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);
        Exec(c, """
            CREATE TEMPORARY TABLE t_di (
              delivery_item_id varchar(36) NOT NULL, delivery_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL, order_item_id varchar(36) DEFAULT NULL,
              item_id varchar(36) NOT NULL, qty decimal(15,4) NOT NULL,
              PRIMARY KEY (delivery_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);

        Exec(c, "INSERT INTO t_soi VALUES ('oi1','o1','t1')");
        Exec(c, "INSERT INTO t_sd VALUES ('d1','t1','draft')");
        Exec(c, "INSERT INTO t_di VALUES ('di1','d1','t1','oi1','i1',5)");
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
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다 (작14 W1)
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
