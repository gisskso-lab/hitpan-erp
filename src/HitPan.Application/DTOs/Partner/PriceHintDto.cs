namespace HitPan.Application.DTOs.Partner;

/// <summary>
/// 단가 참고값 — 명세서 작성 화면에서 <b>커서를 올리면 보여주는 4값</b> (20260820작4 · 설계2).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 설계 (2026-08-20)</b>:
/// <i>"단가는 모든 워크플로우 명세서 작성시(발주,판매,반품,견적,수주,판매) <b>직접 작성이 가능하되</b>,
/// 마우스 커서 갖다대면, <b>업체특별단가·최종단가·표준단가·혹은 상품특별단가</b>를 고객이 볼 수 있도록"</i>
/// </para>
///
/// <para>
/// 🔴 <b>이 값들은 "적용된 단가" 가 아니다.</b> 사람이 보고 고르라고 주는 <b>참고 자료</b>다.
/// 문서에 실제로 들어가는 값은 화면의 입력칸에 있는 것이고, 사람이 언제든 고칠 수 있다.
/// ⚠️ 이 DTO 를 <c>UnitPrice</c> 로 곧장 밀어 넣는 코드를 짜지 마라 —
/// 자동 적용은 <b>업체특별단가 하나만</b>이고(설계2 §4-3 C안), 나머지 셋은 <b>표시 전용</b>이다.
/// </para>
///
/// <para>
/// ⚠️ <b>값이 없으면 <c>null</c> 이다. 0 이 아니다.</b>
/// 0 으로 채우면 화면에서 <b>진짜 0원과 구별이 안 된다</b>(게이트 G-8).
/// 받는 쪽은 <c>null</c> 인 줄을 <b>빼거나 '없음'</b> 으로 그린다.
/// </para>
/// </remarks>
public class PriceHintDto
{
    /// <summary>이 참고값이 어느 상품 것인가.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// 업체특별단가 — <c>partner_special_prices</c>. 🔴 <b>자동 채움에 쓰이는 유일한 값</b>(C안).
    /// </summary>
    /// <remarks>
    /// ⚠️ 할인율 모드(<c>price_type='discount'</c>)면 <c>special_price</c> 가 0 이고 할인율만 있다.
    /// 그 경우 표준단가에 할인율을 적용한 <b>계산된 금액</b>이 여기 담긴다(서비스가 계산한다).
    /// </remarks>
    public decimal? PartnerSpecialPrice { get; set; }

    /// <summary>
    /// 최종단가 — <b>그 업체와 마지막으로 거래한 단가</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>판매와 매입이 서로 다른 값이다</b>(판 값 ≠ 산 값). 화면 성격에 맞는 쪽만 채운다 —
    /// 판매계열(견적·수주·판매)은 <c>sales_delivery_items</c>, 매입계열(발주·매입·반품)은
    /// <c>purchase_receipt_items</c> 에서 온다.
    /// ⚠️ <c>partner_special_prices.last_supply_date</c> 는 <b>날짜일 뿐 금액이 아니다.</b> 그걸 쓰지 마라.
    /// </remarks>
    public decimal? LastPrice { get; set; }

    /// <summary>최종단가가 언제 거래분인지 — 화면에서 <i>"2026-07-15 거래"</i> 로 함께 보여준다.</summary>
    public DateTime? LastPriceDate { get; set; }

    /// <summary>표준단가 — <c>items.std_price</c>. 상품 마스터에 적힌 기준 금액.</summary>
    public decimal? StdPrice { get; set; }

    /// <summary>
    /// 상품특별단가 — <c>item_special_prices</c>. 🔴 <b>표시 전용. 자동 채움에 끼지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 사장님 판정(2026-08-20): <i>"상품 특별단가는 존재 자체가 큰 의미가 없네"</i>
    /// ⇒ 지우지는 않되(헌법 #1·#37) <b>이 축 위에 새 기능을 얹지 않는다.</b>
    /// 말풍선에서도 <b>맨 아래</b>에 둔다(사장님이 <i>"혹은 상품특별단가"</i> 로 마지막에 부르셨다).
    /// </remarks>
    public decimal? ItemSpecialPrice { get; set; }
}
