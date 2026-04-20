using System.Data;
using Dapper;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using HitPan.Domain.Enums;

namespace HitPan.Application.Services;

public class SalesService : ISalesService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDbConnection _db;
    private readonly IPartnerService _partnerService;

    public SalesService(
        IUnitOfWork unitOfWork,
        ICurrentTenant currentTenant,
        IDbConnection db,
        IPartnerService partnerService)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
        _db = db;
        _partnerService = partnerService;
    }

    public async Task<string> CreateOrderAsync(CreateSalesOrderRequest request, CancellationToken ct = default)
    {
        var orderRepo = _unitOfWork.Repository<SalesOrder>();
        var itemRepo = _unitOfWork.Repository<SalesOrderItem>();

        var date = request.OrderDate == default ? DateTime.UtcNow.Date : request.OrderDate.Date;
        var prefix = $"SO-{date:yyyyMMdd}-";
        var today = await orderRepo.FindAsync(x => x.OrderNo.StartsWith(prefix));
        var orderNo = $"{prefix}{today.Count + 1:000}";

        var orderId = Guid.NewGuid().ToString();
        var order = new SalesOrder
        {
            Id = orderId,
            OrderId = orderId,
            TenantId = _currentTenant.TenantId,
            OrderNo = orderNo,
            PartnerId = request.PartnerId,
            EmployeeId = request.EmployeeId,
            OrderDate = date,
            DeliveryDate = request.DeliveryDate,
            Status = SalesOrderStatus.Draft,
            TotalAmount = request.Items.Sum(x => x.SupplyAmount),
            VatAmount = request.Items.Sum(x => x.VatAmount),
            Memo = request.Memo
        };
        await orderRepo.AddAsync(order);

        foreach (var line in request.Items)
        {
            await itemRepo.AddAsync(new SalesOrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderItemId = Guid.NewGuid().ToString(),
                OrderId = orderId,
                TenantId = _currentTenant.TenantId,
                ItemId = line.ItemId,
                OrderedQty = line.OrderedQty,
                DeliveredQty = 0m,
                UnitPrice = line.UnitPrice,
                SupplyAmount = line.SupplyAmount,
                VatAmount = line.VatAmount,
                ItemStatus = "pending"
            });
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return orderId;
    }

    public async Task<(string Id, string DocumentNumber)> CreateDeliveryAsync(CreateDeliveryRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.PartnerId))
        {
            throw new InvalidOperationException("거래처를 선택해주세요.");
        }

        if (request.Items.Count == 0)
        {
            throw new InvalidOperationException("품목이 한 줄 이상 필요합니다.");
        }

        var deliveryRepo = _unitOfWork.Repository<SalesDelivery>();
        var itemRepo = _unitOfWork.Repository<SalesDeliveryItem>();

        const string whSql = """
                             SELECT warehouse_id
                             FROM warehouses
                             WHERE tenant_id = @TenantId
                               AND is_active = 1
                             ORDER BY warehouse_id
                             LIMIT 1
                             """;

        var defaultWarehouseId = await _db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(whSql, new { TenantId = _currentTenant.TenantId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(defaultWarehouseId))
        {
            throw new InvalidOperationException("등록된 창고가 없습니다.");
        }

        var date = request.DeliveryDate == default ? DateTime.UtcNow.Date : request.DeliveryDate.Date;
        var prefix = $"SD-{date:yyyyMMdd}-";
        var today = await deliveryRepo.FindAsync(x => x.DeliveryNo.StartsWith(prefix));
        var deliveryNo = $"{prefix}{today.Count + 1:000}";

        var deliveryId = Guid.NewGuid().ToString();
        var delivery = new SalesDelivery
        {
            Id = deliveryId,
            DeliveryId = deliveryId,
            TenantId = _currentTenant.TenantId,
            DeliveryNo = deliveryNo,
            OrderId = request.OrderId,
            PartnerId = request.PartnerId,
            EmployeeId = request.EmployeeId,
            DeliveryDate = date,
            SourceType = string.IsNullOrWhiteSpace(request.OrderId) ? "direct" : "from_order",
            Status = SalesDeliveryStatus.Draft,
            TotalAmount = request.Items.Sum(x => x.SupplyAmount),
            VatAmount = request.Items.Sum(x => x.VatAmount),
            Memo = request.Memo
        };
        await deliveryRepo.AddAsync(delivery);

        foreach (var line in request.Items)
        {
            var warehouseId = string.IsNullOrWhiteSpace(line.WarehouseId) ? defaultWarehouseId : line.WarehouseId;

            var itemId = line.ItemId?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(itemId))
            {
                var name = line.ItemName?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidOperationException("품목 ID 또는 품명이 필요합니다.");
                }

                const string itemResolveSql = """
                                              SELECT item_id
                                              FROM items
                                              WHERE tenant_id = @TenantId
                                                AND item_name = @ItemName
                                                AND is_active = 1
                                              ORDER BY item_id
                                              LIMIT 1
                                              """;

                itemId = await _db.QueryFirstOrDefaultAsync<string>(
                             new CommandDefinition(
                                 itemResolveSql,
                                 new { TenantId = _currentTenant.TenantId, ItemName = name },
                                 cancellationToken: ct))
                         ?? throw new InvalidOperationException($"등록된 품목을 찾을 수 없습니다: {name}");
            }

            await itemRepo.AddAsync(new SalesDeliveryItem
            {
                Id = Guid.NewGuid().ToString(),
                DeliveryItemId = Guid.NewGuid().ToString(),
                DeliveryId = deliveryId,
                TenantId = _currentTenant.TenantId,
                OrderItemId = line.OrderItemId,
                ItemId = itemId,
                WarehouseId = warehouseId,
                Qty = line.Qty,
                UnitPrice = line.UnitPrice,
                SupplyAmount = line.SupplyAmount,
                VatAmount = line.VatAmount
            });
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return (deliveryId, deliveryNo);
    }

    public async Task ConfirmDeliveryAsync(string deliveryId, ConfirmDeliveryRequest request, CancellationToken ct = default)
    {
        var deliveryRepo = _unitOfWork.Repository<SalesDelivery>();
        var deliveryItemRepo = _unitOfWork.Repository<SalesDeliveryItem>();
        var orderItemRepo = _unitOfWork.Repository<SalesOrderItem>();
        var workflowRepo = _unitOfWork.Repository<WorkflowSetting>();
        var ledgerRepo = _unitOfWork.Repository<StockLedger>();

        var delivery = await deliveryRepo.GetByIdAsync(deliveryId)
            ?? throw new InvalidOperationException("거래명세서를 찾을 수 없습니다.");
        if (delivery.Status != SalesDeliveryStatus.Draft)
        {
            throw new InvalidOperationException("draft 상태 전표만 확정할 수 있습니다.");
        }

        var lines = await deliveryItemRepo.FindAsync(x => x.DeliveryId == deliveryId);

        if (!string.IsNullOrWhiteSpace(delivery.OrderId))
        {
            var allowSetting = await workflowRepo.FindAsync(x => x.SettingKey == "sales.over_delivery_allow" && x.IsActive);
            var overDeliveryAllow = allowSetting.FirstOrDefault()?.SettingValue == "true";
            if (!overDeliveryAllow)
            {
                foreach (var line in lines.Where(x => !string.IsNullOrWhiteSpace(x.OrderItemId)))
                {
                    var orderItem = await orderItemRepo.GetByIdAsync(line.OrderItemId!);
                    if (orderItem is null)
                    {
                        throw new InvalidOperationException("매칭된 수주 라인을 찾을 수 없습니다.");
                    }

                    if (orderItem.DeliveredQty + line.Qty > orderItem.OrderedQty)
                    {
                        throw new InvalidOperationException("수주 잔량을 초과하여 출고할 수 없습니다.");
                    }
                }
            }
        }

        var negativeStockSetting = await workflowRepo.FindAsync(x => x.SettingKey == "stock.negative_stock_allow" && x.IsActive);
        var negativeStockAllow = negativeStockSetting.FirstOrDefault()?.SettingValue == "true";

        if (!negativeStockAllow)
        {
            foreach (var line in lines)
            {
                var balances = await ledgerRepo.FindAsync(x => x.ItemId == line.ItemId && x.WarehouseId == line.WarehouseId);
                var currentBalance = balances.Sum(x => x.QtyIn - x.QtyOut);
                if (currentBalance - line.Qty < 0m)
                {
                    throw new InvalidOperationException("재고가 부족합니다.");
                }
            }
        }

        foreach (var line in lines)
        {
            await ledgerRepo.AddAsync(new StockLedger
            {
                TenantId = delivery.TenantId,
                ItemId = line.ItemId,
                WarehouseId = line.WarehouseId,
                PartnerId = delivery.PartnerId,
                EmployeeId = delivery.EmployeeId,
                LedgerDate = delivery.DeliveryDate,
                Ym = delivery.DeliveryDate.ToString("yyyy-MM"),
                MoveType = StockMoveType.Out,
                SourceType = "sales_delivery",
                SourceId = delivery.DeliveryId,
                DocNo = delivery.DeliveryNo,
                QtyIn = 0m,
                QtyOut = line.Qty,
                UnitCost = line.UnitPrice,
                SupplyAmount = line.SupplyAmount
            });
        }

        if (!string.IsNullOrWhiteSpace(delivery.OrderId))
        {
            foreach (var line in lines.Where(x => !string.IsNullOrWhiteSpace(x.OrderItemId)))
            {
                var orderItem = await orderItemRepo.GetByIdAsync(line.OrderItemId!);
                if (orderItem is null)
                {
                    continue;
                }

                orderItem.DeliveredQty += line.Qty;
                if (orderItem.DeliveredQty <= 0m)
                {
                    orderItem.ItemStatus = "pending";
                }
                else if (orderItem.DeliveredQty < orderItem.OrderedQty)
                {
                    orderItem.ItemStatus = "partial";
                }
                else
                {
                    orderItem.ItemStatus = "closed";
                }
                orderItemRepo.Update(orderItem);
            }
        }

        delivery.Status = SalesDeliveryStatus.Confirmed;
        deliveryRepo.Update(delivery);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<DeliveryDetailDto?> GetDeliveryAsync(string deliveryId, string tenantId, CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               d.delivery_id AS DeliveryId,
                               d.delivery_no AS DeliveryNo,
                               d.delivery_date AS OrderDate,
                               d.partner_id AS PartnerId,
                               p.partner_name AS PartnerName,
                               (d.total_amount + d.vat_amount) AS TotalAmount,
                               d.vat_amount AS VatAmount,
                               d.total_amount AS SupplyAmount,
                               d.status AS Status,
                               d.memo AS Memo,
                               CAST(0 AS DECIMAL(15,2)) AS CashAmount,
                               CAST(0 AS DECIMAL(15,2)) AS CardAmount,
                               CAST(0 AS DECIMAL(15,2)) AS DiscountAmount,
                               d.employee_id AS EmployeeId,
                               e.emp_name AS EmployeeName
                           FROM sales_deliveries d
                           LEFT JOIN partners p
                               ON p.partner_id = d.partner_id
                                  AND p.tenant_id = d.tenant_id
                           LEFT JOIN employees e
                               ON e.employee_id = d.employee_id
                                  AND e.tenant_id = d.tenant_id
                           WHERE d.delivery_id = @DeliveryId
                             AND d.tenant_id = @TenantId
                           """;

        var delivery = await _db.QueryFirstOrDefaultAsync<DeliveryDetailDto>(
            new CommandDefinition(sql, new { DeliveryId = deliveryId, TenantId = tenantId }, cancellationToken: ct));

        if (delivery is null)
        {
            return null;
        }

        const string itemSql = """
                               SELECT
                                   di.item_id AS ItemId,
                                   it.item_name AS ItemName,
                                   CAST(NULL AS CHAR(100)) AS Spec,
                                   it.unit AS Unit,
                                   di.qty AS Qty,
                                   di.unit_price AS UnitPrice,
                                   di.supply_amount AS Amount,
                                   di.vat_amount AS VatAmount,
                                   CAST(NULL AS CHAR(500)) AS Memo,
                                   0 AS RowNo
                               FROM sales_delivery_items di
                               LEFT JOIN items it
                                   ON it.item_id = di.item_id
                                      AND it.tenant_id = di.tenant_id
                               WHERE di.delivery_id = @DeliveryId
                                 AND di.tenant_id = @TenantId
                               ORDER BY di.delivery_item_id
                               """;

        var items = (await _db.QueryAsync<DeliveryItemDto>(
                new CommandDefinition(itemSql, new { DeliveryId = deliveryId, TenantId = tenantId }, cancellationToken: ct)))
            .ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].RowNo = i + 1;
        }

        delivery.Items = items;

        const string balanceSql = """
                                  SELECT COALESCE(receivable_balance, 0)
                                  FROM v_partner_balance
                                  WHERE partner_id = @PartnerId
                                    AND tenant_id = @TenantId
                                  """;

        delivery.PrevReceivable = await _db.QueryFirstOrDefaultAsync<decimal>(
            new CommandDefinition(balanceSql, new { delivery.PartnerId, TenantId = tenantId }, cancellationToken: ct));

        const string todaySql = """
                                SELECT COALESCE(SUM(d.total_amount + d.vat_amount), 0)
                                FROM sales_deliveries d
                                WHERE d.tenant_id = @TenantId
                                  AND d.partner_id = @PartnerId
                                  AND d.delivery_date = CURDATE()
                                  AND d.status <> 'cancelled'
                                """;

        delivery.TodaySales = await _db.QueryFirstOrDefaultAsync<decimal>(
            new CommandDefinition(todaySql, new { TenantId = tenantId, delivery.PartnerId }, cancellationToken: ct));

        delivery.TodayReceipt = 0m;
        return delivery;
    }

    public async Task<List<DeliveryListDto>> GetDeliveriesAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? partnerName = null,
        string? status = null,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               d.delivery_id AS DeliveryId,
                               d.delivery_no AS DeliveryNo,
                               d.delivery_date AS OrderDate,
                               d.partner_id AS PartnerId,
                               p.partner_name AS PartnerName,
                               (d.total_amount + d.vat_amount) AS TotalAmount,
                               d.vat_amount AS VatAmount,
                               d.total_amount AS SupplyAmount,
                               d.status AS Status,
                               d.memo AS Memo
                           FROM sales_deliveries d
                           LEFT JOIN partners p
                               ON p.partner_id = d.partner_id
                                  AND p.tenant_id = d.tenant_id
                           WHERE d.tenant_id = @TenantId
                             AND (@From IS NULL OR d.delivery_date >= @From)
                             AND (@To IS NULL OR d.delivery_date <= @To)
                             AND (@PartnerName IS NULL OR p.partner_name LIKE CONCAT('%', @PartnerName, '%'))
                             AND (@Status IS NULL OR d.status = @Status)
                           ORDER BY d.delivery_date DESC,
                                    d.delivery_no DESC
                           LIMIT 200
                           """;

        var rows = await _db.QueryAsync<DeliveryListDto>(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    From = from?.Date,
                    To = to?.Date,
                    PartnerName = partnerName,
                    Status = status
                },
                cancellationToken: ct));

        return rows.ToList();
    }

    public async Task UpdateDeliveryAsync(
        string deliveryId,
        UpdateDeliveryDto dto,
        string tenantId,
        string userId,
        CancellationToken ct = default)
    {
        const string assertSql = """
                                 SELECT status
                                 FROM sales_deliveries
                                 WHERE delivery_id = @DeliveryId
                                   AND tenant_id = @TenantId
                                 """;

        var status = await _db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(assertSql, new { DeliveryId = deliveryId, TenantId = tenantId }, cancellationToken: ct));

        if (status is null)
        {
            throw new InvalidOperationException("거래명세서를 찾을 수 없습니다.");
        }

        if (!string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("draft 상태 전표만 수정할 수 있습니다.");
        }

        if (dto.Items.Count == 0)
        {
            throw new InvalidOperationException("품목이 한 줄 이상 필요합니다.");
        }

        const string whSql = """
                             SELECT warehouse_id
                             FROM warehouses
                             WHERE tenant_id = @TenantId
                               AND is_active = 1
                             ORDER BY warehouse_id
                             LIMIT 1
                             """;

        var defaultWarehouseId = await _db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(whSql, new { TenantId = tenantId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(defaultWarehouseId))
        {
            throw new InvalidOperationException("등록된 창고가 없습니다.");
        }

        var supplyAmount = dto.Items.Sum(x => x.Amount);
        var vatAmount = dto.Items.Sum(x => x.VatAmount);

        const string updateSql = """
                                 UPDATE sales_deliveries SET
                                     delivery_date = @OrderDate,
                                     partner_id = @PartnerId,
                                     memo = @Memo,
                                     total_amount = @SupplyAmount,
                                     vat_amount = @VatAmount,
                                     updated_at = NOW(6),
                                     updated_by = @UserId
                                 WHERE delivery_id = @DeliveryId
                                   AND tenant_id = @TenantId
                                   AND status = 'draft'
                                 """;

        await _db.ExecuteAsync(
            new CommandDefinition(
                updateSql,
                new
                {
                    DeliveryId = deliveryId,
                    TenantId = tenantId,
                    OrderDate = dto.OrderDate.Date,
                    PartnerId = dto.PartnerId,
                    Memo = dto.Memo,
                    SupplyAmount = supplyAmount,
                    VatAmount = vatAmount,
                    UserId = string.IsNullOrEmpty(userId) ? null : userId
                },
                cancellationToken: ct));

        await _db.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM sales_delivery_items WHERE delivery_id = @DeliveryId AND tenant_id = @TenantId",
                new { DeliveryId = deliveryId, TenantId = tenantId },
                cancellationToken: ct));

        var row = 0;
        foreach (var item in dto.Items)
        {
            row++;
            var itemId = string.IsNullOrWhiteSpace(item.ItemId) ? Guid.NewGuid().ToString() : item.ItemId;
            await _db.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO sales_delivery_items
                        (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, warehouse_id,
                         qty, unit_price, supply_amount, vat_amount)
                    VALUES
                        (@DeliveryItemId, @DeliveryId, @TenantId, NULL, @ItemId, @WarehouseId,
                         @Qty, @UnitPrice, @SupplyAmount, @VatAmount)
                    """,
                    new
                    {
                        DeliveryItemId = Guid.NewGuid().ToString(),
                        DeliveryId = deliveryId,
                        TenantId = tenantId,
                        item.ItemId,
                        WarehouseId = defaultWarehouseId,
                        item.Qty,
                        item.UnitPrice,
                        SupplyAmount = item.Amount,
                        item.VatAmount
                    },
                    cancellationToken: ct));
        }
    }

    public async Task DeleteDeliveryAsync(string deliveryId, string tenantId, CancellationToken ct = default)
    {
        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE sales_deliveries
                SET status = 'cancelled',
                    updated_at = NOW(6)
                WHERE delivery_id = @DeliveryId
                  AND tenant_id = @TenantId
                  AND status = 'draft'
                """,
                new { DeliveryId = deliveryId, TenantId = tenantId },
                cancellationToken: ct));
    }

    public async Task<List<DeliveryListDto>> GetOrdersAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               o.order_id AS DeliveryId,
                               o.order_no AS DeliveryNo,
                               o.order_date AS OrderDate,
                               o.partner_id AS PartnerId,
                               p.partner_name AS PartnerName,
                               (o.total_amount + o.vat_amount) AS TotalAmount,
                               o.vat_amount AS VatAmount,
                               o.total_amount AS SupplyAmount,
                               o.status AS Status,
                               o.memo AS Memo
                           FROM sales_orders o
                           LEFT JOIN partners p
                               ON p.partner_id = o.partner_id
                                  AND p.tenant_id = o.tenant_id
                           WHERE o.tenant_id = @TenantId
                             AND (@From IS NULL OR o.order_date >= @From)
                             AND (@To IS NULL OR o.order_date <= @To)
                             AND (@Status IS NULL OR o.status = @Status)
                           ORDER BY o.order_date DESC,
                                    o.order_no DESC
                           LIMIT 200
                           """;

        var rows = await _db.QueryAsync<DeliveryListDto>(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    From = from?.Date,
                    To = to?.Date,
                    Status = string.IsNullOrWhiteSpace(status) ? null : status
                },
                cancellationToken: ct));

        return rows.ToList();
    }

    /// <summary>
    /// 수주서를 거래명세서로 전환한다. 미출고 품목이 없으면 차단한다.
    /// </summary>
    public async Task<(string DeliveryId, string DocumentNumber)> ConvertOrderToDeliveryAsync(
        string orderId,
        string tenantId,
        CancellationToken ct = default)
    {
        var orderRepo = _unitOfWork.Repository<SalesOrder>();
        var orderItemRepo = _unitOfWork.Repository<SalesOrderItem>();

        var order = await orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException("수주서를 찾을 수 없습니다.");

        if (order.TenantId != tenantId)
        {
            throw new InvalidOperationException("수주서를 찾을 수 없습니다.");
        }

        var items = await orderItemRepo.FindAsync(x => x.OrderId == orderId);
        var deliveryItems = items
            .Where(x => x.OrderedQty - x.DeliveredQty > 0)
            .Select(x => new CreateDeliveryItemRequest
            {
                OrderItemId = x.OrderItemId,
                ItemId = x.ItemId,
                Qty = x.OrderedQty - x.DeliveredQty,
                UnitPrice = x.UnitPrice,
                SupplyAmount = (x.OrderedQty - x.DeliveredQty) * x.UnitPrice,
                VatAmount = Math.Round((x.OrderedQty - x.DeliveredQty) * x.UnitPrice * 0.1m, 0)
            }).ToList();

        if (deliveryItems.Count == 0)
        {
            throw new InvalidOperationException("전환 가능한 미출고 품목이 없습니다.");
        }

        var request = new CreateDeliveryRequest
        {
            OrderId = orderId,
            PartnerId = order.PartnerId,
            EmployeeId = order.EmployeeId,
            DeliveryDate = DateTime.UtcNow.Date,
            Memo = $"수주 {order.OrderNo} 에서 전환",
            Items = deliveryItems
        };

        return await CreateDeliveryAsync(request, ct);
    }

    public Task<List<PartnerSearchDto>> SearchPartnersAsync(string tenantId, string keyword, CancellationToken ct = default)
    {
        return _partnerService.SearchPartnersAsync(tenantId, keyword, ct);
    }
}
