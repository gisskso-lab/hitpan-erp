using System.Text.RegularExpressions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-60 ~ G-64</b> — 안전재고 경고가 <b>발주하면 실제로 사라진다</b> (20260825작1 W1·W7).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇이 났나</b> — 사장님 8/25 실측:
/// <i>"상단 안전재고 미달경고메시지 : 자동발주 실행해도 안없어짐"</i> ·
/// <i>"BOM 도 마찬가지로 자재부족알림 발주해도 안없어짐"</i>
/// </para>
///
/// <para>
/// [진범] <c>BomService.GetAlertsAsync</c> 는 조회 <b>전에</b> 미달 품목을 <c>pending</c> 으로 INSERT 한다.
/// 그 중복 가드가 <b><c>status='pending'</c> 만</b> 봤다. 발주하면 알림은 <c>ordered</c> 로 정상 전환되는데,
/// 곧바로 이어지는 화면 갱신에서 그 <c>ordered</c> 행이 가드에 안 걸려
/// <b>같은 품목에 새 <c>pending</c> 이 다시 INSERT</b> 됐다.
/// ⇒ <b>화면을 갱신하는 그 행위가 경고를 되살렸다.</b> 누를수록 유령 행이 쌓였다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 아무도 못 잡았나</b> — 8/21 에 <b>판매 경로만</b> 같은 이유로 고쳤고(<c>SalesService</c> 멱등 필터),
/// 그 주석이 <i>"BOM 경로는 stock_alerts.status 로 이미 정상 동작"</i> 이라고 <b>단정했다.</b>
/// 그 단정이 틀렸다. <c>OrderAlertAsync</c> 는 정상이지만 <c>GetAlertsAsync</c> 의 재삽입이 그것을 무효로 만들었다.
/// <b>자동발주·안전재고를 지키는 회귀 시험이 0건</b>이라 한쪽만 고친 사실이 드러나지 않았다.
/// </para>
///
/// <para>
/// 🔴 <b>이 게이트는 글자를 세지 않는다 — 동작을 잰다.</b>
/// 사장님 헌법(가짜 게이트 누적 9번): <i>"짜면 곧바로 봉합 빼고 FAIL 확인"</i>.
/// 그래서 G-60·G-61 은 <b>같은 데이터에 가드 두 개(봉합 전/후)를 각각 돌려</b>
/// <b>결과가 갈리는 것</b>으로 판정한다. 봉합을 빼면 G-61 이 반드시 깨진다.
/// G-63 은 <b>경고를 영원히 죽이지 않았다</b>는 반대 방향까지 잰다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 브라우저 배너가 실제로 사라지는지는 검사하지 못한다.
/// 서버가 내려보내는 <c>pending</c> 건수까지가 이 시험의 범위다. 화면은 사장님 실측의 몫이다.
/// </para>
/// </remarks>
public sealed class StockAlertIdempotencyGateTests
{
    private const string Tid = "GATE-W1-TENANT";

    /// <summary>
    /// 붙을 DB. 🔴 <b>운영 DB 를 쓰지 않는다</b>(헌법 #39). 시험용 DB 가 없으면 이 게이트는 안 돈다.
    /// 표는 전부 <c>TEMPORARY</c> 라 이 DB 의 실제 표를 건드리지 않는다.
    /// </summary>
    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // ── 봉합 전/후 가드를 문자열로 들고 있다가 같은 데이터에 각각 돌린다 ──────────────
    //    이것이 이 게이트의 핵심이다. 하나만 돌리면 "원래 됐던 것"과 구분이 안 된다.
    private const string GuardBeforeFix = "sa.status = 'pending'";
    private const string GuardAfterFix = "sa.status IN ('pending','ordered')";

