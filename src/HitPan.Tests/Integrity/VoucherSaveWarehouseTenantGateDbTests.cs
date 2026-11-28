using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
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
/// 🔴 W-X-12 후9 갈래 2(작업지시서 20261129작 · 설계 §4-2 · §6-3 · CTO 전결 K4 · PM 결재 K6) — <b>전표 저장 입구 9곳은
/// 이 회사 · 활성 창고만 저장하고, 발주→매입 전환만 회사만 본다.</b>
/// </summary>
/// <remarks>
/// 🔴 <b>R(🔴) 거절</b> — 줄 하나에 다른 회사 창고(WB) → <see cref="InvalidOperationException"/> · <b>문구까지</b> 대조 · <b>쓴 행 0</b>
///   (신규 = 머리 · 줄 수 그대로 / 수정 = 머리 값 · 기존 줄 그대로 — W6b · W9b 는 트랜잭션 없는 머리 UPDATE 가 안 일어남을 잰다).
/// 🔴 <b>A 같은 회사 활성 창고</b> — Dapper 입구는 실제 저장 행을 · EF 입구(W4 · W5 · W7)는 가짜 작업 단위라 거절 때 <b>저장 호출 Never</b>,
///   정상일 때 <b>가드 문구 예외 없음</b>을 잰다(후1 E3 · E5 · E8 · E9 꼴).
/// 🔴 사실 24 = 설계 §6-3 표 23(W4 2 · W5 3 · W5c 2 · W6a/b 4 · W7 2 · W8 3 · W9a/b 4 · W10 2 · 확정 길 1) + PM 결재 K6 · Q8 1
///   (공개 <c>CreateReceiptAsync</c> 에 쉬게 한 창고 WA0 = 거절 — 공개 쪽에 회사만 길이 잘못 이어지면 빨강).
/// 🔴 <b>전결 2(PR #1058 고침 1 · 작업지시서 §15 · 설계 §6-3)</b> — ⬛ 위 요약의 「9곳 = 회사 · 활성」 은 낡음: <b>반품 4입구(W6a · W6b · W9a · W9b) = 회사만</b>
///   (반품 화면엔 줄 창고 칸이 없다 · #20). 사실 24 → <b>32</b>(+4 쉬게 한 내 창고 반품 = 저장됨 · +4 공백만 창고 id 반품 = 거절 · 쓴 행 0).
///   WA0 저장 4 = 「회사만 → 활성」 되돌림을 동작으로 잡고(R2) · 공백만 4 = 변이 B ⓓ(공백만 거절 줄 뺌)를 잡는다(R6 · R7).
/// 🔴 <b>W5c 전환</b> — <c>ConvertOrderToReceiptAsync</c> 는 발주 머리 · 줄을 EF 저장소로 읽으므로 발주 씨앗은 가짜 저장소에 심는다
///   (창고 검사 · 번호 · 기존 매입 조회는 TEMPORARY 표의 실제 DB 로 간다).
/// 🔴 <b>표 = 시험 연결 하나의 TEMPORARY 표만</b> — 시험 DB 의 기본 표 전부를 같은 이름 TEMPORARY 표로 <c>CREATE TEMPORARY TABLE … LIKE</c> 가린다
///   (선례 <c>VoucherSaveItemTenantGateDbTests.FreshDb</c>). 모든 서비스 · 가짜 작업 단위가 그 연결 하나만 쓴다 → 실표 접촉 0(헌법 #39) · 직렬(헌법 #16).
/// 🔴 제품 <b>공개</b> 함수로만 부른다(반사 0). 이 PC 개발 DB(3306)는 실물이다 — 로컬에서는 돌리지 않는다(CI DB 게이트 잡이 문다).
/// </remarks>
public sealed class VoucherSaveWarehouseTenantGateDbTests
{
    private const string TenantA = "GATE-VSWT-TENANT-A";
    private const string TenantB = "GATE-VSWT-TENANT-B";
    private const string User = "gate-vswt-user";
    private const string PartnerA = "GATE-VSWT-PARTNER-A";
    private const string ItemA1 = "GATE-VSWT-ITEM-A1";
    private const string ItemA2 = "GATE-VSWT-ITEM-A2";
    private const string WhA = "GATE-VSWT-WH-A";
    private const string WhA2 = "GATE-VSWT-WH-A2";
    private const string WhA0 = "GATE-VSWT-WH-A0";
    private const string WhB = "GATE-VSWT-WH-B";
    private const string Orig = "VSWT-ORIG";
    private const string Changed = "VSWT-CHANGED";

    private static string Msg => WarehouseTenantGuard.NotUsableWarehouseMessage;

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    // 줄마다 금액을 다르게 둔다(줄 셈 · 값 대조가 섞이지 않게). 품목은 모두 회사 A — 이 시험은 창고만 가른다.
    private static readonly (string Id, decimal Qty, decimal Price, string Wh)[] SameLines =
        { (ItemA1, 2m, 1100m, WhA), (ItemA2, 3m, 1250m, WhA) };
    private static readonly (string Id, decimal Qty, decimal Price, string Wh)[] MixedLines =
        { (ItemA1, 2m, 1100m, WhA), (ItemA2, 1m, 1370m, WhB) };
    private static readonly (string Id, decimal Qty, decimal Price, string Wh)[] InactiveLines =
        { (ItemA1, 2m, 1100m, WhA), (ItemA2, 1m, 1370m, WhA0) };

