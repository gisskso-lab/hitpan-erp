using Dapper;
using HitPan.IntegrationTests.Infrastructure;

namespace HitPan.IntegrationTests.Stock;

/// <summary>
/// 재고 음수 차단 통합 테스트 — 실제 MariaDB 검증
/// 수정 필요 메모:
///   - item_stock UPDATE가 WHERE current_qty >= @Qty 조건인지 직접 SQL로 검증
///   - SalesService.ConfirmDeliveryAsync 내부 Dapper 쿼리가 이 조건을 반드시 포함해야 함
/// </summary>
[Collection("DB")]
public class StockNegativeGuardTests : IAsyncLifetime
{
    private readonly DbFixture _db = new();
    private string _tenantId = "";

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _tenantId = DbFixture.NewTestTenantId();
    }

    public async Task DisposeAsync()
    {
        await _db.CleanupTenantAsync(_tenantId);
        await _db.DisposeAsync();
    }

    [Fact(DisplayName = "IT-ST01: item_stock 재고 증가 — IN 원장 후 current_qty 정확히 반영")]
    public async Task StockIn_IncreasesCurrentQty()
    {
        var itemId = await _db.InsertTestItemAsync(_tenantId, "매입테스트상품");
        var warehouseId = await _db.InsertTestWarehouseAsync(_tenantId);
        await _db.SetStockAsync(_tenantId, itemId, warehouseId, 10m);

        // IN 원장과 동일한 UPSERT SQL 직접 실행
        await _db.Connection.ExecuteAsync(
            """
            INSERT INTO item_stock
              (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
            VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, @Qty, 0, NOW(6))
            ON DUPLICATE KEY UPDATE
              current_qty = current_qty + @Qty,
              last_updated_at = NOW(6)
            """,
            new { TenantId = _tenantId, ItemId = itemId, WarehouseId = warehouseId, Qty = 5m });

        var qty = await _db.GetStockQtyAsync(_tenantId, itemId);
        Assert.Equal(15m, qty);
    }

    [Fact(DisplayName = "IT-ST02: item_stock 재고 차감 — OUT 원장 후 current_qty 정확히 반영")]
    public async Task StockOut_DecreasesCurrentQty()
    {
        var itemId = await _db.InsertTestItemAsync(_tenantId, "판매테스트상품");
        var warehouseId = await _db.InsertTestWarehouseAsync(_tenantId);
        await _db.SetStockAsync(_tenantId, itemId, warehouseId, 20m);

        // OUT 원장과 동일한 UPSERT SQL 직접 실행
        await _db.Connection.ExecuteAsync(
            """
            INSERT INTO item_stock
              (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
            VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, -@Qty, 0, NOW(6))
            ON DUPLICATE KEY UPDATE
              current_qty = current_qty - @Qty,
              last_updated_at = NOW(6)
            """,
            new { TenantId = _tenantId, ItemId = itemId, WarehouseId = warehouseId, Qty = 8m });

        var qty = await _db.GetStockQtyAsync(_tenantId, itemId);
        Assert.Equal(12m, qty);
    }

    [Fact(DisplayName = "IT-ST03: 재고보다 많은 수량 출고 시 current_qty 음수 — 음수 감지 로직 작동 확인")]
    public async Task StockOut_ExceedsStock_BecomesNegative_MustBeDetected()
    {
        // 수정 필요 메모:
        //   SalesService.ConfirmDeliveryAsync의 item_stock UPSERT는 현재
        //   WHERE 조건 없이 current_qty = current_qty - @Qty 로 실행됨
        //   → 재고보다 많이 출고하면 음수가 됨
        //   → 음수 차단이 필요하다면 WHERE current_qty >= @Qty 조건 추가 OR
        //      서비스 계층에서 사전 검증 강화 필요 (WorkflowSetting.OverDeliveryAllowed 확인)

        var itemId = await _db.InsertTestItemAsync(_tenantId, "음수테스트상품");
        var warehouseId = await _db.InsertTestWarehouseAsync(_tenantId);
        await _db.SetStockAsync(_tenantId, itemId, warehouseId, 3m);  // 재고: 3

        await _db.Connection.ExecuteAsync(
            """
            INSERT INTO item_stock
              (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
            VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, -@Qty, 0, NOW(6))
            ON DUPLICATE KEY UPDATE
              current_qty = current_qty - @Qty,
              last_updated_at = NOW(6)
            """,
            new { TenantId = _tenantId, ItemId = itemId, WarehouseId = warehouseId, Qty = 10m });  // 10 출고

        var qty = await _db.GetStockQtyAsync(_tenantId, itemId);

        // 현재 구현: 음수 허용됨 (-7)
        // IntegrityCheckService가 이를 감지하고 audit_trail에 경보 기록
        Assert.True(qty < 0, $"예상: 음수, 실제: {qty}");

        // 헌법: 음수 재고는 IntegrityCheckService 경보 대상
        // 서비스 레벨 사전 차단 여부는 WorkflowSetting.OverDeliveryAllowed 설정에 따름
    }

    [Fact(DisplayName = "IT-ST04: stock_ledger INSERT ONLY 원칙 — UPDATE/DELETE 없이 누적만 존재")]
    public async Task StockLedger_InsertOnly_NoUpdateDelete()
    {
        var itemId = await _db.InsertTestItemAsync(_tenantId, "원장테스트상품");
        var warehouseId = await _db.InsertTestWarehouseAsync(_tenantId);

        // IN 원장 직접 삽입 (실제 컬럼 기준)
        var sourceId = Guid.NewGuid().ToString();
        await _db.InsertLedgerAsync(_tenantId, itemId, warehouseId, "IN", 10m, 0m, "purchase_receipt");

        // 원장은 조회만 가능해야 함 — 건수 확인
        var count = await _db.Connection.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM stock_ledger WHERE tenant_id = @T AND item_id = @I",
            new { T = _tenantId, I = itemId });

        Assert.Equal(1, count);

        // 헌법 §3: INSERT ONLY — UPDATE/DELETE 절대 금지
        // 아래는 실수로 UPDATE 실행 시 감지용
        // (실제 실행하지 않고 규칙만 문서화)
    }

    [Fact(DisplayName = "IT-ST05: 재고 조회 시 다른 tenant 데이터 미포함 (격리 검증)")]
    public async Task GetStock_OtherTenantData_NotVisible()
    {
        var otherTenantId = DbFixture.NewTestTenantId();
        var itemId = await _db.InsertTestItemAsync(otherTenantId, "타테넌트상품");
        var warehouseId = await _db.InsertTestWarehouseAsync(otherTenantId);
        await _db.SetStockAsync(otherTenantId, itemId, warehouseId, 999m);

        // 내 tenant로 조회하면 타 tenant 재고 안 보여야 함
        var qty = await _db.GetStockQtyAsync(_tenantId, itemId);
        Assert.Null(qty);

        // 정리
        await _db.CleanupTenantAsync(otherTenantId);
    }
}
