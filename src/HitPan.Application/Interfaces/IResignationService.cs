using HitPan.Application.DTOs.Employee;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 전자 퇴직서(사직서) 서비스 — 작20260824작2 [4].
/// </summary>
/// <remarks>
/// 🔴 <b>관리자 퇴사 처리와 다른 자리다.</b> 여기는 <b>직원이 올리는 문서</b>이고,
/// 실제 퇴사 반영(<c>employees.is_resigned</c>)은 기존 <c>EmployeeService.ResignAsync</c> 가 한다.
/// 수리될 때 그것을 <b>부른다</b> — 다시 구현하지 않는다(헌법 #1).
/// </remarks>
public interface IResignationService
{
    /// <summary>목록. 본인 것만 볼지 전체를 볼지는 <paramref name="onlyMine"/> 이 정한다.</summary>
    Task<List<ResignationLetterDto>> GetListAsync(
        string tenantId, string employeeId, bool onlyMine, CancellationToken ct = default);

    Task<ResignationLetterDto?> GetAsync(string resignationId, string tenantId, CancellationToken ct = default);

    /// <summary>작성·수정(draft). 제출 전까지는 고칠 수 있다.</summary>
    Task<string> SaveAsync(SaveResignationRequest request, string tenantId, string actorId, CancellationToken ct = default);

    /// <summary>
    /// 제출 — 결재를 올린다.
    /// </summary>
    /// <returns>
    /// 결재를 못 걸면 그 <b>이유</b>. 성공이면 <c>null</c>.
    /// 🔴 조용히 성공한 척하지 않는다 — 사직서는 결재가 존재 이유다.
    /// </returns>
    Task<string?> SubmitAsync(string resignationId, string tenantId, string actorId, CancellationToken ct = default);

    /// <summary>철회 — 본인이 거둬들인다. 반려(회사가 물린 것)와 구분해 남긴다.</summary>
    Task WithdrawAsync(string resignationId, string tenantId, string actorId, CancellationToken ct = default);

    /// <summary>수리 — 회사가 실제 퇴사일을 정해 확정한다.</summary>
    Task AcceptAsync(string resignationId, AcceptResignationRequest request,
        string tenantId, string actorId, CancellationToken ct = default);
}
