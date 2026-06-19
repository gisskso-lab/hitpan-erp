using HitPan.Application.DTOs.Employee;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 연차 신청/승인/반려 서비스 인터페이스이다.
/// </summary>
public interface ILeaveRequestService
{
    Task<List<LeaveRequestListDto>> GetListAsync(string tenantId, string? employeeId = null, CancellationToken ct = default);
    Task<string> CreateAsync(string tenantId, CreateLeaveRequest request, CancellationToken ct = default);
    // P1-3 봉합(2026-06-20): 실제 처리한 결재자(approverId)를 받아 기록한다.
    // 종전엔 "첫 TenantAdmin"을 서브쿼리로 자동 선택해, 관리자 여럿일 때 실제 승인자 추적이 불가했다.
    // 봉합 (2026-06-20, LV-01): 반환 bool — 실제로 'pending' 건을 처리했으면 true, 대상 없음(미존재/이미 처리)이면 false.
    //   상태 역전·무음 통과 방지(헌법 #20). 컨트롤러가 false면 404/409로 정직하게 응답.
    Task<bool> ApproveAsync(string tenantId, string approverId, ApproveLeaveRequest request, CancellationToken ct = default);
    Task<bool> RejectAsync(string tenantId, string approverId, ApproveLeaveRequest request, CancellationToken ct = default);

    /// 작20260429 (사장님 결재): 대시보드 월간 연차 캘린더용 조회.
    /// 활성 사원 + 해당 월에 걸치는 모든 휴가(승인/대기) 반환.
    Task<LeaveCalendarDto> GetCalendarAsync(string tenantId, int year, int month, CancellationToken ct = default);
}
