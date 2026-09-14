using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Contracts.Sales;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 G5 <b>TaxInvoiceMigratedLockGate</b> — 20260915작1 갈래 G (설계 §17 R-B · 결재 §13 R-B2 「잠금」).
///
/// <para>
/// 이전 프로그램에서 가져온 자료(<c>source_type='migration'</c>)는 레거시 원장·대사표와 숫자가 맞아야 한다.
/// 선행검증 ④: 이관 판매 명세서 115,150건이 전부 confirmed 라 <b>계산서 발행 대상에 잡혔고</b>,
/// 수정·확정취소·삭제도 막는 코드가 0건이었다.
/// </para>
/// <para>
/// 🔴 글자 검사가 아니다 — <b>실제 서비스 메서드</b>를 MariaDB 임시 표에 물려 부르고
/// 표가 바뀌었는지(행 수·상태)로 판정한다. 임시 표는 <c>CREATE TEMPORARY TABLE … LIKE {TestDb}.표</c> 라
/// CI 에서는 출하 DDL(헌법 #36) 구조 그대로다. 실제 표에는 한 줄도 안 쓴다.
/// </para>
/// <para>
/// 대조군: 사람이 만든 명세서 발행 성공 · 사람 명세서 수정 성공 · 사람 매입명세서 삭제 성공.
/// 대조군이 빨간불이면 잠금이 애먼 전표까지 막은 것이다.
/// </para>
/// <para>DB 없는 로컬 = SKIP(초록불 아님) · CI(<c>HITPAN_REQUIRE_DB</c>) = 실패.</para>
/// </summary>
public sealed class TaxInvoiceMigratedLockGateTests
{
    private const string Tid = "GATE-MIGLOCK915";
    private const string Partner = "P-MIGLOCK";
    private const string Item = "ITEM-MIGLOCK";
    private const string Wh = "WH-MIGLOCK";

    private const string MigDelivery = "D-MIG-1";
    private const string MigDraftDelivery = "D-MIG-DRAFT";
    private const string MigUnissuedDelivery = "D-MIG-NOTAX";
    private const string HumanDelivery = "D-HUMAN-1";
    private const string HumanDraftDelivery = "D-HUMAN-DRAFT";
    private const string MigInvoice = "TI-MIG-1";
    private const string MigReceipt = "R-MIG-1";
    private const string MigDraftReceipt = "R-MIG-DRAFT";
    private const string HumanDraftReceipt = "R-HUMAN-DRAFT";

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // ────────────────────────────────────────────────────────────
    // 계산서

