using System.Data;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>
/// monthly_summary 멱등 가드 — 같은 source(매입/매출/입금/지급 확정 건)가 두 번 가산되지 않도록 차단.
/// 작업지시서: 20260425작4 (P0-4 멱등키 인프라). DESIGN_PRINCIPLES §6.1 (무결성 — 중복 없음).
///
/// 흐름:
///   1) monthly_summary_sources에 INSERT IGNORE — UNIQUE(tenant, type, source_id, field) 위반이면 0행 반환
///   2) ROW_COUNT() = 1 일 때만 monthly_summary 가산
///   3) ROW_COUNT() = 0 → 이미 처리된 source → 스킵 (멱등 보장)
///
/// 모든 호출은 호출자의 트랜잭션(dbTx) 안에서 수행되어야 한다.
/// </summary>
public static class MonthlySummaryGuard
{
    public enum SummaryField { TotalSales, TotalPurchase, TotalReceipt, TotalPayment }

    /// <summary>
    /// 멱등 가드 적용 후 monthly_summary 가산을 시도한다.
    /// </summary>
    /// <returns>실제 가산되었으면 true (신규 source), 이미 처리되어 스킵되었으면 false.</returns>
    public static async Task<bool> TryApplyAsync(
        IDbConnection conn,
        IDbTransaction? dbTx,
        string tenantId,
        DateTime date,
        string sourceType,
        string sourceId,
        SummaryField field,
        decimal amount,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(sourceType) || string.IsNullOrEmpty(sourceId))
            throw new ArgumentException("tenantId, sourceType, sourceId 모두 필수입니다.");

        var ym = date.ToString("yyyyMM");
        var fieldName = ToColumnName(field);

        // 1) 멱등 추적 INSERT — 이미 있으면 IGNORE
        var inserted = await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT IGNORE INTO monthly_summary_sources
              (tenant_id, `year_month`, source_type, source_id, field_name, amount_delta)
            VALUES
              (@TenantId, @Ym, @SourceType, @SourceId, @FieldName, @Amount)
            """,
            new
            {
                TenantId = tenantId,
                Ym = ym,
                SourceType = sourceType,
                SourceId = sourceId,
                FieldName = fieldName,
                Amount = amount
            },
            transaction: dbTx,
            cancellationToken: ct));

        // 0이면 이미 처리된 source → 가산 스킵
        if (inserted == 0) return false;

        // 2) monthly_summary 가산 (해당 필드만)
        var sql = $$"""
            INSERT INTO monthly_summary (summary_id, tenant_id, `year_month`, total_sales, total_purchase, total_receipt, total_payment, last_updated_at)
            VALUES (UUID(), @TenantId, @Ym,
              {{(field == SummaryField.TotalSales ? "@Amount" : "0")}},
              {{(field == SummaryField.TotalPurchase ? "@Amount" : "0")}},
              {{(field == SummaryField.TotalReceipt ? "@Amount" : "0")}},
              {{(field == SummaryField.TotalPayment ? "@Amount" : "0")}},
              NOW(6))
            ON DUPLICATE KEY UPDATE
              {{fieldName}} = {{fieldName}} + @Amount,
              last_updated_at = NOW(6)
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new { TenantId = tenantId, Ym = ym, Amount = amount },
            transaction: dbTx,
            cancellationToken: ct));

        return true;
    }

    private static string ToColumnName(SummaryField field) => field switch
    {
        SummaryField.TotalSales => "total_sales",
        SummaryField.TotalPurchase => "total_purchase",
        SummaryField.TotalReceipt => "total_receipt",
        SummaryField.TotalPayment => "total_payment",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}
