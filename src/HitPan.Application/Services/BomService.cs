using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Bom;
using HitPan.Application.Events;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

public class BomService : IBomService
{
    private readonly IDbConnection _db;
    private readonly IEventPublisher _events;
    private readonly IAuditService _audit;

    public BomService(IDbConnection db, IEventPublisher events, IAuditService audit)
    {
        _db = db;
        _events = events;
        _audit = audit;
    }

    public async Task<List<BomListDto>> GetListAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var rows = await _db.QueryAsync<BomListDto>(new CommandDefinition(
            """
            SELECT
              bh.bom_id AS BomId,
              bh.product_item_id AS ProductItemId,
              i.item_name AS ProductItemName,
              bh.bom_name AS BomName,
              bh.bom_version AS BomVersion,
              bh.is_default AS IsDefault,
              bh.is_active AS IsActive,
              COUNT(bi.bom_item_id) AS MaterialCount,
              COALESCE(SUM(bi.qty * (1 + bi.loss_rate/100) * COALESCE(i2.purchase_price, i2.cost_price, 0)),0) AS TotalCost,
              bh.created_at AS CreatedAt
            FROM bom_headers bh
            LEFT JOIN items i ON i.item_id = bh.product_item_id
            LEFT JOIN bom_items bi ON bi.bom_id = bh.bom_id
            LEFT JOIN items i2 ON i2.item_id = bi.material_item_id
            WHERE bh.tenant_id = @TenantId
              AND bh.is_active = 1
            GROUP BY bh.bom_id, bh.product_item_id, i.item_name, bh.bom_name, bh.bom_version, bh.is_default, bh.is_active, bh.created_at
            ORDER BY i.item_name, bh.bom_version DESC
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<BomDetailDto?> GetAsync(string bomId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var header = await _db.QueryFirstOrDefaultAsync<BomDetailDto>(new CommandDefinition(
            """
            SELECT
              bh.bom_id AS BomId,
              bh.product_item_id AS ProductItemId,
              i.item_name AS ProductItemName,
              bh.bom_name AS BomName,
              bh.bom_version AS BomVersion,
              bh.is_default AS IsDefault,
              bh.is_active AS IsActive,
              bh.memo AS Memo
            FROM bom_headers bh
            LEFT JOIN items i ON i.item_id = bh.product_item_id
            WHERE bh.bom_id = @BomId
              AND bh.tenant_id = @TenantId
            """,
            new { BomId = bomId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        if (header is null) return null;

        var items = await _db.QueryAsync<BomItemDto>(new CommandDefinition(
            """
            SELECT
              bi.bom_item_id AS BomItemId,
              bi.seq_no AS SeqNo,
              bi.material_item_id AS MaterialItemId,
              i.item_name AS MaterialItemName,
              i.spec AS Spec,
              bi.unit AS Unit,
              bi.qty AS Qty,
              bi.loss_rate AS LossRate,
              bi.qty * (1 + bi.loss_rate/100) AS ActualQty,
              COALESCE(i.purchase_price, i.cost_price, 0) AS UnitCost,
              bi.qty * (1 + bi.loss_rate/100) * COALESCE(i.purchase_price, i.cost_price, 0) AS TotalCost,
              COALESCE(s.current_qty, 0) AS CurrentStock,
              COALESCE(i.safety_stock, i.safe_stock, 0) AS SafetyStock,
              COALESCE(i.auto_order_enabled, 0) AS AutoOrderEnabled,
              i.auto_order_partner_id AS AutoOrderPartnerId,
              COALESCE(i.auto_order_qty, 0) AS AutoOrderQty,
              bi.memo AS Memo,
              CASE WHEN EXISTS (
                SELECT 1 FROM bom_headers x
                WHERE x.product_item_id = bi.material_item_id
                  AND x.tenant_id = bi.tenant_id
                  AND x.is_active = 1
              ) THEN 1 ELSE 0 END AS HasChildBom
            FROM bom_items bi
            LEFT JOIN items i ON i.item_id = bi.material_item_id
            LEFT JOIN item_stock s ON s.tenant_id = bi.tenant_id AND s.item_id = bi.material_item_id
            WHERE bi.bom_id = @BomId
              AND bi.tenant_id = @TenantId
            ORDER BY bi.seq_no
            """,
            new { BomId = bomId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        header.Items = items.ToList();
        header.TotalCost = header.Items.Sum(x => x.TotalCost);
        return header;
    }

    public async Task<string> CreateAsync(CreateBomDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(dto.ProductItemId) &&
            await HasCircularRefAsync(dto.ProductItemId, dto.Items.Select(x => x.MaterialItemId).ToList(), tenantId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("순환 참조가 감지됐습니다. 자재 구성을 확인해주세요.");

        var bomId = Guid.NewGuid().ToString();
        if (dto.IsDefault)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE bom_headers SET is_default = 0 WHERE tenant_id=@TenantId AND product_item_id=@ItemId",
                new { TenantId = tenantId, ItemId = dto.ProductItemId }, cancellationToken: ct)).ConfigureAwait(false);
        }
        var maxVer = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(bom_version),0) FROM bom_headers WHERE tenant_id=@TenantId AND product_item_id=@ItemId",
            new { TenantId = tenantId, ItemId = dto.ProductItemId }, cancellationToken: ct)).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bom_headers
              (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active, memo, created_at, updated_at)
            VALUES
              (@BomId, @TenantId, @ProductItemId, @BomName, @Version, @IsDefault, 1, @Memo, NOW(6), NOW(6))
            """,
            new
            {
                BomId = bomId,
                TenantId = tenantId,
                ProductItemId = dto.ProductItemId,
                BomName = dto.BomName,
                Version = maxVer + 1,
                IsDefault = dto.IsDefault ? 1 : 0,
                Memo = dto.Memo
            }, cancellationToken: ct)).ConfigureAwait(false);

