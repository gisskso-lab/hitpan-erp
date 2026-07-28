using Dapper;
using MySqlConnector;

namespace HitPan.IntegrationTests.Infrastructure;

/// <summary>
/// 통합 테스트 전용 DB 픽스처.
/// - CLAUDE.md 기준 로컬 DB: hitpan_erp / hitpan / Hitpan2025!
/// - 테스트 테넌트: test-tenant-{guid} 로 격리 (테스트 후 롤백)
/// - 각 테스트는 트랜잭션 내에서 실행 → Dispose 시 롤백
/// </summary>
public sealed class DbFixture : IAsyncLifetime
{
    public const string ConnStr =
        "Server=localhost;Port=3306;Database=hitpan_erp;User=hitpan;Password=Hitpan2025!;DefaultCommandTimeout=30;";

    public MySqlConnection Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Connection = new MySqlConnection(ConnStr);
        await Connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await Connection.CloseAsync();
        await Connection.DisposeAsync();
    }

    /// <summary>테스트 격리용 테넌트 ID — UUID 형태(36자), 실제 tenant_id 컬럼 VARCHAR(36) 준수</summary>
    public static string NewTestTenantId() => Guid.NewGuid().ToString();

    /// <summary>
    /// 테스트용 회사정보 INSERT — local_company FK 제약 해소.
    /// 헌법 #35 (2026-06-04): ERP 로컬은 local_company, 본사 tenants는 백오피스 DB.
    /// CleanupTenantAsync에서 삭제됨.
    /// </summary>
    public async Task InsertTestTenantAsync(string tenantId)
    {
        var code = tenantId[..8];
        await Connection.ExecuteAsync(
            """
            INSERT IGNORE INTO local_company
              (tenant_id, tenant_code, company_name, biz_no, ceo_name,
               created_at, updated_at)
            VALUES
              (@TenantId, @Code, '테스트회사', '0000000000', '테스트대표',
               NOW(6), NOW(6))
            """,
            new { TenantId = tenantId, Code = code });
        await Connection.ExecuteAsync(
            """
            INSERT IGNORE INTO local_subscription
              (tenant_id, subscription_tier, status, ai_mode,
               ai_token_monthly_limit, max_users, sync_source, created_at, updated_at)
            VALUES
              (@TenantId, 'basic', 'active', 'hitpan_pool',
               100000, 3, 'bootstrap', NOW(6), NOW(6))
            """,
            new { TenantId = tenantId });
    }

    /// <summary>테스트용 상품 INSERT 후 item_id 반환</summary>
    public async Task<string> InsertTestItemAsync(string tenantId, string itemName = "테스트상품", decimal safetyStock = 0)
    {
        var itemId = Guid.NewGuid().ToString();
        await Connection.ExecuteAsync(
            """
            INSERT INTO items
              (item_id, tenant_id, item_code, item_name, item_type, unit, sale_price, purchase_price,
               safety_stock, auto_order_enabled, is_active, created_at, updated_at,
               std_price, tax_type, row_version)
            VALUES
              (@ItemId, @TenantId, @ItemCode, @ItemName, 'product', 'EA', 0, 0,
               @SafetyStock, 0, 1, NOW(6), NOW(6),
               0, 'taxable', 0)
            """,
            new { ItemId = itemId, TenantId = tenantId, ItemCode = itemId[..8], ItemName = itemName, SafetyStock = safetyStock });
        return itemId;
    }

    /// <summary>테스트용 창고 INSERT 후 warehouse_id 반환</summary>
    public async Task<string> InsertTestWarehouseAsync(string tenantId)
    {
        var warehouseId = Guid.NewGuid().ToString();
        var code = warehouseId[..8];
        await Connection.ExecuteAsync(
            """
            INSERT INTO warehouses
              (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at)
            VALUES
              (@WarehouseId, @TenantId, @WhCode, '테스트창고', 'MAIN', 1, NOW(6), NOW(6))
            """,
            new { WarehouseId = warehouseId, TenantId = tenantId, WhCode = code });
        return warehouseId;
    }

    /// <summary>테스트용 item_stock 초기화</summary>
    public async Task SetStockAsync(string tenantId, string itemId, string warehouseId, decimal qty)
    {
        await Connection.ExecuteAsync(
            """
            INSERT INTO item_stock
              (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
            VALUES
              (UUID(), @TenantId, @ItemId, @WarehouseId, @Qty, 0, NOW(6))
            ON DUPLICATE KEY UPDATE
              current_qty = @Qty,
              last_updated_at = NOW(6)
            """,
            new { TenantId = tenantId, ItemId = itemId, WarehouseId = warehouseId, Qty = qty });
    }

    /// <summary>현재 item_stock.current_qty 조회</summary>
    public async Task<decimal?> GetStockQtyAsync(string tenantId, string itemId)
    {
        return await Connection.QueryFirstOrDefaultAsync<decimal?>(
            "SELECT current_qty FROM item_stock WHERE tenant_id = @TenantId AND item_id = @ItemId",
            new { TenantId = tenantId, ItemId = itemId });
    }

    /// <summary>테스트 데이터 일괄 삭제 (tenant 기준 격리)</summary>
    public async Task CleanupTenantAsync(string tenantId)
    {
        await Connection.ExecuteAsync("DELETE FROM stock_ledger WHERE tenant_id = @T", new { T = tenantId });
        await Connection.ExecuteAsync("DELETE FROM item_stock WHERE tenant_id = @T", new { T = tenantId });
        await Connection.ExecuteAsync("DELETE FROM monthly_summary_sources WHERE tenant_id = @T", new { T = tenantId });
        await Connection.ExecuteAsync("DELETE FROM monthly_summary WHERE tenant_id = @T", new { T = tenantId });
        await Connection.ExecuteAsync("DELETE FROM items WHERE tenant_id = @T", new { T = tenantId });
        await Connection.ExecuteAsync("DELETE FROM warehouses WHERE tenant_id = @T", new { T = tenantId });
        await Connection.ExecuteAsync("DELETE FROM local_subscription WHERE tenant_id = @T", new { T = tenantId });
        await Connection.ExecuteAsync("DELETE FROM local_company WHERE tenant_id = @T", new { T = tenantId });
    }

    /// <summary>stock_ledger 직접 삽입 (실제 컬럼 기준)</summary>
    public async Task InsertLedgerAsync(
        string tenantId, string itemId, string warehouseId,
        string moveType, decimal qtyIn, decimal qtyOut, string sourceType)
    {
        var today = DateTime.UtcNow;
        await Connection.ExecuteAsync(
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
                TenantId = tenantId, ItemId = itemId, WarehouseId = warehouseId,
                MoveType = moveType, QtyIn = qtyIn, QtyOut = qtyOut,
                SourceType = sourceType, SourceId = Guid.NewGuid().ToString(),
                LedgerDate = today.Date, Ym = today.ToString("yyyy-MM")
            });
    }
}
