using HitPan.Application.DTOs.WorkReport;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 업무보고서(일일·주간·월간·경위서) 서비스. 작(2026-08-13) 그룹웨어 단계3.
/// </summary>
/// <remarks>
/// 사장님 지시(2026-08-12): <i>"일일보고서, 주간보고서, 월간보고서, 경위서 메뉴 추가"</i>
/// <para>
/// ⚠️ <b><see cref="IReportService"/> 와 다른 것이다.</b> 그쪽은 견적·수주·매입 <b>현황 리포트</b>
/// (집계·통계)이고, 이쪽은 직원이 <b>써서 결재를 올리는 문서</b>다. 이름이 비슷해 헷갈리기 쉽다 —
/// 실제로 처음에 같은 이름으로 만들다가 기존 파일을 덮어써 매출수익성 분석이 깨졌다(헌법 #1).
/// </para>
/// </remarks>
public interface IWorkReportService
{
    /// <summary>
    /// 보고서 목록.
    /// </summary>
    /// <param name="employeeId">
    /// 지정하면 그 사원 것만. null 이면 테넌트 전체(관리자용).
    /// 🔴 호출부가 권한을 판정해 넘긴다 — 서비스가 임의로 전체를 열어주지 않는다.
    /// </param>
    Task<List<WorkReportListDto>> GetListAsync(string tenantId, string? employeeId,
        string? reportType, DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>보고서 상세.</summary>
    Task<WorkReportDetailDto?> GetAsync(string tenantId, string reportId, CancellationToken ct = default);

    /// <summary>
    /// 이 사람이 이 보고서의 결재자인가(위임 포함). <b>결재자는 본문을 볼 수 있어야 한다</b>
    /// — 안 읽고 승인하는 결재는 결재가 아니다(P1-4 봉합).
    /// </summary>
    Task<bool> IsApproverAsync(string tenantId, string reportId, string employeeId,
        CancellationToken ct = default);

    /// <summary>새 보고서를 만든다.</summary>
    /// <returns>보고서 ID + 상신했다면 <b>결재가 실제로 올라갔는지</b>.</returns>
    Task<CreateWorkReportResult> CreateAsync(string tenantId, string employeeId, string employeeName,
        SaveWorkReportRequest request, CancellationToken ct = default);

    /// <summary>
    /// 보고서를 고친다. <b>작성중·반려 상태에서만</b> 된다.
    /// </summary>
    /// <returns>
    /// <see cref="SubmitWorkReportResult.Saved"/> 가 고쳐졌는지. 결재중·완료라서 못 고쳤으면 false.
    /// 상신까지 한 경우 <see cref="SubmitWorkReportResult.ApprovalCreated"/> 로 <b>결재가 실제로
    /// 올라갔는지</b> 도 함께 알려준다(P0-1 봉합 — 되는 척 제거).
    /// </returns>
    Task<SubmitWorkReportResult> UpdateAsync(string tenantId, string reportId, string employeeId,
        SaveWorkReportRequest request, CancellationToken ct = default);

    /// <summary>작성중 보고서를 결재에 올린다.</summary>
    /// <returns>저장 여부와 <b>결재가 실제로 만들어졌는지</b>. 못 만들었으면 그 이유가 담긴다.</returns>
    Task<SubmitWorkReportResult> SubmitAsync(string tenantId, string reportId, string employeeId,
        string employeeName, CancellationToken ct = default);

    /// <summary>
    /// 보고서를 지운다. <b>작성중일 때만</b> 된다 — 결재에 올라간 것은 기록이다.
    /// </summary>
    Task<bool> DeleteAsync(string tenantId, string reportId, string employeeId,
        CancellationToken ct = default);
}
