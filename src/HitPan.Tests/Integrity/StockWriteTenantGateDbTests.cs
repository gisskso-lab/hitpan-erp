using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using HitPan.Application.DTOs.Stock;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-X-11 후7 · W-X-12 후9 갈래 1(작업지시서 20261129작 · 설계 §3 · §4-1 · §6-2) — <b>재고 조정 · 이관은
/// 이 회사 품목 · 이 회사 활성 창고만 원장에 쓴다.</b>
/// </summary>
/// <remarks>
/// 🔴 <b>R(🔴) 거절</b> — 예외형 + <b>문구까지</b> · <c>stock_ledger</c> 행 0 · <c>item_stock</c> 행 · 값 그대로 ·
///   접속기록 <see cref="IAuditService.LogAsync"/> <b>Never</b>(= 「검사가 첫 읽기 · 쓰기 · 접속기록보다 앞」 을 동작으로 잰다 · 옛 R-5).
/// 🔴 <b>A 정상</b> — S-9 조정 · T-6 이관은 원장 · 재고 · 접속기록이 실제로 생김을 잰다(가드가 정상 길을 막지 않음).
/// 🔴 사실 15 — S-1 ~ S-9 · T-1 ~ T-6(설계 §6-2 표 그대로).
/// 🔴 <b>표 = 시험 연결 하나의 TEMPORARY 표만</b> — <c>items</c> · <c>warehouses</c> · <c>item_stock</c> · <c>stock_ledger</c> 를
///   같은 이름 TEMPORARY 표로 <c>CREATE TEMPORARY TABLE … LIKE</c>(칸 · 형 · 기본값 = 출하 DDL 로 만든 시험 DB 그대로 · #13 · #36) 가린다
///   (선례 <c>VoucherSaveItemTenantGateDbTests.FreshDb</c>). 서비스는 그 연결 하나만 쓴다 → 실표 접촉 0(헌법 #39) · 한 연결 직렬(헌법 #16).
/// 🔴 제품 <b>공개</b> 함수로만 부른다(반사 0). 이 PC 개발 DB(3306)는 실물이다 — 로컬에서는 돌리지 않는다(CI DB 게이트 잡이 문다).
/// </remarks>
public sealed class StockWriteTenantGateDbTests
{
    private const string TenantA = "GATE-SWT-TENANT-A";
    private const string TenantB = "GATE-SWT-TENANT-B";
    private const string User = "gate-swt-user";
    private const string ItemA1 = "GATE-SWT-ITEM-A1";
    private const string ItemB1 = "GATE-SWT-ITEM-B1";
    private const string WhA = "GATE-SWT-WH-A";
    private const string WhA2 = "GATE-SWT-WH-A2";
    private const string WhA0 = "GATE-SWT-WH-A0";
    private const string WhB = "GATE-SWT-WH-B";
    private const string Missing = "GATE-SWT-WH-NONE";

    private static string ItemMsg => ItemTenantGuard.NotThisTenantItemMessage;
    private static string WhMsg => WarehouseTenantGuard.NotUsableWarehouseMessage;

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // ════════════════════════════════════════════════════════════════════════
    //  S 재고 조정 (W1)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후7 S-1 🔴 조정 · 다른 회사 품목(B1) · WA = 거절(품목) · 원장 0 · 접속기록 Never")]
    public async Task S1_Adjust_OtherTenantItem_Rejected()
    {
        if (Skip("S-1")) return;
        using var db = FreshDb();
        await AssertAdjustRejected(db, new StockAdjustRequest { ItemId = ItemB1, WarehouseId = WhA, ActualQty = 10m }, ItemMsg);
    }

    [Fact(DisplayName = "후9 S-2 🔴 조정 · A1 · 다른 회사 창고(WB) = 거절(창고) · 원장 0 · 접속기록 Never")]
    public async Task S2_Adjust_OtherTenantWarehouse_Rejected()
    {
        if (Skip("S-2")) return;
        using var db = FreshDb();
        await AssertAdjustRejected(db, new StockAdjustRequest { ItemId = ItemA1, WarehouseId = WhB, ActualQty = 10m }, WhMsg);
    }

