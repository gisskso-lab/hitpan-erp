using System.Data;
using Dapper;

namespace HitPan.Application.Services;

// 작20260428이7 P0-A: 문서번호 채번 헬퍼.
// 사고: EF Repository.FindAsync(StartsWith).Count + 1 패턴은 동시 호출 시 같은 번호 생성 →
//       (tenant_id, doc_no) UNIQUE 충돌 → 4/28 174건 자동 사슬 중 71건 stock_ledger 누락.
// 정공법: DB의 실제 MAX 번호 + 1 + 재시도 루프 (충돌 시 다시 채번).
//         §원칙 #20 워크플로우 끊김 금지 + §원칙 #16 connection 안전.
internal static class DocumentNumberHelper
{
    // table/numberColumn 은 호출부 상수만 받으므로 SQL 인젝션 위험 없음.
    public static async Task<string> NextNumberAsync(
        IDbConnection db,
        string tenantId,
        string table,
        string numberColumn,
        string prefix,
        CancellationToken ct = default)
    {
        var sql = $"""
            SELECT COALESCE(
                MAX(CAST(SUBSTRING({numberColumn}, {prefix.Length + 1}) AS UNSIGNED)),
                0
            ) + 1
            FROM {table}
            WHERE tenant_id = @TenantId
              AND {numberColumn} LIKE @Pattern
            """;

        var next = await db.QueryFirstOrDefaultAsync<long>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, Pattern = prefix + "%" },
            cancellationToken: ct));

        return $"{prefix}{next:000}";
    }
}
