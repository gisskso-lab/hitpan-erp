using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HitPan.API.Services;
using HitPan.Application.DTOs.Purchase;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Domain.Entities;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 후1 갈래 2(작업지시서 20261127작 §4-2 · §4-3 · §5-4 · PM 결재 K3 (가)) — <b>전표 저장 입구 13 + 전환 2 는 다른 회사 품목을 저장하지 않는다.</b>
/// </summary>
/// <remarks>
/// 🔴 <b>R-n 거절</b> — 다른 회사 품목 1줄을 섞은 요청 → <see cref="InvalidOperationException"/> · <b>문구까지</b> 대조(tx 를 빠뜨려도
///   같은 예외형이 나므로 형만 보면 빈 측정 · [3-V] P2-2) · <b>쓴 행 0</b>(신규 = 머리 · 상세 행 수 그대로 / 수정 = 머리 값 · 기존 줄 그대로).
///   E11 · E13 은 트랜잭션 없는 UPDATE → DELETE → INSERT 길이라 머리 값(메모 · 합계)이 그대로임을 특히 잰다.
/// 🔴 <b>A-n 같은 회사 그대로</b> — Dapper 입구(E1 · E2 · E4 · E6 · E7 · E10 · E11 · E12 · E13 · K3 둘)는 실제 저장 행 · 값을 본다.
///   EF 입구(E3 · E5 · E8 · E9)는 가짜 작업 단위라 「쓴 행 0」 이 늘 참 → 거절 때 <b>저장 호출 Times.Never</b> 를 재고([3-V] P2-2),
///   같은 회사 품목이면 <b>가드 문구 예외가 안 남</b>을 잰다(다음 단계에서 다른 까닭으로 멈추면 그 예외가 가드 문구가 아님을 단언).
/// 🔴 E7 일괄은 예외를 삼키고 건별 <c>ErrorMessage</c> 로 돌려준다 → 결과 문구 대조 + 쓴 행 0([3-V] P2-1).
/// 🔴 거래명세 「id 비면 이름으로 찾기」 보존 — E7(Dapper)에서 id 빈 줄 + 같은 회사 품명 → 통과 · 그 품목 id 로 저장.
/// 🔴 <b>표 = 시험 연결 하나의 TEMPORARY 표만</b>([3-V] P2-4) — 시험 DB 의 기본 표 전부를 같은 이름 TEMPORARY 표로
///   <c>CREATE TEMPORARY TABLE … LIKE</c>(칸 · 형 · 기본값 = 출하 DDL 로 만든 시험 DB 그대로 · <c>sales_return_items.is_loss</c> 포함 · #13 · #36)
///   가린다(선례 <c>TaxInvoiceMigratedLockGateTests.FreshDb</c>). 모든 서비스 · 가짜 작업 단위가 <b>그 연결 하나</b>만 쓴다
///   (<c>GetDbConnection</c> · <c>BeginTransactionAsync</c> 도 같은 연결) → 실표 접촉 0(헌법 #39) · 한 연결 직렬(헌법 #16).
/// 🔴 제품 <b>공개</b> 함수로만 부른다(반사 0). 이 PC 개발 DB(3306)는 실물이다 — 로컬에서는 돌리지 않는다(CI DB 게이트 잡이 문다).
/// </remarks>
public sealed class VoucherSaveItemTenantGateDbTests
{
    private const string TenantA = "GATE-VSIT-TENANT-A";
    private const string TenantB = "GATE-VSIT-TENANT-B";
    private const string User = "gate-vsit-user";
    private const string PartnerA = "GATE-VSIT-PARTNER-A";
    private const string WhA = "GATE-VSIT-WH-A";
    private const string ItemA1 = "GATE-VSIT-ITEM-A1";
    private const string ItemA2 = "GATE-VSIT-ITEM-A2";
    private const string ItemB1 = "GATE-VSIT-ITEM-B1";
    private const string NameA1 = "VSITNAMEAONE";
    private const string Orig = "VSIT-ORIG";
    private const string Changed = "VSIT-CHANGED";

    private static string Msg => ItemTenantGuard.NotThisTenantItemMessage;

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // 줄마다 금액을 다르게 둔다(줄 셈 · 값 대조가 섞이지 않게).
    private static readonly (string Id, decimal Qty, decimal Price)[] SameTenantLines =
        { (ItemA1, 2m, 1100m), (ItemA2, 3m, 1250m) };
    private static readonly (string Id, decimal Qty, decimal Price)[] MixedLines =
        { (ItemA1, 2m, 1100m), (ItemB1, 1m, 1370m) };

    // ════════════════════════════════════════════════════════════════════════
    //  E1 · E2 견적 (Dapper)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후1 E1 R 🔴 견적 신규 · 다른 회사 품목 섞임 = 거절 · 문구 · 쓴 행 0")]
    public async Task E1_R_QuotationCreate_Rejected()
    {
        if (Skip("E1 R")) return;
        using var db = FreshDb();

        await AssertGuardRejects(() => Quotation(db).CreateAsync(TenantA, QuoteReq(MixedLines, Orig)));

        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM quotations"));
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM quotation_items"));
    }