        foreach (var item in dto.Items)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bom_items
                  (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate, memo)
                VALUES
                  (UUID(), @BomId, @TenantId, @SeqNo, @MaterialItemId, @Qty, @Unit, @LossRate, @Memo)
                """,
                new
                {
                    BomId = bomId,
                    TenantId = tenantId,
                    SeqNo = item.SeqNo,
                    MaterialItemId = item.MaterialItemId,
                    Qty = item.Qty,
                    Unit = item.Unit,
                    LossRate = item.LossRate,
                    Memo = item.Memo
                }, cancellationToken: ct)).ConfigureAwait(false);
        }

        await UpdateCostCacheAsync(bomId, tenantId, ct).ConfigureAwait(false);

        // 감사로그 — BOM 신규 생성
        var afterJson = $"{{\"bom_name\":\"{dto.BomName}\",\"product_item_id\":\"{dto.ProductItemId}\",\"material_count\":{dto.Items?.Count ?? 0}}}";
        await _audit.LogAsync("create", "bom", bomId, afterJson: afterJson, ct: ct);

        return bomId;
    }

    public async Task UpdateAsync(string bomId, CreateBomDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        if (await HasCircularRefAsync(dto.ProductItemId, dto.Items.Select(x => x.MaterialItemId).ToList(), tenantId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("순환 참조가 감지됐습니다.");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE bom_headers SET
              bom_name=@BomName, is_default=@IsDefault, memo=@Memo, updated_at=NOW(6)
            WHERE bom_id=@BomId AND tenant_id=@TenantId
            """,
            new { BomId = bomId, TenantId = tenantId, BomName = dto.BomName, IsDefault = dto.IsDefault ? 1 : 0, Memo = dto.Memo },
            cancellationToken: ct)).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM bom_items WHERE bom_id=@BomId AND tenant_id=@TenantId",
            new { BomId = bomId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        foreach (var item in dto.Items)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bom_items
                  (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate, memo)
                VALUES
                  (UUID(), @BomId, @TenantId, @SeqNo, @MaterialItemId, @Qty, @Unit, @LossRate, @Memo)
                """,
                new { BomId = bomId, TenantId = tenantId, SeqNo = item.SeqNo, MaterialItemId = item.MaterialItemId, Qty = item.Qty, Unit = item.Unit, LossRate = item.LossRate, Memo = item.Memo },
                cancellationToken: ct)).ConfigureAwait(false);
        }
        await UpdateCostCacheAsync(bomId, tenantId, ct).ConfigureAwait(false);

        // 감사로그 — BOM 수정 (구성 자재 변경 포함)
        var afterJson = $"{{\"bom_name\":\"{dto.BomName}\",\"material_count\":{dto.Items?.Count ?? 0}}}";
        await _audit.LogAsync("update", "bom", bomId, afterJson: afterJson, ct: ct);
    }

    public async Task DeleteAsync(string bomId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE bom_headers SET is_active=0, updated_at=NOW(6) WHERE bom_id=@BomId AND tenant_id=@TenantId",
            new { BomId = bomId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        // 감사로그 — BOM 소프트 삭제
        await _audit.LogAsync("delete", "bom", bomId, ct: ct);
    }

    public async Task<string> RegisterBomAsItemAsync(string bomId, string itemType, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var bom = await GetAsync(bomId, tenantId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("BOM을 찾을 수 없습니다.");

        // BOM 이름으로 상품 생성
        var itemId = Guid.NewGuid().ToString();
        var itemCode = "BOM-" + itemId[..Math.Min(8, itemId.Length)];

        // BOM 원가를 매입단가로 설정
        var bomCost = bom.TotalCost;

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO items (
              item_id, tenant_id, item_code, item_name,
              item_group, item_type, category_id, unit, spec,
              purchase_price, sale_price, standard_price,
              tax_type, safety_stock, barcode, memo,
              auto_order_enabled, auto_order_partner_id, auto_order_qty,
              std_price, cost_price, safe_stock,
              is_active, is_deleted, row_version,
              created_at, updated_at)
            VALUES (
              @ItemId, @TenantId, @ItemCode, @ItemName,
              NULL, @ItemType, NULL, 'EA', NULL,
              @BomCost, 0, @BomCost,
              'taxable', 0, NULL, @Memo,
              0, NULL, 0,
              @BomCost, @BomCost, 0,
              1, 0, 0,
              NOW(6), NOW(6))
            """,
            new
            {
                ItemId = itemId,
                TenantId = tenantId,
                ItemCode = itemCode,
                ItemName = bom.BomName,
                ItemType = itemType,
                BomCost = bomCost,
                Memo = $"BOM 자동등록 (BOM ID: {bomId})"
            }, cancellationToken: ct)).ConfigureAwait(false);

        // BOM의 product_item_id 연결
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE bom_headers SET product_item_id = @ItemId, updated_at = NOW(6) WHERE bom_id = @BomId AND tenant_id = @TenantId",
            new { ItemId = itemId, BomId = bomId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        // 재고 초기화 — 기본 창고 조회(FK fk_is_warehouse 위반 방지).
        var defaultWhId = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT warehouse_id FROM warehouses
             WHERE tenant_id=@TenantId AND is_active=1
             ORDER BY (CASE WHEN wh_code IN ('MAIN','WH-MAIN') THEN 0 ELSE 1 END), wh_code
             LIMIT 1
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("활성 창고가 없습니다.");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
            VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, 0, @BomCost, NOW(6))
            ON DUPLICATE KEY UPDATE last_updated_at = NOW(6)
            """,
            new { TenantId = tenantId, ItemId = itemId, WarehouseId = defaultWhId, BomCost = bomCost },
            cancellationToken: ct)).ConfigureAwait(false);

        return itemId;
    }

    public async Task<BomAssembleCheckDto> CheckAssembleAsync(string bomId, decimal produceQty, string tenantId, CancellationToken ct = default)
    {
        var bom = await GetAsync(bomId, tenantId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("BOM을 찾을 수 없습니다.");
        var materials = new List<BomMaterialCheckDto>();
        decimal totalCost = 0;
        foreach (var item in bom.Items)
        {
            var required = Math.Ceiling(item.Qty * (1 + item.LossRate / 100m) * produceQty);
            string? partnerName = null;
            if (!string.IsNullOrWhiteSpace(item.AutoOrderPartnerId))
            {
                partnerName = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
                    "SELECT partner_name FROM partners WHERE partner_id=@Id",
                    new { Id = item.AutoOrderPartnerId }, cancellationToken: ct)).ConfigureAwait(false);
            }

            materials.Add(new BomMaterialCheckDto
            {
                ItemId = item.MaterialItemId,
                ItemName = item.MaterialItemName,
                Unit = item.Unit,
                RequiredQty = required,
                CurrentStock = item.CurrentStock,
                ShortageQty = Math.Max(0, required - item.CurrentStock),
                IsEnough = item.CurrentStock >= required,
                AutoOrderPartnerId = item.AutoOrderPartnerId,
                AutoOrderPartnerName = partnerName,
                AutoOrderQty = item.AutoOrderQty,
                AutoOrderEnabled = item.AutoOrderEnabled
            });
            totalCost += item.UnitCost * required;
        }

        return new BomAssembleCheckDto
        {
            BomId = bomId,
            ProduceQty = produceQty,
            Materials = materials,
            CanProduce = materials.All(x => x.IsEnough),
            TotalCost = totalCost
        };
    }

    public async Task AssembleAsync(BomAssembleDto dto, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var check = await CheckAssembleAsync(dto.BomId, dto.ProduceQty, tenantId, ct).ConfigureAwait(false);
        var bom = await GetAsync(dto.BomId, tenantId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("BOM을 찾을 수 없습니다.");

        // 기본 창고 ID 확보 ("default" 하드코딩 제거 — fk_sal_warehouse FK 위반 방지).
        // 테넌트의 활성 창고 중 wh_code='MAIN'(또는 'WH-MAIN') 우선, 없으면 첫 활성 창고.
        var defaultWarehouseId = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT warehouse_id FROM warehouses
             WHERE tenant_id=@TenantId AND is_active=1
             ORDER BY (CASE WHEN wh_code IN ('MAIN','WH-MAIN') THEN 0 ELSE 1 END), wh_code
             LIMIT 1
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("활성 창고가 없습니다. 창고 마스터에 기본 창고를 등록하세요.");

        // ── 트랜잭션 감싸기 (자재차감 + 완성품증가 + stock_ledger 일괄 atomicity 보장) ──
        // 이전 버그: 중간 실패 시 자재는 차감됐는데 완성품 안 올라가거나, 이중 처리되는 현상
        using var tx = _db.BeginTransaction();
        try
        {
            decimal productionCost = 0;
            var materials = new List<BomMaterialUsedEvent>();
            foreach (var mat in check.Materials)
            {
                var unitCost = await _db.QueryFirstOrDefaultAsync<decimal>(new CommandDefinition(
                    "SELECT COALESCE(purchase_price, cost_price, 0) FROM items WHERE item_id=@ItemId",
                    new { ItemId = mat.ItemId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                productionCost += unitCost * mat.RequiredQty;
                materials.Add(new BomMaterialUsedEvent(mat.ItemId, mat.RequiredQty, unitCost));

                // 자재 재고 차감 로그
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO stock_adjust_logs (
                      adjust_id, tenant_id, item_id, warehouse_id, before_qty, after_qty, adjust_qty,
                      before_cost, after_cost, reason, user_id, created_at)
                    SELECT
                      UUID(), @TenantId, @ItemId, @WarehouseId,
                      COALESCE(current_qty, 0), COALESCE(current_qty, 0) - @Qty, @Qty * -1,
                      COALESCE(avg_cost, 0), COALESCE(avg_cost, 0), @Reason, @UserId, NOW(6)
                    FROM item_stock
                    WHERE tenant_id=@TenantId AND item_id=@ItemId
                    """,
                    new
                    {
                        TenantId = tenantId,
                        ItemId = mat.ItemId,
                        WarehouseId = defaultWarehouseId,
                        Qty = mat.RequiredQty,
                        Reason = $"BOM생산:{bom.BomName} {dto.ProduceQty}개",
                        UserId = userId
                    }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                // 자재 재고 실제 차감
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE item_stock
                    SET current_qty = GREATEST(current_qty - @Qty, 0), last_updated_at = NOW(6)
                    WHERE tenant_id = @TenantId AND item_id = @ItemId
                    """,
                    new { TenantId = tenantId, ItemId = mat.ItemId, Qty = mat.RequiredQty },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            // 완성품 재고 증가 로그
            var unitProductionCost = productionCost / Math.Max(dto.ProduceQty, 1);
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO stock_adjust_logs (
                  adjust_id, tenant_id, item_id, warehouse_id, before_qty, after_qty, adjust_qty,
                  before_cost, after_cost, reason, user_id, created_at)
                SELECT
                  UUID(), @TenantId, @ItemId, @WarehouseId,
                  COALESCE(current_qty, 0), COALESCE(current_qty, 0) + @Qty, @Qty,
                  COALESCE(avg_cost, 0), @UnitCost, @Reason, @UserId, NOW(6)
                FROM item_stock
                WHERE tenant_id=@TenantId AND item_id=@ItemId
                """,
                new
                {
                    TenantId = tenantId,
                    ItemId = bom.ProductItemId,
                    WarehouseId = defaultWarehouseId,
                    Qty = dto.ProduceQty,
                    UnitCost = unitProductionCost,
                    Reason = $"BOM생산:{bom.BomName} {dto.ProduceQty}개 완성",
                    UserId = userId
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // 완성품 재고 실제 증가
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, @Qty, @UnitCost, NOW(6))
                ON DUPLICATE KEY UPDATE
                  current_qty = current_qty + @Qty,
                  avg_cost = @UnitCost,
                  last_updated_at = NOW(6)
                """,
                new { TenantId = tenantId, ItemId = bom.ProductItemId, WarehouseId = defaultWarehouseId,
                      Qty = dto.ProduceQty, UnitCost = unitProductionCost },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // stock_ledger에 BOM 생산 기록 (수불부 정합성)
            foreach (var mat in materials)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
                      move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount)
                    VALUES (@TenantId, @ItemId, @WarehouseId, CURDATE(), DATE_FORMAT(CURDATE(),'%Y-%m'),
                      'out', 'bom_production', @BomId, @DocNo, 0, @Qty, @Cost, @Qty * @Cost)
                    """,
                    new { TenantId = tenantId, ItemId = mat.ItemId, WarehouseId = defaultWarehouseId,
                          BomId = dto.BomId, DocNo = bom.BomName, Qty = mat.UsedQty, Cost = mat.UnitCost },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }
            // 완성품 입고
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
                  move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount)
                VALUES (@TenantId, @ItemId, @WarehouseId, CURDATE(), DATE_FORMAT(CURDATE(),'%Y-%m'),
                  'in', 'bom_production', @BomId, @DocNo, @Qty, 0, @Cost, @Qty * @Cost)
                """,
                new { TenantId = tenantId, ItemId = bom.ProductItemId, WarehouseId = defaultWarehouseId,
                      BomId = dto.BomId, DocNo = bom.BomName, Qty = dto.ProduceQty, Cost = unitProductionCost },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            tx.Commit();

            // 감사로그 — BOM assemble (자재차감 + 완성품증가)
            var assembleJson = $"{{\"product_item_id\":\"{bom.ProductItemId}\",\"produce_qty\":{dto.ProduceQty},\"material_count\":{materials.Count}}}";
            await _audit.LogAsync("assemble", "bom", dto.BomId, afterJson: assembleJson, ct: ct);

            // 이벤트 발행은 커밋 이후 (롤백 시 외부 알림 없어야 함)
            await _events.PublishAsync(
                "bom.assembled",
                new BomAssembledEvent(
                    tenantId,
                    dto.BomId,
                    bom.ProductItemId,
                    dto.ProduceQty,
                    unitProductionCost,
                    materials),
                ct).ConfigureAwait(false);
        }
        catch
        {
            try { tx.Rollback(); } catch { /* 이미 닫힌 tx */ }
            throw;
        }
    }

    public async Task<List<StockAlertDto>> GetAlertsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var rows = await _db.QueryAsync<StockAlertDto>(new CommandDefinition(
            """
            SELECT
              sa.alert_id AS AlertId, sa.item_id AS ItemId, i.item_name AS ItemName,
              sa.alert_type AS AlertType, sa.current_qty AS CurrentQty, sa.safety_qty AS SafetyQty,
              sa.shortage_qty AS ShortageQty, sa.partner_id AS PartnerId, p.partner_name AS PartnerName,
              sa.order_qty AS OrderQty, sa.status AS Status, sa.created_at AS CreatedAt
            FROM stock_alerts sa
            LEFT JOIN items i ON i.item_id = sa.item_id
            LEFT JOIN partners p ON p.partner_id = sa.partner_id
            WHERE sa.tenant_id=@TenantId AND sa.status='pending'
            ORDER BY sa.created_at DESC
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task DismissAlertAsync(string alertId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE stock_alerts SET status='dismissed', updated_at=NOW(6) WHERE alert_id=@AlertId AND tenant_id=@TenantId",
            new { AlertId = alertId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task OrderAlertAsync(string alertId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 1) 알림에서 item_id·부족수량 조회
        var alert = await _db.QueryFirstOrDefaultAsync<(string ItemId, decimal ShortageQty, string? AutoOrderPartnerId, decimal AutoOrderQty, decimal PurchasePrice)>(
            new CommandDefinition(
                """
                SELECT sa.item_id AS ItemId,
                       sa.shortage_qty AS ShortageQty,
                       bi.auto_order_partner_id AS AutoOrderPartnerId,
                       COALESCE(bi.auto_order_qty, sa.shortage_qty) AS AutoOrderQty,
                       COALESCE(i.purchase_price, i.cost_price, 0) AS PurchasePrice
                FROM stock_alerts sa
                LEFT JOIN bom_items bi
                  ON bi.material_item_id = sa.item_id AND bi.tenant_id = sa.tenant_id
                LEFT JOIN items i ON i.item_id = sa.item_id AND i.tenant_id = sa.tenant_id
                WHERE sa.alert_id = @AlertId AND sa.tenant_id = @TenantId
                LIMIT 1
                """,
                new { AlertId = alertId, TenantId = tenantId },
                cancellationToken: ct)).ConfigureAwait(false);

        if (string.IsNullOrEmpty(alert.ItemId))
            throw new InvalidOperationException("알림 또는 품목을 찾을 수 없습니다.");
        if (string.IsNullOrWhiteSpace(alert.AutoOrderPartnerId))
            throw new InvalidOperationException("자동발주 공급처가 설정되지 않았습니다. BOM 자재에서 '자동발주 공급처'를 먼저 지정하세요.");

        var orderQty = alert.AutoOrderQty > 0 ? alert.AutoOrderQty : alert.ShortageQty;
        var unitPrice = alert.PurchasePrice;
        var supply = orderQty * unitPrice;
        var vat = Math.Round(supply * 0.1m, 0, MidpointRounding.AwayFromZero);

        // 2) 발주서 번호 채번(해당일자 순번)
        var today = DateTime.Today;
        var prefix = $"PO-{today:yyyyMMdd}-";
        var cnt = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM purchase_orders WHERE tenant_id=@TenantId AND po_no LIKE CONCAT(@Prefix, '%')",
            new { TenantId = tenantId, Prefix = prefix }, cancellationToken: ct)).ConfigureAwait(false);
        var poNo = $"{prefix}{cnt + 1:000}";
        var poId = Guid.NewGuid().ToString();

        // 3) purchase_orders + purchase_order_items INSERT (단일 tx)
        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO purchase_orders
                  (po_id, tenant_id, po_no, partner_id, po_date, status, total_amount, vat_amount, memo, created_at, updated_at)
                VALUES
                  (@PoId, @TenantId, @PoNo, @PartnerId, @PoDate, 'draft', @Supply, @Vat, @Memo, NOW(6), NOW(6))
                """,
                new
                {
                    PoId = poId,
                    TenantId = tenantId,
                    PoNo = poNo,
                    PartnerId = alert.AutoOrderPartnerId,
                    PoDate = today,
                    Supply = supply,
                    Vat = vat,
                    Memo = $"BOM 자재부족 자동발주 (alert {alertId[..8]})"
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO purchase_order_items
                  (po_item_id, po_id, tenant_id, item_id, ordered_qty, received_qty, unit_price, supply_amount, vat_amount, item_status)
                VALUES
                  (UUID(), @PoId, @TenantId, @ItemId, @Qty, 0, @UnitPrice, @Supply, @Vat, 'pending')
                """,
                new
                {
                    PoId = poId,
                    TenantId = tenantId,
                    ItemId = alert.ItemId,
                    Qty = orderQty,
                    UnitPrice = unitPrice,
                    Supply = supply,
                    Vat = vat
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // 4) 알림 상태 ordered + 발주 ID 연결
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE stock_alerts SET status='ordered', updated_at=NOW(6) WHERE alert_id=@AlertId AND tenant_id=@TenantId",
                new { AlertId = alertId, TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { /* closed */ }
            throw;
        }
    }

    private async Task<bool> HasCircularRefAsync(string productItemId, List<string> materialIds, string tenantId, CancellationToken ct)
    {
        foreach (var matId in materialIds)
        {
            if (matId == productItemId) return true;
            var childBom = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT bom_id FROM bom_headers
                WHERE product_item_id=@MatId AND tenant_id=@TenantId AND is_active=1
                LIMIT 1
                """,
                new { MatId = matId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
            if (childBom is null) continue;
            var childMats = (await _db.QueryAsync<string>(new CommandDefinition(
                "SELECT material_item_id FROM bom_items WHERE bom_id=@BomId AND tenant_id=@TenantId",
                new { BomId = childBom, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
            if (await HasCircularRefAsync(productItemId, childMats, tenantId, ct).ConfigureAwait(false)) return true;
        }
        return false;
    }

    private async Task UpdateCostCacheAsync(string bomId, string tenantId, CancellationToken ct)
    {
        var cost = await _db.QueryFirstOrDefaultAsync<decimal>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(
              bi.qty * (1 + bi.loss_rate/100) * COALESCE(i.purchase_price, i.cost_price, 0)
            ),0)
            FROM bom_items bi
            LEFT JOIN items i ON i.item_id = bi.material_item_id
            WHERE bi.bom_id=@BomId AND bi.tenant_id=@TenantId
            """,
            new { BomId = bomId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        var productItemId = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT product_item_id FROM bom_headers WHERE bom_id=@BomId AND tenant_id=@TenantId",
            new { BomId = bomId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(productItemId)) return;
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bom_cost_cache
              (cache_id, tenant_id, product_item_id, calculated_cost, material_count, is_dirty, last_calculated_at)
            VALUES
              (UUID(), @TenantId, @ProductItemId, @Cost, 0, 0, NOW(6))
            ON DUPLICATE KEY UPDATE
              calculated_cost=@Cost, is_dirty=0, last_calculated_at=NOW(6)
            """,
            new { TenantId = tenantId, ProductItemId = productItemId, Cost = cost }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c)
        {
            await c.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        _db.Open();
    }
}
