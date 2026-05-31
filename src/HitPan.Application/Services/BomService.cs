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
              COALESCE(i.auto_receive_on_order, 0) AS AutoReceiveOnOrder,
              COALESCE(i.item_type, 'material') AS ItemType,
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

        // 사장님 지시 흐름: 완제품은 "새 이름"으로 입력 → 서비스가 items INSERT → 그 ID를 FK로 사용.
        // 기존 상품에 BOM을 덧붙이려면 dto.ProductItemId 를 세팅해 호출한다(레거시/수정 호환).
        var productItemId = dto.ProductItemId;
        if (string.IsNullOrWhiteSpace(productItemId))
        {
            var newName = (dto.ProductItemName ?? dto.BomName)?.Trim();
            if (string.IsNullOrWhiteSpace(newName))
                throw new InvalidOperationException("완제품명을 입력하거나 기존 상품을 지정해주세요.");

            // 자동 상품코드: PROD-yyyyMMdd-HHmmss-<6자리>
            var itemCode = $"PROD-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
            productItemId = Guid.NewGuid().ToString();

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
                  NULL, 'product', NULL, 'EA', NULL,
                  0, 0, 0,
                  'taxable', 0, NULL, @Memo,
                  0, NULL, 0,
                  0, 0, 0,
                  1, 0, 0,
                  NOW(6), NOW(6))
                """,
                new
                {
                    ItemId = productItemId,
                    TenantId = tenantId,
                    ItemCode = itemCode,
                    ItemName = newName,
                    Memo = $"BOM 완제품 자동등록: {dto.BomName}"
                }, cancellationToken: ct)).ConfigureAwait(false);
        }

        // 순환 참조 체크 — 완제품과 자재 목록 간 루프 탐지.
        if (await HasCircularRefAsync(productItemId, dto.Items.Select(x => x.MaterialItemId).ToList(), tenantId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("순환 참조가 감지됐습니다. 자재 구성을 확인해주세요.");

        var bomId = Guid.NewGuid().ToString();
        if (dto.IsDefault)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE bom_headers SET is_default = 0 WHERE tenant_id=@TenantId AND product_item_id=@ItemId",
                new { TenantId = tenantId, ItemId = productItemId }, cancellationToken: ct)).ConfigureAwait(false);
        }
        var maxVer = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(bom_version),0) FROM bom_headers WHERE tenant_id=@TenantId AND product_item_id=@ItemId",
            new { TenantId = tenantId, ItemId = productItemId }, cancellationToken: ct)).ConfigureAwait(false);

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
                ProductItemId = productItemId,
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

        // 감사로그 — BOM 신규 생성 (완제품 자동등록 여부 포함)
        var autoCreated = string.IsNullOrWhiteSpace(dto.ProductItemId);
        var afterJson = $"{{\"bom_name\":\"{dto.BomName}\",\"product_item_id\":\"{productItemId}\",\"auto_product_created\":{(autoCreated ? "true" : "false")},\"material_count\":{dto.Items?.Count ?? 0}}}";
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

    /// <summary>
    /// BOM 저장 후 다이얼로그에서 사용자가 "반제품/완제품"으로 분류한 결과를 반영.
    /// 사장님 지시 (2026-04-26): 새 items 행을 또 만들지 말고 CreateAsync 가 이미 만든
    /// product_item_id 의 item_type 만 갱신하고 매입단가를 BOM 원가로 세팅.
    /// (이전 구현은 BOM- 코드의 새 items 행을 INSERT 하고 bom_headers.product_item_id 를
    ///  덮어써 같은 이름의 행이 2개 생기는 회귀를 유발했음.)
    /// </summary>
    public async Task<string> RegisterBomAsItemAsync(string bomId, string itemType, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var bom = await GetAsync(bomId, tenantId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("BOM을 찾을 수 없습니다.");

        if (string.IsNullOrWhiteSpace(bom.ProductItemId))
        {
            throw new InvalidOperationException("BOM에 연결된 상품이 없습니다. BOM을 먼저 저장해주세요.");
        }

        var bomCost = bom.TotalCost;

        // 기존 product_item_id 행의 item_type 만 'product' → 'finished'/'semi_finished' 로 변경.
        // 매입단가/원가/std_price 도 BOM 원가로 동기화. 코드는 그대로 유지(예: PROD-...).
        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE items
               SET item_type      = @ItemType,
                   purchase_price = @BomCost,
                   cost_price     = @BomCost,
                   standard_price = @BomCost,
                   std_price      = @BomCost,
                   updated_at     = NOW(6)
             WHERE item_id   = @ItemId
               AND tenant_id = @TenantId
            """,
            new
            {
                ItemId = bom.ProductItemId,
                TenantId = tenantId,
                ItemType = itemType,
                BomCost = bomCost
            }, cancellationToken: ct)).ConfigureAwait(false);

        // 기본 창고 재고 초기화(없으면 0으로 생성, 있으면 avg_cost만 BOM 원가로 갱신).
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
            ON DUPLICATE KEY UPDATE avg_cost = @BomCost, last_updated_at = NOW(6)
            """,
            new { TenantId = tenantId, ItemId = bom.ProductItemId, WarehouseId = defaultWhId, BomCost = bomCost },
            cancellationToken: ct)).ConfigureAwait(false);

        return bom.ProductItemId;
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
                AutoOrderEnabled = item.AutoOrderEnabled,
                ItemType = item.ItemType,
                AutoReceiveOnOrder = item.AutoReceiveOnOrder
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

        // §절대원칙 #20 (워크플로우 무결성) — 자재/반제품 재고가 부족하면 완제품 +반영 절대 금지.
        // 사장님 헌법 (2026-04-26): "자재가 들어오기 전에 완제품이 +되는 일은 절대 없어야 함."
        // 부족분은 [발주→매입→매입확정→자재 +반영] 사슬을 끝낸 뒤 다시 생산지시.
        // (자동 사슬은 P1 작지서로 분리.)
        if (!check.CanProduce)
        {
            var shortages = check.Materials
                .Where(m => !m.IsEnough)
                .Select(m => $"• {m.ItemName} {m.ShortageQty}{m.Unit} 부족 (필요 {m.RequiredQty}, 현재 {m.CurrentStock})");
            throw new InvalidOperationException(
                "재고 부족으로 BOM 생산을 진행할 수 없습니다.\n" +
                string.Join("\n", shortages) +
                "\n\n부족한 자재는 발주→매입→매입확정 절차로 입고한 뒤 다시 생산지시 해주세요.");
        }

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

                // 자재 재고 차감 — 재고 부족 시 음수 방지를 위해 조건부 UPDATE 후 0행이면 예외
                var matUpdated = await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE item_stock
                    SET current_qty = current_qty - @Qty, last_updated_at = NOW(6)
                    WHERE tenant_id = @TenantId AND item_id = @ItemId AND current_qty >= @Qty
                    """,
                    new { TenantId = tenantId, ItemId = mat.ItemId, Qty = mat.RequiredQty },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                if (matUpdated == 0)
                    throw new InvalidOperationException(
                        $"자재 재고 부족: 품목 {mat.ItemId}, 필요 수량 {mat.RequiredQty}. BOM 생산을 중단합니다.");
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

            // 회계 기표 — BOM 생산 원가 반영 (INSERT ONLY)
            // 차변: 재공품(제품) — 완성품 원가 전입
            // 대변: 원재료 — 자재 원가 출고
            var totalMaterialCost = productionCost;
            if (totalMaterialCost != 0m)
            {
                await AutoJournalHelper.RecordBomProductionAsync(
                    _db, tx,
                    tenantId,
                    dto.BomId,
                    bom.BomName,
                    DateTime.UtcNow,
                    totalMaterialCost,
                    userId,
                    ct);
            }

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
        catch (Exception)
        {
            try { tx.Rollback(); }
            catch (Exception rbex) { Console.Error.WriteLine($"[BomService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    /// <summary>
    /// 조립 해체 — AssembleAsync 의 정확한 Reverse.
    /// 사장님 지시 (2026-04-26): "조립 해체(삭제) 시 반대로 가격·재고는 원래대로 회귀".
    ///   - 완제품 재고 OUT + stock_ledger out 원장 INSERT
    ///   - 자재 재고 IN + stock_ledger in 원장 INSERT
    ///   - 조립 시 사용된 수량(BOM.qty * (1+loss%) * produceQty) 그대로 자재로 복귀
    ///   - INSERT ONLY 원칙 유지(원본 원장 수정/삭제 안 함, 역행 원장만 추가)
    /// </summary>
    public async Task DisassembleAsync(BomAssembleDto dto, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var bom = await GetAsync(dto.BomId, tenantId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("BOM을 찾을 수 없습니다.");

        var defaultWarehouseId = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT warehouse_id FROM warehouses
             WHERE tenant_id=@TenantId AND is_active=1
             ORDER BY (CASE WHEN wh_code IN ('MAIN','WH-MAIN') THEN 0 ELSE 1 END), wh_code
             LIMIT 1
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("활성 창고가 없습니다.");

        // 완제품 재고 ≥ 해체 수량 검증
        var productStock = await _db.QueryFirstOrDefaultAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(current_qty, 0) FROM item_stock WHERE tenant_id=@Tid AND item_id=@Pid AND warehouse_id=@Wh",
            new { Tid = tenantId, Pid = bom.ProductItemId, Wh = defaultWarehouseId },
            cancellationToken: ct)).ConfigureAwait(false);
        if (productStock < dto.ProduceQty)
        {
            throw new InvalidOperationException($"완제품 재고가 부족합니다. 현재고 {productStock:N1} < 해체 요청 {dto.ProduceQty:N1}.");
        }

        using var tx = _db.BeginTransaction();
        try
        {
            // 완제품의 현재 매입단가 = unit cost (Reverse 시 자재 단가 복원에 사용)
            var unitProductionCost = await _db.QueryFirstOrDefaultAsync<decimal>(new CommandDefinition(
                "SELECT COALESCE(purchase_price, cost_price, avg_cost, 0) FROM items WHERE item_id=@Pid",
                new { Pid = bom.ProductItemId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // 1) 완제품 재고 차감 (OUT)
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
                    TenantId = tenantId, ItemId = bom.ProductItemId, WarehouseId = defaultWarehouseId,
                    Qty = dto.ProduceQty, Reason = $"BOM해체:{bom.BomName} {dto.ProduceQty}개",
                    UserId = userId
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // BOM 해체 시 완제품 차감 — 재고 부족 시 음수 방지
            var disassembleUpdated = await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE item_stock
                SET current_qty = current_qty - @Qty, last_updated_at = NOW(6)
                WHERE tenant_id=@TenantId AND item_id=@ItemId AND warehouse_id=@WarehouseId AND current_qty >= @Qty
                """,
                new { TenantId = tenantId, ItemId = bom.ProductItemId, WarehouseId = defaultWarehouseId, Qty = dto.ProduceQty },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (disassembleUpdated == 0)
                throw new InvalidOperationException(
                    $"완제품 재고 부족: 품목 {bom.ProductItemId}, 필요 수량 {dto.ProduceQty}. BOM 해체를 중단합니다.");

            // 완제품 OUT 원장 (Reverse IN)
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
                  move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo)
                VALUES (@TenantId, @ItemId, @WarehouseId, CURDATE(), DATE_FORMAT(CURDATE(),'%Y-%m'),
                  'out', 'bom_disassemble', @BomId, @DocNo, 0, @Qty, @Cost, @Qty * @Cost, 'BOM 해체 (Reverse IN)')
                """,
                new { TenantId = tenantId, ItemId = bom.ProductItemId, WarehouseId = defaultWarehouseId,
                      BomId = dto.BomId, DocNo = bom.BomName, Qty = dto.ProduceQty, Cost = unitProductionCost },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // 2) 각 자재 재고 복귀 (IN)
            foreach (var item in bom.Items)
            {
                var requiredQty = Math.Ceiling(item.Qty * (1 + item.LossRate / 100m) * dto.ProduceQty);
                var matUnitCost = await _db.QueryFirstOrDefaultAsync<decimal>(new CommandDefinition(
                    "SELECT COALESCE(purchase_price, cost_price, 0) FROM items WHERE item_id=@ItemId",
                    new { ItemId = item.MaterialItemId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                // 자재 재고 복귀 로그
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO stock_adjust_logs (
                      adjust_id, tenant_id, item_id, warehouse_id, before_qty, after_qty, adjust_qty,
                      before_cost, after_cost, reason, user_id, created_at)
                    SELECT
                      UUID(), @TenantId, @ItemId, @WarehouseId,
                      COALESCE(current_qty, 0), COALESCE(current_qty, 0) + @Qty, @Qty,
                      COALESCE(avg_cost, 0), COALESCE(avg_cost, 0), @Reason, @UserId, NOW(6)
                    FROM item_stock
                    WHERE tenant_id=@TenantId AND item_id=@ItemId
                    """,
                    new
                    {
                        TenantId = tenantId, ItemId = item.MaterialItemId, WarehouseId = defaultWarehouseId,
                        Qty = requiredQty, Reason = $"BOM해체:{bom.BomName} {dto.ProduceQty}개 자재복귀",
                        UserId = userId
                    }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                // 자재 재고 실제 증가 (없으면 row 생성)
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                    VALUES (UUID(), @TenantId, @ItemId, @WarehouseId, @Qty, @UnitCost, NOW(6))
                    ON DUPLICATE KEY UPDATE
                      current_qty = current_qty + @Qty,
                      last_updated_at = NOW(6)
                    """,
                    new { TenantId = tenantId, ItemId = item.MaterialItemId, WarehouseId = defaultWarehouseId,
                          Qty = requiredQty, UnitCost = matUnitCost },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                // 자재 IN 원장 (Reverse OUT)
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
                      move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo)
                    VALUES (@TenantId, @ItemId, @WarehouseId, CURDATE(), DATE_FORMAT(CURDATE(),'%Y-%m'),
                      'in', 'bom_disassemble', @BomId, @DocNo, @Qty, 0, @Cost, @Qty * @Cost, 'BOM 해체 자재복귀 (Reverse OUT)')
                    """,
                    new { TenantId = tenantId, ItemId = item.MaterialItemId, WarehouseId = defaultWarehouseId,
                          BomId = dto.BomId, DocNo = bom.BomName, Qty = requiredQty, Cost = matUnitCost },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            // 회계 역분개 — BOM 해체 (생산 기표의 정확한 Reverse, INSERT ONLY)
            // 차변: 원재료 — 자재 원가 복귀
            // 대변: 재공품(제품) — 완성품 원가 역산
            if (unitProductionCost != 0m)
            {
                await AutoJournalHelper.RecordBomDisassembleAsync(
                    _db, tx,
                    tenantId,
                    dto.BomId,
                    bom.BomName,
                    DateTime.UtcNow,
                    unitProductionCost * dto.ProduceQty,
                    userId,
                    ct);
            }

            tx.Commit();

            await _audit.LogAsync("disassemble", "bom", dto.BomId,
                afterJson: $"{{\"product_item_id\":\"{bom.ProductItemId}\",\"produce_qty\":{dto.ProduceQty},\"material_count\":{bom.Items.Count}}}",
                ct: ct);
        }
        catch (Exception)
        {
            try { tx.Rollback(); }
            catch (Exception rbex) { Console.Error.WriteLine($"[BomService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    public async Task<List<StockAlertDto>> GetAlertsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 사장님 헌법 (2026-04-27): 안전재고 미달 알림은 트리거(매입/판매/BOM) 외에도
        // 평소에 감지되어야 함. GetAlertsAsync 호출 시점에 즉석으로 안전재고 미달 자재를
        // 직접 조회하여 stock_alerts 'pending' 으로 자동 보충.
        // (이미 'pending'이 있으면 SyncEventPublisher의 가드와 동일 로직으로 SKIP.)
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO stock_alerts
              (alert_id, tenant_id, item_id,
               alert_type, current_qty, safety_qty, shortage_qty,
               partner_id, order_qty, status, created_at, updated_at)
            SELECT
              UUID(), i.tenant_id, i.item_id,
              'safety_stock',
              COALESCE(s.current_qty, 0),
              COALESCE(i.safety_stock, i.safe_stock, 0),
              COALESCE(i.safety_stock, i.safe_stock, 0) - COALESCE(s.current_qty, 0),
              i.auto_order_partner_id,
              COALESCE(i.auto_order_qty, 0),
              'pending', NOW(6), NOW(6)
            FROM items i
            LEFT JOIN item_stock s
              ON s.tenant_id = i.tenant_id AND s.item_id = i.item_id
            WHERE i.tenant_id = @TenantId
              AND i.is_deleted = 0
              AND i.is_active = 1
              AND COALESCE(i.safety_stock, i.safe_stock, 0) > 0
              AND COALESCE(s.current_qty, 0) <= COALESCE(i.safety_stock, i.safe_stock, 0)
              AND COALESCE(i.auto_order_enabled, 0) = 1
              AND NOT EXISTS (
                SELECT 1 FROM stock_alerts sa
                WHERE sa.tenant_id = i.tenant_id
                  AND sa.item_id = i.item_id
                  AND sa.status = 'pending'
              )
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

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

        // 1) 알림에서 item_id·부족수량 조회 — bom_items 에는 auto_order_* 컬럼이 없으므로
        //    items.auto_order_partner_id / auto_order_qty 만 사용 (사장님 보고 2026-04-26 회귀 수정).
        var alert = await _db.QueryFirstOrDefaultAsync<(string ItemId, decimal ShortageQty, string? AutoOrderPartnerId, decimal AutoOrderQty, decimal PurchasePrice, string Status)>(
            new CommandDefinition(
                """
                SELECT sa.item_id AS ItemId,
                       sa.shortage_qty AS ShortageQty,
                       i.auto_order_partner_id AS AutoOrderPartnerId,
                       COALESCE(NULLIF(i.auto_order_qty, 0), sa.shortage_qty) AS AutoOrderQty,
                       COALESCE(i.purchase_price, i.cost_price, 0) AS PurchasePrice,
                       sa.status AS Status
                FROM stock_alerts sa
                LEFT JOIN items i ON i.item_id = sa.item_id AND i.tenant_id = sa.tenant_id
                WHERE sa.alert_id = @AlertId AND sa.tenant_id = @TenantId
                LIMIT 1
                """,
                new { AlertId = alertId, TenantId = tenantId },
                cancellationToken: ct)).ConfigureAwait(false);

        if (string.IsNullOrEmpty(alert.ItemId))
            throw new InvalidOperationException("알림 또는 품목을 찾을 수 없습니다.");

        // 멱등 체크: 이미 발주됐거나 입고 완료된 알림은 재발주 차단
        if (alert.Status != "pending")
            throw new InvalidOperationException($"이미 처리된 알림입니다. (현재 상태: {alert.Status})");

        if (string.IsNullOrWhiteSpace(alert.AutoOrderPartnerId))
            throw new InvalidOperationException("자동발주 공급처가 설정되지 않았습니다. BOM 자재에서 '자동발주 공급처'를 먼저 지정하세요.");

        var orderQty = alert.AutoOrderQty > 0 ? alert.AutoOrderQty : alert.ShortageQty;
        var unitPrice = alert.PurchasePrice;
        var supply = orderQty * unitPrice;
        var vat = Math.Round(supply * 0.1m, 0, MidpointRounding.AwayFromZero);

        // 2) 발주서 번호 채번(해당일자 순번) — WO-11 한글 prefix
        var today = DateTime.Today;
        var prefix = $"발-{today:yyyyMMdd}-";
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
        catch (Exception)
        {
            try { tx.Rollback(); }
            catch (Exception rbex) { Console.Error.WriteLine($"[BomService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    /// <summary>
    /// 다단 BOM (원자재 → 반제품1 → 반제품2 → 완제품) 순환 참조 검사.
    /// 사장님 지시 (2026-04-26): 2~5단 깊이 공정구조 지원. 재귀로 깊이 무관 탐색.
    /// visited HashSet 으로 이미 손상된 데이터(기존 순환)도 stack overflow 없이 종료.
    /// </summary>
    private async Task<bool> HasCircularRefAsync(string productItemId, List<string> materialIds, string tenantId, CancellationToken ct)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return await HasCircularRefRecursiveAsync(productItemId, materialIds, tenantId, visited, ct);
    }

    private async Task<bool> HasCircularRefRecursiveAsync(
        string productItemId, List<string> materialIds, string tenantId,
        HashSet<string> visited, CancellationToken ct)
    {
        foreach (var matId in materialIds)
        {
            if (matId == productItemId) return true;
            if (!visited.Add(matId)) continue; // 이미 본 자재는 재방문 안 함

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
            if (await HasCircularRefRecursiveAsync(productItemId, childMats, tenantId, visited, ct).ConfigureAwait(false)) return true;
        }
        return false;
    }

    /// <summary>
    /// BOM 원가 재계산 → bom_cost_cache + items 테이블 동시 갱신.
    /// 사장님 지시 (2026-04-26): 조립 자재 단가가 바뀌면 BOM 완제품·반제품의
    /// 매입단가에 즉시 자동 반영. items.purchase_price/cost_price/std_price/standard_price
    /// 4개를 동기화해 매입·재고·원가 어디서 읽어도 같은 값.
    /// 체인 전파: 이 완제품이 다른 BOM 의 자재이기도 하면(반제품 케이스)
    /// 그 상위 BOM 도 재귀 재계산. 순환은 CreateAsync 시 HasCircularRefAsync 가 차단.
    /// </summary>
    private async Task UpdateCostCacheAsync(string bomId, string tenantId, CancellationToken ct)
    {
        await UpdateCostCacheRecursiveAsync(bomId, tenantId, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ct);
    }

    private async Task UpdateCostCacheRecursiveAsync(string bomId, string tenantId, HashSet<string> visited, CancellationToken ct)
    {
        if (!visited.Add(bomId)) return; // 순환 안전망

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

        // items 테이블 매입단가/원가/표준가 동기화 — 사장님 지시 핵심.
        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE items
               SET purchase_price = @Cost,
                   cost_price     = @Cost,
                   standard_price = @Cost,
                   std_price      = @Cost,
                   updated_at     = NOW(6)
             WHERE item_id   = @ProductItemId
               AND tenant_id = @TenantId
            """,
            new { TenantId = tenantId, ProductItemId = productItemId, Cost = cost }, cancellationToken: ct)).ConfigureAwait(false);

        // 체인 전파: 이 완제품을 자재로 쓰는 상위 BOM 모두 재계산.
        var parents = await _db.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT bi.bom_id
              FROM bom_items bi
             WHERE bi.material_item_id = @ProductItemId
               AND bi.tenant_id        = @TenantId
            """,
            new { ProductItemId = productItemId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        foreach (var parentBomId in parents)
        {
            await UpdateCostCacheRecursiveAsync(parentBomId, tenantId, visited, ct);
        }
    }

    public async Task<string?> GetBomIdByItemAsync(string itemId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        return await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT bom_id FROM bom_headers WHERE product_item_id=@ItemId AND tenant_id=@Tid LIMIT 1",
            new { ItemId = itemId, Tid = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RecalculateBomsUsingMaterialAsync(string materialItemId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 자재로 쓰이는 모든 BOM 헤더 조회.
        var bomIds = await _db.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT bi.bom_id
              FROM bom_items bi
             WHERE bi.material_item_id = @ItemId
               AND bi.tenant_id        = @Tid
            """,
            new { ItemId = materialItemId, Tid = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bomId in bomIds)
        {
            await UpdateCostCacheRecursiveAsync(bomId, tenantId, visited, ct);
        }
    }

    public async Task<List<DTOs.Sales.AutoOrderCandidateDto>> GetAssembleAutoOrderCandidatesAsync(
        string bomId, string tenantId, decimal produceQty = 1, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 사장님 헌법 (2026-04-26):
        // "재고가 자동발주수량보다 더 현저하게 부족할 경우 = 사용자가 생산할 때 필요한 재고를 맞춰서 발주.
        //  재고가 자동발주수량보다는 부족하지 않을 경우 = 자동발주 수량으로 발주."
        //
        // 한 줄 룰: SuggestedOrderQty = MAX(부족분, 자동발주수량).
        //   부족분 = CEIL(bi.qty * (1 + bi.loss_rate/100) * produceQty) - 현재고
        //
        // 이유: 자동발주수량은 평소 단위(MOQ 역할)일 뿐, 큰 생산 1회를 50회로 쪼개 누르게 만들면 자동화 의미 없음.
        // 정식 버전에선 moq_qty 별도 컬럼으로 분리 예정 (사장님 지시 2026-04-26).
        const string sql = """
            SELECT
                i.item_id        AS ItemId,
                IFNULL(i.item_code,'') AS ItemCode,
                i.item_name      AS ItemName,
                COALESCE(s.qty, 0) AS CurrentQty,
                COALESCE(i.safety_stock, i.safe_stock, 0) AS SafetyQty,
                GREATEST(
                    CEIL(bi.qty * (1 + bi.loss_rate/100) * @ProduceQty) - COALESCE(s.qty, 0),
                    COALESCE(i.auto_order_qty, 0)
                ) AS SuggestedOrderQty,
                i.auto_order_partner_id AS PartnerId,
                p.partner_name   AS PartnerName,
                COALESCE(i.purchase_price, i.cost_price, 0) AS UnitPrice,
                CASE
                  WHEN COALESCE(s.qty, 0) <= 0 THEN 'out_of_stock'
                  ELSE 'below_safety'
                END AS Reason
              FROM bom_items bi
              JOIN items i
                ON i.item_id = bi.material_item_id AND i.tenant_id = bi.tenant_id
              LEFT JOIN (
                   SELECT tenant_id, item_id, SUM(current_qty) AS qty
                     FROM item_stock GROUP BY tenant_id, item_id
              ) s ON s.tenant_id = i.tenant_id AND s.item_id = i.item_id
              LEFT JOIN partners p
                ON p.partner_id = i.auto_order_partner_id AND p.tenant_id = i.tenant_id
             WHERE bi.bom_id     = @BomId
               AND bi.tenant_id  = @Tid
               AND IFNULL(i.auto_order_enabled, 0) = 1
               AND (
                     COALESCE(s.qty, 0) < CEIL(bi.qty * (1 + bi.loss_rate/100) * @ProduceQty)
                  OR COALESCE(s.qty, 0) <= COALESCE(i.safety_stock, i.safe_stock, 0)
                   )
            """;

        var rows = await _db.QueryAsync<DTOs.Sales.AutoOrderCandidateDto>(new CommandDefinition(
            sql, new { BomId = bomId, Tid = tenantId, ProduceQty = produceQty }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
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
