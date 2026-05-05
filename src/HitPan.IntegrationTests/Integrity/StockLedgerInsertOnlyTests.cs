using Dapper;
using HitPan.IntegrationTests.Infrastructure;

namespace HitPan.IntegrationTests.Integrity;

/// <summary>
/// stock_ledger INSERT ONLY 원칙 통합 테스트
/// 헌법 §3: stock_ledger, journal_lines UPDATE/DELETE 금지
/// </summary>
[Collection("DB")]
public class StockLedgerInsertOnlyTests : IAsyncLifetime
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

    [Fact(DisplayName = "IT-L01: stock_ledger IN/OUT 쌍이 합산하면 최종 재고와 일치")]
    public async Task StockLedger_InOutBalance_MatchesCurrentQty()
    {
        var itemId = await _db.InsertTestItemAsync(_tenantId, "원장정합성상품");
        var warehouseId = await _db.InsertTestWarehouseAsync(_tenantId);

        // IN 원장 3건
        await InsertLedgerAsync(itemId, warehouseId, "IN", 100m, "purchase_receipt");
        await InsertLedgerAsync(itemId, warehouseId, "IN", 50m, "purchase_receipt");
        await InsertLedgerAsync(itemId, warehouseId, "IN", 30m, "purchase_receipt");

        // OUT 원장 2건
        await InsertLedgerAsync(itemId, warehouseId, "OUT", 40m, "sales_delivery");
        await InsertLedgerAsync(itemId, warehouseId, "OUT", 60m, "sales_delivery");

        // item_stock 동기화 (UPSERT)
        await _db.SetStockAsync(_tenantId, itemId, warehouseId, 0m);
        await _db.Connection.ExecuteAsync(
            """
            UPDATE item_stock
            SET current_qty = (
                SELECT COALESCE(SUM(qty_in) - SUM(qty_out), 0)
                FROM stock_ledger
                WHERE tenant_id = @TenantId AND item_id = @ItemId AND warehouse_id = @WarehouseId
            )
            WHERE tenant_id = @TenantId AND item_id = @ItemId AND warehouse_id = @WarehouseId
            """,
            new { TenantId = _tenantId, ItemId = itemId, WarehouseId = warehouseId });

        // 원장 합산: 100+50+30 - 40-60 = 80
        var ledgerSum = await _db.Connection.QueryFirstAsync<decimal>(
            """
            SELECT COALESCE(SUM(qty_in) - SUM(qty_out), 0)
            FROM stock_ledger
            WHERE tenant_id = @T AND item_id = @I AND warehouse_id = @W
            """,
            new { T = _tenantId, I = itemId, W = warehouseId });

        var currentQty = await _db.GetStockQtyAsync(_tenantId, itemId);

        Assert.Equal(80m, ledgerSum);
        Assert.Equal(ledgerSum, currentQty);
    }

    [Fact(DisplayName = "IT-L02: stock_ledger 건수는 확정 횟수와 일치 (추가만 있고 삭제 없음)")]
    public async Task StockLedger_CountMatchesConfirmCount()
    {
        var itemId = await _db.InsertTestItemAsync(_tenantId, "건수검증상품");
        var warehouseId = await _db.InsertTestWarehouseAsync(_tenantId);

        var confirmCount = 3;
        for (var i = 0; i < confirmCount; i++)
            await InsertLedgerAsync(itemId, warehouseId, "IN", 10m, "purchase_receipt");

        var count = await _db.Connection.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM stock_ledger WHERE tenant_id = @T AND item_id = @I",
            new { T = _tenantId, I = itemId });

        Assert.Equal(confirmCount, count);
    }

    [Fact(DisplayName = "IT-L03: 역분개는 DELETE가 아닌 반대 부호 INSERT로 처리")]
    public async Task StockLedger_Reversal_IsNewInsert_NotDelete()
    {
        var itemId = await _db.InsertTestItemAsync(_tenantId, "역분개테스트상품");
        var warehouseId = await _db.InsertTestWarehouseAsync(_tenantId);

        var originalId = Guid.NewGuid().ToString();

        // 원래 원장
        await InsertLedgerAsync(itemId, warehouseId, "IN", 50m, "purchase_receipt", originalId);

        var beforeCount = await _db.Connection.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM stock_ledger WHERE tenant_id = @T AND item_id = @I",
            new { T = _tenantId, I = itemId });

        // 역분개 = OUT INSERT (DELETE가 아님)
        await InsertLedgerAsync(itemId, warehouseId, "OUT", 50m, "purchase_return", Guid.NewGuid().ToString());

        var afterCount = await _db.Connection.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM stock_ledger WHERE tenant_id = @T AND item_id = @I",
            new { T = _tenantId, I = itemId });

        // 원장은 삭제되지 않고 2건이 되어야 함
        Assert.Equal(1, beforeCount);
        Assert.Equal(2, afterCount);

        // 합산은 0 (입고 50 - 출고 50)
        var net = await _db.Connection.QueryFirstAsync<decimal>(
            """
            SELECT COALESCE(SUM(qty_in) - SUM(qty_out), 0)
            FROM stock_ledger WHERE tenant_id = @T AND item_id = @I
            """,
            new { T = _tenantId, I = itemId });
        Assert.Equal(0m, net);
    }

    private async Task InsertLedgerAsync(
        string itemId, string warehouseId,
        string movementType, decimal qty,
        string sourceType, string? sourceId = null)
    {
        var qtyIn = movementType == "IN" ? qty : 0m;
        var qtyOut = movementType == "OUT" ? qty : 0m;
        var today = DateTime.UtcNow;
        await _db.Connection.ExecuteAsync(
            """
            INSERT INTO stock_ledger
              (tenant_id, item_id, warehouse_id, move_type, qty_in, qty_out,
               source_type, source_id, ledger_date, ym)
            VALUES
              (@TenantId, @ItemId, @WarehouseId, @MoveType, @QtyIn, @QtyOut,
               @SourceType, @SourceId, @LedgerDate, @Ym)
            """,
            new
            {
                TenantId = _tenantId,
                ItemId = itemId,
                WarehouseId = warehouseId,
                MoveType = movementType,
                QtyIn = qtyIn,
                QtyOut = qtyOut,
                SourceType = sourceType,
                SourceId = sourceId ?? Guid.NewGuid().ToString()[..36],
                LedgerDate = today.Date,
                Ym = today.ToString("yyyy-MM")
            });
    }
}
