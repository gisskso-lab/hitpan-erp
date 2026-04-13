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

        var ym = DateTime.Now.ToString("yyyyMM");
        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO monthly_summary
                  (summary_id, tenant_id, `year_month`,
                   total_sales, total_purchase,
                   total_receipt, total_payment,
                   last_updated_at)
                VALUES
                  (UUID(), @TenantId, @YearMonth,
                   @Amount, 0, 0, 0, NOW(6))
                ON DUPLICATE KEY UPDATE
                  total_sales = total_sales + @Amount,
                  last_updated_at = NOW(6)
                """,
                new { e.TenantId, YearMonth = ym, Amount = e.TotalAmount },
                cancellationToken: ct)).ConfigureAwait(false);
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

        var ym = DateTime.Now.ToString("yyyyMM");
        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO monthly_summary
                  (summary_id, tenant_id, `year_month`,
                   total_sales, total_purchase,
                   total_receipt, total_payment,
                   last_updated_at)
                VALUES
                  (UUID(), @TenantId, @YearMonth,
                   0, @Amount, 0, 0, NOW(6))
                ON DUPLICATE KEY UPDATE
                  total_purchase = total_purchase + @Amount,
                  last_updated_at = NOW(6)
                """,
                new { p.TenantId, YearMonth = ym, Amount = p.TotalAmount },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}
