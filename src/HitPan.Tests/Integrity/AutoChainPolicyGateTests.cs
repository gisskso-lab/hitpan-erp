using System.Data;
using System.Text.RegularExpressions;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-70 ~ G-75</b> — 자동 사슬은 <b>사 오는 물건에만</b> 태운다 (20260825작1 W2).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 결재 (2026-08-25)</b> — <i>"데이터 정합성이 중요하지 막아!!"</i>
/// </para>
///
/// <para>
/// <b>무엇을 막나</b> — 사슬은 매입확정을 태운다. 매입확정은 재고만 올리는 게 아니라
/// <b>매입 분개</b>를 만들고 <b>외상매입금</b>을 가산한다. 반제품은 만들어 채우는 물건이므로
/// 사슬을 태우면 <b>사지도 않은 물건에 갚을 돈이 생긴다.</b>
/// 재고 오염보다 <b>회계 오염</b>이 무겁다.
/// </para>
///
/// <para>
/// 🔴 <b>두 경로를 함께 잰다</b> — BOM 경로만 막고 판매 경로를 두면
/// 반제품을 판 뒤 자동발주가 돌면서 그대로 새어 나간다.
/// 8/21 이 정확히 <b>한쪽만 고친</b> 사고였다(G-64 와 같은 계통).
/// </para>
///
/// <para>
/// ⚠️ <b>막는 것은 사슬뿐이다</b> — 발주서는 그대로 만든다.
/// 반제품을 외주가공으로 사 오는 길을 끊으면 안 된다(헌법 #20).
/// G-73 이 그것을 지킨다.
/// </para>
/// </remarks>
public sealed class AutoChainPolicyGateTests
{
    // ────────────────────────────────────────────────────────────────────────────
    //  G-70 · G-71 — 정책 자체
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>🟢 G-70 — <b>사 오는 물건</b>은 사슬을 탄다.</summary>
    [Theory]
    [InlineData("material")]   // 자재
    [InlineData("raw")]        // 원자재(레거시)
    [InlineData("product")]    // 상품 — 사입 판매
    [InlineData("expense")]    // 경비성
    [InlineData("MATERIAL")]   // 대소문자가 섞여 들어와도 같은 판단
    [InlineData("  material ")]// 앞뒤 공백이 있어도
    public void G70_사오는_물건은_사슬을_탄다(string itemType)
        => Assert.True(AutoChainPolicy.CanAutoReceive(itemType));

    /// <summary>
    /// 🔴 G-71 — <b>만드는 물건</b>은 사슬을 막는다. 사장님 결재의 핵심.
    /// </summary>
    [Theory]
    [InlineData("semi_finished")]  // 반제품 — 사장님이 지목하신 것
    [InlineData("semi")]           // 반제품 축약형(레거시 데이터에 실재한다)
    [InlineData("assembly")]       // 조립품
    [InlineData("finished")]       // 완제품
    [InlineData("SEMI_FINISHED")]  // 대소문자
    public void G71_만드는_물건은_사슬을_막는다(string itemType)
        => Assert.False(AutoChainPolicy.CanAutoReceive(itemType));

    /// <summary>
    /// 🔴 G-72 — <b>모르는 값은 막는다.</b>
    /// <c>item_type</c> 은 enum 이 아니라 <c>longtext</c> 라(실측) 무엇이든 들어올 수 있다.
    /// 새 유형이 생겼는데 정책에 안 적혔다면 <b>"일단 통과" 가 아니라 "일단 발주서만"</b> 이 안전하다 —
    /// 되돌릴 수 있는 쪽을 고른다.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("brand_new_type_2027")]
    public void G72_모르는_값은_막는다(string? itemType)
        => Assert.False(AutoChainPolicy.CanAutoReceive(itemType));

