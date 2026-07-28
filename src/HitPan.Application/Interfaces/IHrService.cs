using HitPan.Application.DTOs.Employee;

namespace HitPan.Application.Interfaces;

/// <summary>인사·근태 통합 서비스</summary>
public interface IHrService
{
    // 출퇴근
    Task<List<AttendanceDto>> GetAttendanceAsync(string tenantId, DateTime? from, DateTime? to, string? employeeId, CancellationToken ct = default);
    Task<string> CheckInAsync(string tenantId, string employeeId, CheckInOutRequest req, CancellationToken ct = default);
    Task CheckOutAsync(string tenantId, string employeeId, CancellationToken ct = default);

    // 초과근무
    Task<List<OvertimeDto>> GetOvertimeAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<string> CreateOvertimeAsync(CreateOvertimeRequest req, string tenantId, string employeeId, CancellationToken ct = default);
    Task<bool> ApproveOvertimeAsync(string overtimeId, string tenantId, string action, CancellationToken ct = default);

    // HR 경비신청
    Task<List<HrExpenseRequestDto>> GetHrExpensesAsync(string tenantId, string? employeeId, CancellationToken ct = default);
    Task<string> CreateHrExpenseAsync(CreateHrExpenseRequest req, string tenantId, string employeeId, CancellationToken ct = default);
}