    [Fact(DisplayName = "후9 S-3 🔴 조정 · A1 · 없는 창고 = 거절 · 원장 0 · 접속기록 Never")]
    public async Task S3_Adjust_MissingWarehouse_Rejected()
    {
        if (Skip("S-3")) return;
        using var db = FreshDb();
        await AssertAdjustRejected(db, new StockAdjustRequest { ItemId = ItemA1, WarehouseId = Missing, ActualQty = 10m }, WhMsg);
    }

    [Fact(DisplayName = "후9 S-4 🔴 조정 · A1 · 쉬게 한 내 창고(WA0) = 거절(Q3) · 원장 0 · 접속기록 Never")]
    public async Task S4_Adjust_InactiveWarehouse_Rejected()
    {
        if (Skip("S-4")) return;
        using var db = FreshDb();
        await AssertAdjustRejected(db, new StockAdjustRequest { ItemId = ItemA1, WarehouseId = WhA0, ActualQty = 10m }, WhMsg);
    }

    [Fact(DisplayName = "후7 S-5 🔴 조정 · 품목 id 빈 값 = 거절 · 쓴 행 0(빈 item_id 원장 오염 재발 막음)")]
    public async Task S5_Adjust_BlankItemId_Rejected()
    {
        if (Skip("S-5")) return;
        using var db = FreshDb();
        await AssertAdjustRejected(db, new StockAdjustRequest { ItemId = "", WarehouseId = WhA, ActualQty = 10m }, ItemMsg);
    }

    [Fact(DisplayName = "후9 S-6 🔴 조정 · 창고 id 공백만 = 거절 · 쓴 행 0")]
    public async Task S6_Adjust_BlankWarehouseId_Rejected()
    {
        if (Skip("S-6")) return;
        using var db = FreshDb();
        await AssertAdjustRejected(db, new StockAdjustRequest { ItemId = ItemA1, WarehouseId = "  ", ActualQty = 10m }, WhMsg);
    }

    [Fact(DisplayName = "후7 S-7 🔴 조정 · 앞 공백 품목 id(\" \" + A1) = 거절(F-8 · 품목 가드만이면 잘라서 통과해 버림)")]
    public async Task S7_Adjust_PaddedItemId_Rejected()
    {
        if (Skip("S-7")) return;
        using var db = FreshDb();
        await AssertAdjustRejected(db, new StockAdjustRequest { ItemId = " " + ItemA1, WarehouseId = WhA, ActualQty = 10m }, ItemMsg);
    }

    [Fact(DisplayName = "후9 S-8 🔴 조정 · A1 · WB · 실사 = 현재고(diff 0) = 거절 · 접속기록 Never(옛 R-5)")]
    public async Task S8_Adjust_NoDiff_OtherTenantWarehouse_Rejected_NoAudit()
    {
        if (Skip("S-8")) return;
        using var db = FreshDb();
        // 고치기 전 들어간 옛 행 흉내 — 실사 수량 = 현재고라 diff 0 · 가드가 없으면 원장은 안 써도 접속기록에 남의 id 가 남는다.
        SeedStock(db, TenantA, ItemA1, WhB, 5m);
        await AssertAdjustRejected(db, new StockAdjustRequest { ItemId = ItemA1, WarehouseId = WhB, ActualQty = 5m }, WhMsg);
    }

