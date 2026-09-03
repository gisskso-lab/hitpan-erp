using System.Data;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 <b>창고 조회 — 20260904작20</b>
///
/// <para>
/// <see cref="WarehouseResolver"/> 가 <b>"어느 창고냐"를 판정</b>한다면,
/// 이 클래스는 그 판정에 <b>필요한 값을 DB 에서 읽어온다.</b>
/// 판정은 순수 함수로 두고(시험이 DB 없이 돌 수 있게), 조회만 여기 모은다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 공용으로 뺐나</b><br/>
/// 같은 SQL 이 <c>SalesService</c>·<c>PurchaseService</c> 에 <b>따로 두 벌</b> 있었고,
/// <c>BomService</c> 에는 <b>아예 없었다</b>(그래서 BOM 이 상품마스터 기본창고를 못 봤다).
/// 사본이 늘수록 <b>또 한쪽만 고쳐진다</b> — 히트판이 여러 번 겪은 사고다
/// (판매·매입 비대칭 · 생성/수정 비대칭 · 이번 BOM 누락).
/// </para>
///
/// <para>
/// ⚠️ 이 클래스가 하는 일은 <b>조회뿐</b>이다. 입고냐 출고냐, 차감이냐 증가냐는
/// 부르는 쪽이 정한다 — 그건 서로 다르기 때문에 공유하지 않는다.
/// </para>
/// </summary>
public static class WarehouseLookup
{
    /// <summary>
    /// 품목별 <b>상품마스터 기본창고</b> 사전을 한 번에 읽는다 (N+1 방지).
    ///
    /// <para>
    /// 🔴 <c>warehouses</c> 와 조인해 <b>실재하고 활성인 창고만</b> 돌려준다.
    /// 창고가 지워지거나 비활성화된 뒤 <c>items</c> 에 남은 낡은 id 를 그대로 쓰면
    /// <c>item_stock</c>·<c>stock_ledger</c> 가 <b>유령 창고</b>에 쌓인다
    /// (10차 P0-4 가 막았던 사고). 유효하지 않으면 사전에서 빠지고 테넌트 폴백이 받는다.
    /// </para>
    /// </summary>
    public static async Task<Dictionary<string, string>> LoadItemDefaultWarehousesAsync(
        IDbConnection db,
        string tenantId,
        IEnumerable<string> itemIds,
        IDbTransaction? tx = null,
        CancellationToken ct = default)
    {
        var ids = itemIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var rows = await db.QueryAsync<(string ItemId, string WarehouseId)>(
            new CommandDefinition(
                """
                SELECT i.item_id, i.default_warehouse_id
                  FROM items i
                  JOIN warehouses w
                    ON w.warehouse_id = i.default_warehouse_id
                   AND w.tenant_id    = i.tenant_id
                   AND w.is_active    = 1
                 WHERE i.tenant_id = @TenantId
                   AND i.item_id IN @Ids
                   AND i.default_warehouse_id IS NOT NULL
                   AND i.default_warehouse_id <> ''
                """,
                new { TenantId = tenantId, Ids = ids },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToDictionary(r => r.ItemId, r => r.WarehouseId);
    }

    /// <summary>
    /// 테넌트 <b>기본창고</b> — <c>wh_code</c> 'MAIN'/'WH-MAIN' 우선, 그다음 <c>wh_code</c> 순.
    /// 창고결정 3단의 <b>③ 최후 폴백</b>이다(헌법 #20 — 기본창고를 안 정해둔 고객도 흐름이 안 끊긴다).
    /// </summary>
    public static async Task<string?> ResolveTenantDefaultWarehouseAsync(
        IDbConnection db,
        string tenantId,
        IDbTransaction? tx = null,
        CancellationToken ct = default)
        => await db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(
                """
                SELECT warehouse_id FROM warehouses
                 WHERE tenant_id = @TenantId AND is_active = 1
                 ORDER BY (CASE WHEN wh_code IN ('MAIN','WH-MAIN') THEN 0 ELSE 1 END), wh_code
                 LIMIT 1
                """,
                new { TenantId = tenantId },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);
}