    // 🔴 전결 2 — 공백만 창고 id(공용 가드는 빼고 지나감 · 반품 4입구만 따로 거절). 한 사실에 둘 다 넣는다.
    private static readonly string[] Blanks = { "   ", "\t" };
    private static (string Id, decimal Qty, decimal Price, string Wh)[] BlankLines(string blank) =>
        new[] { (ItemA1, 2m, 1100m, WhA), (ItemA2, 1m, 1370m, blank) };

    // ════════════════════════════════════════════════════════════════════════
    //  W4 발주 신규 (EF)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후9 W4 R 🔴 발주 신규(EF) · 줄에 다른 회사 창고 = 거절 · 문구 · 저장 호출 Never")]
    public async Task W4_R_PurchaseOrderCreate_Rejected()
    {
        if (Skip("W4 R")) return;
        using var db = FreshDb();
        var uow = Uow(db);

        await AssertGuardRejects(() => Purchase(db, uow.Object).CreateOrderAsync(PoReq(MixedLines)));

        uow.Verify(u => u.Repository<PurchaseOrder>(), Times.Never);
        uow.Verify(u => u.Repository<PurchaseOrderItem>(), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_orders"));
    }

    [Fact(DisplayName = "후9 W4 A 발주 신규(EF) · 같은 회사 활성 창고 = 가드 문구 예외 없음")]
    public async Task W4_A_PurchaseOrderCreate_NotGuarded()
    {
        if (Skip("W4 A")) return;
        using var db = FreshDb();

        await AssertNotGuarded(() => Purchase(db, Uow(db).Object).CreateOrderAsync(PoReq(SameLines)));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  W5 매입명세서 신규 (EF · 공개 입구 = 회사 + 활성)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후9 W5 R 🔴 매입명세서 신규(EF) · 줄에 다른 회사 창고 = 거절 · 문구 · 저장 호출 Never")]
    public async Task W5_R_ReceiptCreate_Rejected()
    {
        if (Skip("W5 R")) return;
        using var db = FreshDb();
        var uow = Uow(db);

        await AssertGuardRejects(() => Purchase(db, uow.Object).CreateReceiptAsync(ReceiptReq(MixedLines)));

        VerifyReceiptNeverSaved(uow);
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_receipts"));
    }

    [Fact(DisplayName = "후9 W5 A 매입명세서 신규(EF) · 같은 회사 활성 창고 = 가드 문구 예외 없음")]
    public async Task W5_A_ReceiptCreate_NotGuarded()
    {
        if (Skip("W5 A")) return;
        using var db = FreshDb();

        await AssertNotGuarded(() => Purchase(db, Uow(db).Object).CreateReceiptAsync(ReceiptReq(SameLines)));
    }

    [Fact(DisplayName = "후9 W5 빈 창고 줄 = 통과(서버 기본창고 채움 길 보존) · 가드 문구 예외 없음")]
    public async Task W5_BlankWarehouse_NotGuarded()
    {
        if (Skip("W5 빈 창고")) return;
        using var db = FreshDb();

        await AssertNotGuarded(() => Purchase(db, Uow(db).Object).CreateReceiptAsync(
            ReceiptReq(new[] { (ItemA1, 2m, 1100m, ""), (ItemA2, 3m, 1250m, WhA) })));
    }

    [Fact(DisplayName = "후9 W5 Q8 🔴 공개 매입명세서 신규 · 쉬게 한 내 창고(WA0) = 거절 · 저장 호출 Never(공개 입구 = 회사 + 활성)")]
    public async Task W5_Q8_PublicReceiptCreate_InactiveWarehouse_Rejected()
    {
        if (Skip("W5 Q8")) return;
        using var db = FreshDb();
        var uow = Uow(db);

        await AssertGuardRejects(() => Purchase(db, uow.Object).CreateReceiptAsync(ReceiptReq(InactiveLines)));

        VerifyReceiptNeverSaved(uow);
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_receipts"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  W5c 발주 → 매입 전환 (EF · 회사만 — CTO 전결 K4 (나))
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후9 W5c R 🔴 발주→매입 전환 · 발주 줄에 다른 회사 창고(WB) = 거절 · 가드 문구 · 입고 저장 호출 Never")]
    public async Task W5c_R_ConvertOrder_OtherTenantWarehouse_Rejected()
    {
        if (Skip("W5c R")) return;
        using var db = FreshDb();
        var uow = Uow(db);
        SeedPurchaseOrder(uow, "VSWT-PO-1", WhB);

        await AssertGuardRejects(() => Purchase(db, uow.Object).ConvertOrderToReceiptAsync("VSWT-PO-1", TenantA));

        VerifyReceiptNeverSaved(uow);
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_receipts"));
    }

    [Fact(DisplayName = "후9 W5c A 발주→매입 전환 · 발주 줄에 쉬게 한 내 창고(WA0) = 성공 · 입고 저장 호출 됨(매입 흐름 안 끊김 · #20)")]
    public async Task W5c_A_ConvertOrder_InactiveOwnWarehouse_Succeeds()
    {
        if (Skip("W5c A")) return;
        using var db = FreshDb();
        var uow = Uow(db);
        SeedPurchaseOrder(uow, "VSWT-PO-1", WhA0);
        var receiptItemRepo = new Mock<IRepository<PurchaseReceiptItem>>();
        uow.Setup(u => u.Repository<PurchaseReceiptItem>()).Returns(receiptItemRepo.Object);

        var ex = await Record.ExceptionAsync(() => Purchase(db, uow.Object).ConvertOrderToReceiptAsync("VSWT-PO-1", TenantA));

        Assert.Null(ex);
        receiptItemRepo.Verify(r => r.AddAsync(It.Is<PurchaseReceiptItem>(i => i.WarehouseId == WhA0 && i.ItemId == ItemA1)), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  W6a · W6b 매입 반품 (Dapper)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후9 W6a R 🔴 매입 반품 신규 · 줄에 다른 회사 창고 = 거절 · 쓴 행 0")]
    public async Task W6a_R_PurchaseReturnCreate_Rejected()
    {
        if (Skip("W6a R")) return;
        using var db = FreshDb();

        await AssertGuardRejects(() => Purchase(db).CreatePurchaseReturnAsync(PurchaseReturnReq(MixedLines, Orig), TenantA));

        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_returns"));
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_return_items"));
    }

    [Fact(DisplayName = "후9 W6a A 매입 반품 신규 · 같은 회사 활성 창고 = 머리 1 · 줄 2 · 창고 그대로")]
    public async Task W6a_A_PurchaseReturnCreate_Saved()
    {
        if (Skip("W6a A")) return;
        using var db = FreshDb();

        var (returnId, _) = await Purchase(db).CreatePurchaseReturnAsync(PurchaseReturnReq(SameLines, Orig), TenantA);

        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_returns WHERE return_id='{returnId}' AND tenant_id='{TenantA}'"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND warehouse_id='{WhA}'"));
    }

    [Fact(DisplayName = "후9 W6b R 🔴 매입 반품 수정(트랜잭션 없음) · 줄에 다른 회사 창고 = 거절 · 머리 UPDATE 안 일어남 · 기존 줄 그대로")]
    public async Task W6b_R_PurchaseReturnUpdate_Rejected()
    {
        if (Skip("W6b R")) return;
        using var db = FreshDb();
        var svc = Purchase(db);
        var (returnId, _) = await svc.CreatePurchaseReturnAsync(PurchaseReturnReq(SameLines, Orig), TenantA);

        await AssertGuardRejects(() => svc.UpdatePurchaseReturnAsync(returnId, PurchaseReturnUpdateReq(MixedLines, Changed), TenantA));

        Assert.Equal(Orig, Str(db, $"SELECT memo FROM purchase_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_returns WHERE return_id='{returnId}' AND total_amount=5950"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND warehouse_id='{WhA}'"));
        Assert.Equal(0, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE warehouse_id='{WhB}'"));
    }

    [Fact(DisplayName = "후9 W6b A 매입 반품 수정 · 같은 회사 활성 창고 = 머리 · 줄 바뀜")]
    public async Task W6b_A_PurchaseReturnUpdate_Saved()
    {
        if (Skip("W6b A")) return;
        using var db = FreshDb();
        var svc = Purchase(db);
        var (returnId, _) = await svc.CreatePurchaseReturnAsync(PurchaseReturnReq(SameLines, Orig), TenantA);

        await svc.UpdatePurchaseReturnAsync(returnId, PurchaseReturnUpdateReq(new[] { (ItemA2, 4m, 1250m, WhA2) }, Changed), TenantA);

        Assert.Equal(Changed, Str(db, $"SELECT memo FROM purchase_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND warehouse_id='{WhA2}' AND supply_amount=5000"));
    }

    [Fact(DisplayName = "후9 W6a WA0 🔴 매입 반품 신규 · 쉬게 한 내 창고 줄 = 저장됨(전결 2 회사만 · #20) · 줄 창고 WA0 그대로")]
    public async Task W6a_WA0_PurchaseReturnCreate_InactiveOwnWarehouse_Saved()
    {
        if (Skip("W6a WA0")) return;
        using var db = FreshDb();

        var (returnId, _) = await Purchase(db).CreatePurchaseReturnAsync(PurchaseReturnReq(InactiveLines, Orig), TenantA);

        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_returns WHERE return_id='{returnId}' AND tenant_id='{TenantA}'"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND warehouse_id='{WhA0}'"));
    }

    [Fact(DisplayName = "후9 W6b WA0 🔴 매입 반품 수정 · 저장 뒤 쉬게 한 내 창고 줄의 수량만 고침 = 저장됨(전결 2 회사만 · #20)")]
    public async Task W6b_WA0_PurchaseReturnUpdate_InactiveOwnWarehouse_Saved()
    {
        if (Skip("W6b WA0")) return;
        using var db = FreshDb();
        var svc = Purchase(db);
        // [반품전환] 초안 흉내 — 활성 WA2 로 저장한 뒤 WA2 를 쉬게 한다(신규 입구 W6a 에 기대지 않고 W6b 만 잰다).
        var (returnId, _) = await svc.CreatePurchaseReturnAsync(PurchaseReturnReq(new[] { (ItemA2, 2m, 1250m, WhA2) }, Orig), TenantA);
        Exec(db, $"UPDATE warehouses SET is_active = 0 WHERE warehouse_id = '{WhA2}'");

        await svc.UpdatePurchaseReturnAsync(returnId, PurchaseReturnUpdateReq(new[] { (ItemA2, 4m, 1250m, WhA2) }, Changed), TenantA);

        Assert.Equal(Changed, Str(db, $"SELECT memo FROM purchase_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND warehouse_id='{WhA2}' AND supply_amount=5000"));
    }

    [Fact(DisplayName = "후9 W6a 공백 🔴 매입 반품 신규 · 줄 창고 id 공백만(\"   \" · \"\\t\") = 거절 · 문구 · 쓴 행 0(전결 2)")]
    public async Task W6a_Whitespace_PurchaseReturnCreate_Rejected()
    {
        if (Skip("W6a 공백")) return;
        using var db = FreshDb();

        foreach (var blank in Blanks)
        {
            await AssertGuardRejects(() => Purchase(db).CreatePurchaseReturnAsync(PurchaseReturnReq(BlankLines(blank), Orig), TenantA));

            Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_returns"));
            Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM purchase_return_items"));
        }
    }

    [Fact(DisplayName = "후9 W6b 공백 🔴 매입 반품 수정 · 줄 창고 id 공백만(\"   \" · \"\\t\") = 거절 · 머리 UPDATE 안 일어남 · 기존 줄 그대로(전결 2)")]
    public async Task W6b_Whitespace_PurchaseReturnUpdate_Rejected()
    {
        if (Skip("W6b 공백")) return;
        using var db = FreshDb();
        var svc = Purchase(db);
        var (returnId, _) = await svc.CreatePurchaseReturnAsync(PurchaseReturnReq(SameLines, Orig), TenantA);

        foreach (var blank in Blanks)
        {
            await AssertGuardRejects(() => svc.UpdatePurchaseReturnAsync(returnId, PurchaseReturnUpdateReq(BlankLines(blank), Changed), TenantA));

            Assert.Equal(Orig, Str(db, $"SELECT memo FROM purchase_returns WHERE return_id='{returnId}'"));
            Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM purchase_returns WHERE return_id='{returnId}' AND total_amount=5950"));
            Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM purchase_return_items WHERE return_id='{returnId}' AND warehouse_id='{WhA}'"));
            Assert.Equal(2, Count(db, "SELECT COUNT(*) FROM purchase_return_items"));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  W7 거래명세 신규 (EF)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후9 W7 R 🔴 거래명세 신규(EF) · 줄에 다른 회사 창고 = 거절 · 문구 · 저장 호출 Never")]
    public async Task W7_R_DeliveryCreate_Rejected()
    {
        if (Skip("W7 R")) return;
        using var db = FreshDb();
        var uow = Uow(db);

        await AssertGuardRejects(() => Sales(db, uow.Object).CreateDeliveryAsync(DeliveryReq(MixedLines)));

        uow.Verify(u => u.Repository<SalesDelivery>(), Times.Never);
        uow.Verify(u => u.Repository<SalesDeliveryItem>(), Times.Never);
        uow.Verify(u => u.Repository<SalesOrder>(), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_deliveries"));
    }

    [Fact(DisplayName = "후9 W7 A 거래명세 신규(EF) · 같은 회사 활성 창고 = 가드 문구 예외 없음")]
    public async Task W7_A_DeliveryCreate_NotGuarded()
    {
        if (Skip("W7 A")) return;
        using var db = FreshDb();

        await AssertNotGuarded(() => Sales(db, Uow(db).Object).CreateDeliveryAsync(DeliveryReq(SameLines)));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  W8 거래명세 수정 (Dapper · 트랜잭션 전)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후9 W8 R 🔴 거래명세 수정 · 줄에 다른 회사 창고 = 거절 · 머리 · 기존 줄 그대로")]
    public async Task W8_R_DeliveryUpdate_Rejected()
    {
        if (Skip("W8 R")) return;
        using var db = FreshDb();
        SeedDraftDelivery(db, "VSWT-DN-1");

        await AssertGuardRejects(() => Sales(db).UpdateDeliveryAsync("VSWT-DN-1", DeliveryUpdateDto(MixedLines), TenantA, User));

        AssertDraftDeliveryUntouched(db, "VSWT-DN-1");
    }

    [Fact(DisplayName = "후9 W8 A 거래명세 수정 · 같은 회사 활성 창고 = 머리 · 줄 바뀜")]
    public async Task W8_A_DeliveryUpdate_Saved()
    {
        if (Skip("W8 A")) return;
        using var db = FreshDb();
        SeedDraftDelivery(db, "VSWT-DN-1");

        await Sales(db).UpdateDeliveryAsync("VSWT-DN-1", DeliveryUpdateDto(SameLines), TenantA, User);

        Assert.Equal(Changed, Str(db, "SELECT memo FROM sales_deliveries WHERE delivery_id='VSWT-DN-1'"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE delivery_id='VSWT-DN-1' AND warehouse_id='{WhA}'"));
    }

    [Fact(DisplayName = "후9 W8 WA0 🔴 거래명세 수정 · 쉬게 한 내 창고 줄 = 거절(Q3 · CTO 전결 K4 (가)) · 머리 · 기존 줄 그대로")]
    public async Task W8_WA0_DeliveryUpdate_InactiveWarehouse_Rejected()
    {
        if (Skip("W8 WA0")) return;
        using var db = FreshDb();
        SeedDraftDelivery(db, "VSWT-DN-1");

        await AssertGuardRejects(() => Sales(db).UpdateDeliveryAsync("VSWT-DN-1", DeliveryUpdateDto(InactiveLines), TenantA, User));

        AssertDraftDeliveryUntouched(db, "VSWT-DN-1");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  W9a · W9b 판매 반품 (Dapper)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후9 W9a R 🔴 판매 반품 신규 · 줄에 다른 회사 창고 = 거절 · 쓴 행 0")]
    public async Task W9a_R_SalesReturnCreate_Rejected()
    {
        if (Skip("W9a R")) return;
        using var db = FreshDb();

        await AssertGuardRejects(() => Sales(db).CreateSalesReturnAsync(SalesReturnReq(MixedLines, Orig), TenantA));

        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_returns"));
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_return_items"));
    }

    [Fact(DisplayName = "후9 W9a A 판매 반품 신규 · 같은 회사 활성 창고 = 머리 1 · 줄 2 · 창고 그대로")]
    public async Task W9a_A_SalesReturnCreate_Saved()
    {
        if (Skip("W9a A")) return;
        using var db = FreshDb();

        var (returnId, _) = await Sales(db).CreateSalesReturnAsync(SalesReturnReq(SameLines, Orig), TenantA);

        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_returns WHERE return_id='{returnId}' AND tenant_id='{TenantA}'"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}' AND warehouse_id='{WhA}'"));
    }

    [Fact(DisplayName = "후9 W9b R 🔴 판매 반품 수정(트랜잭션 없음) · 줄에 다른 회사 창고 = 거절 · 머리 UPDATE 안 일어남 · 기존 줄 그대로")]
    public async Task W9b_R_SalesReturnUpdate_Rejected()
    {
        if (Skip("W9b R")) return;
        using var db = FreshDb();
        var svc = Sales(db);
        var (returnId, _) = await svc.CreateSalesReturnAsync(SalesReturnReq(SameLines, Orig), TenantA);

        await AssertGuardRejects(() => svc.UpdateSalesReturnAsync(returnId, SalesReturnUpdateReq(MixedLines, Changed), TenantA));

        Assert.Equal(Orig, Str(db, $"SELECT memo FROM sales_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_returns WHERE return_id='{returnId}' AND total_amount=5950"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}' AND warehouse_id='{WhA}'"));
        Assert.Equal(0, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE warehouse_id='{WhB}'"));
    }

    [Fact(DisplayName = "후9 W9b A 판매 반품 수정 · 같은 회사 활성 창고 = 머리 · 줄 바뀜")]
    public async Task W9b_A_SalesReturnUpdate_Saved()
    {
        if (Skip("W9b A")) return;
        using var db = FreshDb();
        var svc = Sales(db);
        var (returnId, _) = await svc.CreateSalesReturnAsync(SalesReturnReq(SameLines, Orig), TenantA);

        await svc.UpdateSalesReturnAsync(returnId, SalesReturnUpdateReq(new[] { (ItemA2, 4m, 1250m, WhA2) }, Changed), TenantA);

        Assert.Equal(Changed, Str(db, $"SELECT memo FROM sales_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND warehouse_id='{WhA2}' AND supply_amount=5000"));
    }

    [Fact(DisplayName = "후9 W9a WA0 🔴 판매 반품 신규 · 쉬게 한 내 창고 줄 = 저장됨(전결 2 회사만 · #20) · 줄 창고 WA0 그대로")]
    public async Task W9a_WA0_SalesReturnCreate_InactiveOwnWarehouse_Saved()
    {
        if (Skip("W9a WA0")) return;
        using var db = FreshDb();

        var (returnId, _) = await Sales(db).CreateSalesReturnAsync(SalesReturnReq(InactiveLines, Orig), TenantA);

        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_returns WHERE return_id='{returnId}' AND tenant_id='{TenantA}'"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND warehouse_id='{WhA0}'"));
    }

    [Fact(DisplayName = "후9 W9b WA0 🔴 판매 반품 수정 · 저장 뒤 쉬게 한 내 창고 줄의 수량만 고침 = 저장됨(전결 2 회사만 · #20)")]
    public async Task W9b_WA0_SalesReturnUpdate_InactiveOwnWarehouse_Saved()
    {
        if (Skip("W9b WA0")) return;
        using var db = FreshDb();
        var svc = Sales(db);
        // 활성 WA2 로 저장한 뒤 WA2 를 쉬게 한다(신규 입구 W9a 에 기대지 않고 W9b 만 잰다).
        var (returnId, _) = await svc.CreateSalesReturnAsync(SalesReturnReq(new[] { (ItemA2, 2m, 1250m, WhA2) }, Orig), TenantA);
        Exec(db, $"UPDATE warehouses SET is_active = 0 WHERE warehouse_id = '{WhA2}'");

        await svc.UpdateSalesReturnAsync(returnId, SalesReturnUpdateReq(new[] { (ItemA2, 4m, 1250m, WhA2) }, Changed), TenantA);

        Assert.Equal(Changed, Str(db, $"SELECT memo FROM sales_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}' AND item_id='{ItemA2}' AND warehouse_id='{WhA2}' AND supply_amount=5000"));
    }

    [Fact(DisplayName = "후9 W9a 공백 🔴 판매 반품 신규 · 줄 창고 id 공백만(\"   \" · \"\\t\") = 거절 · 문구 · 쓴 행 0(전결 2)")]
    public async Task W9a_Whitespace_SalesReturnCreate_Rejected()
    {
        if (Skip("W9a 공백")) return;
        using var db = FreshDb();

        foreach (var blank in Blanks)
        {
            await AssertGuardRejects(() => Sales(db).CreateSalesReturnAsync(SalesReturnReq(BlankLines(blank), Orig), TenantA));

            Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_returns"));
            Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sales_return_items"));
        }
    }

    [Fact(DisplayName = "후9 W9b 공백 🔴 판매 반품 수정 · 줄 창고 id 공백만(\"   \" · \"\\t\") = 거절 · 머리 UPDATE 안 일어남 · 기존 줄 그대로(전결 2)")]
    public async Task W9b_Whitespace_SalesReturnUpdate_Rejected()
    {
        if (Skip("W9b 공백")) return;
        using var db = FreshDb();
        var svc = Sales(db);
        var (returnId, _) = await svc.CreateSalesReturnAsync(SalesReturnReq(SameLines, Orig), TenantA);

        foreach (var blank in Blanks)
        {
            await AssertGuardRejects(() => svc.UpdateSalesReturnAsync(returnId, SalesReturnUpdateReq(BlankLines(blank), Changed), TenantA));

            Assert.Equal(Orig, Str(db, $"SELECT memo FROM sales_returns WHERE return_id='{returnId}'"));
            Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_returns WHERE return_id='{returnId}' AND total_amount=5950"));
            Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM sales_return_items WHERE return_id='{returnId}' AND warehouse_id='{WhA}'"));
            Assert.Equal(2, Count(db, "SELECT COUNT(*) FROM sales_return_items"));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  W10 배송 묶음 (Dapper · 같은 tx · 예외를 건별 문구로 돌려줌)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후9 W10 R 🔴 배송 묶음 · 두 번째 요청에만 다른 회사 창고 = 건별 실패 문구 = 가드 문구 · 쓴 행 0(첫 요청도 롤백)")]
    public async Task W10_R_DeliveryBatch_Rejected()
    {
        if (Skip("W10 R")) return;
        using var db = FreshDb();

        var results = await Batch(db).SaveBatchAsync(new List<CreateDeliveryRequest>
        {
            DeliveryReq(SameLines),
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

    [Fact(DisplayName = "후9 W10 A 배송 묶음 · 같은 회사 활성 창고 = 명세서 2 · 줄 창고 그대로")]
    public async Task W10_A_DeliveryBatch_Saved()
    {
        if (Skip("W10 A")) return;
        using var db = FreshDb();

        var results = await Batch(db).SaveBatchAsync(new List<CreateDeliveryRequest>
        {
            DeliveryReq(SameLines),
            DeliveryReq(new[] { (ItemA2, 5m, 1250m, WhA2) }),
        });

        Assert.All(results, r => Assert.True(r.Success, r.ErrorMessage));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM sales_deliveries WHERE tenant_id='{TenantA}'"));
        Assert.Equal(2, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE warehouse_id='{WhA}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE warehouse_id='{WhA2}' AND item_id='{ItemA2}'"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  확정 길 안 막힘 (#20 · 설계 §4-3 · Q3 「확정 · 취소 길엔 안 걺」)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "후9 확정 길 · WA 로 저장한 매입 반품 초안 → WA 를 쉬게 함 → 확정 = 성공 · 원장 행 생김(#20)")]
    public async Task Confirm_PurchaseReturn_AfterWarehouseDeactivated_Succeeds()
    {
        if (Skip("확정 길")) return;
        using var db = FreshDb();
        var svc = Purchase(db);
        // 반품 수량만큼 재고가 있어야 확정된다(음수 재고 검사) — 회사 A 원장에 입고 행을 먼저 심는다.
        Exec(db, $"""
            INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out)
            VALUES ('{TenantA}', '{ItemA1}', '{WhA}', '2026-11-01', '2026-11', 'in', 'adjustment', 'VSWT-SEED-1', 10, 0)
            """);
        var (returnId, _) = await svc.CreatePurchaseReturnAsync(
            PurchaseReturnReq(new[] { (ItemA1, 2m, 1100m, WhA) }, Orig), TenantA);
        // 저장 뒤 창고를 쉬게 한다(창고 삭제 = is_active 0 과 같은 상태).
        Exec(db, $"UPDATE warehouses SET is_active = 0 WHERE warehouse_id = '{WhA}'");

        await svc.ConfirmPurchaseReturnAsync(returnId, TenantA, null);

        Assert.Equal("confirmed", Str(db, $"SELECT status FROM purchase_returns WHERE return_id='{returnId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM stock_ledger WHERE source_type='purchase_return' AND source_id='{returnId}' AND warehouse_id='{WhA}' AND item_id='{ItemA1}' AND qty_out=2"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  판정 도우미
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>예외형 + <b>문구까지</b> 대조(다른 까닭의 InvalidOperationException 과 가른다 · tx 를 빠뜨린 예외와도 가른다).</summary>
    private static async Task AssertGuardRejects(Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(Msg, ex.Message);
    }

    /// <summary>같은 회사 활성 창고면 가드 문구 예외가 나지 않는다(다음 단계에서 다른 까닭으로 멈추는 것은 허용 · 그 문구가 가드 문구가 아님을 단언).</summary>
    private static async Task AssertNotGuarded(Func<Task> act)
    {
        var ex = await Record.ExceptionAsync(act);
        Assert.False(ex is InvalidOperationException && ex.Message == Msg,
            "🔴 같은 회사 활성 창고인데 가드가 막았다 — 정상 길 막힘(즉석 완화 금지 · PM 보고).");
    }

    private static void VerifyReceiptNeverSaved(Mock<IUnitOfWork> uow)
    {
        uow.Verify(u => u.Repository<PurchaseReceipt>(), Times.Never);
        uow.Verify(u => u.Repository<PurchaseReceiptItem>(), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void AssertDraftDeliveryUntouched(MySqlConnection db, string deliveryId)
    {
        Assert.Equal(Orig, Str(db, $"SELECT memo FROM sales_deliveries WHERE delivery_id='{deliveryId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE delivery_id='{deliveryId}'"));
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM sales_delivery_items WHERE delivery_id='{deliveryId}' AND item_id='{ItemA1}' AND warehouse_id='{WhA}' AND supply_amount=1000"));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  서비스 — 모두 시험 연결 하나만 쓴다
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

    /// <summary>
    /// 발주 씨앗 — 전환은 발주 머리 · 줄을 EF 저장소(<c>GetByIdAsync</c> · <c>FindAsync</c>)로 읽으므로 가짜 저장소에 심는다.
    /// 줄 하나 = A1 · 미입고 2 · 창고 = <paramref name="warehouseId"/>(고치기 전 들어간 옛 발주 · 쉬게 한 창고 발주 흉내).
    /// </summary>
    private static void SeedPurchaseOrder(Mock<IUnitOfWork> uow, string poId, string warehouseId)
    {
        var po = new PurchaseOrder { PoId = poId, TenantId = TenantA, PoNo = "PO-" + poId, PartnerId = PartnerA, PoDate = new DateTime(2026, 11, 29) };
        var line = new PurchaseOrderItem
        {
            PoItemId = poId + "-L1", PoId = poId, TenantId = TenantA, ItemId = ItemA1,
            OrderedQty = 2m, ReceivedQty = 0m, UnitPrice = 1100m, SupplyAmount = 2200m, VatAmount = 220m, WarehouseId = warehouseId
        };
        var poRepo = new Mock<IRepository<PurchaseOrder>>();
        poRepo.Setup(r => r.GetByIdAsync(poId)).ReturnsAsync(po);
        var poItemRepo = new Mock<IRepository<PurchaseOrderItem>>();
        poItemRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<PurchaseOrderItem, bool>>>()))
                  .ReturnsAsync(new List<PurchaseOrderItem> { line });
        uow.Setup(u => u.Repository<PurchaseOrder>()).Returns(poRepo.Object);
        uow.Setup(u => u.Repository<PurchaseOrderItem>()).Returns(poItemRepo.Object);
    }

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

    private static CreatePurchaseOrderRequest PoReq((string Id, decimal Qty, decimal Price, string Wh)[] lines) => new()
    {
        PartnerId = PartnerA, PoDate = new DateTime(2026, 11, 29), Memo = Orig,
        Items = lines.Select(l => new CreatePurchaseOrderItemRequest
        {
            ItemId = l.Id, OrderedQty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m, WarehouseId = l.Wh
        }).ToList()
    };

    private static CreateReceiptRequest ReceiptReq((string Id, decimal Qty, decimal Price, string Wh)[] lines) => new()
    {
        PartnerId = PartnerA, ReceiptDate = new DateTime(2026, 11, 29), Memo = Orig,
        Items = lines.Select(l => new CreateReceiptItemRequest
        {
            ItemId = l.Id, WarehouseId = l.Wh, Qty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m
        }).ToList()
    };

    private static List<CreatePurchaseReturnItemRequest> PurchaseReturnLines((string Id, decimal Qty, decimal Price, string Wh)[] lines) =>
        lines.Select(l => new CreatePurchaseReturnItemRequest
        {
            ItemId = l.Id, WarehouseId = l.Wh, Qty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m
        }).ToList();

    private static CreatePurchaseReturnRequest PurchaseReturnReq((string Id, decimal Qty, decimal Price, string Wh)[] lines, string memo) => new()
    {
        PartnerId = PartnerA, ReturnDate = new DateTime(2026, 11, 29), Memo = memo, Items = PurchaseReturnLines(lines)
    };

    private static UpdatePurchaseReturnRequest PurchaseReturnUpdateReq((string Id, decimal Qty, decimal Price, string Wh)[] lines, string memo) => new()
    {
        PartnerId = PartnerA, ReturnDate = new DateTime(2026, 11, 29), Memo = memo, Items = PurchaseReturnLines(lines)
    };

    private static CreateDeliveryRequest DeliveryReq((string Id, decimal Qty, decimal Price, string Wh)[] lines) => new()
    {
        PartnerId = PartnerA, DeliveryDate = new DateTime(2026, 11, 29), Memo = Orig,
        Items = lines.Select(l => new CreateDeliveryItemRequest
        {
            ItemId = l.Id, WarehouseId = l.Wh, Qty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m
        }).ToList()
    };

    private static UpdateDeliveryDto DeliveryUpdateDto((string Id, decimal Qty, decimal Price, string Wh)[] lines) => new()
    {
        OrderDate = new DateTime(2026, 11, 29), PartnerId = PartnerA, Memo = Changed,
        Items = lines.Select(l => new DeliveryItemDto
        {
            ItemId = l.Id, Qty = l.Qty, UnitPrice = l.Price, Amount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m, WarehouseId = l.Wh
        }).ToList()
    };

    private static List<CreateSalesReturnItemRequest> SalesReturnLines((string Id, decimal Qty, decimal Price, string Wh)[] lines) =>
        lines.Select(l => new CreateSalesReturnItemRequest
        {
            ItemId = l.Id, WarehouseId = l.Wh, Qty = l.Qty, UnitPrice = l.Price, SupplyAmount = l.Qty * l.Price, VatAmount = l.Qty * l.Price / 10m
        }).ToList();

    private static CreateSalesReturnRequest SalesReturnReq((string Id, decimal Qty, decimal Price, string Wh)[] lines, string memo) => new()
    {
        PartnerId = PartnerA, ReturnDate = new DateTime(2026, 11, 29), Memo = memo, Items = SalesReturnLines(lines)
    };

    private static UpdateSalesReturnRequest SalesReturnUpdateReq((string Id, decimal Qty, decimal Price, string Wh)[] lines, string memo) => new()
    {
        PartnerId = PartnerA, ReturnDate = new DateTime(2026, 11, 29), Memo = memo, Items = SalesReturnLines(lines)
    };

    // ════════════════════════════════════════════════════════════════════════
    //  표 · 씨앗
    // ════════════════════════════════════════════════════════════════════════

    private static bool Skip(string fact)
    {
        if (ServerAvailable()) return false;
        DbGateEnvironment.SkipOrFail($"VoucherSaveWarehouseTenantGate {fact}");
        return true;
    }

    /// <summary>
    /// 시험 DB 의 기본 표 전부를 같은 이름 <c>TEMPORARY</c> 표로 가린다(<c>LIKE</c> = 칸 · 형 · 기본값 그대로 · 외래 키는 안 따라옴).
    /// 연결 풀을 끄므로 연결을 닫으면 임시 표도 사라진다 — 실표는 가리기만 하고 건드리지 않는다(헌법 #39).
    /// 씨앗 = 품목 A1 · A2(회사 A) · 창고 WA(회사 A 기본 MAIN · 활성) · WA2(회사 A 활성) · WA0(회사 A 쉬게 함) · WB(회사 B 활성).
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
        foreach (var need in new[] { "items", "warehouses", "sales_orders", "sales_order_items",
                     "sales_deliveries", "sales_delivery_items", "purchase_orders", "purchase_order_items", "purchase_receipts",
                     "purchase_receipt_items", "purchase_returns", "purchase_return_items", "sales_returns", "sales_return_items",
                     "stock_ledger", "item_stock", "partner_balance", "journal_entries", "journal_lines" })
        {
            Assert.Contains(need, allTables);
        }
        foreach (var t in allTables)
        {
            // 같은 이름으로 바로 LIKE 하면 "Not unique table/alias" — 다른 이름으로 만든 뒤 임시 표 이름을 바꾼다.
            Exec(db, $"DROP TEMPORARY TABLE IF EXISTS vswttmp_{t}");
            Exec(db, $"CREATE TEMPORARY TABLE vswttmp_{t} LIKE `{TestDb}`.`{t}`");
            Exec(db, $"ALTER TABLE vswttmp_{t} RENAME TO `{t}`");
        }

        Exec(db, $"""
            INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit, is_active, created_at, updated_at) VALUES
              ('{ItemA1}', '{TenantA}', 'VSWT-A1', 'VSWTNAMEAONE', 'goods', 'EA', 1, NOW(6), NOW(6)),
              ('{ItemA2}', '{TenantA}', 'VSWT-A2', 'VSWTNAMEATWO', 'goods', 'EA', 1, NOW(6), NOW(6))
            """);
        Exec(db, $"""
            INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at) VALUES
              ('{WhA}', '{TenantA}', 'MAIN', 'VSWTWHA', 'normal', 1, NOW(6), NOW(6)),
              ('{WhA2}', '{TenantA}', 'VSWT-A2', 'VSWTWHATWO', 'normal', 1, NOW(6), NOW(6)),
              ('{WhA0}', '{TenantA}', 'VSWT-A0', 'VSWTWHAREST', 'normal', 0, NOW(6), NOW(6)),
              ('{WhB}', '{TenantB}', 'MAIN', 'VSWTWHSECRETB', 'normal', 1, NOW(6), NOW(6))
            """);
        return db;
    }

    private static void SeedDraftDelivery(MySqlConnection db, string deliveryId)
    {
        Exec(db, $"""
            INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, delivery_date, source_type, status,
              total_amount, vat_amount, memo, created_at, updated_at)
            VALUES ('{deliveryId}', '{TenantA}', 'DN-{deliveryId}', '{PartnerA}', '2026-11-29', 'direct', 'draft',
              1000, 100, '{Orig}', NOW(6), NOW(6))
            """);
        Exec(db, $"""
            INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, item_id, warehouse_id, qty,
              unit_price, supply_amount, vat_amount)
            VALUES (UUID(), '{deliveryId}', '{TenantA}', '{ItemA1}', '{WhA}', 1, 1000, 1000, 100)
            """);
    }

    private static int Count(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string Str(MySqlConnection db, string sql)
    {
        using var cmd = new MySqlCommand(sql, db);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? throw new InvalidOperationException("값 없음: " + sql) : Convert.ToString(v)!;
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
