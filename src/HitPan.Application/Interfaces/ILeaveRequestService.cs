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
}
