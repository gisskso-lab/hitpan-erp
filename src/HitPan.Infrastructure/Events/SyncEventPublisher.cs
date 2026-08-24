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
        // ※ item_stock·monthly_summary 갱신은 SalesService.ConfirmDeliveryAsync 동일 트랜잭션 내에서 이미 처리됨
        //   (WO-20260503-10 갈래1: 이중 차감 방지 — OnBomAssembled 와 동일 패턴)
        //   여기서는 partner_balance · 안전재고 알림만 담당 (이중 처리 방지)

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

        // 사장님 헌법 (2026-04-27): 판매 확정으로 재고가 빠졌으니 안전재고 미달 자재가 새로 생겼을 수 있음.
        // BOM 트리거 외에서도 평소 안전망이 작동하도록 모든 트리거 끝에 CheckSafetyStockAsync 호출.
        // 상품마스터 화면이 stock_alerts 'pending' 을 읽어 배너로 노출.
        await CheckSafetyStockAsync(e.TenantId, e.Items.Select(x => x.ItemId).ToList(), ct).ConfigureAwait(false);
    }

    private async Task OnDeliveryCancelled(DeliveryCancelledEvent c, CancellationToken ct)
    {
        // ※ item_stock·monthly_summary 환원은 SalesService.CancelConfirmedDeliveryAsync 에서 처리됨
        //   (WO-20260503-10 갈래1: 이중 환원 방지)
        //   여기서는 partner_balance.total_sales 환원만 담당.

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
    }

    private async Task OnPurchaseConfirmed(PurchaseConfirmedEvent p, CancellationToken ct)
    {
        // ※ item_stock·monthly_summary 갱신은 PurchaseService.ConfirmReceiptAsync 동일 트랜잭션 내에서 이미 처리됨
        //   (WO-20260503-10 갈래1: 이중 가산 방지)
        //   여기서는 partner_balance · 안전재고 알림만 담당.

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

        // 사장님 헌법 (2026-04-27): 매입 확정 후에도 안전재고 체크.
        // 입고된 자재가 안전재고 회복했는지(=pending alert 자동 정리는 별도 P1) +
        // 동일 거래에 들어가지 않은 다른 자재가 그 사이 안전재고 미달이 됐을 수 있어 일괄 점검.
        await CheckSafetyStockAsync(p.TenantId, p.Items.Select(x => x.ItemId).ToList(), ct).ConfigureAwait(false);
    }

    private async Task OnBomAssembled(BomAssembledEvent b, CancellationToken ct)
    {
        // ※ item_stock 업데이트는 BomService.AssembleAsync에서 이미 처리됨
        // ※ BOM 제조원가는 내부 제조비용이므로 total_purchase(외부 매입)에 가산하지 않음 (20260506작3 봉합)

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

            // 봉합 (2026-08-25, 20260825작1 W1): BomService.GetAlertsAsync 와 동일 결함.
            //   'pending' 만 보면 이미 발주된('ordered') 품목에 알림이 다시 생긴다.
            //   두 경로의 가드를 일치시킨다 — 한쪽만 고치면 이벤트 경로로 유령 알림이 되살아난다.
            var exists = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*) FROM stock_alerts
                WHERE tenant_id = @TenantId
                  AND item_id = @ItemId
                  AND status IN ('pending', 'ordered')
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
