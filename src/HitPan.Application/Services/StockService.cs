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

    public StockService(IUnitOfWork unitOfWork, ICurrentTenant currentTenant, IDbConnection dbConnection)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
        _dbConnection = dbConnection;
    }

    public async Task<IReadOnlyList<StockBalanceDto>> GetBalanceAsync(CancellationToken ct = default)
    {
        var repo = _unitOfWork.Repository<StockLedger>();
        var currentYm = DateTime.UtcNow.ToString("yyyy-MM");
        var rows = await repo.FindAsync(x => x.Ym == currentYm);

        return rows
            .GroupBy(x => new { x.ItemId, x.WarehouseId })
            .Select(g => new StockBalanceDto
            {
                ItemId = g.Key.ItemId,
                WarehouseId = g.Key.WarehouseId,
                BalanceQty = g.Sum(x => x.QtyIn - x.QtyOut)
            })
            .ToList();
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
}