    [Fact(DisplayName = "후1 E1 A 견적 신규 · 같은 회사 품목 = 저장 · 줄 2 · 값 그대로")]
    public async Task E1_A_QuotationCreate_Saved()
    {
        if (Skip("E1 A")) return;
        using var db = FreshDb();

        var quoteId = await Quotation(db).CreateAsync(TenantA, QuoteReq(SameTenantLines, Orig));

        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM quotations WHERE quote_id='{quoteId}' AND tenant_id='{TenantA}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM quotation_items WHERE quote_id='{quoteId}' AND item_id='{ItemA1}' AND amount=2200"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM quotation_items WHERE quote_id='{quoteId}' AND item_id='{ItemA2}' AND amount=3750"));
    }

    [Fact(DisplayName = "후1 E2 R 🔴 견적 수정 · 다른 회사 품목 섞임 = 거절 · 머리 · 기존 줄 그대로")]
    public async Task E2_R_QuotationUpdate_Rejected()
    {
        if (Skip("E2 R")) return;
        using var db = FreshDb();
        var svc = Quotation(db);
        var quoteId = await svc.CreateAsync(TenantA, QuoteReq(SameTenantLines, Orig));

        await AssertGuardRejects(() => svc.UpdateAsync(TenantA, quoteId, QuoteUpdateReq(MixedLines, Changed)));

        Assert.Equal(Orig, Str(db, $"SELECT memo FROM quotations WHERE quote_id='{quoteId}'"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM quotation_items WHERE quote_id='{quoteId}'"));
        Assert.Equal(0, Count(db, $"SELECT COUNT(*) FROM quotation_items WHERE item_id='{ItemB1}'"));
    }

    [Fact(DisplayName = "후1 E2 A 견적 수정 · 같은 회사 품목 = 머리 · 줄 바뀜")]
    public async Task E2_A_QuotationUpdate_Saved()
    {
        if (Skip("E2 A")) return;
        using var db = FreshDb();
        var svc = Quotation(db);
        var quoteId = await svc.CreateAsync(TenantA, QuoteReq(SameTenantLines, Orig));

        await svc.UpdateAsync(TenantA, quoteId, QuoteUpdateReq(new[] { (ItemA2, 4m, 1250m) }, Changed));

        Assert.Equal(Changed, Str(db, $"SELECT memo FROM quotations WHERE quote_id='{quoteId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM quotation_items WHERE quote_id='{quoteId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM quotation_items WHERE quote_id='{quoteId}' AND item_id='{ItemA2}' AND amount=5000"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  K3 (가) 견적 → 수주 전환 (Dapper · 트랜잭션 없음 — [3-V] P2-3)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후1 K3 견적→수주 R 🔴 원본 견적 줄에 다른 회사 품목 = 거절 · 수주 머리 · 줄 0 · 견적 상태 그대로")]
    public async Task K3_R_QuoteToOrder_Rejected()
    {
        if (Skip("K3 견적→수주 R")) return;
        using var db = FreshDb();
        var svc = Quotation(db);
        var quoteId = await svc.CreateAsync(TenantA, QuoteReq(SameTenantLines, Orig));
        // 고치기 전 들어간 옛 자료 흉내 — 시험 연결의 TEMPORARY 표 줄만 바꾼다.
        Exec(db, $"UPDATE quotation_items SET item_id='{ItemB1}' WHERE quote_id='{quoteId}' AND item_id='{ItemA2}'");

        await AssertGuardRejects(() => svc.ConvertToSalesOrderAsync(TenantA, quoteId));

        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_orders"));
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_order_items"));
        Assert.Equal("draft", Str(db, $"SELECT status FROM quotations WHERE quote_id='{quoteId}'"));
        Assert.Null(StrOrNull(db, $"SELECT converted_order_id FROM quotations WHERE quote_id='{quoteId}'"));
    }

    [Fact(DisplayName = "후1 K3 견적→수주 A 같은 회사 품목 = 수주 머리 1 · 줄 2 · 견적 converted")]
    public async Task K3_A_QuoteToOrder_Saved()
    {
        if (Skip("K3 견적→수주 A")) return;
        using var db = FreshDb();
        var svc = Quotation(db);
        var quoteId = await svc.CreateAsync(TenantA, QuoteReq(SameTenantLines, Orig));

        var orderNo = await svc.ConvertToSalesOrderAsync(TenantA, quoteId);

        var orderId = Str(db, $"SELECT order_id FROM sales_orders WHERE order_no='{orderNo}' AND tenant_id='{TenantA}'");
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_order_items WHERE order_id='{orderId}' AND item_id='{ItemA1}' AND supply_amount=2200"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_order_items WHERE order_id='{orderId}' AND item_id='{ItemA2}' AND supply_amount=3750"));
        Assert.Equal("converted", Str(db, $"SELECT status FROM quotations WHERE quote_id='{quoteId}'"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  E3 수주 신규 · E5 거래명세 신규 (EF · 가짜 작업 단위)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후1 E3 R 🔴 수주 신규(EF) · 다른 회사 품목 = 거절 · 문구 · 저장 호출 Never")]
    public async Task E3_R_SalesOrderCreate_Rejected()
    {
        if (Skip("E3 R")) return;
        using var db = FreshDb();
        var uow = Uow(db);

        await AssertGuardRejects(() => Sales(db, uow.Object).CreateOrderAsync(SalesOrderReq(MixedLines)));

        uow.Verify(u => u.Repository<SalesOrder>(), Times.Never);
        uow.Verify(u => u.Repository<SalesOrderItem>(), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_orders"));
    }

    [Fact(DisplayName = "후1 E3 A 수주 신규(EF) · 같은 회사 품목 = 가드 문구 예외 없음")]
    public async Task E3_A_SalesOrderCreate_NotGuarded()
    {
        if (Skip("E3 A")) return;
        using var db = FreshDb();
        var uow = Uow(db);

        await AssertNotGuarded(() => Sales(db, uow.Object).CreateOrderAsync(SalesOrderReq(SameTenantLines)));
    }

    [Fact(DisplayName = "후1 E5 R 🔴 거래명세 신규(EF) · 다른 회사 품목 = 거절 · 문구 · 저장 호출 Never")]
    public async Task E5_R_DeliveryCreate_Rejected()
    {
        if (Skip("E5 R")) return;
        using var db = FreshDb();
        var uow = Uow(db);

        await AssertGuardRejects(() => Sales(db, uow.Object).CreateDeliveryAsync(DeliveryReq(MixedLines)));

        uow.Verify(u => u.Repository<SalesDelivery>(), Times.Never);
        uow.Verify(u => u.Repository<SalesDeliveryItem>(), Times.Never);
        uow.Verify(u => u.Repository<SalesOrder>(), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_deliveries"));
    }

    [Fact(DisplayName = "후1 E5 A 거래명세 신규(EF) · 같은 회사 품목(+ id 빈 이름 찾기 줄) = 가드 문구 예외 없음")]
    public async Task E5_A_DeliveryCreate_NotGuarded()
    {
        if (Skip("E5 A")) return;
        using var db = FreshDb();
        var uow = Uow(db);
        var req = DeliveryReq(SameTenantLines);
        req.Items.Add(new CreateDeliveryItemRequest { ItemId = "", ItemName = NameA1, WarehouseId = WhA, Qty = 1m, UnitPrice = 990m, SupplyAmount = 990m, VatAmount = 99m });

        await AssertNotGuarded(() => Sales(db, uow.Object).CreateDeliveryAsync(req));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  E4 수주 수정 · E6 거래명세 수정 (Dapper · 트랜잭션)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후1 E4 R 🔴 수주 수정 · 다른 회사 품목 = 거절 · 머리 · 기존 줄 그대로")]
    public async Task E4_R_SalesOrderUpdate_Rejected()
    {
        if (Skip("E4 R")) return;
        using var db = FreshDb();
        SeedDraftOrder(db, "VSIT-SO-1");

        await AssertGuardRejects(() => Sales(db).UpdateOrderAsync("VSIT-SO-1", SalesOrderUpdateReq(MixedLines), TenantA));

        Assert.Equal(Orig, Str(db, "SELECT memo FROM sales_orders WHERE order_id='VSIT-SO-1'"));
        Assert.Equal(1, Count(db, "SELECT COUNT(*) FROM sales_order_items WHERE order_id='VSIT-SO-1'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_order_items WHERE order_id='VSIT-SO-1' AND item_id='{ItemA1}' AND supply_amount=1000"));
    }

    [Fact(DisplayName = "후1 E4 A 수주 수정 · 같은 회사 품목 = 머리 · 줄 바뀜")]
    public async Task E4_A_SalesOrderUpdate_Saved()
    {
        if (Skip("E4 A")) return;
        using var db = FreshDb();
        SeedDraftOrder(db, "VSIT-SO-1");

        await Sales(db).UpdateOrderAsync("VSIT-SO-1", SalesOrderUpdateReq(SameTenantLines), TenantA);

        Assert.Equal(Changed, Str(db, "SELECT memo FROM sales_orders WHERE order_id='VSIT-SO-1'"));
        Assert.Equal(2, Count(db, "SELECT COUNT(*) FROM sales_order_items WHERE order_id='VSIT-SO-1'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_order_items WHERE order_id='VSIT-SO-1' AND item_id='{ItemA2}' AND supply_amount=3750"));
    }

    [Fact(DisplayName = "후1 E6 R 🔴 거래명세 수정 · 다른 회사 품목 = 거절 · 머리 · 기존 줄 그대로")]
    public async Task E6_R_DeliveryUpdate_Rejected()
    {
        if (Skip("E6 R")) return;
        using var db = FreshDb();
        SeedDraftDelivery(db, "VSIT-DN-1");

        await AssertGuardRejects(() => Sales(db).UpdateDeliveryAsync("VSIT-DN-1", DeliveryUpdateDto(MixedLines), TenantA, User));

        Assert.Equal(Orig, Str(db, "SELECT memo FROM sales_deliveries WHERE delivery_id='VSIT-DN-1'"));
        Assert.Equal(1, Count(db, "SELECT COUNT(*) FROM sales_delivery_items WHERE delivery_id='VSIT-DN-1'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE delivery_id='VSIT-DN-1' AND item_id='{ItemA1}' AND supply_amount=1000"));
    }

    [Fact(DisplayName = "후1 E6 A 거래명세 수정 · 같은 회사 품목 = 머리 · 줄 바뀜")]
    public async Task E6_A_DeliveryUpdate_Saved()
    {
        if (Skip("E6 A")) return;
        using var db = FreshDb();
        SeedDraftDelivery(db, "VSIT-DN-1");

        await Sales(db).UpdateDeliveryAsync("VSIT-DN-1", DeliveryUpdateDto(SameTenantLines), TenantA, User);

        Assert.Equal(Changed, Str(db, "SELECT memo FROM sales_deliveries WHERE delivery_id='VSIT-DN-1'"));
        Assert.Equal(2, Count(db, "SELECT COUNT(*) FROM sales_delivery_items WHERE delivery_id='VSIT-DN-1'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE delivery_id='VSIT-DN-1' AND item_id='{ItemA2}' AND supply_amount=3750"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  E7 거래명세 일괄 (Dapper · 같은 tx · 예외를 결과 문구로 돌려줌 — [3-V] P2-1)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후1 E7 R 🔴 거래명세 일괄 · 두 번째 요청에 다른 회사 품목 = 건별 실패 문구 = 가드 문구 · 쓴 행 0")]
    public async Task E7_R_DeliveryBatch_Rejected()
    {
        if (Skip("E7 R")) return;
        using var db = FreshDb();

        var results = await Batch(db).SaveBatchAsync(new List<CreateDeliveryRequest>
        {
            DeliveryReq(SameTenantLines),
            DeliveryReq(MixedLines),
        });

        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.Equal(Msg, r.ErrorMessage);
        });
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_deliveries"));
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_delivery_items"));
    }

    [Fact(DisplayName = "후1 E7 A 거래명세 일괄 · 같은 회사 품목 = 명세서 2 · 줄 값 그대로")]
    public async Task E7_A_DeliveryBatch_Saved()
    {
        if (Skip("E7 A")) return;
        using var db = FreshDb();

        var results = await Batch(db).SaveBatchAsync(new List<CreateDeliveryRequest>
        {
            DeliveryReq(SameTenantLines),
            DeliveryReq(new[] { (ItemA2, 5m, 1250m) }),
        });

        Assert.All(results, r => Assert.True(r.Success, r.ErrorMessage));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM sales_deliveries WHERE tenant_id='{TenantA}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE item_id='{ItemA1}' AND supply_amount=2200"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE item_id='{ItemA2}' AND supply_amount=3750"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE item_id='{ItemA2}' AND supply_amount=6250"));
    }

    [Fact(DisplayName = "후1 E7 이름 찾기 보존 · id 빈 줄 + 같은 회사 품명 = 통과 · 그 품목 id 로 저장")]
    public async Task E7_NameLookup_Preserved()
    {
        if (Skip("E7 이름 찾기")) return;
        using var db = FreshDb();
        var req = DeliveryReq(Array.Empty<(string, decimal, decimal)>());
        req.Items.Add(new CreateDeliveryItemRequest { ItemId = "  ", ItemName = NameA1, WarehouseId = WhA, Qty = 1m, UnitPrice = 990m, SupplyAmount = 990m, VatAmount = 99m });

        var results = await Batch(db).SaveBatchAsync(new List<CreateDeliveryRequest> { req });

        Assert.True(results.Single().Success, results.Single().ErrorMessage);
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE item_id='{ItemA1}' AND supply_amount=990"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  E8 발주 신규 · E9 매입명세서 신규 (EF · 가짜 작업 단위)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후1 E8 R 🔴 발주 신규(EF) · 다른 회사 품목 = 거절 · 문구 · 저장 호출 Never")]
    public async Task E8_R_PurchaseOrderCreate_Rejected()
    {
        if (Skip("E8 R")) return;
        using var db = FreshDb();
        var uow = Uow(db);

        await AssertGuardRejects(() => Purchase(db, uow.Object).CreateOrderAsync(PoReq(MixedLines)));

        uow.Verify(u => u.Repository<PurchaseOrder>(), Times.Never);
        uow.Verify(u => u.Repository<PurchaseOrderItem>(), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_orders"));
    }

    [Fact(DisplayName = "후1 E8 A 발주 신규(EF) · 같은 회사 품목 = 가드 문구 예외 없음")]
    public async Task E8_A_PurchaseOrderCreate_NotGuarded()
    {
        if (Skip("E8 A")) return;
        using var db = FreshDb();

        await AssertNotGuarded(() => Purchase(db, Uow(db).Object).CreateOrderAsync(PoReq(SameTenantLines)));
    }

    [Fact(DisplayName = "후1 E9 R 🔴 매입명세서 신규(EF) · 다른 회사 품목 = 거절 · 문구 · 저장 호출 Never")]
    public async Task E9_R_ReceiptCreate_Rejected()
    {
        if (Skip("E9 R")) return;
        using var db = FreshDb();
        var uow = Uow(db);

        await AssertGuardRejects(() => Purchase(db, uow.Object).CreateReceiptAsync(ReceiptReq(MixedLines)));

        uow.Verify(u => u.Repository<PurchaseReceipt>(), Times.Never);
        uow.Verify(u => u.Repository<PurchaseReceiptItem>(), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_receipts"));
    }

    [Fact(DisplayName = "후1 E9 A 매입명세서 신규(EF) · 같은 회사 품목 = 가드 문구 예외 없음")]
    public async Task E9_A_ReceiptCreate_NotGuarded()
    {
        if (Skip("E9 A")) return;
        using var db = FreshDb();

        await AssertNotGuarded(() => Purchase(db, Uow(db).Object).CreateReceiptAsync(ReceiptReq(SameTenantLines)));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  E10 · E11 매입 반품 (Dapper) · K3 (가) 매입명세서 → 반품 W3 (작업 단위 tx · 같은 연결)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후1 E10 R 🔴 매입 반품 신규 · 다른 회사 품목 = 거절 · 쓴 행 0")]
    public async Task E10_R_PurchaseReturnCreate_Rejected()
    {
        if (Skip("E10 R")) return;
        using var db = FreshDb();

        await AssertGuardRejects(() => Purchase(db).CreatePurchaseReturnAsync(PurchaseReturnReq(MixedLines, Orig), TenantA));

        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_returns"));
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_return_items"));
    }

    [Fact(DisplayName = "후1 E10 A 매입 반품 신규 · 같은 회사 품목 = 머리 1 · 줄 2 · 값 그대로")]
    public async Task E10_A_PurchaseReturnCreate_Saved()
    {
        if (Skip("E10 A")) return;
        using var db = FreshDb();

        var (returnId, _) = await Purchase(db).CreatePurchaseReturnAsync(PurchaseReturnReq(SameTenantLines, Orig), TenantA);

        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_returns WHERE return_id='{returnId}' AND tenant_id='{TenantA}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND item_id='{ItemA1}' AND supply_amount=2200"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND supply_amount=3750"));
    }

    [Fact(DisplayName = "후1 E11 R 🔴 매입 반품 수정(트랜잭션 없음) · 다른 회사 품목 = 거절 · 머리 UPDATE 안 일어남 · 기존 줄 그대로")]
    public async Task E11_R_PurchaseReturnUpdate_Rejected()
    {
        if (Skip("E11 R")) return;
        using var db = FreshDb();
        var svc = Purchase(db);
        var (returnId, _) = await svc.CreatePurchaseReturnAsync(PurchaseReturnReq(SameTenantLines, Orig), TenantA);

        await AssertGuardRejects(() => svc.UpdatePurchaseReturnAsync(returnId, PurchaseReturnUpdateReq(MixedLines, Changed), TenantA));

        Assert.Equal(Orig, Str(db, $"SELECT memo FROM purchase_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_returns WHERE return_id='{returnId}' AND total_amount=5950"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}'"));
        Assert.Equal(0, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE item_id='{ItemB1}'"));
    }

    [Fact(DisplayName = "후1 E11 A 매입 반품 수정 · 같은 회사 품목 = 머리 · 줄 바뀜")]
    public async Task E11_A_PurchaseReturnUpdate_Saved()
    {
        if (Skip("E11 A")) return;
        using var db = FreshDb();
        var svc = Purchase(db);
        var (returnId, _) = await svc.CreatePurchaseReturnAsync(PurchaseReturnReq(SameTenantLines, Orig), TenantA);

        await svc.UpdatePurchaseReturnAsync(returnId, PurchaseReturnUpdateReq(new[] { (ItemA2, 4m, 1250m) }, Changed), TenantA);

        Assert.Equal(Changed, Str(db, $"SELECT memo FROM purchase_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND supply_amount=5000"));
    }

    [Fact(DisplayName = "후1 K3 매입명세서→반품 R 🔴 원본 매입 줄에 다른 회사 품목 = 거절 · 반품 머리 · 줄 0(롤백)")]
    public async Task K3_R_ReceiptToReturn_Rejected()
    {
        if (Skip("K3 매입→반품 R")) return;
        using var db = FreshDb();
        SeedConfirmedReceipt(db, "VSIT-RC-1", MixedLines);

        await AssertGuardRejects(() => Purchase(db).ConvertReceiptToReturnAsync("VSIT-RC-1", TenantA));

        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_returns"));
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_return_items"));
    }

    [Fact(DisplayName = "후1 K3 매입명세서→반품 A 같은 회사 품목 = 반품 머리 1 · 줄 2 · 값 그대로")]
    public async Task K3_A_ReceiptToReturn_Saved()
    {
        if (Skip("K3 매입→반품 A")) return;
        using var db = FreshDb();
        SeedConfirmedReceipt(db, "VSIT-RC-1", SameTenantLines);

        var (returnId, _) = await Purchase(db).ConvertReceiptToReturnAsync("VSIT-RC-1", TenantA);

        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_returns WHERE return_id='{returnId}' AND receipt_id='VSIT-RC-1'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND item_id='{ItemA1}' AND supply_amount=2200"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND supply_amount=3750"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  E12 · E13 판매 반품 (Dapper)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후1 E12 R 🔴 판매 반품 신규 · 다른 회사 품목 = 거절 · 쓴 행 0")]
    public async Task E12_R_SalesReturnCreate_Rejected()
    {
        if (Skip("E12 R")) return;
        using var db = FreshDb();

        await AssertGuardRejects(() => Sales(db).CreateSalesReturnAsync(SalesReturnReq(MixedLines, Orig), TenantA));

        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_returns"));
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_return_items"));
    }

    [Fact(DisplayName = "후1 E12 A 판매 반품 신규 · 같은 회사 품목 = 머리 1 · 줄 2 · 값 그대로")]
    public async Task E12_A_SalesReturnCreate_Saved()
    {
        if (Skip("E12 A")) return;
        using var db = FreshDb();

        var (returnId, _) = await Sales(db).CreateSalesReturnAsync(SalesReturnReq(SameTenantLines, Orig), TenantA);

        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_returns WHERE return_id='{returnId}' AND tenant_id='{TenantA}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}' AND item_id='{ItemA1}' AND supply_amount=2200"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND supply_amount=3750"));
    }

    [Fact(DisplayName = "후1 E13 R 🔴 판매 반품 수정(트랜잭션 없음) · 다른 회사 품목 = 거절 · 머리 UPDATE 안 일어남 · 기존 줄 그대로")]
    public async Task E13_R_SalesReturnUpdate_Rejected()
    {
        if (Skip("E13 R")) return;
        using var db = FreshDb();
        var svc = Sales(db);
        var (returnId, _) = await svc.CreateSalesReturnAsync(SalesReturnReq(SameTenantLines, Orig), TenantA);

        await AssertGuardRejects(() => svc.UpdateSalesReturnAsync(returnId, SalesReturnUpdateReq(MixedLines, Changed), TenantA));

        Assert.Equal(Orig, Str(db, $"SELECT memo FROM sales_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_returns WHERE return_id='{returnId}' AND total_amount=5950"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}'"));
        Assert.Equal(0, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE item_id='{ItemB1}'"));
    }

    [Fact(DisplayName = "후1 E13 A 판매 반품 수정 · 같은 회사 품목 = 머리 · 줄 바뀜")]
    public async Task E13_A_SalesReturnUpdate_Saved()
    {
        if (Skip("E13 A")) return;
        using var db = FreshDb();
        var svc = Sales(db);
        var (returnId, _) = await svc.CreateSalesReturnAsync(SalesReturnReq(SameTenantLines, Orig), TenantA);

        await svc.UpdateSalesReturnAsync(returnId, SalesReturnUpdateReq(new[] { (ItemA2, 4m, 1250m) }, Changed), TenantA);

        Assert.Equal(Changed, Str(db, $"SELECT memo FROM sales_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND supply_amount=5000"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  판정 도우미
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>예외형 + <b>문구까지</b> 대조([3-V] P2-2 — 다른 까닭의 InvalidOperationException 과 가른다).</summary>
    private static async Task AssertGuardRejects(Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(Msg, ex.Message);
    }

    /// <summary>같은 회사 품목이면 가드 문구 예외가 나지 않는다(다음 단계에서 다른 까닭으로 멈추는 것은 허용 · 그 문구가 가드 문구가 아님을 단언).</summary>
    private static async Task AssertNotGuarded(Func<Task> act)
    {
        var ex = await Record.ExceptionAsync(act);
        Assert.False(ex is InvalidOperationException && ex.Message == Msg,
            "🔴 같은 회사 품목인데 가드가 막았다 — 정상 길 막힘(§11 · 즉석 완화 금지 · PM 보고).");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  서비스 — 모두 시험 연결 하나만 쓴다([3-V] P2-4)
    // ════════════════════════════════════════════════════════════════════════

    private static ICurrentTenant Tenant()
    {
        var t = new Mock<ICurrentTenant>();
        t.SetupGet(x => x.TenantId).Returns(TenantA);
        t.SetupGet(x => x.UserId).Returns(User);
        return t.Object;
    }

    /// <summary>
    /// 가짜 작업 단위 — <c>GetDbConnection</c> · <c>BeginTransactionAsync</c> 는 <b>같은 시험 연결</b>(TEMPORARY 표는 연결마다 따로).
    /// <c>Repository&lt;T&gt;()</c> 는 Moq 기본 가짜(<see cref="DefaultValue.Mock"/>)라 EF 입구의 저장 호출 여부를 잴 수 있다.
    /// </summary>
    private static Mock<IUnitOfWork> Uow(MySqlConnection db)
    {
        var uow = new Mock<IUnitOfWork> { DefaultValue = DefaultValue.Mock };
        uow.Setup(u => u.GetDbConnection()).Returns(db);
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync(() => new ConnTx(db.BeginTransaction()));
        return uow;
    }

    private static QuotationService Quotation(MySqlConnection db) => new(db, Tenant());

    private static SalesService Sales(MySqlConnection db, IUnitOfWork? uow = null) =>
        new(uow ?? Uow(db).Object, Tenant(), db, null!, Mock.Of<IAuditService>(), null!);

    private static PurchaseService Purchase(MySqlConnection db, IUnitOfWork? uow = null) =>
        new(uow ?? Uow(db).Object, Tenant(), db, Mock.Of<IAuditService>());

    private static DeliveryBatchService Batch(MySqlConnection db) => new(db, Tenant());

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

    // ════════════════════════════════════════════════════════════════════════
    //  요청 꼴
    // ════════════════════════════════════════════════════════════════════════

    private static List<QuotationItemDto> QuoteLines((string Id, decimal Qty, decimal Price)[] lines) =>
        lines.Select((l, i) => new QuotationItemDto
        {
            ItemId = l.Id, Qty = l.Qty, UnitPrice = l.Price, Amount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m, SortOrder = i + 1
        }).ToList();

    private static CreateQuotationRequest QuoteReq((string Id, decimal Qty, decimal Price)[] lines, string memo) => new()
    {
        PartnerId = PartnerA, QuoteDate = new DateTime(2026, 11, 27), Memo = memo, Items = QuoteLines(lines)
    };

    private static UpdateQuotationRequest QuoteUpdateReq((string Id, decimal Qty, decimal Price)[] lines, string memo) => new()
    {
        PartnerId = PartnerA, QuoteDate = new DateTime(2026, 11, 27), Status = "draft", Memo = memo, Items = QuoteLines(lines)
    };

    private static CreateSalesOrderRequest SalesOrderReq((string Id, decimal Qty, decimal Price)[] lines) => new()
    {
        PartnerId = PartnerA, OrderDate = new DateTime(2026, 11, 27), Memo = Orig,
        Items = lines.Select(l => new CreateSalesOrderItemRequest
        {
            ItemId = l.Id, OrderedQty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m
        }).ToList()
    };

    private static UpdateSalesOrderRequest SalesOrderUpdateReq((string Id, decimal Qty, decimal Price)[] lines) => new()
    {
        PartnerId = PartnerA, OrderDate = new DateTime(2026, 11, 27), Memo = Changed,
        Items = lines.Select(l => new UpdateSalesOrderItemRequest
        {
            ItemId = l.Id, OrderedQty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m
        }).ToList()
    };

    private static CreateDeliveryRequest DeliveryReq((string Id, decimal Qty, decimal Price)[] lines) => new()
    {
        PartnerId = PartnerA, DeliveryDate = new DateTime(2026, 11, 27), Memo = Orig,
        Items = lines.Select(l => new CreateDeliveryItemRequest
        {
            ItemId = l.Id, WarehouseId = WhA, Qty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m
        }).ToList()
    };

    private static UpdateDeliveryDto DeliveryUpdateDto((string Id, decimal Qty, decimal Price)[] lines) => new()
    {
        OrderDate = new DateTime(2026, 11, 27), PartnerId = PartnerA, Memo = Changed,
        Items = lines.Select(l => new DeliveryItemDto
        {
            ItemId = l.Id, Qty = l.Qty, UnitPrice = l.Price, Amount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m, WarehouseId = WhA
        }).ToList()
    };

    private static CreatePurchaseOrderRequest PoReq((string Id, decimal Qty, decimal Price)[] lines) => new()
    {
        PartnerId = PartnerA, PoDate = new DateTime(2026, 11, 27), Memo = Orig,
        Items = lines.Select(l => new CreatePurchaseOrderItemRequest
        {
            ItemId = l.Id, OrderedQty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m, WarehouseId = WhA
        }).ToList()
    };

    private static CreateReceiptRequest ReceiptReq((string Id, decimal Qty, decimal Price)[] lines) => new()
    {
        PartnerId = PartnerA, ReceiptDate = new DateTime(2026, 11, 27), Memo = Orig,
        Items = lines.Select(l => new CreateReceiptItemRequest
        {
            ItemId = l.Id, WarehouseId = WhA, Qty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m
        }).ToList()
    };

    private static List<CreatePurchaseReturnItemRequest> PurchaseReturnLines((string Id, decimal Qty, decimal Price)[] lines) =>
        lines.Select(l => new CreatePurchaseReturnItemRequest
        {
            ItemId = l.Id, WarehouseId = WhA, Qty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m
        }).ToList();

    private static CreatePurchaseReturnRequest PurchaseReturnReq((string Id, decimal Qty, decimal Price)[] lines, string memo) => new()
    {
        PartnerId = PartnerA, ReturnDate = new DateTime(2026, 11, 27), Memo = memo, Items = PurchaseReturnLines(lines)
    };

    private static UpdatePurchaseReturnRequest PurchaseReturnUpdateReq((string Id, decimal Qty, decimal Price)[] lines, string memo) => new()
    {
        PartnerId = PartnerA, ReturnDate = new DateTime(2026, 11, 27), Memo = memo, Items = PurchaseReturnLines(lines)
    };

    private static List<CreateSalesReturnItemRequest> SalesReturnLines((string Id, decimal Qty, decimal Price)[] lines) =>
        lines.Select(l => new CreateSalesReturnItemRequest
        {
            ItemId = l.Id, WarehouseId = WhA, Qty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m
        }).ToList();

    private static CreateSalesReturnRequest SalesReturnReq((string Id, decimal Qty, decimal Price)[] lines, string memo) => new()
    {
        PartnerId = PartnerA, ReturnDate = new DateTime(2026, 11, 27), Memo = memo, Items = SalesReturnLines(lines)
    };

    private static UpdateSalesReturnRequest SalesReturnUpdateReq((string Id, decimal Qty, decimal Price)[] lines, string memo) => new()
    {
        PartnerId = PartnerA, ReturnDate = new DateTime(2026, 11, 27), Memo = memo, Items = SalesReturnLines(lines)
    };

    // ════════════════════════════════════════════════════════════════════════
    //  표 · 씨앗
    // ════════════════════════════════════════════════════════════════════════

    private static bool Skip(string fact)
    {
        if (ServerAvailable()) return false;
        DbGateEnvironment.SkipOrFail($"VoucherSaveItemTenantGate {fact}");
        return true;
    }

    /// <summary>
    /// 시험 DB 의 기본 표 전부를 같은 이름 <c>TEMPORARY</c> 표로 가린다(<c>LIKE</c> = 칸 · 형 · 기본값 그대로 · 외래 키는 안 따라옴).
    /// 연결 풀을 끄므로 연결을 닫으면 임시 표도 사라진다 — 실표는 가리기만 하고 건드리지 않는다(헌법 #39).
    /// 씨앗 = 품목 A1 · A2(회사 A) · B1(회사 B) · 회사 A 기본 창고(MAIN).
    /// </summary>
    private static MySqlConnection FreshDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();

        var allTables = new List<string>();
        using (var cmd = new MySqlCommand(
            // FULLTEXT 색인 표는 임시 표로 못 만든다(MariaDB) — 이 흐름들이 안 쓰는 표라 뺀다(선례 TaxInvoiceMigratedLockGateTests).
            $"""
            SELECT t.TABLE_NAME FROM information_schema.TABLES t
             WHERE t.TABLE_SCHEMA='{TestDb}' AND t.TABLE_TYPE='BASE TABLE'
               AND NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS s
                                WHERE s.TABLE_SCHEMA=t.TABLE_SCHEMA AND s.TABLE_NAME=t.TABLE_NAME AND s.INDEX_TYPE='FULLTEXT')
            """, db))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) allTables.Add(r.GetString(0));
        }
        // 이 시험이 기대는 표가 빠졌으면(FULLTEXT 등) 실표에 닿을 수 있다 — 먼저 멈춘다.
        foreach (var need in new[] { "items", "quotations", "quotation_items", "sales_orders", "sales_order_items",
                     "sales_deliveries", "sales_delivery_items", "purchase_orders", "purchase_receipts", "purchase_receipt_items",
                     "purchase_returns", "purchase_return_items", "sales_returns", "sales_return_items", "warehouses" })
        {
            Assert.Contains(need, allTables);
        }
        foreach (var t in allTables)
        {
            // 같은 이름으로 바로 LIKE 하면 "Not unique table/alias" — 다른 이름으로 만든 뒤 임시 표 이름을 바꾼다.
            Exec(db, $"DROP TEMPORARY TABLE IF EXISTS vsittmp_{t}");
            Exec(db, $"CREATE TEMPORARY TABLE vsittmp_{t} LIKE `{TestDb}`.`{t}`");
            Exec(db, $"ALTER TABLE vsittmp_{t} RENAME TO `{t}`");
        }

        Exec(db, $"""
            INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit, is_active, created_at, updated_at) VALUES
              ('{ItemA1}', '{TenantA}', 'VSIT-A1', '{NameA1}', 'goods', 'EA', 1, NOW(6), NOW(6)),
              ('{ItemA2}', '{TenantA}', 'VSIT-A2', 'VSITNAMEATWO', 'goods', 'EA', 1, NOW(6), NOW(6)),
              ('{ItemB1}', '{TenantB}', 'VSIT-B1', 'VSITSECRETB', 'goods', 'EA', 1, NOW(6), NOW(6))
            """);
        Exec(db, $"""
            INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at)
            VALUES ('{WhA}', '{TenantA}', 'MAIN', 'VSITWH', 'normal', 1, NOW(6), NOW(6))
            """);
        return db;
    }

    private static void SeedDraftOrder(MySqlConnection db, string orderId)
    {
        Exec(db, $"""
            INSERT INTO sales_orders (order_id, tenant_id, order_no, partner_id, order_date, status,
              total_amount, vat_amount, memo, created_at, updated_at)
            VALUES ('{orderId}', '{TenantA}', 'SO-{orderId}', '{PartnerA}', '2026-11-27', 'draft',
              1000, 100, '{Orig}', NOW(6), NOW(6))
            """);
        Exec(db, $"""
            INSERT INTO sales_order_items (order_item_id, order_id, tenant_id, item_id, ordered_qty, delivered_qty,
              unit_price, supply_amount, vat_amount, item_status)
            VALUES (UUID(), '{orderId}', '{TenantA}', '{ItemA1}', 1, 0, 1000, 1000, 100, 'pending')
            """);
    }

    private static void SeedDraftDelivery(MySqlConnection db, string deliveryId)
    {
        Exec(db, $"""
            INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, delivery_date, source_type, status,
              total_amount, vat_amount, memo, created_at, updated_at)
            VALUES ('{deliveryId}', '{TenantA}', 'DN-{deliveryId}', '{PartnerA}', '2026-11-27', 'direct', 'draft',
              1000, 100, '{Orig}', NOW(6), NOW(6))
            """);
        Exec(db, $"""
            INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, item_id, warehouse_id, qty,
              unit_price, supply_amount, vat_amount)
            VALUES (UUID(), '{deliveryId}', '{TenantA}', '{ItemA1}', '{WhA}', 1, 1000, 1000, 100)
            """);
    }

    private static void SeedConfirmedReceipt(MySqlConnection db, string receiptId, (string Id, decimal Qty, decimal Price)[] lines)
    {
        var supply = lines.Sum(l => l.Qty * l.Price);
        Exec(db, $"""
            INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, source_type, status,
              total_amount, vat_amount, created_at)
            VALUES ('{receiptId}', '{TenantA}', 'RC-{receiptId}', '{PartnerA}', '2026-11-27', 'direct', 'confirmed',
              {Sql(supply)}, {Sql(supply / 10m)}, NOW(6))
            """);
        foreach (var l in lines)
        {
            Exec(db, $"""
                INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, item_id, warehouse_id, qty,
                  unit_price, supply_amount, vat_amount)
                VALUES (UUID(), '{receiptId}', '{TenantA}', '{l.Id}', '{WhA}', {Sql(l.Qty)}, {Sql(l.Price)},
                  {Sql(l.Qty * l.Price)}, {Sql(l.Qty * l.Price / 10m)})
                """);
        }
    }

    private static string Sql(decimal v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static int Count(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string Str(MySqlConnection db, string sql) =>
        StrOrNull(db, sql) ?? throw new InvalidOperationException("값 없음: " + sql);

    private static string? StrOrNull(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToString(v);
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
        // 연결 문자열 글자 대신 빌더(비밀 탐지 · 인계44 §6) — 값 · 옵션은 그대로.
        return new MySqlConnectionStringBuilder
        {
            Server = host, Port = uint.Parse(port), Database = TestDb, UserID = user, Password = pass,
            AllowUserVariables = true, GuidFormat = MySqlGuidFormat.None, Pooling = false, ConnectionTimeout = 5,
        }.ConnectionString;
    }
}
