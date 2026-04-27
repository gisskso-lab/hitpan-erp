using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.Events;
using HitPan.Application.Interfaces;

namespace HitPan.Infrastructure.Events;

public sealed class SyncEventPublisher : IEventPublisher
{
    private readonly IDbConnection _db;

    public SyncEventPublisher(IDbConnection db)
    {
        _db = db;
    }

    public async Task PublishAsync<T>(
        string eventType,
        T payload,
        CancellationToken ct = default)
        where T : class
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        switch (eventType)
        {
            case "delivery.confirmed" when payload is DeliveryConfirmedEvent e:
                await OnDeliveryConfirmed(e, ct).ConfigureAwait(false);
                break;
            case "delivery.cancelled" when payload is DeliveryCancelledEvent c:
                await OnDeliveryCancelled(c, ct).ConfigureAwait(false);
                break;
            case "purchase.confirmed" when payload is PurchaseConfirmedEvent p:
                await OnPurchaseConfirmed(p, ct).ConfigureAwait(false);
                break;
            case "bom.assembled" when payload is BomAssembledEvent b:
                await OnBomAssembled(b, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open)
        {
            return;
        }

        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }

        _db.Open();
    }

    private async Task OnDeliveryConfirmed(DeliveryConfirmedEvent e, CancellationToken ct)
    {
        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO partner_balance
                  (balance_id, tenant_id, partner_id,
                   total_sales, total_receipt,
                   total_purchase, total_payment,
                   last_updated_at)
                VALUES
                  (UUID(), @TenantId, @PartnerId,
                   @Amount, 0, 0, 0, NOW(6))
                ON DUPLICATE KEY UPDATE
                  total_sales = total_sales + @Amount,
                  last_updated_at = NOW(6)
                """,
                new { e.TenantId, e.PartnerId, Amount = e.TotalAmount },
                cancellationToken: ct)).ConfigureAwait(false);

        foreach (var item in e.Items)
        {
            await _db.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO item_stock
                      (stock_id, tenant_id, item_id,
                       warehouse_id, current_qty,
                       avg_cost, last_updated_at)
                    VALUES
                      (UUID(), @TenantId, @ItemId,
                       'default', @Qty * -1,
                       @UnitPrice, NOW(6))
                    ON DUPLICATE KEY UPDATE
                      current_qty = current_qty - @Qty,
                      last_updated_at = NOW(6)
                    """,
                    new
                    {
                        e.TenantId,
                        item.ItemId,
                        item.Qty,
                        item.UnitPrice
                    },
                    cancellationToken: ct)).ConfigureAwait(false);
        }

        // monthly_summary 가산 — 멱등 가드 (작4 P0-4)
        await HitPan.Application.Services.MonthlySummaryGuard.TryApplyAsync(
            _db, dbTx: null,
            tenantId: e.TenantId,
            date: DateTime.Now,
            sourceType: "delivery_confirmed",
            sourceId: e.DeliveryId,
            field: HitPan.Application.Services.MonthlySummaryGuard.SummaryField.TotalSales,
            amount: e.TotalAmount,
            ct: ct).ConfigureAwait(false);

        // 사장님 헌법 (2026-04-27): 판매 확정으로 재고가 빠졌으니 안전재고 미달 자재가 새로 생겼을 수 있음.
        // BOM 트리거 외에서도 평소 안전망이 작동하도록 모든 트리거 끝에 CheckSafetyStockAsync 호출.
        // 상품마스터 화면이 stock_alerts 'pending' 을 읽어 배너로 노출.
        await CheckSafetyStockAsync(e.TenantId, e.Items.Select(x => x.ItemId).ToList(), ct).ConfigureAwait(false);
    }

    private async Task OnDeliveryCancelled(DeliveryCancelledEvent c, CancellationToken ct)
    {
        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE partner_balance
                SET total_sales = GREATEST(0, total_sales - @Amount),
                    last_updated_at = NOW(6)
                WHERE tenant_id = @TenantId
                  AND partner_id = @PartnerId
                """,
                new { c.TenantId, c.PartnerId, Amount = c.TotalAmount },
                cancellationToken: ct)).ConfigureAwait(false);

        foreach (var item in c.Items)
        {
            await _db.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE item_stock
                    SET current_qty = current_qty + @Qty,
                        last_updated_at = NOW(6)
                    WHERE tenant_id = @TenantId
                      AND item_id = @ItemId
                    """,
                    new { c.TenantId, item.ItemId, item.Qty },
                    cancellationToken: ct)).ConfigureAwait(false);
        }

        var ym = DateTime.Now.ToString("yyyyMM");
        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE monthly_summary
                SET total_sales = GREATEST(0, total_sales - @Amount),
                    last_updated_at = NOW(6)
                WHERE tenant_id = @TenantId
                  AND `year_month` = @YearMonth
                """,
                new { c.TenantId, YearMonth = ym, Amount = c.TotalAmount },
                cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task OnPurchaseConfirmed(PurchaseConfirmedEvent p, CancellationToken ct)
    {
        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO partner_balance
                  (balance_id, tenant_id, partner_id,
                   total_sales, total_receipt,
                   total_purchase, total_payment,
                   last_updated_at)
                VALUES
                  (UUID(), @TenantId, @PartnerId,
                   0, 0, @Amount, 0, NOW(6))
                ON DUPLICATE KEY UPDATE
                  total_purchase = total_purchase + @Amount,
                  last_updated_at = NOW(6)
                """,
                new { p.TenantId, p.PartnerId, Amount = p.TotalAmount },
                cancellationToken: ct)).ConfigureAwait(false);

        foreach (var item in p.Items)
        {
            await _db.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO item_stock
                      (stock_id, tenant_id, item_id,
                       warehouse_id, current_qty,
                       avg_cost, last_updated_at)
                    VALUES
                      (UUID(), @TenantId, @ItemId,
                       'default', @Qty,
                       @UnitPrice, NOW(6))
                    ON DUPLICATE KEY UPDATE
                      avg_cost = (
                        (current_qty * avg_cost + @Qty * @UnitPrice) /
                        NULLIF(current_qty + @Qty, 0)
                      ),
                      current_qty = current_qty + @Qty,
                      last_updated_at = NOW(6)
                    """,
                    new
                    {
                        p.TenantId,
                        item.ItemId,
                        item.Qty,
                        item.UnitPrice
                    },
                    cancellationToken: ct)).ConfigureAwait(false);
        }

        // monthly_summary 가산 — 멱등 가드 (작4 P0-4)
        await HitPan.Application.Services.MonthlySummaryGuard.TryApplyAsync(
            _db, dbTx: null,
            tenantId: p.TenantId,
            date: DateTime.Now,
            sourceType: "purchase_confirmed",
            sourceId: p.PoId,
            field: HitPan.Application.Services.MonthlySummaryGuard.SummaryField.TotalPurchase,
            amount: p.TotalAmount,
            ct: ct).ConfigureAwait(false);

        // 사장님 헌법 (2026-04-27): 매입 확정 후에도 안전재고 체크.
        // 입고된 자재가 안전재고 회복했는지(=pending alert 자동 정리는 별도 P1) +
        // 동일 거래에 들어가지 않은 다른 자재가 그 사이 안전재고 미달이 됐을 수 있어 일괄 점검.
        await CheckSafetyStockAsync(p.TenantId, p.Items.Select(x => x.ItemId).ToList(), ct).ConfigureAwait(false);
    }

    private async Task OnBomAssembled(BomAssembledEvent b, CancellationToken ct)
    {
        // ※ item_stock 업데이트는 BomService.AssembleAsync에서 이미 처리됨
        //   여기서는 월별 요약·안전재고 알림만 담당 (이중 처리 방지)

        // monthly_summary 가산 — 멱등 가드 (작4 P0-4)
        var bomCost = b.ProductionCost * b.ProducedQty;
        await HitPan.Application.Services.MonthlySummaryGuard.TryApplyAsync(
            _db, dbTx: null,
            tenantId: b.TenantId,
            date: DateTime.Now,
            sourceType: "bom_assembled",
            sourceId: b.BomId,
            field: HitPan.Application.Services.MonthlySummaryGuard.SummaryField.TotalPurchase,
            amount: bomCost,
            ct: ct).ConfigureAwait(false);

        await CheckSafetyStockAsync(b.TenantId, b.Materials.Select(x => x.ItemId).ToList(), ct).ConfigureAwait(false);
    }

    private async Task CheckSafetyStockAsync(string tenantId, List<string> itemIds, CancellationToken ct)
    {
        foreach (var itemId in itemIds.Distinct(StringComparer.Ordinal))
        {
            var info = await _db.QueryFirstOrDefaultAsync<SafetyStockInfo>(new CommandDefinition(
                """
                SELECT
                  COALESCE(s.current_qty, 0) AS CurrentQty,
                  COALESCE(i.safety_stock, i.safe_stock, 0) AS SafetyStock,
                  COALESCE(i.auto_order_enabled, 0) AS AutoOrderEnabled,
                  i.auto_order_partner_id AS PartnerId,
                  COALESCE(i.auto_order_qty, 0) AS OrderQty
                FROM items i
                LEFT JOIN item_stock s
                  ON s.tenant_id = i.tenant_id
                 AND s.item_id = i.item_id
                WHERE i.item_id = @ItemId
                  AND i.tenant_id = @TenantId
                """,
                new { ItemId = itemId, TenantId = tenantId },
                cancellationToken: ct)).ConfigureAwait(false);

            if (info is null || info.CurrentQty > info.SafetyStock || info.SafetyStock <= 0)
            {
                continue;
            }

            var exists = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*) FROM stock_alerts
                WHERE tenant_id = @TenantId
                  AND item_id = @ItemId
                  AND status = 'pending'
                """,
                new { TenantId = tenantId, ItemId = itemId },
                cancellationToken: ct)).ConfigureAwait(false);
            if (exists > 0)
            {
                continue;
            }

            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO stock_alerts (
                  alert_id, tenant_id, item_id,
                  alert_type, current_qty, safety_qty, shortage_qty,
                  partner_id, order_qty, status, created_at, updated_at)
                VALUES (
                  UUID(), @TenantId, @ItemId,
                  'safety_stock', @CurrentQty, @SafetyQty, @ShortageQty,
                  @PartnerId, @OrderQty, 'pending', NOW(6), NOW(6))
                """,
                new
                {
                    TenantId = tenantId,
                    ItemId = itemId,
                    CurrentQty = info.CurrentQty,
                    SafetyQty = info.SafetyStock,
                    ShortageQty = info.SafetyStock - info.CurrentQty,
                    PartnerId = info.PartnerId,
                    OrderQty = info.OrderQty
                },
                cancellationToken: ct)).ConfigureAwait(false);
        }
    }

    private sealed class SafetyStockInfo
    {
        public decimal CurrentQty { get; set; }
        public decimal SafetyStock { get; set; }
        public bool AutoOrderEnabled { get; set; }
        public string? PartnerId { get; set; }
        public decimal OrderQty { get; set; }
    }
}
