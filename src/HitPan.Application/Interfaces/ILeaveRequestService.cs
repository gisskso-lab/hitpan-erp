using HitPan.Application.DTOs.Employee;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 연차 신청/승인/반려 서비스 인터페이스이다.
/// </summary>
public interface ILeaveRequestService
{
    Task<List<LeaveRequestListDto>> GetListAsync(string tenantId, string? employeeId = null, CancellationToken ct = default);
    Task<string> CreateAsync(string tenantId, CreateLeaveRequest request, CancellationToken ct = default);
    Task ApproveAsync(string tenantId, ApproveLeaveRequest request, CancellationToken ct = default);
    Task RejectAsync(string tenantId, ApproveLeaveRequest request, CancellationToken ct = default);

    /// 작20260429 (사장님 결재): 대시보드 월간 연차 캘린더용 조회.
    /// 활성 사원 + 해당 월에 걸치는 모든 휴가(승인/대기) 반환.
    Task<LeaveCalendarDto> GetCalendarAsync(string tenantId, int year, int month, CancellationToken ct = default);
}