    [Fact(DisplayName = "후7 S-9 조정 · A1 · WA · 실사 10 = 원장 1행(in 10) · item_stock 10 · 접속기록 1")]
    public async Task S9_Adjust_SameTenant_Saved()
    {
        if (Skip("S-9")) return;
        using var db = FreshDb();
        var audit = new Mock<IAuditService>();

        var result = await Stock(db, audit).AdjustStockAsync(TenantA, User,
            new StockAdjustRequest { ItemId = ItemA1, WarehouseId = WhA, ActualQty = 10m, Reason = "SWT" }, CancellationToken.None);

        Assert.Equal(10m, result.DiffQty);
        Assert.Equal(1, Count(db, "SELECT COUNT(*) FROM stock_ledger"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM stock_ledger WHERE tenant_id='{TenantA}' AND item_id='{ItemA1}' AND warehouse_id='{WhA}' AND move_type='in' AND qty_in=10 AND qty_out=0 AND source_type='adjustment'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM item_stock WHERE tenant_id='{TenantA}' AND item_id='{ItemA1}' AND warehouse_id='{WhA}' AND current_qty=10"));
        VerifyAudit(audit, Times.Once());
    }

    // ════════════════════════════════════════════════════════════════════════
    //  T 재고 이관 (W2 출발 · W3 도착)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후7 T-1 🔴 이관 · 다른 회사 품목(B1) · WA → WA2(씨앗으로 B1 재고를 직접 심음) = 거절 · 0행")]
    public async Task T1_Transfer_OtherTenantItem_Rejected()
    {
        if (Skip("T-1")) return;
        using var db = FreshDb();
        // X1 이 뚫린 상황 흉내 — 회사 A 재고 표에 B1 행이 이미 있다(간접 차단 = 가용 수량 검사에 기대지 않음).
        SeedStock(db, TenantA, ItemB1, WhA, 10m);
        await AssertTransferRejected(db, new StockTransferRequest { ItemId = ItemB1, FromWarehouseId = WhA, ToWarehouseId = WhA2, Qty = 1m }, ItemMsg);
    }

    [Fact(DisplayName = "후9 T-2 🔴 이관 · A1 · 출발 = 다른 회사 창고(WB · 씨앗 재고) → WA = 거절 · 0행")]
    public async Task T2_Transfer_FromOtherTenantWarehouse_Rejected()
    {
        if (Skip("T-2")) return;
        using var db = FreshDb();
        SeedStock(db, TenantA, ItemA1, WhB, 10m);
        await AssertTransferRejected(db, new StockTransferRequest { ItemId = ItemA1, FromWarehouseId = WhB, ToWarehouseId = WhA, Qty = 1m }, WhMsg);
    }

    [Fact(DisplayName = "후9 T-3 🔴 이관 · A1 · WA(재고 있음) → 도착 = 다른 회사 창고(WB) = 거절(F-10 · 간접 차단 없는 자리)")]
    public async Task T3_Transfer_ToOtherTenantWarehouse_Rejected()
    {
        if (Skip("T-3")) return;
        using var db = FreshDb();
        SeedStock(db, TenantA, ItemA1, WhA, 10m);
        await AssertTransferRejected(db, new StockTransferRequest { ItemId = ItemA1, FromWarehouseId = WhA, ToWarehouseId = WhB, Qty = 1m }, WhMsg);
    }

    [Fact(DisplayName = "후9 T-4 🔴 이관 · A1 · WA → 쉬게 한 내 창고(WA0) = 거절(Q3)")]
    public async Task T4_Transfer_ToInactiveWarehouse_Rejected()
    {
        if (Skip("T-4")) return;
        using var db = FreshDb();
        SeedStock(db, TenantA, ItemA1, WhA, 10m);
        await AssertTransferRejected(db, new StockTransferRequest { ItemId = ItemA1, FromWarehouseId = WhA, ToWarehouseId = WhA0, Qty = 1m }, WhMsg);
    }

    [Fact(DisplayName = "후9 T-5 🔴 이관 · 도착 창고 id 빈 값 = 거절")]
    public async Task T5_Transfer_BlankToWarehouse_Rejected()
    {
        if (Skip("T-5")) return;
        using var db = FreshDb();
        SeedStock(db, TenantA, ItemA1, WhA, 10m);
        await AssertTransferRejected(db, new StockTransferRequest { ItemId = ItemA1, FromWarehouseId = WhA, ToWarehouseId = "", Qty = 1m }, WhMsg);
    }

    [Fact(DisplayName = "후7 T-6 이관 · A1 · WA(10) → WA2 · 4 = 원장 2행(out 4 · in 4 · 같은 source_id) · WA 6 · WA2 4")]
    public async Task T6_Transfer_SameTenant_Saved()
    {
        if (Skip("T-6")) return;
        using var db = FreshDb();
        SeedStock(db, TenantA, ItemA1, WhA, 10m);
        var audit = new Mock<IAuditService>();

        await Stock(db, audit).TransferStockAsync(TenantA, User,
            new StockTransferRequest { ItemId = ItemA1, FromWarehouseId = WhA, ToWarehouseId = WhA2, Qty = 4m, Memo = "SWT" }, CancellationToken.None);

        Assert.Equal(2, Count(db, "SELECT COUNT(*) FROM stock_ledger"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM stock_ledger WHERE item_id='{ItemA1}' AND warehouse_id='{WhA}' AND move_type='out' AND qty_out=4 AND source_type='transfer'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM stock_ledger WHERE item_id='{ItemA1}' AND warehouse_id='{WhA2}' AND move_type='in' AND qty_in=4 AND source_type='transfer'"));
        Assert.Equal(1, Count(db, "SELECT COUNT(DISTINCT source_id) FROM stock_ledger"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM item_stock WHERE tenant_id='{TenantA}' AND item_id='{ItemA1}' AND warehouse_id='{WhA}' AND current_qty=6"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM item_stock WHERE tenant_id='{TenantA}' AND item_id='{ItemA1}' AND warehouse_id='{WhA2}' AND current_qty=4"));
        VerifyAudit(audit, Times.Once());
    }

    // ════════════════════════════════════════════════════════════════════════
    //  판정 도우미
    // ════════════════════════════════════════════════════════════════════════

    private static async Task AssertAdjustRejected(MySqlConnection db, StockAdjustRequest req, string expectedMessage)
    {
        var ledgerBefore = Count(db, "SELECT COUNT(*) FROM stock_ledger");
        var stockBefore = StockSnapshot(db);
        var audit = new Mock<IAuditService>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Stock(db, audit).AdjustStockAsync(TenantA, User, req, CancellationToken.None));

        Assert.Equal(expectedMessage, ex.Message);
        Assert.Equal(ledgerBefore, Count(db, "SELECT COUNT(*) FROM stock_ledger"));
        Assert.Equal(stockBefore, StockSnapshot(db));
        VerifyAudit(audit, Times.Never());
    }

    private static async Task AssertTransferRejected(MySqlConnection db, StockTransferRequest req, string expectedMessage)
    {
        var ledgerBefore = Count(db, "SELECT COUNT(*) FROM stock_ledger");
        var stockBefore = StockSnapshot(db);
        var audit = new Mock<IAuditService>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Stock(db, audit).TransferStockAsync(TenantA, User, req, CancellationToken.None));

        Assert.Equal(expectedMessage, ex.Message);
        Assert.Equal(ledgerBefore, Count(db, "SELECT COUNT(*) FROM stock_ledger"));
        Assert.Equal(stockBefore, StockSnapshot(db));
        VerifyAudit(audit, Times.Never());
    }

    private static void VerifyAudit(Mock<IAuditService> audit, Times times) =>
        audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), times);

