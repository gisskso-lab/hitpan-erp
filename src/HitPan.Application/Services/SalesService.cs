using System.Data;
using Dapper;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using HitPan.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace HitPan.Application.Services;

public class SalesService : ISalesService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDbConnection _db;
    private readonly IPartnerService _partnerService;
    private readonly IAuditService _audit;
    private readonly IServiceProvider _services;

    public SalesService(
        IUnitOfWork unitOfWork,
        ICurrentTenant currentTenant,
        IDbConnection db,
        IPartnerService partnerService,
        IAuditService audit,
        IServiceProvider services)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
        _db = db;
        _partnerService = partnerService;
        _audit = audit;
        _services = services;
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

        // 합계 0원 판매는 확정 금지 — journal_lines CHECK 제약 위반 방지(§20 워크플로우 오염 차단).
        if (delivery.TotalAmount + delivery.VatAmount <= 0m)
        {
            throw new InvalidOperationException("합계가 0원인 거래명세서는 확정할 수 없습니다. 품목·수량·단가를 확인해주세요.");
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

            // 3) monthly_summary 매출 갱신 — 멱등 가드 (작4 P0-4, 동일 tx)
            await MonthlySummaryGuard.TryApplyAsync(
                conn, dbTx,
                tenantId: delivery.TenantId,
                date: delivery.DeliveryDate,
                sourceType: "delivery_confirmed",
                sourceId: delivery.DeliveryId,
                field: MonthlySummaryGuard.SummaryField.TotalSales,
                amount: delivery.TotalAmount + delivery.VatAmount,
                ct: ct);

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
                             AND (d.is_deleted = 0 OR d.is_deleted IS NULL)
                             AND d.status <> 'cancelled'
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
        // 사장님 지시 (2026-04-26): 거래명세서는 전자계산서 발행 전이면 삭제 가능.
        //   - 권한은 컨트롤러에서 SalesManager 정책으로 이미 강제됨.
        //   - tax_invoices 에 delivery_id 발행 레코드 있으면 거부 (감사·세무 무결성).
        //   - status='draft'    → cancelled 표시
        //   - status='confirmed' → CancelConfirmedDeliveryAsync 로 Reverse 원장 발행
        //     (재고·원장·회계 모두 복귀, INSERT ONLY 원칙 유지).

        var invoiced = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM tax_invoices WHERE delivery_id=@Id AND tenant_id=@Tid",
            new { Id = deliveryId, Tid = tenantId }, cancellationToken: ct));
        if (invoiced > 0)
        {
            throw new InvalidOperationException("전자계산서가 발행된 거래명세서는 삭제할 수 없습니다. 계산서를 먼저 취소해주세요.");
        }

        var status = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT status FROM sales_deliveries WHERE delivery_id=@Id AND tenant_id=@Tid",
            new { Id = deliveryId, Tid = tenantId }, cancellationToken: ct));
        if (string.IsNullOrEmpty(status)) return; // 이미 없음

        if (string.Equals(status, "confirmed", StringComparison.OrdinalIgnoreCase))
        {
            // 확정된 거래는 Reverse 경로 — 재고·잔액·회계 무결성 유지.
            await CancelConfirmedDeliveryAsync(deliveryId, tenantId, employeeId: null, ct);
            return;
        }

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

        await _audit.LogAsync("delete", "sales_delivery", deliveryId, ct: ct);
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

    // ─────────────────────────────────────────────────────────────────────
    // 수주서 단건 조회 — 목록 클릭 → 편집 화면 로드용.
    // ─────────────────────────────────────────────────────────────────────
    public async Task<SalesOrderDetailDto?> GetOrderDetailAsync(
        string orderId, string tenantId, CancellationToken ct = default)
    {
        const string headerSql = """
            SELECT o.order_id     AS OrderId,
                   o.order_no     AS OrderNo,
                   o.order_date   AS OrderDate,
                   o.delivery_date AS DeliveryDate,
                   o.partner_id   AS PartnerId,
                   COALESCE(p.partner_name, '') AS PartnerName,
                   o.total_amount AS TotalAmount,
                   o.vat_amount   AS VatAmount,
                   o.status       AS Status,
                   o.memo         AS Memo
              FROM sales_orders o
              LEFT JOIN partners p
                ON p.partner_id = o.partner_id
               AND p.tenant_id  = o.tenant_id
             WHERE o.order_id  = @Id
               AND o.tenant_id = @Tid
               AND o.is_deleted = 0
            """;

        var header = await _db.QueryFirstOrDefaultAsync<SalesOrderDetailDto>(
            new CommandDefinition(headerSql, new { Id = orderId, Tid = tenantId }, cancellationToken: ct));
        if (header is null) return null;

        const string linesSql = """
            SELECT soi.order_item_id AS OrderItemId,
                   soi.item_id       AS ItemId,
                   COALESCE(i.item_name, '') AS ItemName,
                   COALESCE(i.spec, '')      AS Spec,
                   IFNULL(i.unit, 'EA')      AS Unit,
                   soi.ordered_qty   AS Qty,
                   soi.unit_price    AS UnitPrice,
                   soi.supply_amount AS SupplyAmount,
                   soi.vat_amount    AS VatAmount
              FROM sales_order_items soi
              LEFT JOIN items i
                ON i.item_id   = soi.item_id
               AND i.tenant_id = soi.tenant_id
             WHERE soi.order_id  = @Id
               AND soi.tenant_id = @Tid
             ORDER BY soi.order_item_id
            """;

        var lines = await _db.QueryAsync<SalesOrderDetailItemDto>(
            new CommandDefinition(linesSql, new { Id = orderId, Tid = tenantId }, cancellationToken: ct));
        header.Items = lines.ToList();
        return header;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 수주서 draft 삭제 — soft delete. 판매전환된 라인 있으면 차단.
    // ─────────────────────────────────────────────────────────────────────
    public async Task DeleteSalesOrderAsync(string orderId, string tenantId, CancellationToken ct = default)
    {
        var row = await _db.QueryFirstOrDefaultAsync<(string Status, byte IsDeleted)?>(new CommandDefinition(
            "SELECT status AS Status, is_deleted AS IsDeleted FROM sales_orders WHERE order_id=@Id AND tenant_id=@Tid",
            new { Id = orderId, Tid = tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException("수주서를 찾을 수 없습니다.");

        if (row.IsDeleted == 1)
        {
            throw new InvalidOperationException("이미 삭제된 수주서입니다.");
        }

        // 판매전환된 라인 차단 — 단, 거래명세서가 cancelled 면 무시(사장님 정책: 삭제=취소).
        var activeDelivered = await _db.QueryFirstOrDefaultAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*)
              FROM sales_delivery_items di
              JOIN sales_deliveries sd ON sd.delivery_id = di.delivery_id AND sd.tenant_id = di.tenant_id
             WHERE di.order_item_id IN (
                     SELECT order_item_id FROM sales_order_items
                      WHERE order_id=@Id AND tenant_id=@Tid
                   )
               AND di.tenant_id = @Tid
               AND sd.status <> 'cancelled'
            """,
            new { Id = orderId, Tid = tenantId }, cancellationToken: ct));
        if (activeDelivered > 0)
        {
            throw new InvalidOperationException("이미 판매전환(출고)된 라인이 있어 삭제할 수 없습니다. 거래명세서를 먼저 취소해주세요.");
        }

        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE sales_orders SET is_deleted=1, updated_at=NOW(6) WHERE order_id=@Id AND tenant_id=@Tid",
            new { Id = orderId, Tid = tenantId }, cancellationToken: ct));

        await _audit.LogAsync("delete", "sales_order", orderId, ct: ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 자동발주 후보 조회 — 거래명세서 확정 직후 안전재고 위반 품목 추출.
    // 사장님 지시 (2026-04-26): 판매 반영 시 재고가 안전재고 이하/0 이면
    //   "자동발주 하시겠습니까?" 다이얼로그를 띄울 후보를 내려준다.
    // 조건: 라인 품목 중 auto_order_enabled=1 AND
    //       (item_stock 합계 <= items.safety_stock OR <= 0)
    // ─────────────────────────────────────────────────────────────────────
    public async Task<List<AutoOrderCandidateDto>> GetAutoOrderCandidatesAsync(
        string deliveryId, string tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT
                   i.item_id        AS ItemId,
                   IFNULL(i.item_code,'') AS ItemCode,
                   i.item_name      AS ItemName,
                   COALESCE(s.qty, 0) AS CurrentQty,
                   COALESCE(i.safety_stock, i.safe_stock, 0) AS SafetyQty,
                   COALESCE(i.auto_order_qty, 0) AS SuggestedOrderQty,
                   i.auto_order_partner_id AS PartnerId,
                   p.partner_name   AS PartnerName,
                   COALESCE(i.purchase_price, i.cost_price, 0) AS UnitPrice,
                   CASE
                     WHEN COALESCE(s.qty, 0) <= 0 THEN 'out_of_stock'
                     ELSE 'below_safety'
                   END AS Reason
              FROM sales_delivery_items di
              JOIN items i
                ON i.item_id = di.item_id AND i.tenant_id = di.tenant_id
              LEFT JOIN (
                   SELECT tenant_id, item_id, SUM(current_qty) AS qty
                     FROM item_stock GROUP BY tenant_id, item_id
              ) s ON s.tenant_id = i.tenant_id AND s.item_id = i.item_id
              LEFT JOIN partners p
                ON p.partner_id = i.auto_order_partner_id AND p.tenant_id = i.tenant_id
             WHERE di.delivery_id = @DeliveryId
               AND di.tenant_id   = @Tid
               AND IFNULL(i.auto_order_enabled, 0) = 1
               AND (
                     COALESCE(s.qty, 0) <= COALESCE(i.safety_stock, i.safe_stock, 0)
                  OR COALESCE(s.qty, 0) <= 0
                   )
            """;

        var rows = await _db.QueryAsync<AutoOrderCandidateDto>(new CommandDefinition(
            sql, new { DeliveryId = deliveryId, Tid = tenantId }, cancellationToken: ct));
        return rows.ToList();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 자동발주 즉시 생성 — 사장님 지시 (2026-04-26): 다이얼로그 OK 시
    //   "바로 자동발주가 되어야 정상이지". 공급처별로 묶어 발주서(draft) 1건씩 생성.
    // 공급처 미설정 품목은 스킵 + 사유 반환(워크플로우 §20 끊김 금지).
    // ─────────────────────────────────────────────────────────────────────
    public async Task<List<AutoOrderResultDto>> CreateAutoOrdersAsync(
        IReadOnlyList<AutoOrderCandidateDto> candidates, string tenantId, bool autoReceive = false, CancellationToken ct = default)
    {
        var results = new List<AutoOrderResultDto>();
        if (candidates.Count == 0) return results;

        // 공급처별 그룹핑. 미지정 품목은 별도 실패 결과.
        var noPartner = candidates.Where(c => string.IsNullOrWhiteSpace(c.PartnerId)).ToList();
        if (noPartner.Count > 0)
        {
            results.Add(new AutoOrderResultDto
            {
                Success = false,
                Reason = $"{noPartner.Count}개 품목에 자동발주 공급처 미설정 — 상품마스터에서 지정 필요.",
                ItemIds = noPartner.Select(x => x.ItemId).ToList()
            });
        }

        var groups = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.PartnerId))
            .GroupBy(c => c.PartnerId!);

        var today = DateTime.Today;
        var prefix = $"PO-{today:yyyyMMdd}-";

        foreach (var grp in groups)
        {
            var partnerId = grp.Key;
            var lines = grp.ToList();
            var supply = lines.Sum(x => Math.Max(x.SuggestedOrderQty, 1m) * x.UnitPrice);
            var vat = Math.Round(supply * 0.1m, 0, MidpointRounding.AwayFromZero);

            using var tx = _db.BeginTransaction();
            try
            {
                var cnt = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM purchase_orders WHERE tenant_id=@Tid AND po_no LIKE CONCAT(@Prefix, '%')",
                    new { Tid = tenantId, Prefix = prefix }, transaction: tx, cancellationToken: ct));
                var poNo = $"{prefix}{cnt + 1:000}";
                var poId = Guid.NewGuid().ToString();

                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO purchase_orders
                      (po_id, tenant_id, po_no, partner_id, po_date, status, total_amount, vat_amount, memo, created_at, updated_at)
                    VALUES
                      (@PoId, @Tid, @PoNo, @PartnerId, @PoDate, 'draft', @Supply, @Vat, @Memo, NOW(6), NOW(6))
                    """,
                    new
                    {
                        PoId = poId, Tid = tenantId, PoNo = poNo,
                        PartnerId = partnerId, PoDate = today,
                        Supply = supply, Vat = vat,
                        Memo = "안전재고 자동발주 (판매확정 트리거)"
                    }, transaction: tx, cancellationToken: ct));

                foreach (var line in lines)
                {
                    var qty = line.SuggestedOrderQty > 0 ? line.SuggestedOrderQty : Math.Max(line.SafetyQty - line.CurrentQty, 1m);
                    var lineSupply = qty * line.UnitPrice;
                    var lineVat = Math.Round(lineSupply * 0.1m, 0, MidpointRounding.AwayFromZero);

                    await _db.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO purchase_order_items
                          (po_item_id, po_id, tenant_id, item_id, ordered_qty, received_qty, unit_price, supply_amount, vat_amount, item_status)
                        VALUES
                          (UUID(), @PoId, @Tid, @ItemId, @Qty, 0, @UnitPrice, @Supply, @Vat, 'pending')
                        """,
                        new
                        {
                            PoId = poId, Tid = tenantId, ItemId = line.ItemId,
                            Qty = qty, UnitPrice = line.UnitPrice,
                            Supply = lineSupply, Vat = lineVat
                        }, transaction: tx, cancellationToken: ct));
                }

                tx.Commit();
                await _audit.LogAsync("create", "purchase_order", poId,
                    afterJson: $"{{\"source\":\"auto_order\",\"po_no\":\"{poNo}\",\"item_count\":{lines.Count}}}",
                    ct: ct);

                var resultRow = new AutoOrderResultDto
                {
                    Success = true,
                    PoId = poId,
                    PoNo = poNo,
                    PartnerId = partnerId,
                    PartnerName = lines[0].PartnerName,
                    ItemIds = lines.Select(x => x.ItemId).ToList()
                };

                // 사장님 지시 (2026-04-26): 자동발주 → 매입처리까지 원클릭.
                // autoReceive=true 면 발주 직후 매입전환 + 매입 확정까지 진행 → 자재 재고 즉시 +반영.
                if (autoReceive)
                {
                    try
                    {
                        var purSvc = _services.GetService<IPurchaseService>()
                            ?? throw new InvalidOperationException("매입 서비스를 찾을 수 없습니다.");
                        var (receiptId, receiptNo) = await purSvc.ConvertOrderToReceiptAsync(poId, tenantId, ct);
                        await purSvc.ConfirmReceiptAsync(receiptId, new ConfirmReceiptRequest(), ct);
                        resultRow.Reason = $"매입 자동확정: {receiptNo}";
                    }
                    catch (Exception ex)
                    {
                        // 발주는 성공했으니 결과는 Success=true 유지하되 사유에 매입 실패 표시.
                        resultRow.Reason = $"발주 OK / 매입 자동확정 실패: {ex.Message}";
                    }
                }

                results.Add(resultRow);
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { /* 이미 닫힌 tx */ }
                results.Add(new AutoOrderResultDto
                {
                    Success = false,
                    PartnerId = partnerId,
                    PartnerName = lines[0].PartnerName,
                    ItemIds = lines.Select(x => x.ItemId).ToList(),
                    Reason = ex.Message
                });
            }
        }

        return results;
    }
}