    private static string ReplenishSql(string guard) => $"""
        INSERT INTO stock_alerts
          (alert_id, tenant_id, item_id, alert_type, current_qty, safety_qty, shortage_qty,
           partner_id, order_qty, status, created_at, updated_at)
        SELECT UUID(), i.tenant_id, i.item_id, 'safety_stock',
               COALESCE(s.current_qty, 0),
               COALESCE(i.safety_stock, i.safe_stock, 0),
               COALESCE(i.safety_stock, i.safe_stock, 0) - COALESCE(s.current_qty, 0),
               i.auto_order_partner_id, COALESCE(i.auto_order_qty, 0),
               'pending', NOW(6), NOW(6)
          FROM items i
          LEFT JOIN item_stock s ON s.tenant_id = i.tenant_id AND s.item_id = i.item_id
         WHERE i.tenant_id = @Tid
           AND i.is_deleted = 0
           AND i.is_active = 1
           AND COALESCE(i.safety_stock, i.safe_stock, 0) > 0
           AND COALESCE(s.current_qty, 0) <= COALESCE(i.safety_stock, i.safe_stock, 0)
           AND COALESCE(i.auto_order_enabled, 0) = 1
           AND NOT EXISTS (
                 SELECT 1 FROM stock_alerts sa
                  WHERE sa.tenant_id = i.tenant_id
                    AND sa.item_id = i.item_id
                    AND {guard}
               )
        """;

    // ────────────────────────────────────────────────────────────────────────────
    //  G-60 · G-61 — 대조실험: 같은 데이터, 가드만 다르게. 결과가 갈려야 한다.
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>🔴 G-60 — <b>봉합 전 가드는 반드시 FAIL 한다.</b> 이게 초록이면 시험이 가짜다.</summary>
    [Fact]
    public void G60_봉합전_가드는_발주후에도_경고를_되살린다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라."); return; }
        using var db = FreshDb();
        SeedShortageItem(db);

        RunReplenish(db, GuardBeforeFix);          // 최초 조회 → pending 1
        PlaceOrder(db);                            // 발주 → ordered
        RunReplenish(db, GuardBeforeFix);          // 화면 갱신

