using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Bom;
using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Events;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

public class BomService : IBomService
{
    private readonly IDbConnection _db;
    private readonly IEventPublisher _events;
    private readonly IAuditService _audit;

    /// <summary>
    /// 자동 사슬에서 <c>IPurchaseService</c> 를 꺼내 쓴다 (20260825작1 W2).
    /// <para>
    /// 🔴 <c>IPurchaseService</c> 를 <b>생성자로 직접 받지 않는다</b> — 순환 의존 위험.
    /// 판매 정본(<c>SalesService</c>)도 같은 이유로 <c>IServiceProvider</c> 를 통해 꺼낸다.
    /// </para>
    /// <para>
    /// ⚠️ 없어도 <b>발주까지는 정상 동작</b>해야 한다 — 사슬만 못 탄다.
    /// 그래서 필수가 아니라 선택 인자다(기존 시험 코드를 깨뜨리지 않는다).
    /// </para>
    /// </summary>
    private readonly IServiceProvider? _services;

    public BomService(IDbConnection db, IEventPublisher events, IAuditService audit,
                      IServiceProvider? services = null)
    {
        _db = db;
        _events = events;
        _audit = audit;
        _services = services;
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
              -- 신규 (2026-08-25, 20260825작1 W6, 사장님 지시):
              --   "BOM그리드에 반제품 혹은 자재의 수량에 맞춰 생산가능 수가 나와야 함"
              --   자재마다 (현재고 ÷ 1개당 소요량) 을 구하고 그중 가장 작은 값 = 만들 수 있는 개수.
              --   가장 모자란 자재 하나가 생산 수량을 결정한다.
              --   · FLOOR — 반 개는 못 만든다. 내림이 맞다
              --   · NULLIF(...,0) — 소요량 0 인 줄이 있으면 0 나눗셈이 되어 통째로 NULL 이 된다
              --   · 자재가 한 줄도 없는 BOM 은 MIN 이 NULL ⇒ 바깥 COALESCE 로 0
              --   · 재고 행이 없는 자재는 s2.qty 가 NULL ⇒ 0 으로 봐서 생산가능 0 (부족한 게 맞다)
              COALESCE(MIN(FLOOR(
                  COALESCE(s2.qty, 0) / NULLIF(bi.qty * (1 + bi.loss_rate/100), 0)
              )), 0) AS ProducibleQty,
              bh.created_at AS CreatedAt
            FROM bom_headers bh
            LEFT JOIN items i ON i.item_id = bh.product_item_id
            LEFT JOIN bom_items bi ON bi.bom_id = bh.bom_id
            LEFT JOIN items i2 ON i2.item_id = bi.material_item_id
            -- 🔴 창고합산 서브쿼리 — item_stock 은 (item_id, warehouse_id) 단위라
            --    단순 JOIN 하면 창고가 여럿일 때 같은 자재가 여러 줄로 붙어 원가·개수가 부풀려진다.
            --    GetAssembleAutoOrderCandidatesAsync(:1163-1166) 가 쓰는 방식을 그대로 따른다.
            LEFT JOIN (
                 SELECT tenant_id, item_id, SUM(current_qty) AS qty
                   FROM item_stock GROUP BY tenant_id, item_id
            ) s2 ON s2.tenant_id = bi.tenant_id AND s2.item_id = bi.material_item_id
            WHERE bh.tenant_id = @TenantId
              AND bh.is_active = 1
            GROUP BY bh.bom_id, bh.product_item_id, i.item_name, bh.bom_name, bh.bom_version, bh.is_default, bh.is_active, bh.created_at
            ORDER BY i.item_name, bh.bom_version DESC
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        var list = rows.ToList();
        await FillBomLevelsAsync(list, tenantId, ct).ConfigureAwait(false);
        return list;
    }

