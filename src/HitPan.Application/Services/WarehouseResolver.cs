namespace HitPan.Application.Services;

/// <summary>
/// 🔴 <b>창고 결정 — 20260903작19</b>
///
/// <para><b>사장님 오더 (2026-09-03)</b></para>
/// <list type="number">
///   <item>디폴트 값으로, 상품등록시 지정한 A창고</item>
///   <item>고객사가 a품목을 a→b창고로 분산하고 싶을때는 <b>재고이송으로 수동변경</b></item>
///   <item>매출매입이 이뤄지는 디폴트 값은 상품등록시 지정한 A창고</item>
///   <item>디폴트값으로 지정되어 있지만, 재고가 분산되어 있을경우 <b>창고를 사용자가 지정</b>할 수 있도록</item>
/// </list>
///
/// <para>
/// 🔴 <b>왜 「자동 배분」이 아닌가</b><br/>
/// 여러 창고에서 자동으로 긁어모으면 <b>시스템이 임의로 재고를 옮긴 셈</b>이 된다.
/// 사장님 지시 2번은 그 반대다 — <b>창고 간 이동은 사람이 재고이송으로 한다.</b>
/// 자동으로 넘나들면 현장에서 실제 물건이 있는 창고와 장부가 어긋난다.
/// ⇒ 출고는 <b>한 창고에서</b> 나간다. 이 클래스는 "어느 창고냐" 만 정한다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 공용으로 뺐나</b><br/>
/// 매입(<c>PurchaseService</c>)은 이 순서를 이미 쓰고 있었고 매출(<c>SalesService</c>)만 빠져 있었다.
/// 각자 갖고 있으면 <b>또 한쪽만 고쳐진다</b> — 히트판이 여러 번 겪은 사고다
/// (판매·매입 비대칭 · 생성/수정 비대칭). 한 곳에 두고 양쪽이 부른다.
/// </para>
///
/// <para>
/// ⚠️ 복붙이 아니다. 매입은 <b>입고</b>, 매출은 <b>출고</b>라 결정 이후 처리는 서로 다르다.
/// 같은 것은 <b>"어느 창고를 쓰느냐"</b> 하나뿐이고, 공유하는 것도 그것뿐이다.
/// </para>
/// </summary>
public static class WarehouseResolver
{
    /// <summary>
    /// 창고를 정한다 — <b>① 사용자 지정 → ② 상품마스터 기본창고 → ③ 테넌트 기본창고.</b>
    ///
    /// <para>
    /// 🔴 <b>①이 1순위인 이유</b>: 사장님 지시 4번 —
    /// <i>"재고가 분산되어 있을경우, 창고를 사용자가 지정할수 있도록"</i>.
    /// 화면에서 고른 창고가 있으면 그것이 마스터 기본값을 이긴다.
    /// </para>
    /// <para>
    /// 🔴 <b>②가 핵심</b>: 지시 1·3번. 종전 매출은 이 단계가 없어 ①이 비면 곧장 ③으로 갔다.
    /// </para>
    /// <para>
    /// ③은 마지막 안전망이다 — 상품에 기본창고를 안 정해둔 고객도 판매가 나가야 한다(헌법 #20).
    /// </para>
    /// </summary>
    /// <param name="userSpecifiedWarehouseId">화면/라인에서 사용자가 고른 창고. 없으면 null/빈값.</param>
    /// <param name="itemId">품목 id.</param>
    /// <param name="itemDefaultWarehouses">품목 → 상품마스터 기본창고 사전.
    ///   🔴 <b>실재하고 활성인 창고만</b> 담겨 있어야 한다 — 지워진 창고 id 가 섞이면 유령 창고가 생긴다.</param>
    /// <param name="tenantDefaultWarehouseId">테넌트 기본창고(MAIN 우선). 최후 폴백.</param>
    public static string Resolve(
        string? userSpecifiedWarehouseId,
        string itemId,
        IReadOnlyDictionary<string, string> itemDefaultWarehouses,
        string tenantDefaultWarehouseId)
    {
        // ① 사용자가 고른 창고가 이긴다 (지시 4번)
        if (!string.IsNullOrWhiteSpace(userSpecifiedWarehouseId))
            return userSpecifiedWarehouseId!;

        // ② 상품마스터 기본창고 (지시 1·3번 — 매출에 없던 단계)
        if (!string.IsNullOrWhiteSpace(itemId)
            && itemDefaultWarehouses.TryGetValue(itemId, out var itemWarehouse)
            && !string.IsNullOrWhiteSpace(itemWarehouse))
        {
            return itemWarehouse;
        }

        // ③ 테넌트 기본창고 — 흐름이 끊기지 않게 하는 안전망 (헌법 #20)
        return tenantDefaultWarehouseId;
    }
}