    /// <summary>재고 표 전체를 한 글자로 — 거절 전후 행 수 · 값이 그대로인지 한 번에 대조.</summary>
    private static string StockSnapshot(MySqlConnection db) =>
        StrOrEmpty(db, "SELECT COALESCE(GROUP_CONCAT(CONCAT(tenant_id,'|',item_id,'|',warehouse_id,'|',current_qty) ORDER BY tenant_id, item_id, warehouse_id SEPARATOR ';'), '') FROM item_stock");

    private static StockService Stock(MySqlConnection db, Mock<IAuditService> audit)
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.SetupGet(x => x.TenantId).Returns(TenantA);
        tenant.SetupGet(x => x.UserId).Returns(User);
        return new StockService(Mock.Of<IUnitOfWork>(), tenant.Object, db, audit.Object);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  표 · 씨앗
    // ════════════════════════════════════════════════════════════════════════

    private static bool Skip(string fact)
    {
        if (ServerAvailable()) return false;
        DbGateEnvironment.SkipOrFail($"StockWriteTenantGate {fact}");
        return true;
    }

    /// <summary>
    /// 시험 DB 의 <c>items</c> · <c>warehouses</c> · <c>item_stock</c> · <c>stock_ledger</c> 를 같은 이름 <c>TEMPORARY</c> 표로 가린다
    /// (<c>LIKE</c> = 칸 · 형 · 기본값 그대로). 연결 풀을 끄므로 연결을 닫으면 임시 표도 사라진다 — 실표는 가리기만 한다(헌법 #39).
    /// 씨앗 = 품목 A1(회사 A) · B1(회사 B) · 창고 WA · WA2(회사 A 활성) · WA0(회사 A 쉬게 함) · WB(회사 B 활성).
    /// </summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();

