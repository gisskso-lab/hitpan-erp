namespace HitPan.Application.Common;

/// <summary>
/// 자동 사슬(발주 → 매입확정)을 <b>어느 품목에 태워도 되는가</b>를 정하는 한 곳.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 결재 (2026-08-25)</b> — <i>"데이터 정합성이 중요하지 막아!!"</i>
/// </para>
/// <para>
/// 🔴 <b>왜 막나</b> — 사슬은 <b>매입확정</b>을 태운다. 매입확정은 재고만 올리는 게 아니라
/// <b>매입 분개</b>를 만들고 <b>외상매입금</b>(<c>partner_balance.total_purchase</c>)을 가산한다.
/// 반제품은 <b>사 오는 게 아니라 만드는 것</b>이므로, 사슬을 태우면
/// <b>사지도 않은 물건이 재고로 잡히고 갚을 돈이 생긴다.</b>
/// 재고 오염보다 <b>회계 오염</b>이 무겁다.
/// </para>
/// <para>
/// 🔴 <b>사장님 4/27 헌법과 같은 방향</b>
/// (<c>docs/개발/erp/next_session_prompt_20260428.md:23·26</c>):
/// <i>"자재가 들어오기 전에 완제품이 +되는 일은 절대 없어야 함"</i> ·
/// <i>"다단 반제품/완제품은 자동발주 아닌 자동생산"</i>
/// </para>
/// <para>
/// ⚠️ <b>막는 것은 사슬뿐이다.</b> 발주서는 그대로 만들고 알림도 그대로 띄운다 —
/// 반제품을 <b>외주가공으로 사 오는</b> 길을 끊으면 안 된다(헌법 #20 워크플로우 안 끊김).
/// 반제품은 <b>「발주서만」 경로로 강제</b>될 뿐이다.
/// </para>
/// <para>
/// 🔴 <b>이 판단을 여기 한 곳에만 둔다.</b> BOM 경로와 판매 경로가 각자 판정하면
/// 한쪽만 고쳐지는 일이 또 생긴다 — 8/21 이 정확히 그랬다.
/// </para>
/// </remarks>
public static class AutoChainPolicy
{
    /// <summary>
    /// 이 품목에 자동 매입확정(사슬)을 태워도 되나.
    /// <b>사서 채우는 물건만</b> 허용한다.
    /// </summary>
    /// <param name="itemType">
    /// <c>items.item_type</c>. 🔴 이 칸은 <b>enum 이 아니라 <c>longtext</c></b> 라(실측)
    /// 어떤 값이든 들어올 수 있다. 그래서 <b>모르는 값은 막는다</b> — 정합성이 먼저다.
    /// </param>
    public static bool CanAutoReceive(string? itemType) =>
        Normalize(itemType) switch
        {
            // 사 오는 것 — 사슬 허용
            "material" or "raw" or "product" or "expense" => true,

            // 만드는 것 — 사슬 금지 (semi 는 레거시 축약형, assembly 는 조립품)
            "semi_finished" or "semi" or "assembly" or "finished" or "promo" => false,

            // 🔴 모르는 값은 막는다. 새 유형이 생겼는데 여기 안 적혔다면
            //    "일단 통과" 보다 "일단 막고 발주서만" 이 안전하다 — 되돌릴 수 있는 쪽을 고른다.
            _ => false
        };

    /// <summary>사람에게 보여줄 이유. 🔴 개발용어를 쓰지 않는다(고객 화면에 그대로 나간다).</summary>
    public static string BlockedReason(string itemName) =>
        $"{itemName}은(는) 만들어서 채우는 품목이라 매입확정까지 자동으로 하지 않습니다. "
      + "발주서만 만들었습니다.";

    private static string Normalize(string? itemType) =>
        (itemType ?? string.Empty).Trim().ToLowerInvariant();
}
