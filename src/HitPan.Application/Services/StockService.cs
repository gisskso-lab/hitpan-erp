using System.Data;
using Dapper;
using HitPan.Application.DTOs.Stock;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;

namespace HitPan.Application.Services;

public class StockService : IStockService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDbConnection _dbConnection;
    private readonly IAuditService _audit;

    public StockService(IUnitOfWork unitOfWork, ICurrentTenant currentTenant, IDbConnection dbConnection, IAuditService audit)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
        _dbConnection = dbConnection;
        _audit = audit;
    }

    public async Task<IReadOnlyList<StockBalanceDto>> GetBalanceAsync(CancellationToken ct = default)
    {
        // item_stock + items + warehouses 조인으로 전체 재고현황 반환
        if (_dbConnection.State != System.Data.ConnectionState.Open)
        {
            if (_dbConnection is System.Data.Common.DbConnection c) await c.OpenAsync(ct);
            else _dbConnection.Open();
        }
        var tenantId = _currentTenant.TenantId;
        const string sql = """
            SELECT s.item_id AS ItemId, i.item_code AS ItemCode, i.item_name AS ItemName,
                   i.spec AS Spec, i.unit AS Unit, i.item_group AS ItemGroup,
                   s.warehouse_id AS WarehouseId, w.wh_name AS WarehouseName,
                   s.current_qty AS CurrentQty, s.current_qty AS BalanceQty,
                   s.avg_cost AS AvgCost,
                   COALESCE(i.safety_stock, i.safe_stock, 0) AS SafetyStock
            FROM item_stock s
            JOIN items i ON i.item_id = s.item_id AND i.is_deleted = 0
            LEFT JOIN warehouses w ON w.warehouse_id = s.warehouse_id AND w.tenant_id = s.tenant_id
            WHERE s.tenant_id = @TenantId
            ORDER BY i.item_group, i.item_code
            """;
        var rows = await _dbConnection.QueryAsync<StockBalanceDto>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<StockLedgerRow>> GetLedgerAsync(StockLedgerQueryRequest request, CancellationToken ct = default)
    {
        var fromDate = request.FromDate.Date;
        var toDate = request.ToDate.Date;
        if (toDate < fromDate)
        {
            throw new InvalidOperationException("조회 기간이 올바르지 않습니다.");
        }

        var currentYm = DateTime.UtcNow.ToString("yyyy-MM");
        var fromYm = fromDate.ToString("yyyy-MM");
        var toYm = toDate.ToString("yyyy-MM");
        var ledgerType = request.LedgerType.ToLowerInvariant();

        var parameters = new DynamicParameters();
        parameters.Add("tenantId", _currentTenant.TenantId);
        parameters.Add("fromDate", fromDate);
        parameters.Add("toDate", toDate);
        parameters.Add("fromYm", fromYm);
        parameters.Add("toYm", toYm);
        parameters.Add("currentYm", currentYm);
        parameters.Add("itemId", request.ItemId);
        parameters.Add("partnerId", request.PartnerId);
        parameters.Add("warehouseId", request.WarehouseId);
        parameters.Add("employeeId", request.EmployeeId);

        var ledgerFilter = """
            l.tenant_id = @tenantId
            AND l.ledger_date BETWEEN @fromDate AND @toDate
            AND (@itemId IS NULL OR l.item_id = @itemId)
            AND (@partnerId IS NULL OR l.partner_id = @partnerId)
            AND (@warehouseId IS NULL OR l.warehouse_id = @warehouseId)
            AND (@employeeId IS NULL OR l.employee_id = @employeeId)
            """;

        var snapshotFilter = """
            s.tenant_id = @tenantId
            AND s.ym BETWEEN @fromYm AND @toYm
            AND (@itemId IS NULL OR s.item_id = @itemId)
            AND (@partnerId IS NULL OR s.partner_id = @partnerId)
            AND (@warehouseId IS NULL OR s.warehouse_id = @warehouseId)
            """;

        var dataSourceSql = BuildDataSourceSql(ledgerType, fromYm, toYm, currentYm, ledgerFilter, snapshotFilter);
        var groupByColumns = GetGroupByColumns(ledgerType);
        var selectKeys = string.Join(", ", groupByColumns.Select(x => $"x.{x}"));
        var selectKeysWithAlias = string.Join(", ", groupByColumns.Select(x => $"x.{x} AS {x}"));

        var sql = $"""
            SELECT
                {selectKeysWithAlias},
                SUM(x.qty_in) AS QtyIn,
                SUM(x.qty_out) AS QtyOut,
                SUM(x.qty_in) - SUM(x.qty_out) AS BalanceQty
            FROM (
                {dataSourceSql}
            ) x
            GROUP BY {selectKeys}
            ORDER BY {selectKeys}
            """;

        var result = await _dbConnection.QueryAsync<StockLedgerRow>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return result.ToList();
    }

    private static string BuildDataSourceSql(
        string ledgerType,
        string fromYm,
        string toYm,
        string currentYm,
        string ledgerFilter,
        string snapshotFilter)
    {
        if (ledgerType is "employee" or "adjust")
        {
            return BuildLedgerSql(ledgerType, ledgerFilter);
        }

        var pastOnly = string.CompareOrdinal(toYm, currentYm) < 0;
        var currentOnly = string.CompareOrdinal(fromYm, currentYm) >= 0;

        if (pastOnly)
        {
            return BuildSnapshotSql(ledgerType, snapshotFilter);
        }

        if (currentOnly)
        {
            return BuildLedgerSql(ledgerType, ledgerFilter);
        }

        return $"""
            {BuildSnapshotSql(ledgerType, snapshotFilter + " AND s.ym < @currentYm")}
            UNION ALL
            {BuildLedgerSql(ledgerType, ledgerFilter + " AND l.ym >= @currentYm")}
            """;
    }

    private static string BuildLedgerSql(string ledgerType, string whereClause)
    {
        var sourceWhere = ledgerType == "adjust" ? "AND l.source_type = 'adjust'" : string.Empty;

        return $"""
            SELECT
                l.item_id AS ItemId,
                l.partner_id AS PartnerId,
                l.warehouse_id AS WarehouseId,
                l.employee_id AS EmployeeId,
                l.ledger_date AS LedgerDate,
                l.ym AS Ym,
                l.source_type AS SourceType,
                l.created_by AS CreatedBy,
                l.qty_in AS QtyIn,
                l.qty_out AS QtyOut
            FROM stock_ledger l
            WHERE {whereClause}
            {sourceWhere}
            """;
    }

    private static string BuildSnapshotSql(string ledgerType, string whereClause)
    {
        if (ledgerType == "adjust")
        {
            return """
                SELECT
                    NULL AS ItemId,
                    NULL AS PartnerId,
                    NULL AS WarehouseId,
                    NULL AS EmployeeId,
                    NULL AS LedgerDate,
                    NULL AS Ym,
                    NULL AS SourceType,
                    NULL AS CreatedBy,
                    0 AS QtyIn,
                    0 AS QtyOut
                WHERE 1 = 0
                """;
        }

        return $"""
            SELECT
                s.item_id AS ItemId,
                s.partner_id AS PartnerId,
                s.warehouse_id AS WarehouseId,
                NULL AS EmployeeId,
                NULL AS LedgerDate,
                s.ym AS Ym,
                NULL AS SourceType,
                NULL AS CreatedBy,
                s.in_qty AS QtyIn,
                s.out_qty AS QtyOut
            FROM stock_monthly_snapshot s
            WHERE {whereClause}
            """;
    }

    private static string[] GetGroupByColumns(string ledgerType)
    {
        return ledgerType switch
        {
            "item" => ["ItemId", "WarehouseId", "LedgerDate"],
            "partner" => ["PartnerId", "ItemId", "LedgerDate"],
            "warehouse" => ["WarehouseId", "ItemId"],
            "employee" => ["EmployeeId", "ItemId", "LedgerDate"],
            "cross" => ["ItemId", "PartnerId"],
            "period" => ["ItemId", "Ym"],
            "adjust" => ["SourceType", "CreatedBy"],
            _ => ["ItemId", "WarehouseId", "LedgerDate"]
        };
    }

    // ── 재고 실사·조정 ──

    /// <summary>재고 실사 조정 처리</summary>
    public async Task<StockAdjustResultDto> AdjustStockAsync(string tenantId, string userId, StockAdjustRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 1. 현재 장부 수량 조회
        const string qtySQL = """
            SELECT COALESCE(current_qty, 0)
            FROM item_stock
            WHERE tenant_id = @TenantId AND item_id = @ItemId AND warehouse_id = @WarehouseId
            """;
        var currentQty = await _dbConnection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(qtySQL, new { TenantId = tenantId, req.ItemId, req.WarehouseId }, cancellationToken: ct)).ConfigureAwait(false);

        // 2. 차이 계산
        var diff = req.ActualQty - currentQty;
        var adjustType = diff > 0 ? "increase" : diff < 0 ? "decrease" : "match";
        var now = DateTime.UtcNow;

        // 3. 차이가 있으면 stock_ledger INSERT (INSERT ONLY 원칙)
        if (diff != 0)
        {
            // ── 트랜잭션 감싸기 (재고 정합성 보장 — 중간 실패 시 전체 롤백) ──
            // stock_ledger INSERT + item_stock UPDATE를 원자적으로 묶어 부분 실패로 인한 불일치 방지
            using var tx = _dbConnection.BeginTransaction();
            try
            {
                const string ledgerInsert = """
                    INSERT INTO stock_ledger
                        (tenant_id, item_id, warehouse_id, ledger_date, ym,
                         move_type, source_type, qty_in, qty_out, memo, created_by, created_at)
                    VALUES
                        (@TenantId, @ItemId, @WarehouseId, @LedgerDate, @Ym,
                         @MoveType, 'adjustment', @QtyIn, @QtyOut, @Memo, @CreatedBy, @CreatedAt)
                    """;
                await _dbConnection.ExecuteAsync(new CommandDefinition(ledgerInsert, new
                {
                    TenantId = tenantId,
                    req.ItemId,
                    req.WarehouseId,
                    LedgerDate = now,
                    Ym = now.ToString("yyyy-MM"),
                    MoveType = diff > 0 ? "in" : "out",
                    QtyIn = diff > 0 ? Math.Abs(diff) : 0m,
                    QtyOut = diff < 0 ? Math.Abs(diff) : 0m,
                    Memo = req.Reason ?? "재고 실사 조정",
                    CreatedBy = userId,
                    CreatedAt = now
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                // 4. item_stock 갱신
                const string updateStock = """
                    UPDATE item_stock
                    SET current_qty = @ActualQty, available_qty = @ActualQty, updated_at = @Now
                    WHERE tenant_id = @TenantId AND item_id = @ItemId AND warehouse_id = @WarehouseId
                    """;
                await _dbConnection.ExecuteAsync(new CommandDefinition(updateStock, new
                {
                    req.ActualQty,
                    Now = now,
                    TenantId = tenantId,
                    req.ItemId,
                    req.WarehouseId
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { /* 이미 닫힌 tx */ }
                throw;
            }
        }

        // 5. 품목명·창고명 조회 후 결과 반환
        const string nameSql = """
            SELECT i.item_name AS ItemName, COALESCE(w.wh_name, '') AS WarehouseName
            FROM items i
            LEFT JOIN warehouses w ON w.warehouse_id = @WarehouseId AND w.tenant_id = @TenantId
            WHERE i.item_id = @ItemId AND i.tenant_id = @TenantId
            """;
        var names = await _dbConnection.QuerySingleOrDefaultAsync<(string ItemName, string WarehouseName)>(
            new CommandDefinition(nameSql, new { TenantId = tenantId, req.ItemId, req.WarehouseId }, cancellationToken: ct)).ConfigureAwait(false);

        // 감사로그 — 재고 실사 조정 (before_qty, actual_qty, diff 기록)
        var afterJson = $"{{\"item_id\":\"{req.ItemId}\",\"warehouse_id\":\"{req.WarehouseId}\",\"before_qty\":{currentQty},\"actual_qty\":{req.ActualQty},\"diff\":{diff},\"adjust_type\":\"{adjustType}\"}}";
        await _audit.LogAsync("adjust", "stock", req.ItemId, afterJson: afterJson, reason: req.Reason, ct: ct);

        return new StockAdjustResultDto
        {
            ItemId = req.ItemId,
            ItemName = names.ItemName ?? string.Empty,
            WarehouseName = names.WarehouseName ?? string.Empty,
            BeforeQty = currentQty,
            ActualQty = req.ActualQty,
            DiffQty = diff,
            AdjustType = adjustType,
            AdjustedAt = now
        };
    }

    /// <summary>재고 조정 이력 조회</summary>
    public async Task<List<StockAdjustResultDto>> GetAdjustHistoryAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT l.item_id AS ItemId, i.item_name AS ItemName,
                   COALESCE(w.wh_name, '') AS WarehouseName,
                   0 AS BeforeQty,
                   CASE WHEN l.move_type = 'in' THEN l.qty_in ELSE -l.qty_out END AS DiffQty,
                   CASE WHEN l.move_type = 'in' THEN l.qty_in ELSE l.qty_out END AS ActualQty,
                   CASE WHEN l.move_type = 'in' THEN 'increase' ELSE 'decrease' END AS AdjustType,
                   l.created_at AS AdjustedAt
            FROM stock_ledger l
            JOIN items i ON i.item_id = l.item_id AND i.tenant_id = l.tenant_id
            LEFT JOIN warehouses w ON w.warehouse_id = l.warehouse_id AND w.tenant_id = l.tenant_id
            WHERE l.tenant_id = @TenantId
              AND l.source_type = 'adjustment'
              AND (@From IS NULL OR l.ledger_date >= @From)
              AND (@To IS NULL OR l.ledger_date <= @To)
            ORDER BY l.created_at DESC
            """;
        var rows = await _dbConnection.QueryAsync<StockAdjustResultDto>(
            new CommandDefinition(sql, new { TenantId = tenantId, From = from, To = to }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    // ── 재고 이송 ──

    /// <summary>재고 이송 처리 (출고 + 입고 두 건 INSERT)</summary>
    public async Task TransferStockAsync(string tenantId, string userId, StockTransferRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 유효성 검사: 출발 창고와 도착 창고가 동일하면 안 됨
        if (req.FromWarehouseId == req.ToWarehouseId)
            throw new InvalidOperationException("출발 창고와 도착 창고가 동일합니다.");

        // 출발 창고 가용 수량 확인
        const string checkSql = """
            SELECT COALESCE(available_qty, 0)
            FROM item_stock
            WHERE tenant_id = @TenantId AND item_id = @ItemId AND warehouse_id = @WarehouseId
            """;
        var availableQty = await _dbConnection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(checkSql, new { TenantId = tenantId, req.ItemId, WarehouseId = req.FromWarehouseId }, cancellationToken: ct)).ConfigureAwait(false);

        if (availableQty < req.Qty)
            throw new InvalidOperationException($"출발 창고 가용 수량 부족 (가용: {availableQty}, 요청: {req.Qty})");

        var now = DateTime.UtcNow;
        var ym = now.ToString("yyyy-MM");

        // ── 트랜잭션 감싸기 (재고 정합성 보장 — 중간 실패 시 전체 롤백) ──
        // 출고+입고 stock_ledger 2건 + item_stock 2건 UPDATE를 원자적으로 묶는다.
        // 중간 실패 시 출발 창고만 차감되고 도착 창고에 안 들어가는 유령 재고 방지.
        using var tx = _dbConnection.BeginTransaction();
        try
        {
            // stock_ledger: 출고 INSERT
            const string outInsert = """
                INSERT INTO stock_ledger
                    (tenant_id, item_id, warehouse_id, ledger_date, ym,
                     move_type, source_type, qty_in, qty_out, memo, created_by, created_at)
                VALUES
                    (@TenantId, @ItemId, @WarehouseId, @LedgerDate, @Ym,
                     'out', 'transfer', 0, @Qty, @Memo, @CreatedBy, @CreatedAt)
                """;
            await _dbConnection.ExecuteAsync(new CommandDefinition(outInsert, new
            {
                TenantId = tenantId,
                req.ItemId,
                WarehouseId = req.FromWarehouseId,
                LedgerDate = now,
                Ym = ym,
                req.Qty,
                Memo = req.Memo ?? "재고 이송",
                CreatedBy = userId,
                CreatedAt = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // stock_ledger: 입고 INSERT
            const string inInsert = """
                INSERT INTO stock_ledger
                    (tenant_id, item_id, warehouse_id, ledger_date, ym,
                     move_type, source_type, qty_in, qty_out, memo, created_by, created_at)
                VALUES
                    (@TenantId, @ItemId, @WarehouseId, @LedgerDate, @Ym,
                     'in', 'transfer', @Qty, 0, @Memo, @CreatedBy, @CreatedAt)
                """;
            await _dbConnection.ExecuteAsync(new CommandDefinition(inInsert, new
            {
                TenantId = tenantId,
                req.ItemId,
                WarehouseId = req.ToWarehouseId,
                LedgerDate = now,
                Ym = ym,
                req.Qty,
                Memo = req.Memo ?? "재고 이송",
                CreatedBy = userId,
                CreatedAt = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // item_stock: 출발 창고 차감
            const string updateFrom = """
                UPDATE item_stock
                SET current_qty = current_qty - @Qty, available_qty = available_qty - @Qty, updated_at = @Now
                WHERE tenant_id = @TenantId AND item_id = @ItemId AND warehouse_id = @WarehouseId
                """;
            await _dbConnection.ExecuteAsync(new CommandDefinition(updateFrom, new
            {
                req.Qty, Now = now, TenantId = tenantId, req.ItemId, WarehouseId = req.FromWarehouseId
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // item_stock: 도착 창고 증가 (없으면 INSERT)
            const string upsertTo = """
                INSERT INTO item_stock (tenant_id, item_id, warehouse_id, current_qty, available_qty, avg_cost, updated_at)
                VALUES (@TenantId, @ItemId, @WarehouseId, @Qty, @Qty, 0, @Now)
                ON DUPLICATE KEY UPDATE
                    current_qty = current_qty + @Qty,
                    available_qty = available_qty + @Qty,
                    updated_at = @Now
                """;
            await _dbConnection.ExecuteAsync(new CommandDefinition(upsertTo, new
            {
                TenantId = tenantId, req.ItemId, WarehouseId = req.ToWarehouseId, req.Qty, Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { /* 이미 닫힌 tx */ }
            throw;
        }

        // 감사로그 — 재고 이송 (from_warehouse → to_warehouse, qty 기록)
        var afterJson = $"{{\"item_id\":\"{req.ItemId}\",\"from_warehouse\":\"{req.FromWarehouseId}\",\"to_warehouse\":\"{req.ToWarehouseId}\",\"qty\":{req.Qty}}}";
        await _audit.LogAsync("transfer", "stock", req.ItemId, afterJson: afterJson, reason: req.Memo, ct: ct);
    }

    /// <summary>재고 이송 이력 조회</summary>
    public async Task<List<StockTransferDto>> GetTransferHistoryAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT l.ledger_id AS LedgerId, l.ledger_date AS TransferDate,
                   i.item_name AS ItemName, COALESCE(i.spec, '') AS Spec,
                   COALESCE(wf.wh_name, '') AS FromWarehouse,
                   COALESCE(wt.wh_name, '') AS ToWarehouse,
                   l.qty_out AS Qty, l.memo AS Memo, l.created_by AS CreatedBy
            FROM stock_ledger l
            JOIN items i ON i.item_id = l.item_id AND i.tenant_id = l.tenant_id
            LEFT JOIN warehouses wf ON wf.warehouse_id = l.warehouse_id AND wf.tenant_id = l.tenant_id
            LEFT JOIN stock_ledger l2 ON l2.tenant_id = l.tenant_id
                AND l2.item_id = l.item_id AND l2.source_type = 'transfer' AND l2.move_type = 'in'
                AND l2.created_at = l.created_at AND l2.created_by = l.created_by
            LEFT JOIN warehouses wt ON wt.warehouse_id = l2.warehouse_id AND wt.tenant_id = l2.tenant_id
            WHERE l.tenant_id = @TenantId
              AND l.source_type = 'transfer'
              AND l.move_type = 'out'
              AND (@From IS NULL OR l.ledger_date >= @From)
              AND (@To IS NULL OR l.ledger_date <= @To)
            ORDER BY l.created_at DESC
            """;
        var rows = await _dbConnection.QueryAsync<StockTransferDto>(
            new CommandDefinition(sql, new { TenantId = tenantId, From = from, To = to }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    // ── 창고 분리 현황 ──

    /// <summary>창고별 재고 현황 (자사/위탁 분리)</summary>
    public async Task<List<WarehouseSplitDto>> GetWarehouseSplitAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT w.warehouse_id AS WarehouseId, w.wh_code AS WhCode, w.wh_name AS WhName,
                   COALESCE(w.wh_type, 'normal') AS WhType, COALESCE(w.location, '') AS Location,
                   COUNT(DISTINCT s.item_id) AS ItemCount,
                   COALESCE(SUM(s.current_qty), 0) AS TotalQty,
                   COALESCE(SUM(s.current_qty * s.avg_cost), 0) AS TotalValue
            FROM warehouses w
            LEFT JOIN item_stock s ON s.warehouse_id = w.warehouse_id AND s.tenant_id = w.tenant_id
            WHERE w.tenant_id = @TenantId AND w.is_active = 1
            GROUP BY w.warehouse_id, w.wh_code, w.wh_name, w.wh_type, w.location
            ORDER BY w.wh_type, w.wh_code
            """;
        var rows = await _dbConnection.QueryAsync<WarehouseSplitDto>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <summary>DB 연결 열기 헬퍼</summary>
    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_dbConnection.State != ConnectionState.Open)
        {
            if (_dbConnection is System.Data.Common.DbConnection c)
                await c.OpenAsync(ct).ConfigureAwait(false);
            else
                _dbConnection.Open();
        }
    }
}
