namespace HitPan.Domain.Enums;

/// <summary>
/// 고용형태.
/// </summary>
/// <remarks>
/// 🔴 사장님 지시(2026-08-12): <i>"정직원이냐, 알바냐, 계약직이냐, 무기계약직이냐 에 따라서도 달라짐"</i>
/// — 연차·퇴직금·4대보험이 고용형태로 갈리므로 <b>실제로 쓰는 말</b>로 나눈다.
///
/// <para>
/// ⚠️ <b>값(문자열)은 절대 바꾸지 않는다.</b> DB <c>employees.emp_type</c> 에 소문자 이름이
/// 그대로 저장돼 있어(<c>EmployeeConfiguration</c> 의 <c>v.ToString().ToLowerInvariant()</c>),
/// 이름을 바꾸면 기존 행이 전부 매칭 실패한다. 그런데 <c>ParseEmpType</c> 이 매칭 실패를
/// <b>조용히 Regular 로 폴백</b>하므로 — 화면엔 "정직원" 으로 보이면서 DB엔 딴 값이 남는다.
/// 오염이 눈에 안 띈다. 그래서 <b>기존 4개는 값도 번호도 그대로 두고, 새 것만 뒤에 붙인다.</b>
/// </para>
///
/// <para>
/// 화면 표시 이름(정직원·알바 …)은 <c>EmployeeTypeLabels</c> 가 갖는다.
/// 값은 코드가 쓰는 열쇠, 이름은 사람이 읽는 말 — 둘을 섞지 않는다.
/// </para>
/// </remarks>
public enum EmployeeType
{
    /// <summary>정직원. DB 값 <c>regular</c>.</summary>
    Regular = 1,

    /// <summary>계약직(기간제). DB 값 <c>contract</c>.</summary>
    Contract = 2,

    /// <summary>
    /// 단시간 근로자 — 흔히 말하는 <b>알바</b>. DB 값 <c>part</c>.
    /// </summary>
    /// <remarks>
    /// 🔴 주 15시간 미만이면 연차·주휴가 달라진다. 그 판정은 이 값만으로 못 하고
    /// <c>employees.weekly_hours</c>(주당 소정근로시간)를 같이 봐야 한다.
    /// </remarks>
    Part = 3,

    /// <summary>파견. DB 값 <c>dispatch</c>.</summary>
    Dispatch = 4,

    /// <summary>
    /// 무기계약직 — 기간의 정함이 없으나 정직원과 처우가 다른 자리. DB 값 <c>permanent</c>.
    /// </summary>
    /// <remarks>
    /// 작(2026-08-13) 단계4 신설. 사장님이 지목한 4형태 중 유일하게 없던 것이다.
    /// 퇴직금·연차는 정직원과 같이 보되 급여 체계가 다른 경우가 많아 따로 둔다.
    /// </remarks>
    Permanent = 5,

    /// <summary>
    /// 일용직. DB 값 <c>daily</c>.
    /// </summary>
    /// <remarks>
    /// 4대보험·퇴직금 판정이 다른 자리라 함께 연다. 안 쓰는 회사는 안 고르면 그만이다.
    /// </remarks>
    Daily = 6
}

/// <summary>
/// 고용형태 표시 이름. 화면·명세서가 이걸 쓴다.
/// </summary>
/// <remarks>
/// 🔴 종전엔 <c>EmployeePage.razor</c> 마크업에 "정규직"·"계약직" 이 하드코딩돼 있었다.
/// 그 한 곳뿐이라 다른 화면(급여·연차)이 생기면 각자 다시 적게 되고, 말이 갈린다.
/// 여기 한 곳에서만 정한다.
/// </remarks>
public static class EmployeeTypeLabels
{
    public const string Regular = "정직원";
    public const string Contract = "계약직";
    public const string Part = "알바(단시간)";
    public const string Dispatch = "파견";
    public const string Permanent = "무기계약직";
    public const string Daily = "일용직";

    /// <summary>DB 값(소문자) → 사람이 읽는 이름.</summary>
    public static string Of(string? empType) => empType?.ToLowerInvariant() switch
    {
        "regular" => Regular,
        "contract" => Contract,
        "part" => Part,
        "dispatch" => Dispatch,
        "permanent" => Permanent,
        "daily" => Daily,
        // 모르는 값은 감추지 않고 그대로 보여준다 — 조용히 "정직원" 으로 보이면
        // 오염을 영영 못 찾는다(ParseEmpType 폴백이 그 병을 갖고 있다).
        _ => string.IsNullOrWhiteSpace(empType) ? "-" : empType!
    };
}
