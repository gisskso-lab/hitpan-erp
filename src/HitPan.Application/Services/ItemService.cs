using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Item;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

public sealed class ItemService : IItemService
{
    private readonly IDbConnection _db;
    private readonly IAuditService _audit;

    public ItemService(IDbConnection db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<ItemListDto>> GetListAsync(
        string tenantId,
        string? search = null,
        string? group = null,
        string? type = null,
        bool excludeBom = false,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // excludeBom=true 인 경우 BOM 결과물(완제품·반제품) 및 BOM- 로 시작하는 코드 제외.
        // 상품 마스터 화면은 순수 판매·자재 상품만 보여주고 BOM 헤더는 BOM 화면에서만 조회.
        const string sql = """
                           SELECT
                             i.item_id AS ItemId,
                             IFNULL(i.item_code, '') AS ItemCode,
                             i.item_name AS ItemName,
                             i.item_group AS ItemGroup,
                             LOWER(IFNULL(i.item_type, 'product')) AS ItemType,
                             IFNULL(i.unit, 'EA') AS Unit,
                             i.spec AS Spec,
                             COALESCE(i.sale_price, i.std_price, 0) AS SalePrice,
                             COALESCE(i.purchase_price, i.cost_price, 0) AS PurchasePrice,
                             COALESCE(i.standard_price, i.std_price, 0) AS StandardPrice,
                             COALESCE(s.qty, 0) AS CurrentStock,
                             COALESCE(i.safety_stock, i.safe_stock, 0) AS SafetyStock,
                             LOWER(IFNULL(i.tax_type, 'taxable')) AS TaxType,
                             i.is_active AS IsActive,
                             i.created_at AS CreatedAt
                           FROM items i
                           LEFT JOIN (
                             SELECT tenant_id, item_id, SUM(current_qty) AS qty
                             FROM item_stock
                             GROUP BY tenant_id, item_id
                           ) s ON s.tenant_id = i.tenant_id AND s.item_id = i.item_id
                           WHERE i.tenant_id = @TenantId
                             AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
                             AND (@Search IS NULL OR @Search = '' OR
                                  i.item_name LIKE CONCAT('%', @Search, '%') OR
                                  IFNULL(i.item_code, '') LIKE CONCAT('%', @Search, '%'))
                             AND (@Group IS NULL OR @Group = '' OR i.item_group = @Group)
                             AND (@Type IS NULL OR @Type = '' OR LOWER(i.item_type) = LOWER(@Type))
                             AND (
                                   @ExcludeBom = 0
                                OR (
                                     LOWER(IFNULL(i.item_type, 'product')) NOT IN ('finished', 'semi_finished', 'semi')
                                 AND (IFNULL(i.item_code, '') NOT LIKE 'BOM-%')
                                   )
                             )
                           ORDER BY i.item_group, i.item_name
                           """;

        var rows = await _db.QueryAsync<ItemListDto>(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                Search = search?.Trim(),
                Group = group?.Trim(),
                Type = type?.Trim(),
                ExcludeBom = excludeBom ? 1 : 0
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<ItemDetailDto?> GetAsync(string itemId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
                           SELECT
                             i.item_id AS ItemId,
                             IFNULL(i.item_code, '') AS ItemCode,
                             i.item_name AS ItemName,
                             i.item_group AS ItemGroup,
                             LOWER(IFNULL(i.item_type, 'product')) AS ItemType,
                             IFNULL(i.unit, 'EA') AS Unit,
                             i.spec AS Spec,
                             COALESCE(i.sale_price, i.std_price, 0) AS SalePrice,
                             COALESCE(i.purchase_price, i.cost_price, 0) AS PurchasePrice,
                             COALESCE(i.standard_price, i.std_price, 0) AS StandardPrice,
                             COALESCE(s.qty, 0) AS CurrentStock,
                             COALESCE(i.safety_stock, i.safe_stock, 0) AS SafetyStock,
                             LOWER(IFNULL(i.tax_type, 'taxable')) AS TaxType,
                             i.is_active AS IsActive,
                             i.created_at AS CreatedAt,
                             IFNULL(i.auto_order_enabled, 0) AS AutoOrderEnabled,
                             i.auto_order_partner_id AS AutoOrderPartnerId,
                             IFNULL(i.auto_order_qty, 0) AS AutoOrderQty,
                             i.barcode AS Barcode,
                             i.memo AS Memo,
                             IFNULL(i.row_version, 0) AS RowVersion
                           FROM items i
                           LEFT JOIN (
                             SELECT tenant_id, item_id, SUM(current_qty) AS qty
                             FROM item_stock
                             GROUP BY tenant_id, item_id
                           ) s ON s.tenant_id = i.tenant_id AND s.item_id = i.item_id
                           WHERE i.item_id = @ItemId
                             AND i.tenant_id = @TenantId
                             AND (i.is_deleted = 0 OR i.is_deleted IS NULL)
                           """;

        return await _db.QueryFirstOrDefaultAsync<ItemDetailDto>(new CommandDefinition(
            sql,
            new { ItemId = itemId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<string> CreateAsync(CreateItemDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var dup = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM items
            WHERE tenant_id = @TenantId
              AND item_name = @Name
              AND (is_deleted = 0 OR is_deleted IS NULL)
            """,
            new { TenantId = tenantId, Name = dto.ItemName.Trim() },
            cancellationToken: ct)).ConfigureAwait(false);

        if (dup > 0)
        {
            throw new InvalidOperationException("이미 등록된 상품명입니다.");
        }

        var id = Guid.NewGuid().ToString();
        var code = string.IsNullOrWhiteSpace(dto.ItemCode)
            ? "I-" + id[..Math.Min(8, id.Length)]
            : dto.ItemCode.Trim();

        var codeDup = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM items
            WHERE tenant_id = @TenantId
              AND item_code = @Code
              AND (is_deleted = 0 OR is_deleted IS NULL)
            """,
            new { TenantId = tenantId, Code = code },
            cancellationToken: ct)).ConfigureAwait(false);

        if (codeDup > 0)
        {
            throw new InvalidOperationException($"이미 사용 중인 상품코드입니다: {code}");
        }

        var itemType = NormalizeItemType(dto.ItemType);
        var taxType = string.IsNullOrWhiteSpace(dto.TaxType) ? "taxable" : dto.TaxType.Trim();
        var unit = string.IsNullOrWhiteSpace(dto.Unit) ? "EA" : dto.Unit.Trim();

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO items (
              item_id, tenant_id,
              item_code, item_name,
              item_group, item_type,
              category_id, unit, spec,
              purchase_price, sale_price, standard_price,
              tax_type, safety_stock, barcode, memo,
              auto_order_enabled, auto_order_partner_id, auto_order_qty,
              std_price, cost_price, safe_stock,
              is_active, is_deleted, row_version,
              created_at, updated_at)
            VALUES (
              @Id, @TenantId,
              @ItemCode, @ItemName,
              @ItemGroup, @ItemType,
              NULL, @Unit, @Spec,
              @PurchasePrice, @SalePrice, @StandardPrice,
              @TaxType, @SafetyStock, @Barcode, @Memo,
              @AutoOrderEnabled, @AutoOrderPartnerId, @AutoOrderQty,
              @StandardPrice, @PurchasePrice, @SafetyStock,
              1, 0, 0,
              NOW(6), NOW(6))
            """,
            new
            {
                Id = id,
                TenantId = tenantId,
                ItemCode = code,
                ItemName = dto.ItemName.Trim(),
                ItemGroup = dto.ItemGroup,
                ItemType = itemType,
                Unit = unit,
                Spec = dto.Spec,
                PurchasePrice = dto.PurchasePrice,
                SalePrice = dto.SalePrice,
                StandardPrice = dto.StandardPrice,
                TaxType = taxType,
                SafetyStock = dto.SafetyStock,
                Barcode = dto.Barcode,
                Memo = dto.Memo,
                AutoOrderEnabled = dto.AutoOrderEnabled ? 1 : 0,
                AutoOrderPartnerId = dto.AutoOrderPartnerId,
                AutoOrderQty = dto.AutoOrderQty
            },
            cancellationToken: ct)).ConfigureAwait(false);