    // ────────────────────────────────────────────────────────────────────────────
    //  G-73 ~ G-75 — 두 경로가 이 정책을 실제로 쓰는가
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G-73 — <b>막는 것은 사슬뿐이다.</b> 반제품이어도 <b>발주서는 만든다</b>.
    /// 정책이 발주 자체를 막는 자리에 쓰이면 외주 매입 길이 끊긴다(#20).
    /// </summary>
    [Fact]
    public void G73_반제품도_발주서는_만든다()
    {
        var bom = ReadSource("src", "HitPan.Application", "Services", "BomService.cs");

        // 정책은 '사슬 여부'(wantChain)를 정하는 데만 쓰여야 한다.
        Assert.Matches(@"wantChain\s*&&\s*!AutoChainPolicy\.CanAutoReceive", bom);

        // 발주를 막는 자리(throw)에 정책이 끼어들면 안 된다.
        var policyLines = bom.Split('\n')
            .Where(l => l.Contains("AutoChainPolicy.CanAutoReceive", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(policyLines);
        Assert.All(policyLines, l =>
            Assert.False(l.Contains("throw", StringComparison.Ordinal),
                "반제품이라고 발주 자체를 막으면 외주 매입 길이 끊긴다 (#20)"));
    }

    /// <summary>
    /// 🔴 G-74 — <b>판매 경로도 같은 정책을 쓴다.</b>
    /// BOM 만 막고 판매를 두면 반제품을 판 뒤 자동발주로 그대로 샌다.
    /// </summary>
    [Fact]
    public void G74_판매경로도_같은_정책을_쓴다()
    {
        var sales = ReadSource("src", "HitPan.Application", "Services", "SalesService.cs");

        Assert.Contains("AutoChainPolicy.CanAutoReceive", sales);

        // 후보 조회가 item_type 을 실어 와야 판정할 수 있다.
        Assert.Matches(@"item_type.*AS\s+ItemType", sales);
    }

    /// <summary>
    /// 🔴 G-75 — <b>정책이 한 곳에만 있다.</b>
    /// 각 서비스가 자기만의 판정식(<c>item_type == "semi_finished"</c> 같은 것)을 들고 있으면
    /// 한쪽만 고쳐지는 일이 또 난다.
    /// </summary>
    [Fact]
    public void G75_판정식이_흩어져_있지_않다()
    {
        // 사슬을 다루는 두 파일에 손으로 쓴 반제품 비교가 남아 있으면 반려.
        var handRolled = new Regex(
            @"item_?type\s*(==|!=|\.Equals)\s*""(semi_finished|semi|assembly|finished)""",
            RegexOptions.IgnoreCase);

        foreach (var (name, path) in new[]
                 {
                     ("BomService", new[] { "src", "HitPan.Application", "Services", "BomService.cs" }),
                     ("SalesService", new[] { "src", "HitPan.Application", "Services", "SalesService.cs" }),
                 })
        {
            var src = ReadSource(path);
            Assert.False(handRolled.IsMatch(src),
                $"{name} 이 반제품 판정을 직접 하고 있다 — AutoChainPolicy 한 곳으로 모아라");
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G-76 · G-77 — 🔴 진짜 서비스를 돌려서 **회계에 손이 가는지** 잰다
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>G-76 — 반제품에 사슬을 걸어도 매입확정이 불리지 않는다.</b>
    /// <para>
    /// 이것이 사장님 결재의 <b>진짜 증명</b>이다. 매입확정(<c>ConfirmReceiptAsync</c>)은
    /// <b>회계에 손대는 유일한 통로</b>다 — 그것이 안 불리면 매입 분개도 외상매입금도 안 생긴다.
    /// 정책 함수만 시험하면 <b>"정책은 맞는데 아무도 안 부르는"</b> 경우를 놓친다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task G76_반제품은_매입확정이_아예_불리지_않는다()
    {
        if (!DbAvailable()) { Skipped(); return; }
        using var db = FreshAlertDb();
        SeedAlert(db, itemType: "semi_finished", autoReceiveOnOrder: true);

        var pur = new Mock<IPurchaseService>(MockBehavior.Strict);   // 뭐라도 불리면 즉시 실패한다
        var svc = NewBomService(db, pur.Object);

        var r = await svc.OrderAlertAsync("ALERT-1", Tid, autoReceive: true);

        Assert.True(r.OrderCreated);                 // 🟢 발주서는 만들어진다 (외주 매입 길 유지)
        Assert.False(r.ReceiptConfirmed);            // 🔴 매입확정까지 안 간다
        Assert.False(string.IsNullOrWhiteSpace(r.ChainSkippedReason));   // 이유를 보여준다

        // 🔴 회계로 가는 통로가 한 번도 안 열렸다.
        pur.Verify(x => x.ConvertOrderToReceiptAsync(It.IsAny<string>(), It.IsAny<string>(),
                                                     It.IsAny<CancellationToken>()), Times.Never);
        pur.Verify(x => x.ConfirmReceiptAsync(It.IsAny<string>(), It.IsAny<ConfirmReceiptRequest>(),
                                              It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 🟢 <b>G-77 — 대조군.</b> 같은 조건에서 <b>자재</b>는 사슬을 탄다.
    /// 이게 없으면 <i>"그냥 사슬이 아예 안 도는 것"</i> 과 구분이 안 된다.
    /// </summary>
    [Fact]
    public async Task G77_자재는_사슬을_탄다_대조군()
    {
        if (!DbAvailable()) { Skipped(); return; }
        using var db = FreshAlertDb();
        SeedAlert(db, itemType: "material", autoReceiveOnOrder: true);

        var pur = new Mock<IPurchaseService>();
        pur.Setup(x => x.ConvertOrderToReceiptAsync(It.IsAny<string>(), It.IsAny<string>(),
                                                    It.IsAny<CancellationToken>()))
           .ReturnsAsync(("RECEIPT-1", "매-1"));

        var r = await NewBomService(db, pur.Object).OrderAlertAsync("ALERT-1", Tid, autoReceive: true);

        Assert.True(r.OrderCreated);
        Assert.True(r.ReceiptConfirmed);             // 🟢 자재는 끝까지 간다
        pur.Verify(x => x.ConfirmReceiptAsync(It.IsAny<string>(), It.IsAny<ConfirmReceiptRequest>(),
                                              It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 🔴 G-78 — <b>품목 스위치가 꺼져 있으면</b> 자재여도 사슬을 안 탄다.
    /// 반자동 원칙 — 코드가 임의로 켜지 않는다.
    /// </summary>
    [Fact]
    public async Task G78_스위치가_꺼져_있으면_자재도_발주서만()
    {
        if (!DbAvailable()) { Skipped(); return; }
        using var db = FreshAlertDb();
        SeedAlert(db, itemType: "material", autoReceiveOnOrder: false);

        var pur = new Mock<IPurchaseService>(MockBehavior.Strict);
        var r = await NewBomService(db, pur.Object).OrderAlertAsync("ALERT-1", Tid, autoReceive: true);

        Assert.True(r.OrderCreated);
        Assert.False(r.ReceiptConfirmed);
        pur.VerifyNoOtherCalls();
    }

    // ── 배선 ────────────────────────────────────────────────────────────────────

    private const string Tid = "GATE-CHAIN-TENANT";

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    /// <summary><c>IServiceProvider</c> 로 <c>IPurchaseService</c> 를 넘긴다 — 실제 배선과 같은 길.</summary>
    private static BomService NewBomService(IDbConnection db, IPurchaseService pur)
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(x => x.GetService(typeof(IPurchaseService))).Returns(pur);
        return new BomService(db, new Mock<IEventPublisher>().Object,
                              new Mock<IAuditService>().Object, sp.Object);
    }

    private static void Skipped() =>
        Console.Error.WriteLine("[SKIP] MariaDB 없음 — 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라.");

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        // 🔴 비밀번호를 코드에 적지 않는다 — 기존 게이트(ApproverCandidateGateTests:65) 관례를 따른다.
        //    로컬에서 돌릴 때는 HITPAN_DB_PASS 를 환경변수로 준다. 비어 있으면 DB 에 못 붙어
        //    이 게이트는 "안 돌았다" 로 건너뛴다(초록불을 검증으로 읽지 마라).
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private static bool DbAvailable()
    {
        try
        {
            using var c = new MySqlConnection(ConnString());
            c.Open();
            return true;
        }
        catch (MySqlException) { return false; }
    }

    /// <summary>발주에 필요한 표만 <c>TEMPORARY</c> 로 세운다 — 실제 표는 안 건드린다(#39).</summary>
    private static MySqlConnection FreshAlertDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();
        Exec(db, """
            CREATE TEMPORARY TABLE items (
              item_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              item_name varchar(200) NOT NULL,
              item_type longtext NOT NULL,
              auto_order_partner_id varchar(36) NULL,
              auto_order_qty decimal(10,2) NOT NULL DEFAULT 0,
              auto_receive_on_order tinyint(1) NOT NULL DEFAULT 0,
              purchase_price decimal(15,2) NULL,
              cost_price decimal(15,2) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE stock_alerts (
              alert_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              shortage_qty decimal(10,2) NOT NULL DEFAULT 0,
              status varchar(20) NOT NULL DEFAULT 'pending',
              updated_at datetime(6) NOT NULL DEFAULT current_timestamp(6)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_orders (
              po_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              po_no varchar(50) NOT NULL,
              partner_id varchar(36) NULL,
              po_date date NULL,
              status varchar(20) NOT NULL DEFAULT 'draft',
              total_amount decimal(15,2) NOT NULL DEFAULT 0,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0,
              memo varchar(500) NULL,
              is_auto tinyint(1) NOT NULL DEFAULT 0,
              is_deleted tinyint(1) NOT NULL DEFAULT 0,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              updated_at datetime(6) NOT NULL DEFAULT current_timestamp(6)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        Exec(db, """
            CREATE TEMPORARY TABLE purchase_order_items (
              po_item_id varchar(36) NOT NULL PRIMARY KEY,
              po_id varchar(36) NOT NULL,
              tenant_id varchar(36) NOT NULL,
              item_id varchar(36) NOT NULL,
              ordered_qty decimal(10,2) NOT NULL DEFAULT 0,
              received_qty decimal(10,2) NOT NULL DEFAULT 0,
              unit_price decimal(15,2) NOT NULL DEFAULT 0,
              supply_amount decimal(15,2) NOT NULL DEFAULT 0,
              vat_amount decimal(15,2) NOT NULL DEFAULT 0,
              item_status varchar(20) NOT NULL DEFAULT 'pending'
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        return db;
    }

    private static void SeedAlert(MySqlConnection db, string itemType, bool autoReceiveOnOrder)
    {
        Exec(db, $"""
            INSERT INTO items (item_id, tenant_id, item_name, item_type,
                               auto_order_partner_id, auto_order_qty, auto_receive_on_order, purchase_price)
            VALUES ('IT-1', '{Tid}', 'gate item', '{itemType}',
                    'PT-1', 10, {(autoReceiveOnOrder ? 1 : 0)}, 1000);
            """);
        Exec(db, $"""
            INSERT INTO stock_alerts (alert_id, tenant_id, item_id, shortage_qty, status)
            VALUES ('ALERT-1', '{Tid}', 'IT-1', 5, 'pending');
            """);
    }

    private static void Exec(IDbConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ── 도우미 ──────────────────────────────────────────────────────────────────

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, "src", "HitPan.Application")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "레포 루트를 찾아야 한다");

        var full = Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        Assert.True(File.Exists(full), $"소스가 없다: {full}");
        return File.ReadAllText(full);
    }
}