        foreach (var t in new[] { "items", "warehouses", "item_stock", "stock_ledger" })
        {
            // 가릴 표가 시험 DB 의 기본 표이고 FULLTEXT 색인이 없어야 임시 표로 만들 수 있다 — 아니면 먼저 멈춘다(실표 접촉 0).
            Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='{TestDb}' AND TABLE_NAME='{t}' AND TABLE_TYPE='BASE TABLE'"));
            Assert.Equal(0, Count(db, $"SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA='{TestDb}' AND TABLE_NAME='{t}' AND INDEX_TYPE='FULLTEXT'"));
            // 같은 이름으로 바로 LIKE 하면 "Not unique table/alias" — 다른 이름으로 만든 뒤 임시 표 이름을 바꾼다.
            Exec(db, $"DROP TEMPORARY TABLE IF EXISTS swttmp_{t}");
            Exec(db, $"CREATE TEMPORARY TABLE swttmp_{t} LIKE `{TestDb}`.`{t}`");
            Exec(db, $"ALTER TABLE swttmp_{t} RENAME TO `{t}`");
        }
        // 가려졌는지 확인 — 임시 표는 비어 있어야 한다.
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM stock_ledger"));
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM item_stock"));

        Exec(db, $"""
            INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit, is_active, created_at, updated_at) VALUES
              ('{ItemA1}', '{TenantA}', 'SWT-A1', 'SWTNAMEAONE', 'goods', 'EA', 1, NOW(6), NOW(6)),
              ('{ItemB1}', '{TenantB}', 'SWT-B1', 'SWTSECRETB', 'goods', 'EA', 1, NOW(6), NOW(6))
            """);
        Exec(db, $"""
            INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at) VALUES
              ('{WhA}', '{TenantA}', 'SWT-A', 'SWTWHA', 'normal', 1, NOW(6), NOW(6)),
              ('{WhA2}', '{TenantA}', 'SWT-A2', 'SWTWHATWO', 'normal', 1, NOW(6), NOW(6)),
              ('{WhA0}', '{TenantA}', 'SWT-A0', 'SWTWHAREST', 'normal', 0, NOW(6), NOW(6)),
              ('{WhB}', '{TenantB}', 'SWT-B', 'SWTWHSECRETB', 'normal', 1, NOW(6), NOW(6))
            """);
        return db;
    }

    private static void SeedStock(MySqlConnection db, string tenantId, string itemId, string warehouseId, decimal qty) =>
        Exec(db, $"""
            INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
            VALUES (UUID(), '{tenantId}', '{itemId}', '{warehouseId}', {qty.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 0, NOW(6))
            """);

    private static int Count(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string StrOrEmpty(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? string.Empty : Convert.ToString(v) ?? string.Empty;
    }

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        cmd.ExecuteNonQuery();
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다
        try { using var c = new MySqlConnection(ConnString()); c.Open(); return true; }
        catch (MySqlException) { return false; }
    }

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        // 🔴 비밀번호를 코드에 적지 않는다 (기존 게이트 관례).
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        // ⚠️ GuidFormat=None 필수 — 빠지면 char(36) 이 Guid 로 와서 string DTO 매핑이 터진다.
        // ⚠️ Pooling=false — 임시 표가 풀에 남은 연결로 새어 다른 게이트에 보이지 않게 한다.
        return new MySqlConnectionStringBuilder
        {
            Server = host, Port = uint.Parse(port), Database = TestDb, UserID = user, Password = pass,
            AllowUserVariables = true, GuidFormat = MySqlGuidFormat.None, Pooling = false, ConnectionTimeout = 5,
        }.ConnectionString;
    }
}