        // 감사로그 — 상품 생성
        var afterJson = $"{{\"item_code\":\"{code}\",\"item_name\":\"{dto.ItemName.Trim()}\",\"item_type\":\"{itemType}\"}}";
        await _audit.LogAsync("create", "item", id, afterJson: afterJson, ct: ct);

        return id;
    }

    public async Task UpdateAsync(string itemId, UpdateItemDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var itemType = NormalizeItemType(dto.ItemType);
        var taxType = string.IsNullOrWhiteSpace(dto.TaxType) ? "taxable" : dto.TaxType.Trim();
        var unit = string.IsNullOrWhiteSpace(dto.Unit) ? "EA" : dto.Unit.Trim();
        var code = string.IsNullOrWhiteSpace(dto.ItemCode) ? null : dto.ItemCode.Trim();

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE items SET
                item_code        = COALESCE(@ItemCode, item_code),
                item_name        = @ItemName,
                item_group       = @ItemGroup,
                item_type        = @ItemType,
                unit             = @Unit,
                spec             = @Spec,
                purchase_price   = @PurchasePrice,
                sale_price       = @SalePrice,
                standard_price   = @StandardPrice,
                tax_type         = @TaxType,
                safety_stock     = @SafetyStock,
                barcode          = @Barcode,
                memo             = @Memo,
                auto_order_enabled   = @AutoOrderEnabled,
                auto_order_partner_id = @AutoOrderPartnerId,
                auto_order_qty   = @AutoOrderQty,
                std_price        = @StandardPrice,
                cost_price       = @PurchasePrice,
                safe_stock       = @SafetyStock,
                is_active        = @IsActive,
                row_version      = row_version + 1,
                updated_at       = NOW(6)
            WHERE item_id = @ItemId
              AND tenant_id = @TenantId
              AND row_version = @RowVersion
              AND (is_deleted = 0 OR is_deleted IS NULL)
            """,
            new
            {
                ItemId = itemId,
                TenantId = tenantId,
                ItemCode = code,
                ItemName = dto.ItemName.Trim(),
                ItemGroup = dto.ItemGroup,
                ItemType = itemType,
                Unit = unit,
                Spec = dto.Spec,
                PurchasePrice = dto.PurchasePrice,
                SalePrice = dto.SalePrice,
                StandardPrice = dto.StandardPrice,
                TaxType = taxType,
                SafetyStock = dto.SafetyStock,
                Barcode = dto.Barcode,
                Memo = dto.Memo,
                AutoOrderEnabled = dto.AutoOrderEnabled ? 1 : 0,
                AutoOrderPartnerId = dto.AutoOrderPartnerId,
                AutoOrderQty = dto.AutoOrderQty,
                IsActive = dto.IsActive ? 1 : 0,
                RowVersion = dto.RowVersion
            },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new InvalidOperationException("다른 사용자가 수정했습니다. 새로고침 후 다시 시도해주세요.");
        }

        // 감사로그 — 상품 수정 (핵심 변경 필드만 기록)
        var afterJson = $"{{\"item_name\":\"{dto.ItemName.Trim()}\",\"sale_price\":{dto.SalePrice},\"purchase_price\":{dto.PurchasePrice},\"is_active\":{(dto.IsActive ? "true" : "false")}}}";
        await _audit.LogAsync("update", "item", itemId, afterJson: afterJson, ct: ct);
    }

    public async Task DeleteAsync(string itemId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE items SET
                is_deleted = 1,
                is_active = 0,
                deleted_at = NOW(6),
                updated_at = NOW(6)
            WHERE item_id = @ItemId
              AND tenant_id = @TenantId
            """,
            new { ItemId = itemId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        // 감사로그 — 상품 소프트 삭제
        await _audit.LogAsync("delete", "item", itemId, ct: ct);
    }

    public async Task<List<ItemSpecialPriceDto>> GetSpecialPricesAsync(string itemId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
                           SELECT
                             sp.price_id AS PriceId,
                             sp.partner_id AS PartnerId,
                             p.partner_name AS PartnerName,
                             IFNULL(sp.price_type, 'fixed') AS PriceType,
                             sp.unit_price AS UnitPrice,
                             sp.start_date AS StartDate,
                             sp.end_date AS EndDate,
                             sp.is_active AS IsActive
                           FROM item_special_prices sp
                           LEFT JOIN partners p
                             ON p.partner_id = sp.partner_id
                            AND p.tenant_id = sp.tenant_id
                           WHERE sp.item_id = @ItemId
                             AND sp.tenant_id = @TenantId
                           ORDER BY p.partner_name
                           """;

        var rows = await _db.QueryAsync<ItemSpecialPriceDto>(new CommandDefinition(
            sql,
            new { ItemId = itemId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task UpsertSpecialPriceAsync(string itemId, ItemSpecialPriceDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var priceId = string.IsNullOrWhiteSpace(dto.PriceId)
            ? Guid.NewGuid().ToString()
            : dto.PriceId.Trim();
        var priceType = string.IsNullOrWhiteSpace(dto.PriceType) ? "fixed" : dto.PriceType.Trim();

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO item_special_prices (
              price_id, tenant_id, item_id,
              partner_id, price_type, unit_price,
              start_date, end_date,
              is_active, created_at, updated_at)
            VALUES (
              @PriceId, @TenantId, @ItemId,
              @PartnerId, @PriceType, @UnitPrice,
              @StartDate, @EndDate,
              @IsActive, NOW(6), NOW(6))
            ON DUPLICATE KEY UPDATE
              price_type = @PriceType,
              unit_price = @UnitPrice,
              start_date = @StartDate,
              end_date = @EndDate,
              is_active = @IsActive,
              updated_at = NOW(6)
            """,
            new
            {
                PriceId = priceId,
                TenantId = tenantId,
                ItemId = itemId,
                PartnerId = dto.PartnerId,
                PriceType = priceType,
                UnitPrice = dto.UnitPrice,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                IsActive = dto.IsActive ? 1 : 0
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteSpecialPriceAsync(string priceId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM item_special_prices
            WHERE price_id = @PriceId
              AND tenant_id = @TenantId
            """,
            new { PriceId = priceId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<List<ItemGroupDto>> GetGroupsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
                           SELECT
                             group_id AS GroupId,
                             group_name AS GroupName,
                             sort_order AS SortOrder,
                             is_active AS IsActive
                           FROM item_groups
                           WHERE tenant_id = @TenantId
                             AND is_active = 1
                           ORDER BY sort_order, group_name
                           """;

        var rows = await _db.QueryAsync<ItemGroupDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    private static string NormalizeItemType(string? t)
    {
        var v = (t ?? "product").Trim().ToLowerInvariant();
        return v switch
        {
            "product" or "material" or "finished" or "semi" or "semi_finished" or "expense" => v,
            _ => "product"
        };
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
}