        Assert.Equal(1, PendingCount(db));         // 🔴 되살아난다 = 사장님이 보신 증상
        Assert.Equal(2, TotalCount(db));           // 🔴 유령 행이 쌓인다
    }

    /// <summary>🟢 G-61 — <b>봉합 후에는 발주하면 경고가 사라진다.</b> 봉합을 빼면 여기가 깨진다.</summary>
    [Fact]
    public void G61_봉합후_발주하면_경고가_사라진다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라."); return; }
        using var db = FreshDb();
        SeedShortageItem(db);

        RunReplenish(db, GuardAfterFix);
        PlaceOrder(db);
        RunReplenish(db, GuardAfterFix);

        Assert.Equal(0, PendingCount(db));         // 🟢 배너가 사라진다
        Assert.Equal(1, TotalCount(db));           // 🟢 유령 0
    }

    /// <summary>🟢 G-62 — 화면을 <b>10번 새로고침해도</b> 유령이 안 쌓인다.</summary>
    [Fact]
    public void G62_갱신_10회에도_유령행이_안_쌓인다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라."); return; }
        using var db = FreshDb();
        SeedShortageItem(db);

        RunReplenish(db, GuardAfterFix);
        PlaceOrder(db);
        for (var i = 0; i < 10; i++) RunReplenish(db, GuardAfterFix);

        Assert.Equal(0, PendingCount(db));
        Assert.Equal(1, TotalCount(db));
    }

    /// <summary>
    /// 🔴 G-63 — <b>반대 방향.</b> 봉합이 경고를 <b>영원히 죽이지 않았는지</b> 잰다.
    /// 입고로 닫힌 뒤 다시 미달이면 <b>새로 알려야 한다.</b> 이걸 안 재면
    /// "경고가 다시는 안 뜨는" 더 나쁜 봉합을 초록불로 통과시킨다.
    /// </summary>
    [Fact]
    public void G63_입고로_닫힌뒤_다시_미달이면_새로_알린다()
    {
        if (!ServerAvailable()) { Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라."); return; }
        using var db = FreshDb();
        SeedShortageItem(db);

        RunReplenish(db, GuardAfterFix);
        PlaceOrder(db);
        ConfirmReceipt(db, newQty: 25);            // 매입확정: received + 재고 25

        RunReplenish(db, GuardAfterFix);
        Assert.Equal(0, PendingCount(db));         // 재고 충분(25>10) → 안 뜬다

        SetStock(db, 3);                           // 다시 미달로 떨어짐
        RunReplenish(db, GuardAfterFix);
        Assert.Equal(1, PendingCount(db));         // 🟢 새로 알린다
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G-64 — 두 경로의 가드가 <b>일치</b>하는가 (한쪽만 고치면 이벤트 경로로 되살아난다)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-64 — <c>BomService</c> 와 <c>SyncEventPublisher</c> 의 가드가 <b>둘 다</b> ordered 를 본다.
    /// 8/21 사고(판매만 고치고 BOM 은 안 고침)의 재발 방지.
    /// ⚠️ 이 항목만은 글자를 본다 — 두 파일이 <b>같은 판단</b>을 하는지는 소스 대조가 유일한 방법이다.
    /// </summary>
    [Fact]
    public void G64_두_경로의_가드가_일치한다()
    {
        var root = RepoRoot();

        var bom = File.ReadAllText(Path.Combine(root,
            "src", "HitPan.Application", "Services", "BomService.cs"));
        var sync = File.ReadAllText(Path.Combine(root,
            "src", "HitPan.Infrastructure", "Events", "SyncEventPublisher.cs"));

        // 'pending' 만 보는 가드가 남아 있으면 안 된다 (IN 절이 아닌 단독 등호 비교)
        var lonePending = new Regex(@"status\s*=\s*'pending'");

        Assert.DoesNotContain("status IN ('pending')", bom);
        Assert.Matches(@"sa\.status\s+IN\s*\(\s*'pending'\s*,\s*'ordered'\s*\)", bom);
        Assert.Matches(@"status\s+IN\s*\(\s*'pending'\s*,\s*'ordered'\s*\)", sync);

        // SyncEventPublisher 의 중복 검사에 'pending' 단독 비교가 남아 있으면 반려
        var syncGuardBlock = Between(sync, "SELECT COUNT(*) FROM stock_alerts", "\"\"\"");
        Assert.False(lonePending.IsMatch(syncGuardBlock),
            "SyncEventPublisher 의 중복 가드가 아직 'pending' 만 본다 — BomService 와 갈린다");
    }

    // ── 도우미 ──────────────────────────────────────────────────────────────────

    private static string Between(string text, string start, string end)
    {
        var i = text.IndexOf(start, StringComparison.Ordinal);
        if (i < 0) return string.Empty;
        var j = text.IndexOf(end, i + start.Length, StringComparison.Ordinal);
        return j < 0 ? text[i..] : text[i..j];
    }

    /// <summary>
    /// 레포 루트 — <c>src/HitPan.Application</c> 이 있는 곳.
    /// ⚠️ <c>HitPan.sln</c> 은 <b><c>src/</c> 안</b>에 있다. 그것을 기준으로 잡으면
    /// 경로가 <c>src/src/…</c> 가 된다(초판이 이렇게 틀렸다).
    /// </summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, "src", "HitPan.Application")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "레포 루트(src/HitPan.Application 이 있는 곳)를 찾아야 한다");
        return dir!.FullName;
    }

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        // 🔴 비밀번호를 코드에 적지 않는다 — 기존 게이트(ApproverCandidateGateTests:65) 관례를 따른다.
        //    로컬에서 돌릴 때는 HITPAN_DB_PASS 를 환경변수로 준다. 비어 있으면 DB 에 못 붙어
        //    이 게이트는 "안 돌았다" 로 건너뛴다(초록불을 검증으로 읽지 마라).
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    /// <summary>시험용 DB 에 실제로 붙을 수 있는지. 못 붙으면 이 게이트는 <b>안 돈다</b>.</summary>
    private static bool ServerAvailable()
    {
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

    /// <summary>
    /// 시험용 <b>임시 테이블</b> 3개를 만든다 — 남의 데이터에 기대지 않는다.
    /// <para>
    /// 🔴 <b>새 DB 를 만들지 않는다.</b> <c>hitpan</c> 계정에는 <c>CREATE DATABASE</c> 권한이 없다
    /// (최소 권한 — 정상이다). 초판이 새 DB 를 만들려다 <i>Access denied</i> 로 전부 깨졌다.
    /// </para>
    /// <para>
    /// <c>TEMPORARY</c> 테이블은 <b>커넥션이 닫히면 자동으로 사라지고</b>, 같은 이름의 실제 표를
    /// 가리기만 할 뿐 <b>건드리지 않는다.</b> 운영 표를 만질 위험이 없다(헌법 #39).
    /// </para>
    /// </summary>
    private MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ServerConnString().Replace("User=", $"Database={TestDb};User="));
        db.Open();
        Exec(db, """
            CREATE TEMPORARY TABLE items (
              item_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              item_type longtext NOT NULL,
              safety_stock decimal(10,2) NULL,
              safe_stock decimal(10,2) NULL,
              auto_order_enabled tinyint(1) NOT NULL DEFAULT 0,
              auto_order_partner_id varchar(36) NULL,
              auto_order_qty decimal(10,2) NOT NULL DEFAULT 0,
              is_active tinyint(1) NOT NULL DEFAULT 1,
              is_deleted tinyint(1) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE item_stock (
              stock_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              warehouse_id varchar(36) NOT NULL DEFAULT 'MAIN',
              current_qty decimal(10,2) NOT NULL DEFAULT 0
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE stock_alerts (
              alert_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              alert_type varchar(20) NOT NULL,
              current_qty decimal(10,2) NOT NULL DEFAULT 0,
              safety_qty decimal(10,2) NOT NULL DEFAULT 0,
              shortage_qty decimal(10,2) NOT NULL DEFAULT 0,
              partner_id varchar(36) NULL,
              order_qty decimal(10,2) NOT NULL DEFAULT 0,
              status varchar(20) NOT NULL DEFAULT 'pending',
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              updated_at datetime(6) NOT NULL DEFAULT current_timestamp(6)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        return db;
    }

    /// <summary>현재고 5 &lt; 안전재고 10 인 자재 1건. 자동발주 ON.</summary>
    private static void SeedShortageItem(MySqlConnection db)
    {
        Exec(db, $"""
            INSERT INTO items
              (item_id, tenant_id, item_type, safety_stock,
               auto_order_enabled, auto_order_partner_id, auto_order_qty)
            VALUES ('GATE-ITEM-1', '{Tid}', 'material', 10, 1, 'GATE-PARTNER-1', 20);
            """);
        Exec(db, $"""
            INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty)
            VALUES ('GATE-STOCK-1', '{Tid}', 'GATE-ITEM-1', 'MAIN', 5);
            """);
    }

    private static void RunReplenish(MySqlConnection db, string guard)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = ReplenishSql(guard);
        cmd.Parameters.AddWithValue("@Tid", Tid);
        cmd.ExecuteNonQuery();
    }

    /// <summary>발주 — <c>OrderAlertAsync</c> 가 하는 상태 전환과 같다.</summary>
    private static void PlaceOrder(MySqlConnection db) => Exec(db, $"""
        UPDATE stock_alerts SET status='ordered', updated_at=NOW(6)
         WHERE tenant_id='{Tid}' AND status='pending';
        """);

    /// <summary>매입확정 — <c>PurchaseService</c> 가 pending·ordered 를 received 로 닫고 재고를 올린다.</summary>
    private static void ConfirmReceipt(MySqlConnection db, decimal newQty)
    {
        Exec(db, $"""
            UPDATE stock_alerts SET status='received', updated_at=NOW(6)
             WHERE tenant_id='{Tid}' AND status IN ('pending','ordered');
            """);
        SetStock(db, newQty);
    }

    private static void SetStock(MySqlConnection db, decimal qty) => Exec(db, $"""
        UPDATE item_stock SET current_qty={qty} WHERE tenant_id='{Tid}';
        """);

    private static int PendingCount(MySqlConnection db) =>
        Scalar(db, $"SELECT COUNT(*) FROM stock_alerts WHERE tenant_id='{Tid}' AND status='pending';");

    private static int TotalCount(MySqlConnection db) =>
        Scalar(db, $"SELECT COUNT(*) FROM stock_alerts WHERE tenant_id='{Tid}';");

    private static int Scalar(MySqlConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // 🟢 뒷정리 코드가 없다 — TEMPORARY 테이블은 커넥션이 닫히면 서버가 알아서 지운다.
    //    시험이 중간에 죽어도 남는 것이 없다. DROP 을 우리가 들고 있지 않는 편이 안전하다.
}
