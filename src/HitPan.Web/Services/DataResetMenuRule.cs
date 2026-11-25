namespace HitPan.Web.Services;

/// <summary>
/// 20261125작 — 사이드바 「모든데이터 초기화」 한 줄을 보일지 가른다(대표 결재 §146 「초기화만」 · PM Q3 · K4 (가)).
/// </summary>
/// <remarks>
/// <para>
/// 재료 = <c>GET api/users/my-level</c> 응답의 <c>dataResetGate</c> 칸. 서버 짝 = <c>HitPan.Application.DTOs.DataReset.DataResetGateCodes</c>
/// (네 글자가 같아야 한다 · 시험이 이 상수로 서버 응답을 대조한다).
/// </para>
/// <para>
/// ⚠️ 메뉴를 감추는 것은 <b>차단이 아니다.</b> 막는 것은 서버(<c>DataResetService.ResetAllAsync</c> 의 대표 판정)다.
/// 대표 0명 · 2명 이상인 설치본은 관리자에게 메뉴를 <b>보여 준다</b> — 들어가서 실행하면 서버가 「대표 계정 확인」 안내를 띄운다(「막고 안내」 의 「안내」 자리).
/// </para>
/// <para>
/// Blazor 의존 0 순수 C# — 시험 프로젝트가 소스를 링크해 직접 부른다(<see cref="ApiErrorText"/> 선례).
/// </para>
/// </remarks>
public static class DataResetMenuRule
{
    public const string Allowed = "allowed";
    public const string NotParent = "not_parent";
    public const string NoParent = "no_parent";
    public const string MultipleParents = "multiple_parents";

    /// <summary>
    /// 참 = 메뉴 보임(allowed · no_parent · multiple_parents) / 거짓 = 안 보임(not_parent · null · 빈칸 · 모르는 값).
    /// 못 읽었으면 안 보인다 — 못 읽었는데 열어 주면 막은 적이 없는 것과 같다(Sidebar 계정 유형 판정 원칙).
    /// </summary>
    public static bool ShowResetLink(string? gate) => gate switch
    {
        Allowed => true,
        NoParent => true,
        MultipleParents => true,
        _ => false,
    };
}
