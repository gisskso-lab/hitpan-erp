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
    private readonly IAuditService _audit;

    public SalesService(
        IUnitOfWork unitOfWork,
        ICurrentTenant currentTenant,
        IDbConnection db,
        IPartnerService partnerService,
        IAuditService audit)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
        _db = db;
        _partnerService = partnerService;
        _audit = audit;
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

        // 감사로그 — 수주서 생성
        var soAfterJson = $"{{\"order_no\":\"{orderNo}\",\"partner_id\":\"{request.PartnerId}\",\"item_count\":{request.Items.Count}}}";
        await _audit.LogAsync("create", "sales_order", orderId, afterJson: soAfterJson, ct: ct);

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

        // 1+1 기획상품(promo) 자동 2배 처리 — 영업이 1개 입력해도 시스템이 2개로 기록
        await ApplyPromoDoubleAsync(request.Items, ct);

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

        // 감사로그 — 거래명세서 생성 (초안)
        var delAfterJson = $"{{\"delivery_no\":\"{deliveryNo}\",\"partner_id\":\"{request.PartnerId}\",\"item_count\":{request.Items.Count}}}";
        await _audit.LogAsync("create", "sales_delivery", deliveryId, afterJson: delAfterJson, ct: ct);

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

        // 월마감 체크 — 마감된 월의 전표 확정 차단
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, delivery.TenantId, delivery.DeliveryDate, ct);

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

        // 조립상품(assembly) BOM 폭파 — 자재별 추가 OUT 원장 생성
        await ExplodeAssemblyBomAsync(delivery, lines, ledgerRepo, ct);

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

        // ── 단일 트랜잭션 (EF + Dapper 공유) ──
        // 브라운킴 지적 듀얼 트랜잭션 해소: EF의 DbContext 트랜잭션을 시작하고
        // Dapper는 DbContext의 실연결·트랜잭션으로 실행 → 중간 실패 시 전체 롤백.
        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // 1) EF 변경 저장 (stock_ledger INSERT + status='confirmed' + order_items UPDATE)
            await _unitOfWork.SaveChangesAsync(ct);

            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            // 2) item_stock 차감 (Dapper · 동일 tx)
            foreach (var line in lines)
            {
                const string updateStockSql = """
                    INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                    VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, -@Qty, @UnitCost, NOW(6))
                    ON DUPLICATE KEY UPDATE
                      current_qty = current_qty - @Qty,
                      last_updated_at = NOW(6)
                    """;

                await conn.ExecuteAsync(new CommandDefinition(
                    updateStockSql,
                    new
                    {
                        TenantId = delivery.TenantId,
                        ItemId = line.ItemId,
                        WarehouseId = line.WarehouseId,
                        Qty = line.Qty,
                        UnitCost = line.UnitPrice
                    },
                    transaction: dbTx,
                    cancellationToken: ct));
            }

            // 3) monthly_summary 매출 갱신 (Dapper · 동일 tx)
            var ymStr = delivery.DeliveryDate.ToString("yyyyMM");
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO monthly_summary (summary_id, tenant_id, `year_month`, total_sales, total_purchase, total_receipt, total_payment, last_updated_at)
                VALUES (UUID(), @TenantId, @Ym, @Sales, 0, 0, 0, NOW(6))
                ON DUPLICATE KEY UPDATE
                  total_sales = total_sales + @Sales,
                  last_updated_at = NOW(6)
                """,
                new
                {
                    TenantId = delivery.TenantId,
                    Ym = ymStr,
                    Sales = delivery.TotalAmount + delivery.VatAmount
                },
                transaction: dbTx,
                cancellationToken: ct));

            // 4) 회계 자동 기표 (차변 외상매출금 / 대변 매출+부가세예수금)
            await AutoJournalHelper.RecordSalesConfirmAsync(
                conn, dbTx,
                delivery.TenantId,
                delivery.DeliveryId,
                delivery.DeliveryNo,
                delivery.DeliveryDate,
                delivery.PartnerId,
                delivery.TotalAmount,
                delivery.VatAmount,
                delivery.EmployeeId,
                ct);

            // 5) 전체 커밋 — EF + Dapper 쓰기가 원자적으로 확정
            await tx.CommitAsync(ct);

            // 감사로그 (트랜잭션 밖)
            await _audit.LogAsync("confirm", "sales_delivery", deliveryId, ct: ct);
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* 이미 닫힌 tx */ }
            throw;
        }

        // 결재 트리거: 결재 설정이 ON이면 결재 문서 자동 생성 (커밋 이후 실행)
        await ApprovalTriggerHelper.TryCreateApprovalAsync(_db,
            "delivery", delivery.DeliveryId, delivery.DeliveryNo,
            $"거래명세서 확정: {delivery.DeliveryNo}",
            delivery.TotalAmount + delivery.VatAmount,
            delivery.TenantId, delivery.EmployeeId ?? "system", "확정자", ct);
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

        // 1+1 기획상품 자동 2배 (UpdateDelivery 경로에도 적용)
        await ApplyPromoDoubleToUpdateAsync(dto.Items, tenantId, ct);

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

        // 트랜잭션으로 헤더 UPDATE + 품목 DELETE/INSERT를 원자적으로 묶는다.
        if (_db.State != ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync(ct);
            else _db.Open();
        }
        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(updateSql,
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
                transaction: tx, cancellationToken: ct));

            await _db.ExecuteAsync(new CommandDefinition(
                "DELETE FROM sales_delivery_items WHERE delivery_id = @DeliveryId AND tenant_id = @TenantId",
                new { DeliveryId = deliveryId, TenantId = tenantId },
                transaction: tx, cancellationToken: ct));

            foreach (var item in dto.Items)
            {
                await _db.ExecuteAsync(new CommandDefinition(
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
                    transaction: tx, cancellationToken: ct));
            }

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { /* already closed */ }
            throw;
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

    /// <summary>
    /// 확정된 거래명세서 취소 — Reverse 원장 발행으로 재고·잔액 복귀.
    /// 원장은 INSERT ONLY 원칙을 유지하고 move_type='in'의 역행 원장을 새로 기록한다.
    /// 조립상품(BOM 폭파)도 자재 역행 IN으로 복귀시킨다.
    /// </summary>
    public async Task CancelConfirmedDeliveryAsync(string deliveryId, string tenantId, string? employeeId, CancellationToken ct = default)
    {
        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            "SELECT delivery_id, delivery_no, partner_id, delivery_date, status, total_amount, vat_amount FROM sales_deliveries WHERE delivery_id=@Id AND tenant_id=@Tid",
            new { Id = deliveryId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("거래명세서를 찾을 수 없습니다.");

        if ((string)header.status != "confirmed")
        {
            throw new InvalidOperationException("confirmed 상태만 취소할 수 있습니다. (draft은 삭제 사용)");
        }

        if (_db.State != ConnectionState.Open)
        {
            if (_db is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync(ct);
            else _db.Open();
        }
        using var tx = _db.BeginTransaction();
        try
        {
            var items = (await _db.QueryAsync<dynamic>(new CommandDefinition(
                "SELECT item_id, warehouse_id, qty, unit_price, supply_amount FROM sales_delivery_items WHERE delivery_id=@Id AND tenant_id=@Tid",
                new { Id = deliveryId, Tid = tenantId }, transaction: tx, cancellationToken: ct))).ToList();

            DateTime dd = (DateTime)header.delivery_date;
            string ym = dd.ToString("yyyy-MM");

            // 1) 원본 완제품 OUT의 역행 IN 원장
            foreach (var it in items)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO stock_ledger
                      (tenant_id, item_id, warehouse_id, partner_id, employee_id, ledger_date, ym,
                       move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo)
                    VALUES
                      (@Tid, @ItemId, @Wh, @PartnerId, @EmpId, @Date, @Ym,
                       'in', 'sales_cancel', @Did, @DocNo, @Qty, 0, @UnitPrice, @Supply, '매출취소 Reverse')
                    """,
                    new
                    {
                        Tid = tenantId,
                        ItemId = (string)it.item_id,
                        Wh = (string)it.warehouse_id,
                        PartnerId = (string)header.partner_id,
                        EmpId = employeeId,
                        Date = dd, Ym = ym,
                        Did = deliveryId,
                        DocNo = (string)header.delivery_no,
                        Qty = (decimal)it.qty,
                        UnitPrice = (decimal)it.unit_price,
                        Supply = (decimal)it.supply_amount
                    },
                    transaction: tx, cancellationToken: ct));
            }

            // 2) BOM 폭파 자재의 역행 IN 원장 (조립상품 판매였다면)
            const string bomReverseSql = """
                INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, employee_id,
                  ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo)
                SELECT @Tid, l.item_id, l.warehouse_id, l.partner_id, @EmpId,
                  @Date, @Ym, 'in', 'bom_explosion_cancel', @Did, @DocNo,
                  l.qty_out, 0, 0, 0, '조립취소 자재복귀'
                FROM stock_ledger l
                WHERE l.source_id=@Did AND l.source_type='bom_explosion' AND l.tenant_id=@Tid
                """;
            await _db.ExecuteAsync(new CommandDefinition(bomReverseSql,
                new
                {
                    Tid = tenantId,
                    EmpId = employeeId,
                    Date = dd, Ym = ym,
                    Did = deliveryId,
                    DocNo = (string)header.delivery_no
                },
                transaction: tx, cancellationToken: ct));

            // 3) item_stock 복귀 (완제품 + 자재)
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                SELECT UUID(), @Tid, item_id, warehouse_id, qty, unit_price, NOW(6)
                FROM sales_delivery_items WHERE delivery_id=@Did AND tenant_id=@Tid
                ON DUPLICATE KEY UPDATE current_qty = current_qty + VALUES(current_qty), last_updated_at=NOW(6)
                """,
                new { Tid = tenantId, Did = deliveryId },
                transaction: tx, cancellationToken: ct));

            // 4) 연결된 수금(collections) 무효화 — ref_doc이 이 명세서인 수금 전부
            var voidedCollections = await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE collections
                SET is_active=0, updated_at=NOW(6)
                WHERE tenant_id=@Tid AND ref_doc_type='sales_delivery' AND ref_doc_id=@Did AND is_active=1
                """,
                new { Tid = tenantId, Did = deliveryId },
                transaction: tx, cancellationToken: ct));

            // 5) partner_balance 재계산 (매출 차감 + 수금 역산)
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE partner_balance pb
                SET total_sales = COALESCE((SELECT SUM(total_amount+vat_amount) FROM sales_deliveries
                                            WHERE tenant_id=@Tid AND partner_id=@Pid AND status='confirmed'), 0),
                    total_receipt = COALESCE((SELECT SUM(amount) FROM collections
                                              WHERE tenant_id=@Tid AND partner_id=@Pid AND is_active=1
                                                AND ref_doc_type='sales_delivery'), 0),
                    last_updated_at = NOW(6)
                WHERE tenant_id=@Tid AND partner_id=@Pid
                """,
                new { Tid = tenantId, Pid = (string)header.partner_id },
                transaction: tx, cancellationToken: ct));

            // 6) 상태 변경
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE sales_deliveries SET status='cancelled', updated_at=NOW(6) WHERE delivery_id=@Id AND tenant_id=@Tid",
                new { Id = deliveryId, Tid = tenantId },
                transaction: tx, cancellationToken: ct));

            tx.Commit();

            await _audit.LogAsync("cancel", "sales_delivery", deliveryId,
                beforeJson: $"{{\"status\":\"confirmed\"}}",
                afterJson: $"{{\"status\":\"cancelled\",\"reverse_ledger\":true}}", ct: ct);
        }
        catch
        {
            try { tx.Rollback(); } catch { /* already closed */ }
            throw;
        }
    }

    public async Task<List<SalesOrderListDto>> GetOrdersAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? status = null,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               o.order_id AS OrderId,
                               o.order_no AS OrderNo,
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

        var rows = await _db.QueryAsync<SalesOrderListDto>(
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

    // 결재 트리거는 ApprovalTriggerHelper.TryCreateApprovalAsync로 통합됨

    // ─────────────────────────────────────────────────────────────────────
    // 1+1 기획상품 런타임: promo 타입 품목의 qty·금액을 자동 2배 처리
    // 영업이 "1개" 입력해도 실제 2개가 기록되어 증정분 누락 방지.
    // ─────────────────────────────────────────────────────────────────────
    // UpdateDelivery 경로 전용 — DTO 타입 분기
    private async Task ApplyPromoDoubleToUpdateAsync(List<DeliveryItemDto> lines, string tenantId, CancellationToken ct)
    {
        var itemIds = lines.Where(x => !string.IsNullOrWhiteSpace(x.ItemId))
                           .Select(x => x.ItemId).Distinct().ToList();
        if (itemIds.Count == 0) return;

        const string sql = "SELECT item_id FROM items WHERE tenant_id=@TenantId AND item_type='promo' AND item_id IN @Ids";
        var promoIds = (await _db.QueryAsync<string>(
                           new CommandDefinition(sql, new { TenantId = tenantId, Ids = itemIds },
                                                 cancellationToken: ct))).ToHashSet();
        if (promoIds.Count == 0) return;

        foreach (var line in lines.Where(l => promoIds.Contains(l.ItemId)))
        {
            line.Qty *= 2m;
            line.Amount *= 2m;
            line.VatAmount *= 2m;
        }
    }

    private async Task ApplyPromoDoubleAsync(List<CreateDeliveryItemRequest> lines, CancellationToken ct)
    {
        var itemIds = lines.Where(x => !string.IsNullOrWhiteSpace(x.ItemId))
                           .Select(x => x.ItemId!).Distinct().ToList();
        if (itemIds.Count == 0) return;

        const string sql = "SELECT item_id, item_type FROM items WHERE tenant_id=@TenantId AND item_id IN @Ids";
        var rows = (await _db.QueryAsync<(string item_id, string item_type)>(
                        new CommandDefinition(sql, new { TenantId = _currentTenant.TenantId, Ids = itemIds },
                                              cancellationToken: ct))).ToList();
        var promoIds = rows.Where(r => r.item_type == "promo").Select(r => r.item_id).ToHashSet();
        if (promoIds.Count == 0) return;

        foreach (var line in lines)
        {
            if (line.ItemId != null && promoIds.Contains(line.ItemId))
            {
                line.Qty *= 2m;
                line.SupplyAmount *= 2m;
                line.VatAmount *= 2m;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 조립상품 BOM 폭파: assembly 품목 출고 시 BOM 자재별 추가 OUT 원장 생성
    // 완제품 OUT 원장은 유지(추적용), 자재 OUT은 이곳에서 추가 기록.
    // ─────────────────────────────────────────────────────────────────────
    private async Task ExplodeAssemblyBomAsync(
        SalesDelivery delivery,
        IReadOnlyList<SalesDeliveryItem> lines,
        IRepository<StockLedger> ledgerRepo,
        CancellationToken ct)
    {
        var itemIds = lines.Select(x => x.ItemId).Distinct().ToList();
        if (itemIds.Count == 0) return;

        const string assemblySql = "SELECT item_id FROM items WHERE tenant_id=@TenantId AND item_type='assembly' AND item_id IN @Ids";
        var assemblyIds = (await _db.QueryAsync<string>(
                              new CommandDefinition(assemblySql,
                                  new { TenantId = delivery.TenantId, Ids = itemIds },
                                  cancellationToken: ct))).ToHashSet();
        if (assemblyIds.Count == 0) return;

        const string bomSql = """
            SELECT bi.material_item_id AS MaterialItemId, bi.qty AS BomQty
            FROM bom_headers bh
            JOIN bom_items bi ON bi.bom_id = bh.bom_id
            WHERE bh.tenant_id=@TenantId
              AND bh.product_item_id=@ProductId
              AND bh.is_default=1
              AND bh.is_active=1
            """;

        foreach (var line in lines.Where(l => assemblyIds.Contains(l.ItemId)))
        {
            var materials = await _db.QueryAsync<(string MaterialItemId, decimal BomQty)>(
                new CommandDefinition(bomSql,
                    new { TenantId = delivery.TenantId, ProductId = line.ItemId },
                    cancellationToken: ct));

            foreach (var m in materials)
            {
                await ledgerRepo.AddAsync(new StockLedger
                {
                    TenantId = delivery.TenantId,
                    ItemId = m.MaterialItemId,
                    WarehouseId = line.WarehouseId,
                    PartnerId = delivery.PartnerId,
                    EmployeeId = delivery.EmployeeId,
                    LedgerDate = delivery.DeliveryDate,
                    Ym = delivery.DeliveryDate.ToString("yyyy-MM"),
                    MoveType = StockMoveType.Out,
                    SourceType = "bom_explosion",
                    SourceId = delivery.DeliveryId,
                    DocNo = delivery.DeliveryNo,
                    QtyIn = 0m,
                    QtyOut = line.Qty * m.BomQty,
                    UnitCost = 0m,
                    SupplyAmount = 0m,
                    Memo = $"조립 자재소비 (완제품: {line.ItemId})"
                });
            }
        }
    }
}
