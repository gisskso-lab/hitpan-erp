using HitPan.Application.DTOs.Leave;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 연차 엔진. 작(2026-08-13) 그룹웨어 단계5.
/// </summary>
/// <remarks>
/// 🔴 <b>반자동 3단</b>(사장님 2026-08-12: <i>"히트판은 100%자동화는 없어. 무조건 반자동이야"</i>)
/// <list type="number">
/// <item>제안 — <see cref="SuggestAsync"/> 가 기준값으로 계산해 <b>보여만 준다</b></item>
/// <item>수정 — 사람이 일수를 고친다(사유를 남긴다)</item>
/// <item>확정 — <see cref="ConfirmAsync"/> 를 불러야 <b>비로소 잔여에 반영</b>된다</item>
/// </list>
///
/// 🔴 <b>법정값을 코드에 넣지 않는다</b>(설계도 §0 지침 ①). 연차 일수·기준 시간은
/// 전부 <c>labor_policy_settings</c> 에서 읽는다 — 상수로 빼는 것도 금지다.
/// 재배포가 필요하면 그건 실패다. 법은 계속 바뀐다.
/// </remarks>
public interface IAnnualLeaveService
{
    /// <summary>
    /// ① 제안 — 이 해에 줄 연차를 계산해 <b>보여준다</b>. 저장하지 않는다.
    /// </summary>
    /// <remarks>
    /// <paramref name="employeeId"/> 가 null 이면 전 직원분을 준다(일괄 화면용).
    /// </remarks>
    Task<List<AnnualLeaveSuggestionDto>> SuggestAsync(string tenantId, int grantYear,
        string? employeeId = null, CancellationToken ct = default);

    /// <summary>
    /// ②③ 수정 + 확정 — 사람이 정한 일수를 저장하고 <b>잔여에 반영</b>한다.
    /// </summary>
    /// <returns>만들어진 부여 ID.</returns>
    Task<string> ConfirmAsync(string tenantId, string confirmedBy,
        ConfirmAnnualLeaveRequest request, CancellationToken ct = default);

    /// <summary>부여 이력 — 언제 누가 얼마를 왜 정했는지.</summary>
    Task<List<AnnualLeaveGrantDto>> GetGrantsAsync(string tenantId, int? grantYear,
        string? employeeId, CancellationToken ct = default);

    /// <summary>노무 기준값 목록 — 지금 유효한 것.</summary>
    Task<List<LaborPolicyDto>> GetPoliciesAsync(string tenantId, DateTime? asOf = null,
        CancellationToken ct = default);

    /// <summary>
    /// 기준값을 고친다. <b>기존 행을 덮지 않고 새 시행일로 행을 추가</b>한다.
    /// </summary>
    /// <remarks>
    /// 🔴 덮어쓰면 과거 계산을 설명할 수 없다 — 작년 연차를 올해 기준으로 다시 계산하게 된다.
    /// </remarks>
    Task<string> SavePolicyAsync(string tenantId, string updatedBy,
        SaveLaborPolicyRequest request, CancellationToken ct = default);
}
