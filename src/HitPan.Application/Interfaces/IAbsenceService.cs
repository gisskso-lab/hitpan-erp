using HitPan.Application.DTOs.Leave;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 휴직 — 기간이 있는 부재. 작(2026-08-13) 그룹웨어 단계6.
/// </summary>
/// <remarks>
/// 🔴 사장님(2026-08-13): <i>"복잡하게 생각할것 없이 그냥 휴직은 상태처리, 상태확인 정도로만"</i> /
/// <i>"휴직은 모든걸 다 수동으로."</i>
///
/// 이 서비스는 <b>계산하지 않는다.</b> 기간·급여·사유를 사람이 넣고, 상태를 본다.
/// </remarks>
public interface IAbsenceService
{
    /// <summary>휴직 목록. 관리자는 전원, 일반 직원은 본인 것만.</summary>
    Task<List<AbsenceDto>> GetListAsync(string tenantId, string? employeeId, string? status,
        DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>휴직 한 건.</summary>
    Task<AbsenceDto?> GetAsync(string tenantId, string absenceId, CancellationToken ct = default);

    /// <summary>휴직 저장(신규·수정). <paramref name="request"/> 의 Submit 이 true 면 결재까지 올린다.</summary>
    Task<SaveAbsenceResult> SaveAsync(string tenantId, string actorId, string actorName,
        SaveAbsenceRequest request, CancellationToken ct = default);

    /// <summary>결재에 올린다.</summary>
    Task<SaveAbsenceResult> SubmitAsync(string tenantId, string actorId, string actorName,
        string absenceId, CancellationToken ct = default);

    /// <summary>승인. 사원 상태도 함께 '휴직' 으로 바꾼다.</summary>
    Task ApproveAsync(string tenantId, string actorId, string absenceId, CancellationToken ct = default);

    /// <summary>반려.</summary>
    Task RejectAsync(string tenantId, string actorId, string absenceId, string? reason,
        CancellationToken ct = default);

    /// <summary>취소(신청 철회).</summary>
    Task CancelAsync(string tenantId, string actorId, string absenceId, CancellationToken ct = default);

    /// <summary>복직 처리. 실제 복직일을 사람이 넣는다. 사원 상태도 '재직' 으로 되돌린다.</summary>
    Task ReturnAsync(string tenantId, string actorId, ReturnFromAbsenceRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 시작일이 지난 승인 건을 '휴직중' 으로 맞춘다.
    /// 🔴 <b>복직은 자동으로 하지 않는다</b> — 실제 복직일은 사람이 넣는다.
    /// </summary>
    Task<int> SyncStatusAsync(string tenantId, CancellationToken ct = default);

    /// <summary>그 날짜에 휴직 중인 사원 id 들. 조직도·연차가 본다.</summary>
    Task<List<string>> GetActiveEmployeeIdsAsync(string tenantId, DateTime asOf,
        CancellationToken ct = default);

    /// <summary>
    /// 그 달 휴직자와 <b>정해 둔 급여 금액</b>. 🔴 급여(단계8)·회계가 여기서 가져간다.
    /// </summary>
    /// <remarks>
    /// 사장님(2026-08-13): <i>"휴직시 급여 : 텍스트 박스로 수동입력 →
    /// 그러면 자연스럽게 급여, 회계이슈도 해결될듯"</i> / <i>"각 고객사 니즈나 사정도 부합시킬 수 있고."</i>
    ///
    /// 그 말이 맞다. 회사마다 육아휴직에 얹어주는 곳·무급인 곳·몇 달만 주는 곳이 다 다른데,
    /// 자동 계산으로는 그걸 못 맞춘다. <b>금액을 직접 받으면 어떤 회사든 그대로 된다.</b>
    /// 급여는 이 금액을 <b>그대로 쓰기만</b> 하면 되고, 다시 계산하지 않는다.
    /// </remarks>
    Task<List<AbsencePayDto>> GetPayForMonthAsync(string tenantId, int year, int month,
        CancellationToken ct = default);

    /// <summary>사원의 재직 중 상태를 바꾼다(재직·휴직·연차).</summary>
    Task SetWorkStatusAsync(string tenantId, string employeeId, string workStatus,
        CancellationToken ct = default);
}