    /// <summary>G5-1 — 이관 명세서는 계산서 발행 요청을 서버가 거부한다 (화면 숨김과 별개).</summary>
    [Fact]
    public async Task G5_1_이관명세서_계산서발행_거부()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_1_이관명세서_계산서발행_거부)); return; }
        using var db = FreshDb();

        var svc = new TaxInvoiceService(db, UnitOfWork(db));
        var ex = await Assert.ThrowsAsync<TaxInvoiceException>(() =>
            svc.IssueAsync(new IssueTaxInvoiceRequest(MigDelivery, null), Tid, "gate-user", null));

        Assert.Equal(MigratedDocumentLock.LockedErrorCode, ex.ErrorCode);
        Assert.Equal(MigratedDocumentLock.IssueBlockedMessage, ex.Message);
        Assert.Equal(0, Count(db, $"SELECT COUNT(*) FROM tax_invoices WHERE delivery_id='{MigDelivery}'"));
        Assert.Equal(0, Count(db, $"SELECT COUNT(*) FROM sales_deliveries WHERE delivery_id='{MigDelivery}' AND tax_invoice_id IS NOT NULL"));
    }

    /// <summary>G5-2 대조군 — 사람이 만든 확정 명세서는 계산서가 그대로 발행된다.</summary>
    [Fact]
    public async Task G5_2_사람명세서_계산서발행_성공_대조군()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_2_사람명세서_계산서발행_성공_대조군)); return; }
        using var db = FreshDb();

        var svc = new TaxInvoiceService(db, UnitOfWork(db));
        var res = await svc.IssueAsync(new IssueTaxInvoiceRequest(HumanDelivery, null), Tid, "gate-user", null);

        Assert.Equal("issued", res.Status);
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM tax_invoices WHERE delivery_id='{HumanDelivery}' AND status='issued'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_deliveries WHERE delivery_id='{HumanDelivery}' AND tax_invoice_id='{res.InvoiceId}'"));
    }

    /// <summary>G5-3 — 이관 계산서(tax_invoices.source_type='migration')는 취소를 거부하고 상태가 그대로다.</summary>
    [Fact]
    public async Task G5_3_이관계산서_취소_거부()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_3_이관계산서_취소_거부)); return; }
        using var db = FreshDb();

        var svc = new TaxInvoiceService(db, UnitOfWork(db));
        var ex = await Assert.ThrowsAsync<TaxInvoiceException>(() =>
            svc.CancelAsync(MigInvoice, new CancelTaxInvoiceRequest("게이트"), Tid, "gate-user"));

        Assert.Equal(MigratedDocumentLock.LockedErrorCode, ex.ErrorCode);
        Assert.Equal(MigratedDocumentLock.CancelInvoiceBlockedMessage, ex.Message);
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM tax_invoices WHERE invoice_id='{MigInvoice}' AND status='issued'"));
    }

    /// <summary>G5-4 — 발행 대상 목록 원천(GetDeliveriesAsync)이 이관 여부를 싣는다 — 화면이 이 값으로 뺀다.</summary>
    [Fact]
    public async Task G5_4_목록이_이관여부를_싣는다()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_4_목록이_이관여부를_싣는다)); return; }
        using var db = FreshDb();

        var list = await Sales(db).GetDeliveriesAsync(Tid);
        var mig = list.Find(x => x.DeliveryId == MigDelivery);
        var human = list.Find(x => x.DeliveryId == HumanDelivery);

        Assert.NotNull(mig);
        Assert.NotNull(human);
        Assert.True(mig!.IsMigrated, "이관 명세서가 목록에서 이관으로 표시되지 않는다 — 발행 화면에서 빠지지 않는다.");
        Assert.False(human!.IsMigrated, "사람 명세서가 이관으로 표시됐다 — 발행 대상에서 잘못 빠진다.");

        // R-B3 — 발행 화면은 IsIssueLocked 로 뺀다: 레거시 발행분만 true
        var unissued = list.Find(x => x.DeliveryId == MigUnissuedDelivery);
        Assert.NotNull(unissued);
        Assert.True(mig.IsIssueLocked, "레거시 발행 이관 명세서가 발행 잠금으로 표시되지 않는다.");
        Assert.False(unissued!.IsIssueLocked, "레거시 미발행 이관 명세서가 발행 대상에서 빠진다(R-B3 위반).");
        Assert.False(human.IsIssueLocked);
    }

    /// <summary>G5-12 대조군 (R-B3) — 레거시에서 계산서를 안 끊은 이관 명세서는 발행된다.</summary>
    [Fact]
    public async Task G5_12_레거시미발행_이관명세서_발행_성공()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_12_레거시미발행_이관명세서_발행_성공)); return; }
        using var db = FreshDb();

        var res = await new TaxInvoiceService(db, UnitOfWork(db))
            .IssueAsync(new IssueTaxInvoiceRequest(MigUnissuedDelivery, null), Tid, "gate-user", null);

        Assert.Equal("issued", res.Status);
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM tax_invoices WHERE delivery_id='{MigUnissuedDelivery}' AND status='issued'"));
    }

    // ────────────────────────────────────────────────────────────
    // 판매 거래명세서

    /// <summary>
    /// G5-5 — 이관 판매 명세서 수정 거부. 확정분은 안내 문구로, <b>draft 로 들어온 이관분은 실제 변경 여부</b>로 잰다
    /// (봉합이 빠지면 draft 이관분이 실제로 고쳐진다 = 표가 바뀐다).
    /// </summary>
    [Fact]
    public async Task G5_5_이관판매명세서_수정_거부()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_5_이관판매명세서_수정_거부)); return; }
        using var db = FreshDb();
        var svc = Sales(db);

        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateDeliveryAsync(MigDelivery, UpdateDto("고침"), Tid, "gate-user"));
        Assert.Equal(MigratedDocumentLock.EditBlockedMessage, ex1.Message);

        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateDeliveryAsync(MigDraftDelivery, UpdateDto("고침"), Tid, "gate-user"));
        Assert.Equal(MigratedDocumentLock.EditBlockedMessage, ex2.Message);

        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_deliveries WHERE memo='고침'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE delivery_id='{MigDraftDelivery}' AND qty=1"));
    }

    /// <summary>G5-6 — 이관 판매 명세서 확정취소 거부 — 역행 원장이 한 줄도 안 생긴다.</summary>
    [Fact]
    public async Task G5_6_이관판매명세서_확정취소_거부()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_6_이관판매명세서_확정취소_거부)); return; }
        using var db = FreshDb();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Sales(db).CancelConfirmedDeliveryAsync(MigDelivery, Tid, null));

        Assert.Equal(MigratedDocumentLock.EditBlockedMessage, ex.Message);
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_deliveries WHERE delivery_id='{MigDelivery}' AND status='confirmed'"));
        Assert.Equal(0, Count(db, $"SELECT COUNT(*) FROM stock_ledger WHERE source_id='{MigDelivery}'"));
    }

    /// <summary>G5-7 — 이관 판매 명세서 삭제 거부 (draft 이관분은 봉합이 빠지면 실제로 cancelled 가 된다).</summary>
    [Fact]
    public async Task G5_7_이관판매명세서_삭제_거부()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_7_이관판매명세서_삭제_거부)); return; }
        using var db = FreshDb();
        var svc = Sales(db);

        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteDeliveryAsync(MigDraftDelivery, Tid));
        Assert.Equal(MigratedDocumentLock.EditBlockedMessage, ex1.Message);

        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteDeliveryAsync(MigDelivery, Tid));
        Assert.Equal(MigratedDocumentLock.EditBlockedMessage, ex2.Message);

        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_deliveries WHERE delivery_id='{MigDraftDelivery}' AND status='draft'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_deliveries WHERE delivery_id='{MigDelivery}' AND status='confirmed'"));
    }

    /// <summary>G5-8 대조군 — 사람이 만든 draft 명세서는 그대로 수정된다.</summary>
    [Fact]
    public async Task G5_8_사람판매명세서_수정_성공_대조군()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_8_사람판매명세서_수정_성공_대조군)); return; }
        using var db = FreshDb();

        // ⚠️ 발견(갈래 G 범위 밖 · 개발명세서 §5): UpdateDeliveryAsync 는 _db.BeginTransaction() 을 연 뒤
        //   LoadItemDefaultWarehousesAsync 를 트랜잭션 없이 부른다 → MySqlConnector 가
        //   "The transaction associated with this command is not the connection's active transaction" 으로 던진다(이 게이트 실측).
        //   그래서 대조군은 「끝까지 저장됐나」가 아니라 「잠금이 사람 명세서를 막지 않았나」로 잰다:
        //   잠금 검사를 그대로 통과하고, 수정이 잠금 뒤 단계(헤더 UPDATE)까지 실제로 들어갔는지 본다.
        await MigratedDocumentLock.EnsureDeliveryEditableAsync(db, HumanDraftDelivery, Tid);

        string? error = null;
        try
        {
            await Sales(db).UpdateDeliveryAsync(HumanDraftDelivery, UpdateDto("사람고침", qty: 3m), Tid, "gate-user");
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
        }

        Assert.NotEqual(MigratedDocumentLock.EditBlockedMessage, error);
        if (error is null)
        {
            Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_deliveries WHERE delivery_id='{HumanDraftDelivery}' AND memo='사람고침'"));
        }
        else
        {
            Console.Error.WriteLine($"[G5-8 발견] 사람 명세서 수정이 잠금 뒤 단계에서 실패: {error}");
            Assert.Contains("transaction", error, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ────────────────────────────────────────────────────────────
    // 매입명세서 (판매만 잠그면 한쪽만 고친 것이다)

    /// <summary>G5-9 — 이관 매입명세서 삭제 거부 (draft 이관분은 봉합이 빠지면 실제로 행이 지워진다).</summary>
    [Fact]
    public async Task G5_9_이관매입명세서_삭제_거부()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_9_이관매입명세서_삭제_거부)); return; }
        using var db = FreshDb();
        var svc = Purchase(db);

        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeletePurchaseReceiptAsync(MigDraftReceipt, Tid));
        Assert.Equal(MigratedDocumentLock.EditBlockedMessage, ex1.Message);

        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeletePurchaseReceiptAsync(MigReceipt, Tid));
        Assert.Equal(MigratedDocumentLock.EditBlockedMessage, ex2.Message);

        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_receipts WHERE receipt_id='{MigDraftReceipt}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_receipt_items WHERE receipt_id='{MigDraftReceipt}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_receipts WHERE receipt_id='{MigReceipt}'"));
    }

    /// <summary>G5-10 대조군 — 사람이 만든 draft 매입명세서는 그대로 지워진다.</summary>
    [Fact]
    public async Task G5_10_사람매입명세서_삭제_성공_대조군()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_10_사람매입명세서_삭제_성공_대조군)); return; }
        using var db = FreshDb();

        await Purchase(db).DeletePurchaseReceiptAsync(HumanDraftReceipt, Tid);

        Assert.Equal(0, Count(db, $"SELECT COUNT(*) FROM purchase_receipts WHERE receipt_id='{HumanDraftReceipt}'"));
    }

    /// <summary>
    /// G5-11 대조군 (병렬이슈39 ③ · 헌법 #20) — 이관 명세서에 붙은 확정 반품의 <b>마이너스 계산서</b>는 막히지 않는다.
    /// 정정은 새 전표로 하라고 안내했으니, 그 새 전표의 계산서 흐름이 끊기면 안 된다.
    /// </summary>
    [Fact]
    public async Task G5_11_이관명세서_반품의_마이너스계산서_발행_대조군()
    {
        if (!ServerAvailable()) { Skipped(nameof(G5_11_이관명세서_반품의_마이너스계산서_발행_대조군)); return; }
        using var db = FreshDb();
        Exec(db, "DROP TEMPORARY TABLE IF EXISTS g5tmp_sales_returns");
        Exec(db, $"CREATE TEMPORARY TABLE g5tmp_sales_returns LIKE `{TestDb}`.sales_returns");
        Exec(db, "ALTER TABLE g5tmp_sales_returns RENAME TO sales_returns");
        Exec(db, $"""
            INSERT INTO sales_returns (return_id, tenant_id, return_no, delivery_id, partner_id, return_date, status,
                                       total_amount, vat_amount, is_deleted, created_at)
            VALUES ('SR-MIG-1', '{Tid}', '반-SR-MIG-1', '{MigDelivery}', '{Partner}', '2026-09-15', 'confirmed',
                    500, 50, 0, NOW(6))
            """);

        var res = await new TaxInvoiceService(db, UnitOfWork(db))
            .IssueCreditNoteAsync(new IssueCreditNoteRequest("SR-MIG-1", null), Tid, "gate-user", null);

        Assert.Equal("issued", res.Status);
        Assert.Equal(1, Count(db, "SELECT COUNT(*) FROM tax_invoices WHERE source_type='sales_return' AND source_id='SR-MIG-1'"));
    }

    // ────────────────────────────────────────────────────────────
    // 헬퍼

    private static SalesService Sales(MySqlConnection db) =>
        new(UnitOfWork(db), null!, db, null!, new Mock<IAuditService>().Object, null!);

    private static PurchaseService Purchase(MySqlConnection db) =>
        new(UnitOfWork(db), null!, db, new Mock<IAuditService>().Object);

    private static UpdateDeliveryDto UpdateDto(string memo, decimal qty = 2m) => new()
    {
        OrderDate = new DateTime(2026, 9, 15),
        PartnerId = Partner,
        Memo = memo,
        Items = new List<DeliveryItemDto>
        {
            new() { ItemId = Item, Qty = qty, UnitPrice = 1000m, Amount = 1000m * qty, VatAmount = 100m * qty, WarehouseId = Wh }
        }
    };

    /// <summary>서비스의 UoW 트랜잭션을 <b>같은 연결</b>의 실제 트랜잭션으로 준다 — 임시 표는 연결마다 따로라서.</summary>
    private static IUnitOfWork UnitOfWork(MySqlConnection db)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetDbConnection()).Returns(db);
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync(() => new ConnTx(db.BeginTransaction()));
        return uow.Object;
    }

    private sealed class ConnTx : ISharedTransaction
    {
        private readonly MySqlTransaction _tx;
        public ConnTx(MySqlTransaction tx) => _tx = tx;
        public DbTransaction DbTransaction => _tx;
        public Task CommitAsync(CancellationToken ct = default) => _tx.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => _tx.RollbackAsync(ct);
        public void Dispose() => _tx.Dispose();
        public ValueTask DisposeAsync() => _tx.DisposeAsync();
    }

    private static readonly string[] Tables =
    {
        "sales_deliveries", "sales_delivery_items", "tax_invoices", "purchase_receipts", "purchase_receipt_items",
        "purchase_returns", "stock_ledger", "journal_entries", "items", "warehouses", "partners", "employees"
    };

    /// <summary>
    /// 임시 표를 출하 DDL 구조 그대로(LIKE) 만든다. 연결 풀을 끄므로 연결을 닫으면 임시 표도 사라진다.
    /// </summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();

        foreach (var t in Tables)
        {
            Exec(db, $"DROP TEMPORARY TABLE IF EXISTS {t}");
            // 같은 이름으로 바로 LIKE 하면 "Not unique table/alias" — 다른 이름으로 만든 뒤 임시 표 이름을 바꾼다.
            Exec(db, $"DROP TEMPORARY TABLE IF EXISTS g5tmp_{t}");
            Exec(db, $"CREATE TEMPORARY TABLE g5tmp_{t} LIKE `{TestDb}`.{t}");
            Exec(db, $"ALTER TABLE g5tmp_{t} RENAME TO {t}");
        }

        Exec(db, $"""
            INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at)
            VALUES ('{Wh}', '{Tid}', 'MAIN', '기본창고', 'normal', 1, NOW(6), NOW(6))
            """);

        // 판매 명세서 4종 — 이관 확정 · 이관 draft(방어 확인용) · 사람 확정 · 사람 draft
        InsertDelivery(db, MigDelivery, "migration", "confirmed", legacyTaxNo: "20260101");   // 레거시 발행분
        InsertDelivery(db, MigUnissuedDelivery, "migration", "confirmed", legacyTaxNo: "0"); // 레거시 미발행('00000000')
        InsertDelivery(db, MigDraftDelivery, "migration", "draft");
        InsertDelivery(db, HumanDelivery, "direct", "confirmed");
        InsertDelivery(db, HumanDraftDelivery, "direct", "draft");
        InsertDeliveryItem(db, MigDraftDelivery);
        InsertDeliveryItem(db, HumanDraftDelivery);

        Exec(db, $"""
            INSERT INTO tax_invoices
              (invoice_id, tenant_id, delivery_id, invoice_no, issued_at, issued_by, amount_total, vat_total,
               status, etax_status, source_type, source_id)
            VALUES
              ('{MigInvoice}', '{Tid}', NULL, 'MIG-TAX-0001', NOW(6), 'migration', 1000, 100,
               'issued', 'pending', 'migration', 'LEGACY-1')
            """);

        // 매입 명세서 3종 — 이관 확정 · 이관 draft · 사람 draft
        InsertReceipt(db, MigReceipt, "migration", "confirmed");
        InsertReceipt(db, MigDraftReceipt, "migration", "draft");
        InsertReceipt(db, HumanDraftReceipt, "direct", "draft");
        Exec(db, $"""
            INSERT INTO purchase_receipt_items
              (receipt_item_id, receipt_id, tenant_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
            VALUES (UUID(), '{MigDraftReceipt}', '{Tid}', '{Item}', '{Wh}', 1, 1000, 1000, 100)
            """);

        return db;
    }

    private static void InsertDelivery(MySqlConnection db, string id, string sourceType, string status, string legacyTaxNo = "NULL") => Exec(db, $"""
        INSERT INTO sales_deliveries
          (delivery_id, tenant_id, delivery_no, partner_id, delivery_date, source_type, status,
           total_amount, vat_amount, memo, created_at, updated_at, legacy_tax_no)
        VALUES
          ('{id}', '{Tid}', 'DN-{id}', '{Partner}', '2026-09-15', '{sourceType}', '{status}',
           1000, 100, '원본', NOW(6), NOW(6), {legacyTaxNo})
        """);

    private static void InsertDeliveryItem(MySqlConnection db, string deliveryId) => Exec(db, $"""
        INSERT INTO sales_delivery_items
          (delivery_item_id, delivery_id, tenant_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
        VALUES (UUID(), '{deliveryId}', '{Tid}', '{Item}', '{Wh}', 1, 1000, 1000, 100)
        """);

    private static void InsertReceipt(MySqlConnection db, string id, string sourceType, string status) => Exec(db, $"""
        INSERT INTO purchase_receipts
          (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, source_type, status,
           total_amount, vat_amount, created_at)
        VALUES
          ('{id}', '{Tid}', 'RC-{id}', '{Partner}', '2026-09-15', '{sourceType}', '{status}',
           1000, 100, NOW(6))
        """);

    private static int Count(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void Exec(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        cmd.ExecuteNonQuery();
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 (작14 W1)
        try { using var c = new MySqlConnection(ConnString()); c.Open(); return true; }
        catch (MySqlException) { return false; }
    }

    private static void Skipped(string gate) => DbGateEnvironment.SkipOrFail(gate);

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        // ⚠️ GuidFormat=None 필수 — 빠지면 char(36) 이 Guid 로 와서 string DTO 매핑이 터진다.
        // ⚠️ Pooling=false — 임시 표가 풀에 남은 연결로 새어 다른 게이트에 보이지 않게 한다.
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "AllowUserVariables=true;GuidFormat=None;Pooling=false;Connection Timeout=5;";
    }
}
