using HitPan.Backoffice.API.Security;

namespace HitPan.Backoffice.API.Attributes;

/// <summary>
/// 🔴 <b>대리점 토큰에게 열어 주는 유일한 표식</b> (20261007작10 ①사이클 갈래 ㄱ · 설계 §1-2).
///
/// <para>백오피스 API 는 <b>거부 기본값</b>이다. 대리점 토큰은 이 표식이 붙은 액션 외에는
/// <b>전부 403</b> 이고(<c>BoAccessGuard</c>), 표식이 붙은 액션에서는 행 범위가
/// <b>토큰에서만</b> 온다(<c>ResellerScopeArgumentFilter</c> · <c>IResellerScope</c> · 헌법 #2).</para>
///
/// <para>🔴 <b>붙이는 것이 권한을 주는 행위다.</b> 컨트롤러 36엔드포인트를 한 줄도 고치지 않고 닫은 구조라
/// (설계 §1-1 — 컨트롤러마다 <c>WHERE</c> 를 손으로 넣는 방식은 9/22 <c>TenantMiddleware</c>
/// 화이트리스트 3연발과 같은 모양이라 금지), 이 표식을 새로 붙일 때는
/// <b>그 액션의 모든 조회가 범위값을 <see cref="Services.IResellerScope"/> 에서만 받는지</b> 확인해야 한다.
/// 쿼리·바디·헤더에서 <c>resellerId</c> 를 직접 읽는 액션에 붙이면 범위 출처가 둘이 된다.</para>
///
/// <para>게이트 G-1 이 「모든 액션이 익명·본사전용·<see cref="ResellerScopedAttribute"/> 중
/// 정확히 하나에 속하는가」를 리플렉션으로 전수 확인한다 — 표식 없이 신설한 라우트는 빨간불이다.</para>
/// </summary>
/// <remarks>
/// ⚠️ 이 표식은 <b>인증 필터가 아니다</b>(표식 스스로는 아무것도 막지 않는다).
/// 판정은 전역 <c>BoAccessGuard</c> 한 곳에서만 일어난다 — 강제 지점을 늘리지 않는 것이 설계값이다.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ResellerScopedAttribute : Attribute
{
    /// <summary>
    /// 이 액션에 필요한 <b>대리점 사다리 최소 등급</b>. 기본값은 <see cref="BoRoles.ResellerUser"/>
    /// (= 대리점 소속이면 누구나). 상위 전용이면 <see cref="BoRoles.ResellerAdmin"/> 를 준다.
    /// </summary>
    public string MinRole { get; }

    public ResellerScopedAttribute(string minRole = BoRoles.ResellerUser)
    {
        MinRole = minRole;
    }

    /// <summary>사다리 최소 등급(갈래 안 비교용). 모르는 값은 0 이 되어 아무 대리점도 못 막는 일이 없다.</summary>
    public int MinRank => BoRoles.Rank(BoRoles.Normalize(MinRole));
}