    /// <summary>
    /// 각 BOM 의 <b>제조 단계</b>를 채운다 (20260825작1 W5, 사장님 지시).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 사장님 원문: <i>"BOM 그리드에 버전 의미가 반제품→반반제품→…→완제품과 같은 상품의 버전 아님?
    /// 볼트너트(1차반제품)-볼트너트오링(완제품)인데 V1로 나옴"</i>
    /// </para>
    /// <para>
    /// <c>bom_version</c> 은 <b>문서 개정 회차</b>라 제조 단계와 무관하다(완성품마다 첫 등록이면 늘 1).
    /// 고장이 아니라 <b>다른 것</b>이었다. 그래서 단계를 <b>따로</b> 계산해 보여준다.
    /// </para>
    /// <para>
    /// <b>단계 = 자기 자재 중 가장 깊은 것 + 1.</b> 사 오는 자재(BOM 이 없는 품목)는 1단계.
    /// 볼트너트(자재로만 구성) = 2, 볼트너트오링(볼트너트를 자재로 씀) = 3.
    /// </para>
    /// <para>
    /// 🔴 <b>DB 를 안 바꾼다</b> — 컬럼을 새로 만들지 않고 이미 있는 부모·자식 관계로 계산한다.
    /// 🔴 <b>쿼리는 1회</b> — 목록 화면이라 BOM 마다 재귀 조회를 날리면 화면이 느려진다.
    /// 테넌트 전체 연결을 한 번에 읽어 <b>메모리에서</b> 푼다.
    /// 🔴 <b>순환참조가 있어도 멈추지 않는다</b> — 손상된 데이터로 화면이 굳으면 P0 다.
    /// 방문 중인 품목을 다시 만나면 그 가지를 <b>끊고</b> 계속한다.
    /// </para>
    /// </remarks>
    private async Task FillBomLevelsAsync(List<BomListDto> list, string tenantId, CancellationToken ct)
    {
        if (list.Count == 0) return;

        // 이 테넌트의 (완성품 → 자재) 연결을 통째로 한 번에 읽는다.
        var edges = (await _db.QueryAsync<(string ProductItemId, string MaterialItemId)>(
            new CommandDefinition(
                """
                SELECT bh.product_item_id AS ProductItemId, bi.material_item_id AS MaterialItemId
                  FROM bom_headers bh
                  JOIN bom_items bi ON bi.bom_id = bh.bom_id AND bi.tenant_id = bh.tenant_id
                 WHERE bh.tenant_id = @TenantId
                   AND bh.is_active = 1
                """,
                new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        var childrenOf = edges
            .GroupBy(e => e.ProductItemId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.MaterialItemId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var memo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var onPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in list)
        {
            row.BomLevel = Depth(row.ProductItemId);
        }

        int Depth(string itemId)
        {
            if (memo.TryGetValue(itemId, out var done)) return done;

            // 순환 — 이 가지를 끊는다. 값을 memo 에 넣지 않는다(다른 경로에서는 정상일 수 있다).
            if (!onPath.Add(itemId)) return 1;

            var level = 1;
            if (childrenOf.TryGetValue(itemId, out var mats))
            {
                var deepest = 0;
                foreach (var mat in mats)
                {
                    var d = Depth(mat);
                    if (d > deepest) deepest = d;
                }
                level = deepest + 1;
            }

            onPath.Remove(itemId);
            memo[itemId] = level;
            return level;
        }
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
            // 봉합 (2026-06-22, 11차 전수조사 BOM-RUN P0): stock_ledger UNIQUE 키
            //   (tenant, source_type='bom_production', source_id, item_id, move_type) 에서 종전엔 source_id 로
            //   dto.BomId(BOM 정의 ID, 생산 회차 불변)를 써, 같은 BOM 을 두 번째 생산하면 1차와 동일 키로
            //   재INSERT → UNIQUE 위반 → 생산 전체 롤백(헌법 #20 BOM 흐름 끊김, "두 번째부터 생산 불가").
            //   7차 B-1 봉합은 "한 BOM 같은 자재 2줄"만 합산했을 뿐 회차 차원은 미처리였다. 생산 회차마다 고유한
            //   source_id 를 부여해 N 회 생산해도 키가 매번 달라 충돌하지 않게 한다. 한 생산 호출의 자재 OUT·
            //   완제품 IN 은 같은 회차 ID 를 공유(한 트랜잭션 = 한 회차). 회계 기표(journal)는 entryId 가 매번
            //   새 GUID 라 멱등 충돌 없음 → BomId 참조 유지(생산 BOM 추적). 감사로그·이벤트도 BomId 유지.
            // 재봉합 (2026-06-22, 12차 전수조사 BOM-RUN-REGRESS P0): 11차 봉합이 source_id 를
            //   "{BomId}:{GUID}" (73자)로 만들었으나 stock_ledger.source_id 는 varchar(36)이라 strict 모드
            //   ERROR 1406(Data too long)→첫 생산부터 전체 롤백(원 버그보다 악화). 회차 고유성은 GUID
            //   단독(36자)으로도 충분히 달성(UNIQUE 키 회피 목적 동일). BomId 역추적은 doc_no(=bom.BomName)
            //   원장 기록 + 회계 기표·감사로그의 dto.BomId 직접 참조로 보존되므로 source_id 에 BomId 불필요.
            //   DDL 무변경(헌법 #36 단일진실원 동기화 불필요), strict 안전.
            var productionRunId = Guid.NewGuid().ToString();

            // 봉합 (2026-06-22, 12차 1단 교차검증 BOM-DOCNO P1, 선재결함): stock_ledger.doc_no 는 varchar(20)
            //   인데 bom_name 은 varchar(100) → 한글 21자+ BOM명이면 strict ERROR 1406 으로 생산 롤백.
            //   doc_no 는 표시용 보조필드이고 BOM 전체명·BomId 는 회계 description 에 보존되므로 20자로 안전 절단.
            var ledgerDocNo = bom.BomName.Length > 20 ? bom.BomName[..20] : bom.BomName;

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
                // 봉합 (2026-06-22, 10차 BOM-WH-01 P1): warehouse_id 필터 없으면 자재가 2창고 분산 시
                //   두 행 모두 차감(과차감)되고, stock_adjust_logs·ledger는 defaultWarehouseId 단일창고라
                //   불일치. 차감 창고를 ledger 기록 창고(defaultWarehouseId)와 일치시켜 정합 확보.
                //   단일창고 고객은 동작 불변, 다창고만 정상화.
                var matUpdated = await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE item_stock
                    SET current_qty = current_qty - @Qty, last_updated_at = NOW(6)
                    WHERE tenant_id = @TenantId AND item_id = @ItemId
                      AND warehouse_id = @WarehouseId AND current_qty >= @Qty
                    """,
                    new { TenantId = tenantId, ItemId = mat.ItemId, WarehouseId = defaultWarehouseId, Qty = mat.RequiredQty },
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
            // 봉합 (2026-06-21, 7차 전수조사 B-1 P0): stock_ledger UNIQUE 키 (tenant, source_type=bom_production,
            //   source_id=BomId, item_id, move_type=out) 단위 유일. 한 BOM 에 같은 자재가 2줄(bom_items 중복 자재)이면
            //   자재 OUT 이 같은 키로 2번 INSERT → UNIQUE 위반 → 생산 전체 롤백(헌법 #20 BOM 흐름 끊김). 자재를
            //   item_id 로 합산해 키당 1행만 기록(수량 합산, 단가는 합산금액/합산수량). 판매·매입 합산 봉합과 동일 패턴.
            foreach (var matGrp in materials.GroupBy(m => m.ItemId))
            {
                var qtySum = matGrp.Sum(m => m.UsedQty);
                var costSum = matGrp.Sum(m => m.UsedQty * m.UnitCost);
                var avgCost = qtySum != 0m ? costSum / qtySum : matGrp.First().UnitCost;
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
                      move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount)
                    VALUES (@TenantId, @ItemId, @WarehouseId, CURDATE(), DATE_FORMAT(CURDATE(),'%Y-%m'),
                      'out', 'bom_production', @BomId, @DocNo, 0, @Qty, @Cost, @Supply)
                    """,
                    new { TenantId = tenantId, ItemId = matGrp.Key, WarehouseId = defaultWarehouseId,
                          BomId = productionRunId, DocNo = ledgerDocNo, Qty = qtySum, Cost = avgCost, Supply = costSum },
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
                      BomId = productionRunId, DocNo = ledgerDocNo, Qty = dto.ProduceQty, Cost = unitProductionCost },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // 회계 기표 — BOM 생산 원가 반영 (INSERT ONLY)
            // 차변: 재공품(제품) — 완성품 원가 전입
            // 대변: 원재료 — 자재 원가 출고
            var totalMaterialCost = productionCost;
            if (totalMaterialCost != 0m)
            {
                // 재봉합 (2026-06-22, 12차 2단 교차검증 BOM-RUN-JOURNAL P0): journal_entries 도
                //   uq_je_source (tenant, source_type='bom_production', source_id) UNIQUE 라, source_id 로
                //   dto.BomId(회차 불변)를 쓰면 같은 BOM 두 번째 생산부터 ERROR 1062 → 전체 롤백(헌법 #20).
                //   11차/12차 stock_ledger 봉합이 1406/1062의 1406만 풀어 이 회계 충돌이 그 뒤에서 드러났다.
                //   stock_ledger 와 대칭으로 회계도 회차 GUID(productionRunId, 36자=source_id varchar(36) 정합)를
                //   source_id 로 쓰고, BomId 역추적은 description(documentNo)에 BomId 를 실어 보존한다.
                await AutoJournalHelper.RecordBomProductionAsync(
                    _db, tx,
                    tenantId,
                    productionRunId,
                    $"{bom.BomName}(BOM:{dto.BomId})",
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
            // 봉합 (2026-06-22, 11차 전수조사 BOM-RUN P0): AssembleAsync 와 동일 — 종전 source_id=dto.BomId
            //   (회차 불변)라 같은 BOM 두 번째 해체부터 stock_ledger UNIQUE 위반→전체 롤백. 해체 회차마다
            //   고유 source_id 부여(완제품 OUT·자재 IN 공유). bom_disassemble 타입도 같은 회차 ID 로 묶인다.
            // 재봉합 (2026-06-22, 12차 전수조사 BOM-RUN-REGRESS P0): AssembleAsync 와 동일 — 11차의
            //   "{BomId}:{GUID}"(73자)가 source_id varchar(36) 초과로 ERROR 1406. GUID 단독(36자)으로 회차
            //   고유성 확보, BomId 역추적은 doc_no + 회계 역분개·감사로그의 dto.BomId 로 보존. DDL 무변경.
            var disassembleRunId = Guid.NewGuid().ToString();

            // 봉합 (2026-06-22, 12차 1단 교차검증 BOM-DOCNO P1): AssembleAsync 와 동일 — doc_no varchar(20)
            //   초과(한글 21자+ BOM명) ERROR 1406 방지. 표시용 20자 절단, 전체명·BomId 는 회계 description 보존.
            var ledgerDocNo = bom.BomName.Length > 20 ? bom.BomName[..20] : bom.BomName;

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
                      BomId = disassembleRunId, DocNo = ledgerDocNo, Qty = dto.ProduceQty, Cost = unitProductionCost },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // 2) 각 자재 재고 복귀 (IN)
            // 봉합 (2026-06-21, 7차 전수조사 B-1 P0): 해체 자재 IN 원장도 stock_ledger UNIQUE 키
            //   (tenant, source_type=bom_disassemble, source_id=BomId, item_id, move_type=in) 단위 유일.
            //   한 BOM 에 같은 자재가 2줄이면 자재 IN 이 같은 키로 2번 INSERT → UNIQUE 위반 → 해체 전체 롤백
            //   (헌법 #20). bom.Items 를 material_item_id 로 합산(복귀수량 합)한 뒤 자재당 1회만 로그·재고·원장 기록.
            var disassembleGroups = bom.Items
                .GroupBy(i => i.MaterialItemId)
                .Select(g => new
                {
                    MaterialItemId = g.Key,
                    RequiredQty = g.Sum(i => Math.Ceiling(i.Qty * (1 + i.LossRate / 100m) * dto.ProduceQty))
                })
                .ToList();
            foreach (var item in disassembleGroups)
            {
                var requiredQty = item.RequiredQty;
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
                          BomId = disassembleRunId, DocNo = ledgerDocNo, Qty = requiredQty, Cost = matUnitCost },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            // 회계 역분개 — BOM 해체 (생산 기표의 정확한 Reverse, INSERT ONLY)
            // 차변: 원재료 — 자재 원가 복귀
            // 대변: 재공품(제품) — 완성품 원가 역산
            if (unitProductionCost != 0m)
            {
                // 재봉합 (2026-06-22, 12차 2단 교차검증 BOM-RUN-JOURNAL P0): AssembleAsync 와 동일 —
                //   journal uq_je_source 회차 충돌 방지. source_id 를 회차 GUID(disassembleRunId)로,
                //   BomId 역추적은 description 에 보존. stock_ledger 봉합과 대칭.
                await AutoJournalHelper.RecordBomDisassembleAsync(
                    _db, tx,
                    tenantId,
                    disassembleRunId,
                    $"{bom.BomName}(BOM:{dto.BomId})",
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
              -- 봉합 (2026-08-25, 20260825작1 W1, 사장님 실측 지적): 재삽입 루프 차단.
              --   종전 가드는 status='pending' 만 봤다. 그래서 발주로 알림이 'ordered' 로 넘어간 직후
              --   화면이 갱신되면(Items.razor:234 / Bom.razor:407) 이 INSERT 가 가드를 그냥 통과해
              --   같은 품목에 새 'pending' 을 만들었다 ⇒ "발주해도 경고가 안 사라진다"(사장님).
              --   발주만으론 재고가 안 늘므로 미달 조건은 계속 참이고, 갱신할수록 유령 행이 쌓였다.
              --   'ordered' 를 가드에 포함해 "이미 조치된 품목"을 다시 만들지 않는다.
              --   · 'dismissed' 는 넣지 않는다 — 사용자가 닫은 뒤 다시 미달이면 알려야 한다
              --   · 'received' 도 넣지 않는다 — 입고로 닫힌 뒤 재차 미달이면 새 알림이 맞다
              --   (매입확정 시 PurchaseService:398-408 이 pending·ordered 를 'received' 로 닫는다)
              AND NOT EXISTS (
                SELECT 1 FROM stock_alerts sa
                WHERE sa.tenant_id = i.tenant_id
                  AND sa.item_id = i.item_id
                  AND sa.status IN ('pending', 'ordered')
              )
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        var rows = await _db.QueryAsync<StockAlertDto>(new CommandDefinition(
            """
            SELECT
              sa.alert_id AS AlertId, sa.item_id AS ItemId, i.item_name AS ItemName,
              sa.alert_type AS AlertType, sa.current_qty AS CurrentQty, sa.safety_qty AS SafetyQty,
              sa.shortage_qty AS ShortageQty, sa.partner_id AS PartnerId, p.partner_name AS PartnerName,
              sa.order_qty AS OrderQty, sa.status AS Status, sa.created_at AS CreatedAt,
              sa.updated_at AS UpdatedAt
            FROM stock_alerts sa
            LEFT JOIN items i ON i.item_id = sa.item_id
            LEFT JOIN partners p ON p.partner_id = sa.partner_id
            -- 변경 (2026-08-25, 20260825작1 W3, 사장님 지시): 'pending' 외 두 가지를 더 내려보낸다.
            --   화면이 세 가지 안내를 구분해 띄워야 하기 때문이다:
            --   · pending  → 🔴 미달 경고 (조치 필요)
            --   · ordered  → 🟡 "자동발주 되었습니다. 매입처리 하셔야 재고에 반영됩니다"
            --                 사장님 지시: **매입처리 될 때까지** 뜬다 ⇒ 기한을 안 건다
            --   · received → 🟢 "매입처리까지 완료되어 재고에 반영되었습니다"
            --                 사장님 지시: **30분간** ⇒ updated_at(매입확정 시각) 기준으로 자른다
            --   ⚠️ 조회를 넓히는 것뿐이다. 배너에 무엇을 띄울지는 화면이 status 로 가른다.
            WHERE sa.tenant_id=@TenantId
              AND (
                    sa.status IN ('pending', 'ordered')
                 OR (sa.status = 'received' AND sa.updated_at >= NOW(6) - INTERVAL 30 MINUTE)
                  )
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

    public async Task<OrderAlertResultDto> OrderAlertAsync(
        string alertId, string tenantId, bool autoReceive = false, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 1) 알림에서 item_id·부족수량 조회 — bom_items 에는 auto_order_* 컬럼이 없으므로
        //    items.auto_order_partner_id / auto_order_qty 만 사용 (사장님 보고 2026-04-26 회귀 수정).
        var alert = await _db.QueryFirstOrDefaultAsync<(string ItemId, string ItemName, decimal ShortageQty, string? AutoOrderPartnerId, decimal AutoOrderQty, decimal PurchasePrice, string Status, string ItemType, bool AutoReceiveOnOrder)>(
            new CommandDefinition(
                """
                SELECT sa.item_id AS ItemId,
                       COALESCE(i.item_name, '') AS ItemName,
                       sa.shortage_qty AS ShortageQty,
                       i.auto_order_partner_id AS AutoOrderPartnerId,
                       COALESCE(NULLIF(i.auto_order_qty, 0), sa.shortage_qty) AS AutoOrderQty,
                       COALESCE(i.purchase_price, i.cost_price, 0) AS PurchasePrice,
                       sa.status AS Status,
                       -- 신규 (2026-08-25, 20260825작1 W2): 사슬 판정에 필요한 두 칸.
                       COALESCE(i.item_type, 'material') AS ItemType,
                       COALESCE(i.auto_receive_on_order, 0) AS AutoReceiveOnOrder
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

        // 사슬을 태워도 되는지 여기서 정한다 (20260825작1 W2, 사장님 결재).
        //   세 조건이 모두 맞아야 한다 — 하나라도 아니면 발주서만 만든다.
        //   ① 사용자가 「자동 사슬」을 골랐다
        //   ② 품목의 「자동 매입확정」 스위치가 켜져 있다 (반자동 원칙 — 코드가 임의로 켜지 않는다)
        //   ③ 🔴 그 품목이 '사 오는 물건' 이다 (반제품·완제품이면 막는다 — 회계 오염 차단)
        var result = new OrderAlertResultDto { ItemName = alert.ItemName };
        var wantChain = autoReceive && alert.AutoReceiveOnOrder;

        if (wantChain && !AutoChainPolicy.CanAutoReceive(alert.ItemType))
        {
            wantChain = false;
            result.ChainSkippedReason = AutoChainPolicy.BlockedReason(alert.ItemName);
        }

        // 🔴 단가가 0 이면 매입확정이 거부된다("합계가 0원인 매입은 확정할 수 없습니다").
        //    미리 걸러 이유를 보여준다 — 안 그러면 발주만 남고 사슬이 조용히 끊긴다.
        if (wantChain && supply <= 0)
        {
            wantChain = false;
            result.ChainSkippedReason =
                $"{alert.ItemName}의 매입단가가 없어 매입확정까지 자동으로 하지 않았습니다. "
              + "발주서만 만들었습니다. 상품에서 매입단가를 넣어주세요.";
        }

        // 2) 발주서 번호 채번(해당일자 순번) — WO-11 한글 prefix
        var today = DateTime.Today;
        var prefix = $"발-{today:yyyyMMdd}-";
        var poId = Guid.NewGuid().ToString();

        // 3) purchase_orders + purchase_order_items INSERT (단일 tx)
        using var tx = _db.BeginTransaction();
        try
        {
            // 봉합 (2026-06-23, 5차 전수조사 SALES-02): 종전 COUNT(*)+1 채번은 트랜잭션 밖에서 실행되고
            //   소프트삭제 행을 세서 갭 충돌 위험이 있었다. DocumentNumberHelper(MAX+1)로 일원화하고,
            //   채번을 트랜잭션 안으로 이동해 INSERT 와 같은 가시성 컨텍스트에서 채번한다. (설계팀장 승인)
            var poNo = await DocumentNumberHelper.NextNumberAsync(
                _db, tenantId, "purchase_orders", "po_no", prefix, ct, transaction: tx).ConfigureAwait(false);

            await _db.ExecuteAsync(new CommandDefinition(
                """
                -- 변경 (2026-08-25, 20260825작1 W2-0-B, 사장님 결재): is_auto=1 추가.
                --   종전엔 이 컬럼 자체가 빠져 있어 0 이 들어갔다(판매 경로는 1 을 넣는다).
                --   그 탓에 판매 경로 멱등 필터(po.is_auto=1 요구)가 BOM 발주를 못 봐서
                --   BOM 에서 발주한 품목이 판매확정 때 또 후보로 떴다 — 중복 발주.
                INSERT INTO purchase_orders
                  (po_id, tenant_id, po_no, partner_id, po_date, status, total_amount, vat_amount, memo, is_auto, created_at, updated_at)
                VALUES
                  (@PoId, @TenantId, @PoNo, @PartnerId, @PoDate, 'draft', @Supply, @Vat, @Memo, 1, NOW(6), NOW(6))
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
                    // 변경 (2026-08-25, 20260825작1 W2, 사장님 결재): 비고 앞머리에 「자동발주서」.
                    //   종전: "BOM 자재부족 자동발주 (alert 3f2a8b1c)" ← alert·내부 식별자가
                    //   고객 화면에 그대로 노출됐다. 목록에서 비고는 잘려 보이므로 앞부분이 살아야 한다.
                    Memo = "자동발주서 — BOM 자재부족"
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

        result.OrderCreated = true;

        // ── 자동 사슬 — 🔴 반드시 tx.Commit() **뒤**에서 부른다 (20260825작1 W2-1) ──────────
        //
        //  왜 밖인가: IPurchaseService 는 EF DbContext 커넥션을 쓰고 여기 _db 는 Dapper 커넥션이다.
        //  DI 등록이 서로 다른 인스턴스를 준다(InfrastructureExtensions:60·65) ⇒ **물리적으로 다른 커넥션**.
        //  tx 안에서 부르면 EF 쪽에서 아직 커밋 안 된 발주서가 안 보여
        //  "발주서를 찾을 수 없습니다" 가 나거나, stock_alerts 같은 행을 두 커넥션이 잡아 락 대기가 난다.
        //  🔴 판매 정본(SalesService:1757→1774)도 정확히 이 순서다 — 커밋 → 감사로그 → 사슬.
        //
        //  ⚠️ 사슬이 실패해도 **발주서는 남는다**(위에서 이미 커밋됐다). 그게 맞다 —
        //     발주는 실제로 났고, 매입확정만 사람이 마저 하면 된다. 흐름이 안 끊긴다(#20).
        if (!wantChain) return result;

        var purSvc = _services?.GetService(typeof(IPurchaseService)) as IPurchaseService;
        if (purSvc is null)
        {
            result.ChainSkippedReason =
                $"{alert.ItemName}의 매입확정을 자동으로 처리하지 못했습니다. 발주서만 만들었습니다.";
            Console.Error.WriteLine(
                $"[WARN] 자동 사슬: IPurchaseService 를 못 찾았다 — AlertId={alertId} TenantId={tenantId}");
            return result;
        }

        try
        {
            var (receiptId, _) = await purSvc.ConvertOrderToReceiptAsync(poId, tenantId, ct)
                                             .ConfigureAwait(false);
            await purSvc.ConfirmReceiptAsync(receiptId, new ConfirmReceiptRequest(), ct)
                        .ConfigureAwait(false);
            result.ReceiptConfirmed = true;
        }
        catch (Exception ex)
        {
            // 🔴 조용히 성공으로 위장하지 않는다 (#15 빈 catch 금지).
            //    4/28 사고가 정확히 이것이었다 — 사용자는 성공이라 알았는데 원장이 안 올라갔고,
            //    그래서 "재고부족이 안 사라진다" 로 나타났다.
            result.ChainSkippedReason =
                $"{alert.ItemName}은(는) 발주서까지 만들었고 매입확정은 못 했습니다. 발주서에서 매입처리를 진행해주세요.";
            Console.Error.WriteLine(
                $"[WARN] 자동 사슬 매입확정 실패 — AlertId={alertId} TenantId={tenantId} "
              + $"ex={ex.GetType().Name} msg={ex.Message}");
        }

        return result;
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
