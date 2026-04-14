using System.Data;
using Dapper;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;

namespace HitPan.API.Services;

public sealed class DeliveryBatchService
{
    private readonly IDbConnection _db;
    private readonly ICurrentTenant _currentTenant;

    public DeliveryBatchService(IDbConnection db, ICurrentTenant currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    public async Task<List<BatchSaveResult>> SaveBatchAsync(List<CreateDeliveryRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0)
        {
            return new List<BatchSaveResult>();
        }

        using var tx = _db.BeginTransaction();
        var results = new List<BatchSaveResult>(requests.Count);

        try
        {
            var tenantId = _currentTenant.TenantId;
            var now = DateTime.Now;
            var dateToken = now.ToString("yyyyMMdd");
            var deliveryDate = now.Date;

            var nextSeq = await GetNextSequenceAsync(tenantId, dateToken, tx, ct);
            var defaultWarehouseId = await GetDefaultWarehouseIdAsync(tenantId, tx, ct);

            foreach (var request in requests)
            {
                if (string.IsNullOrWhiteSpace(request.PartnerId))
                {
                    throw new InvalidOperationException("거래처를 선택해주세요.");
                }

                if (request.Items.Count == 0)
                {
                    throw new InvalidOperationException("품목이 한 줄 이상 필요합니다.");
                }

                var actualDate = request.DeliveryDate == default ? deliveryDate : request.DeliveryDate.Date;
                var no = $"DN-{dateToken}-{nextSeq:000}";
                nextSeq++;

                var deliveryId = Guid.NewGuid().ToString();
                var supply = request.Items.Sum(x => x.SupplyAmount);
                var vat = request.Items.Sum(x => x.VatAmount);

                const string insertDeliverySql = """
                                                 INSERT INTO sales_deliveries
                                                     (delivery_id, tenant_id, delivery_no, order_id, partner_id, employee_id,
                                                      delivery_date, source_type, status, total_amount, vat_amount, memo,
                                                      created_at, updated_at)
                                                 VALUES
                                                     (@DeliveryId, @TenantId, @DeliveryNo, @OrderId, @PartnerId, @EmployeeId,
                                                      @DeliveryDate, @SourceType, @Status, @TotalAmount, @VatAmount, @Memo,
                                                      NOW(6), NOW(6))
                                                 """;

                await _db.ExecuteAsync(new CommandDefinition(
                    insertDeliverySql,
                    new
                    {
                        DeliveryId = deliveryId,
                        TenantId = tenantId,
                        DeliveryNo = no,
                        request.OrderId,
                        request.PartnerId,
                        request.EmployeeId,
                        DeliveryDate = actualDate,
                        SourceType = string.IsNullOrWhiteSpace(request.OrderId) ? "direct" : "from_order",
                        Status = "draft",
                        TotalAmount = supply,
                        VatAmount = vat,
                        request.Memo
                    },
                    tx,
                    cancellationToken: ct));

                foreach (var item in request.Items)
                {
                    var itemId = item.ItemId?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(itemId))
                    {
                        itemId = await ResolveItemIdAsync(tenantId, item.ItemName, tx, ct);
                    }

                    var warehouseId = string.IsNullOrWhiteSpace(item.WarehouseId) ? defaultWarehouseId : item.WarehouseId;

                    const string insertItemSql = """
                                                 INSERT INTO sales_delivery_items
                                                     (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, warehouse_id,
                                                      qty, unit_price, supply_amount, vat_amount)
                                                 VALUES
                                                     (@DeliveryItemId, @DeliveryId, @TenantId, @OrderItemId, @ItemId, @WarehouseId,
                                                      @Qty, @UnitPrice, @SupplyAmount, @VatAmount)
                                                 """;

                    await _db.ExecuteAsync(new CommandDefinition(
                        insertItemSql,
                        new
                        {
                            DeliveryItemId = Guid.NewGuid().ToString(),
                            DeliveryId = deliveryId,
                            TenantId = tenantId,
                            item.OrderItemId,
                            ItemId = itemId,
                            WarehouseId = warehouseId,
                            item.Qty,
                            item.UnitPrice,
                            item.SupplyAmount,
                            item.VatAmount
                        },
                        tx,
                        cancellationToken: ct));
                }

                results.Add(new BatchSaveResult
                {
                    DeliveryId = deliveryId,
                    DeliveryNo = no,
                    Success = true
                });
            }

            tx.Commit();
            return results;
        }
        catch (Exception ex)
        {
            tx.Rollback();

            if (results.Count == 0)
            {
                return requests.Select(_ => new BatchSaveResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                }).ToList();
            }

            return results.Select(_ => new BatchSaveResult
            {
                Success = false,
                ErrorMessage = ex.Message
            }).ToList();
        }
    }

    private async Task<int> GetNextSequenceAsync(string tenantId, string yyyymmdd, IDbTransaction tx, CancellationToken ct)
    {
        const string sql = """
                           SELECT COALESCE(MAX(CAST(SUBSTRING(delivery_no, 13, 3) AS UNSIGNED)), 0)
                           FROM sales_deliveries
                           WHERE tenant_id = @TenantId
                             AND delivery_no LIKE @Pattern
                           FOR UPDATE
                           """;

        var maxSeq = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                Pattern = $"DN-{yyyymmdd}-%"
            },
            tx,
            cancellationToken: ct));

        return maxSeq + 1;
    }

    private async Task<string> GetDefaultWarehouseIdAsync(string tenantId, IDbTransaction tx, CancellationToken ct)
    {
        const string sql = """
                           SELECT warehouse_id
                           FROM warehouses
                           WHERE tenant_id = @TenantId
                             AND is_active = 1
                           ORDER BY warehouse_id
                           LIMIT 1
                           """;

        var warehouseId = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            sql,
            new { TenantId = tenantId },
            tx,
            cancellationToken: ct));

        if (string.IsNullOrWhiteSpace(warehouseId))
        {
            throw new InvalidOperationException("등록된 창고가 없습니다.");
        }

        return warehouseId;
    }

    private async Task<string> ResolveItemIdAsync(string tenantId, string? itemName, IDbTransaction tx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            throw new InvalidOperationException("품목 ID 또는 품명이 필요합니다.");
        }

        const string sql = """
                           SELECT item_id
                           FROM items
                           WHERE tenant_id = @TenantId
                             AND item_name = @ItemName
                             AND is_active = 1
                           ORDER BY item_id
                           LIMIT 1
                           """;

        var itemId = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, ItemName = itemName.Trim() },
            tx,
            cancellationToken: ct));

        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new InvalidOperationException($"등록된 품목을 찾을 수 없습니다: {itemName}");
        }

        return itemId;
    }
}
