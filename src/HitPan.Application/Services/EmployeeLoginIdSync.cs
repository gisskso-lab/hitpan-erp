using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 20261005작5 P-6 — 사원계정 칸(<c>employees.login_id</c> · DB-137)을 <c>users</c> 기준으로 다시 맞춘다. 앱 기동 때 1회.
/// </summary>
/// <remarks>
/// <para>🔴 왜 있나 — 되돌림(1.3.52 재게시) 동안 옛 판은 이 칸을 모른다. 그 사이 만든 계정은 칸이 비고,
/// 폐기·퇴사한 계정은 옛 아이디가 남는다. 다시 이 판으로 오면(재전진) 사본이 어긋난 채 보인다(설계 §10 R-3).
/// 마이그(DB-137)는 한 번만 돌므로 그 일을 못 한다 ⇒ 기동마다 같은 규칙으로 맞춘다.</para>
/// <para>규칙(DB-137 채우기와 같다): 연결된 계정이 살아 있으면(<c>is_deleted=0</c> · 표식 없음) 그 아이디 ·
/// 아니면 NULL. 사용 안 함(<c>is_active=0</c>)은 계정이 있으므로 아이디를 둔다.</para>
/// <para>멱등 — 어긋난 행만 고친다. 맞는 DB 에서는 두 문장 모두 0행. 사원 이메일 칸(<c>email</c>)은 안 건드린다.</para>
/// <para>칸이 없으면(DB-137 미적용 · 마이그 실패) 건너뛰고 경고만 남긴다 — 기동을 막지 않는다(Program.cs 마이그 블록과 같은 원칙).</para>
/// </remarks>
public static class EmployeeLoginIdSync
{
    public sealed record Result(bool ColumnMissing, int Filled, int Cleared);

    // 살아 있는 연결 계정의 아이디와 다르면(또는 비어 있으면) 맞춘다.
    internal const string FillSql = """
        UPDATE employees e
          JOIN users u
            ON u.user_id   = e.user_id
           AND u.tenant_id = e.tenant_id
           SET e.login_id = u.email
         WHERE u.is_deleted = 0
           AND u.email NOT LIKE 'resigned+%'
           AND u.email NOT LIKE 'retired+%'
           AND (e.login_id IS NULL OR e.login_id <> u.email)
        """;

    // 살아 있는 연결 계정이 없는데 칸에 값이 남아 있으면 비운다.
    internal const string ClearSql = """
        UPDATE employees e
           SET e.login_id = NULL
         WHERE e.login_id IS NOT NULL
           AND NOT EXISTS (
                 SELECT 1 FROM users u
                  WHERE u.user_id   = e.user_id
                    AND u.tenant_id = e.tenant_id
                    AND u.is_deleted = 0
                    AND u.email NOT LIKE 'resigned+%'
                    AND u.email NOT LIKE 'retired+%')
        """;

    public static async Task<Result> ResyncAsync(IDbConnection db, ILogger? logger, CancellationToken ct = default)
    {
        if (db.State != ConnectionState.Open)
        {
            if (db is DbConnection dbc) await dbc.OpenAsync(ct).ConfigureAwait(false);
            else db.Open();
        }

        var hasColumn = await db.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM information_schema.columns
             WHERE table_schema = DATABASE()
               AND table_name   = 'employees'
               AND column_name  = 'login_id'
            """, cancellationToken: ct)).ConfigureAwait(false);

        if (hasColumn == 0)
        {
            logger?.LogWarning("[EmployeeLoginIdSync] employees.login_id 칸이 없다(DB-137 미적용) — 사원계정 재맞춤을 건너뛴다");
            return new Result(true, 0, 0);
        }

        var filled = await db.ExecuteAsync(new CommandDefinition(FillSql, cancellationToken: ct)).ConfigureAwait(false);
        var cleared = await db.ExecuteAsync(new CommandDefinition(ClearSql, cancellationToken: ct)).ConfigureAwait(false);

        if (filled > 0 || cleared > 0)
            logger?.LogWarning("[EmployeeLoginIdSync] 사원계정 사본을 다시 맞췄다 — 채움 {Filled}행 · 비움 {Cleared}행(되돌림 뒤 재전진 흔적일 수 있다)", filled, cleared);

        return new Result(false, filled, cleared);
    }
}
